using System;
using System.Collections.Generic;
using System.Text;
using Rpg.Core;
using Rpg.Player;
using UnityEngine;

namespace Rpg.Dialogue
{
    /// <summary>
    /// Straight-line guide with obstacle traverse fallback. NPCs always move directly to the destination;
    /// on sustained stall, traverse mode temporarily disables collisions and follows terrain with an
    /// uphill bias before returning to normal walking.
    /// </summary>
    [DefaultExecutionOrder(40)]
    public sealed class NpcGuideToLocation : MonoBehaviour
    {
        enum GuideMode { Idle, Guiding, Returning }

        enum PathPhase { StraightLine, ObstacleTraverse }

        const float TravelCheckIntervalSeconds = 1f;
        const float MinProgressFractionOfMax = 0.1f;
        const float DistImproveEpsilonMeters = 0.05f;
        const int MaxTraverseStallCyclesBeforeGiveUpExclusive = 10;
        const float ObstacleTraverseDurationSeconds = 5f;
        const float TraverseFeetClearanceAboveTerrain = 0.35f;
        const float TraverseLookaheadMeters = 2f;
        const float TraverseUphillLiftBlend = 0.45f;
        const float TraverseMaxVerticalSpeedMetersPerSec = 5f;
        const float CornerReachPaddingMeters = 1.5f;
        const float TurnLerp = 7f;
        const float WalkAnimSpeedParam = 1f;
        const float IndicatorHeightMeters = 2.6f;
        const float DebugLineHeightAboveTerrainMeters = 0.2f;
        const float DebugPolylineSampleStepMeters = 2f;
        const float DebugLineWidthWorldMeters = 0.18f;
        const float GuideStartDelaySeconds = 5f;

        static readonly Color DebugGoalLegColor = new Color(0.25f, 0.85f, 1f, 1f);

        Transform _target;
        string _npcId;
        string _guideTargetId;
        float _speed;
        float _stopDistance;
        CharacterController _cc;
        Terrain _terrain;
        readonly List<Vector3> _plannedWaypoints = new List<Vector3>(2);
        readonly List<Vector3> _travelHistory = new List<Vector3>();
        readonly List<Vector3> _debugPolylineScratch = new List<Vector3>(256);
        int _waypointIndex;
        GuideMode _mode = GuideMode.Idle;
        Vector3 _originPosition;
        bool _hasOriginPosition;

        bool _legEndsAtGoal;
        bool _ignoreHeightAvoidLimit;

        PathPhase _pathPhase = PathPhase.StraightLine;
        float _travelSampleDeadline;
        Vector3 _travelSampleOrigin;

        int _avoidAttemptIndex;
        float _avoidDistToLegGoalBefore;
        float _obstacleTraverseDeadline;
        bool _ccDisabledForTraverse;

        Transform _planDebugLineRoot;
        LineRenderer _activeLegLineRenderer;
        Material _activeLegLineMaterial;

        Animator _animator;
        CityPeopleLocomotionDriver _cityPeopleDriver;
        StylizedNpcAnimatorDriver _stylizedDriver;
        bool _disabledCityPeopleDriverForGuide;
        bool _disabledStylizedDriverForGuide;
        int _speedHash;
        int _sprintHash;
        int _groundedHash;
        int _verticalSpeedHash;
        bool _hasSpeed;
        bool _hasSprint;
        bool _hasGrounded;
        bool _hasVerticalSpeed;
        string _idleStateName;
        string _walkStateName;
        bool _hasSimpleWalkIdleStates;
        bool _animShowingWalk;
        float _guideStartMoveAtTime;

        Transform _guideIndicator;
        Renderer _guideIndicatorRenderer;

        public bool IsGuidingActive => enabled && _mode == GuideMode.Guiding;

        public void Begin(string npcId, string targetId, Transform target, float speedMetersPerSec, float stopDistanceMeters)
        {
            TerrainGuideGlobalMetrics.EnsureInitialized();
            _npcId = npcId ?? string.Empty;
            _guideTargetId = targetId ?? string.Empty;
            _target = target;
            _speed = Mathf.Clamp(speedMetersPerSec, 0.5f, 8f);
            _stopDistance = Mathf.Clamp(stopDistanceMeters, 1f, 30f);
            _cc = GetComponent<CharacterController>();
            if (!_hasOriginPosition)
            {
                _originPosition = transform.position;
                _hasOriginPosition = true;
            }
            _travelHistory.Clear();
            _travelHistory.Add(transform.position);
            _avoidAttemptIndex = 0;
            _pathPhase = PathPhase.StraightLine;
            if (_target != null)
            {
                _mode = GuideMode.Guiding;
                _guideStartMoveAtTime = Time.time + GuideStartDelaySeconds;
                InitializeGuideSession();
                EnsureAnimationBindings();
                DisableCompetingAnimDrivers();
                EnsureGuideIndicator();
                SetGuideIndicatorActive(true, new Color(0.2f, 1f, 0.35f, 0.95f));
                enabled = _plannedWaypoints.Count > 0;
            }
            else
                enabled = false;
        }

        public void BeginReturnToOrigin()
        {
            if (!_hasOriginPosition)
                return;
            TerrainGuideGlobalMetrics.EnsureInitialized();
            EnsureAnimationBindings();
            DisableCompetingAnimDrivers();
            _target = null;
            _guideTargetId = "origin";
            _mode = GuideMode.Returning;
            _guideStartMoveAtTime = 0f;
            _avoidAttemptIndex = 0;
            _pathPhase = PathPhase.StraightLine;
            InitializeGuideSession();
            EnsureGuideIndicator();
            SetGuideIndicatorActive(true, new Color(0.25f, 0.7f, 1f, 0.95f));
            enabled = _plannedWaypoints.Count > 0;
        }

        void Awake() => enabled = false;

        void LateUpdate()
        {
            if (!enabled || _plannedWaypoints.Count == 0)
                return;
            RefreshActiveLegDebugDisplay();
        }

        void OnGUI()
        {
            if (!enabled || _pathPhase != PathPhase.ObstacleTraverse)
                return;
            var cam = Camera.main;
            if (cam == null)
                return;
            var world = transform.position + Vector3.up * 3.1f;
            var sp = cam.WorldToScreenPoint(world);
            if (sp.z <= 0f)
                return;
            var rect = new Rect(sp.x - 42f, Screen.height - sp.y - 10f, 84f, 18f);
            var prev = GUI.color;
            GUI.color = new Color(1f, 0.95f, 0.3f, 0.95f);
            GUI.Label(rect, "TRAVERSE");
            GUI.color = prev;
        }

        void OnDisable()
        {
            ApplyIdleAnimationState();
            if (_disabledCityPeopleDriverForGuide && _cityPeopleDriver != null)
                _cityPeopleDriver.enabled = true;
            _disabledCityPeopleDriverForGuide = false;
            if (_disabledStylizedDriverForGuide && _stylizedDriver != null)
                _stylizedDriver.enabled = true;
            _disabledStylizedDriverForGuide = false;
            SetGuideIndicatorActive(false, Color.clear);
            RestoreCharacterControllerAfterTraverse();
            DestroyPlanDebugLine();
        }

        void Update()
        {
            if (_plannedWaypoints.Count == 0)
            {
                enabled = false;
                return;
            }
            UpdateGuideIndicatorVisual();
            if (_mode == GuideMode.Guiding && Time.time < _guideStartMoveAtTime)
            {
                ApplyIdleAnimationState();
                return;
            }

            if (!TryGetCurrentWaypoint(out var waypoint))
            {
                enabled = false;
                return;
            }

            var here = transform.position;
            var toWaypoint = waypoint - here;
            toWaypoint.y = 0f;
            var dist = toWaypoint.magnitude;
            var reachRadius = Mathf.Max(_stopDistance, CornerReachPaddingMeters);
            if (dist <= reachRadius)
            {
                EndObstacleTraverseIfActive();
                if (_legEndsAtGoal)
                {
                    if (_mode == GuideMode.Guiding)
                    {
                        NotifyGuideArrived();
                        _mode = GuideMode.Idle;
                    }
                    if (_mode == GuideMode.Returning)
                        _mode = GuideMode.Idle;
                    DestroyPlanDebugLine();
                    enabled = false;
                    return;
                }
                if (_mode == GuideMode.Returning && !_legEndsAtGoal)
                {
                    BeginStraightToGoalLeg();
                    return;
                }
                DestroyPlanDebugLine();
                enabled = false;
                return;
            }

            if (_pathPhase == PathPhase.ObstacleTraverse)
            {
                ProcessObstacleTraverseFrame(waypoint);
                if (Time.time >= _obstacleTraverseDeadline)
                    EvaluateTraverseAttemptOutcome(transform.position, waypoint);
            }
            else
            {
                if (dist < 0.001f)
                {
                    enabled = false;
                    return;
                }
                var dir = toWaypoint / dist;
                if (dist > reachRadius)
                {
                    transform.rotation = Quaternion.Slerp(
                        transform.rotation,
                        Quaternion.LookRotation(new Vector3(dir.x, 0f, dir.z), Vector3.up),
                        Mathf.Clamp01(Time.deltaTime * TurnLerp));

                    var step = dir * (_speed * Time.deltaTime);
                    if (_cc != null && _cc.enabled)
                        _cc.Move(new Vector3(step.x, 0f, step.z));
                    else
                        transform.position += new Vector3(step.x, 0f, step.z);
                }

                if (_pathPhase == PathPhase.StraightLine)
                    ProcessStraightLineTravelWindow(transform.position, dist, waypoint);
            }

            if (_mode == GuideMode.Guiding
                && (_travelHistory.Count == 0
                    || Vector3.Distance(_travelHistory[_travelHistory.Count - 1], transform.position) > 1.0f))
                _travelHistory.Add(transform.position);
            ApplyWalkingAnimationState();
        }

        void InitializeGuideSession()
        {
            try
            {
                _terrain = ResolveNearestTerrain(transform.position);
                BeginStraightToGoalLeg();
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                ApplyStraightPathFallback(UltimateGoalWorld());
                var msg = "[Guide] Session init failed. Straight-line fallback.\n" + FormatExceptionForGuideUi(ex);
                Debug.LogWarning(msg);
                DialogueManager.Instance?.AppendGuideNavigationSystem(msg);
            }
        }

        void BeginStraightToGoalLeg()
        {
            var g = UltimateGoalWorld();
            RefreshHeightLimitBypass(g);
            _plannedWaypoints.Clear();
            _plannedWaypoints.Add(ClampToTerrain(g));
            _waypointIndex = 0;
            _legEndsAtGoal = true;
            _pathPhase = PathPhase.StraightLine;
            _avoidAttemptIndex = 0;
            ResetStraightLineProgressProbe();
            Debug.Log("[Guide] Leg → goal (straight + obstacle traverse on stall).");
        }

        void ApplyStraightPathFallback(Vector3 goalWorld)
        {
            RestoreCharacterControllerAfterTraverse();
            _plannedWaypoints.Clear();
            _plannedWaypoints.Add(ClampToTerrain(goalWorld));
            _waypointIndex = 0;
            _legEndsAtGoal = true;
            ResetStraightLineProgressProbe();
            _avoidAttemptIndex = 0;
            _pathPhase = PathPhase.StraightLine;
        }

        static string FormatExceptionForGuideUi(Exception ex)
        {
            if (ex == null)
                return string.Empty;
            var sb = new StringBuilder();
            sb.AppendLine(ex.ToString());
            if (ex.InnerException != null)
            {
                sb.AppendLine("--- Inner ---");
                sb.AppendLine(ex.InnerException.ToString());
            }
            return sb.ToString();
        }

        Vector3 UltimateGoalWorld()
        {
            if (_target != null)
                return ClampToTerrain(_target.position);
            return ClampToTerrain(_originPosition);
        }

        float SampleAbsoluteTerrainWorldY(float x, float z)
        {
            var xz = new Vector3(x, 0f, z);
            if (TerrainGuideGlobalMetrics.TrySampleWorldTerrainHeight(xz, out var y))
                return y;
            if (_terrain != null)
                return _terrain.SampleHeight(xz) + _terrain.transform.position.y;
            return float.NaN;
        }

        void ProcessStraightLineTravelWindow(Vector3 posAfter, float distToWaypoint, Vector3 waypoint)
        {
            if (Time.time < _travelSampleDeadline)
                return;
            var maxExpected = _speed * TravelCheckIntervalSeconds;
            var minRequired = MinProgressFractionOfMax * maxExpected;
            var progressed = PlanarDistance(_travelSampleOrigin, posAfter);
            if (IsHighTerrainPenaltyActive(posAfter))
                progressed = 0f;

            if (progressed <= minRequired)
                BeginObstacleTraversePhase(posAfter, waypoint);
            else
            {
                _travelSampleOrigin = posAfter;
                _travelSampleDeadline = Time.time + TravelCheckIntervalSeconds;
            }
        }

        void RefreshHeightLimitBypass(Vector3 waypoint)
        {
            TerrainGuideGlobalMetrics.EnsureInitialized();
            if (!TerrainGuideGlobalMetrics.TrySampleWorldTerrainHeight(waypoint, out var y))
            {
                _ignoreHeightAvoidLimit = true;
                return;
            }
            _ignoreHeightAvoidLimit = y >= TerrainGuideGlobalMetrics.AvoidTerrainHeightWorldY;
        }

        bool IsHighTerrainPenaltyActive(Vector3 worldPos) =>
            !_ignoreHeightAvoidLimit && TerrainGuideGlobalMetrics.IsTerrainAboveAvoidThreshold(worldPos);

        void BeginObstacleTraversePhase(Vector3 here, Vector3 legGoal)
        {
            _pathPhase = PathPhase.ObstacleTraverse;
            _avoidDistToLegGoalBefore = PlanarDistance(legGoal, here);
            _obstacleTraverseDeadline = Time.time + ObstacleTraverseDurationSeconds;
            if (_cc != null && _cc.enabled)
            {
                _cc.enabled = false;
                _ccDisabledForTraverse = true;
            }
            else
                _ccDisabledForTraverse = false;
            Debug.Log($"[Guide] Obstacle traverse {ObstacleTraverseDurationSeconds:F0}s (collision off, terrain-up bias).");
        }

        void ProcessObstacleTraverseFrame(Vector3 waypoint)
        {
            var here = transform.position;
            var toWp = waypoint - here;
            toWp.y = 0f;
            var pd = toWp.magnitude;
            if (pd < 0.0001f)
                return;
            var planarDir = toWp / pd;
            var horizStep = Mathf.Min(_speed * Time.deltaTime, Mathf.Max(0f, pd - 0.02f));
            if (horizStep <= 0f)
                return;
            var nx = here.x + planarDir.x * horizStep;
            var nz = here.z + planarDir.z * horizStep;

            var yHere = SampleAbsoluteTerrainWorldY(nx, nz);
            var lx = nx + planarDir.x * TraverseLookaheadMeters;
            var lz = nz + planarDir.z * TraverseLookaheadMeters;
            var yLook = SampleAbsoluteTerrainWorldY(lx, lz);
            if (float.IsNaN(yHere))
                yHere = here.y;
            if (float.IsNaN(yLook))
                yLook = yHere;

            var ground = Mathf.Max(yHere, yLook);
            var minFeet = ground + TraverseFeetClearanceAboveTerrain;
            var uphill = Mathf.Max(0f, yLook - yHere);
            var wantY = Mathf.Max(minFeet, here.y + uphill * TraverseUphillLiftBlend);
            wantY = Mathf.Max(wantY, here.y);
            var maxDy = TraverseMaxVerticalSpeedMetersPerSec * Time.deltaTime;
            var targetY = Mathf.MoveTowards(here.y, wantY, maxDy);
            targetY = Mathf.Max(targetY, Mathf.Min(yHere, yLook) + TraverseFeetClearanceAboveTerrain * 0.35f);

            transform.position = new Vector3(nx, targetY, nz);
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                Quaternion.LookRotation(new Vector3(planarDir.x, 0f, planarDir.z), Vector3.up),
                Mathf.Clamp01(Time.deltaTime * TurnLerp));
        }

        void EvaluateTraverseAttemptOutcome(Vector3 posAfter, Vector3 legGoal)
        {
            RestoreCharacterControllerAfterTraverse();
            _pathPhase = PathPhase.StraightLine;
            var distAfter = PlanarDistance(legGoal, posAfter);
            if (distAfter + DistImproveEpsilonMeters < _avoidDistToLegGoalBefore)
            {
                if (_legEndsAtGoal && _mode == GuideMode.Guiding && _target != null && _plannedWaypoints.Count > 0)
                {
                    var wp = ClampToTerrain(_target.position);
                    _plannedWaypoints[_plannedWaypoints.Count - 1] = wp;
                    RefreshHeightLimitBypass(wp);
                }
                ReturnToStraightLineAfterSuccessfulTraverse(posAfter);
                return;
            }
            _avoidAttemptIndex++;
            if (_avoidAttemptIndex >= MaxTraverseStallCyclesBeforeGiveUpExclusive)
            {
                GiveUpPathTooDifficult();
                return;
            }
            ResetStraightLineProgressProbe();
            _travelSampleOrigin = posAfter;
            _travelSampleDeadline = Time.time + TravelCheckIntervalSeconds;
        }

        void ReturnToStraightLineAfterSuccessfulTraverse(Vector3 here)
        {
            _pathPhase = PathPhase.StraightLine;
            _avoidAttemptIndex = 0;
            _travelSampleOrigin = here;
            _travelSampleDeadline = Time.time + TravelCheckIntervalSeconds;
        }

        void RestoreCharacterControllerAfterTraverse()
        {
            if (!_ccDisabledForTraverse || _cc == null)
            {
                _ccDisabledForTraverse = false;
                return;
            }
            _cc.enabled = true;
            _ccDisabledForTraverse = false;
            var p = transform.position;
            transform.position = ClampToTerrain(new Vector3(p.x, p.y, p.z));
        }

        void EndObstacleTraverseIfActive()
        {
            if (_pathPhase != PathPhase.ObstacleTraverse)
                return;
            RestoreCharacterControllerAfterTraverse();
            _pathPhase = PathPhase.StraightLine;
            ResetStraightLineProgressProbe();
            _avoidAttemptIndex = 0;
        }

        void ResetStraightLineProgressProbe()
        {
            _travelSampleOrigin = transform.position;
            _travelSampleDeadline = Time.time + TravelCheckIntervalSeconds;
        }

        void GiveUpPathTooDifficult()
        {
            RestoreCharacterControllerAfterTraverse();
            _plannedWaypoints.Clear();
            _target = null;
            _pathPhase = PathPhase.StraightLine;
            _mode = GuideMode.Idle;
            NotifyGuidePathTooDifficult();
            DestroyPlanDebugLine();
            enabled = false;
        }

        void EnsurePlanDebugRoot()
        {
            if (_planDebugLineRoot != null)
                return;
            var go = new GameObject("NpcGuidePlanPath");
            go.transform.SetParent(null, false);
            _planDebugLineRoot = go.transform;
        }

        static Material CreateDebugLineMaterialBase()
        {
            var shader = Shader.Find("Sprites/Default")
                         ?? Shader.Find("Unlit/Color")
                         ?? Shader.Find("Legacy Shaders/Particles/Alpha Blended Premultiply");
            return new Material(shader);
        }

        void RefreshActiveLegDebugDisplay()
        {
            try
            {
                EnsurePlanDebugRoot();
                if (_plannedWaypoints.Count == 0)
                {
                    if (_activeLegLineRenderer != null)
                        _activeLegLineRenderer.enabled = false;
                    return;
                }
                if (!TryGetCurrentWaypoint(out var wp))
                {
                    if (_activeLegLineRenderer != null)
                        _activeLegLineRenderer.enabled = false;
                    return;
                }
                var from = transform.position;
                EnsureActiveLegLineRenderer(DebugGoalLegColor);
                _debugPolylineScratch.Clear();
                AppendElevatedPolylineSamples(from, wp);
                if (_debugPolylineScratch.Count < 2)
                {
                    _activeLegLineRenderer.enabled = false;
                    return;
                }
                _activeLegLineRenderer.enabled = true;
                _activeLegLineRenderer.positionCount = _debugPolylineScratch.Count;
                for (var i = 0; i < _debugPolylineScratch.Count; i++)
                    _activeLegLineRenderer.SetPosition(i, _debugPolylineScratch[i]);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Guide] Debug line: {ex.Message}");
            }
        }

        void EnsureActiveLegLineRenderer(Color color)
        {
            if (_activeLegLineRenderer != null)
            {
                ApplyLegLineColors(color);
                return;
            }
            var segGo = new GameObject("PlanCurrentLeg");
            segGo.transform.SetParent(_planDebugLineRoot, false);
            var lr = segGo.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.loop = false;
            lr.numCornerVertices = 3;
            lr.numCapVertices = 2;
            lr.startWidth = DebugLineWidthWorldMeters;
            lr.endWidth = DebugLineWidthWorldMeters;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            _activeLegLineMaterial = CreateDebugLineMaterialBase();
            lr.material = _activeLegLineMaterial;
            _activeLegLineRenderer = lr;
            ApplyLegLineColors(color);
        }

        void ApplyLegLineColors(Color color)
        {
            if (_activeLegLineMaterial == null || _activeLegLineRenderer == null)
                return;
            _activeLegLineMaterial.color = color;
            if (_activeLegLineMaterial.HasProperty("_EmissionColor"))
            {
                _activeLegLineMaterial.EnableKeyword("_EMISSION");
                _activeLegLineMaterial.SetColor("_EmissionColor", color * 1.15f);
            }
            _activeLegLineRenderer.startColor = color;
            _activeLegLineRenderer.endColor = color;
        }

        void AppendElevatedPolylineSamples(Vector3 a, Vector3 b)
        {
            var len = PlanarDistance(a, b);
            var steps = Mathf.Max(1, Mathf.CeilToInt(len / DebugPolylineSampleStepMeters));
            for (var i = 0; i <= steps; i++)
            {
                var t = i / (float)steps;
                var x = Mathf.Lerp(a.x, b.x, t);
                var z = Mathf.Lerp(a.z, b.z, t);
                var yBase = SampleTerrainYWorld(new Vector3(x, 0f, z));
                var y = yBase + DebugLineHeightAboveTerrainMeters;
                var elevated = new Vector3(x, y, z);
                if (_debugPolylineScratch.Count > 0)
                {
                    var last = _debugPolylineScratch[_debugPolylineScratch.Count - 1];
                    if (PlanarDistance(last, elevated) < 0.04f && Mathf.Abs(last.y - elevated.y) < 0.02f)
                        continue;
                }
                _debugPolylineScratch.Add(elevated);
            }
        }

        float SampleTerrainYWorld(Vector3 world)
        {
            var y = SampleAbsoluteTerrainWorldY(world.x, world.z);
            return float.IsNaN(y) ? world.y : y;
        }

        void DestroyPlanDebugLine()
        {
            if (_activeLegLineMaterial != null)
            {
                Destroy(_activeLegLineMaterial);
                _activeLegLineMaterial = null;
            }
            if (_planDebugLineRoot != null)
                Destroy(_planDebugLineRoot.gameObject);
            _planDebugLineRoot = null;
            _activeLegLineRenderer = null;
        }

        static float PlanarDistance(Vector3 a, Vector3 b)
        {
            var dx = a.x - b.x;
            var dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        void EnsureGuideIndicator()
        {
            if (_guideIndicator != null)
                return;
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "GuideIndicator";
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, IndicatorHeightMeters, 0f);
            go.transform.localScale = new Vector3(0.22f, 0.22f, 0.22f);
            var c = go.GetComponent<Collider>();
            if (c != null)
                Destroy(c);
            _guideIndicator = go.transform;
            _guideIndicatorRenderer = go.GetComponent<Renderer>();
            if (_guideIndicatorRenderer != null)
                _guideIndicatorRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.SetActive(false);
        }

        void SetGuideIndicatorActive(bool on, Color color)
        {
            if (_guideIndicator == null)
                return;
            _guideIndicator.gameObject.SetActive(on);
            if (!on || _guideIndicatorRenderer == null)
                return;
            if (_guideIndicatorRenderer.material != null)
            {
                _guideIndicatorRenderer.material.color = color;
                _guideIndicatorRenderer.material.EnableKeyword("_EMISSION");
                _guideIndicatorRenderer.material.SetColor("_EmissionColor", color * 1.4f);
            }
        }

        void UpdateGuideIndicatorVisual()
        {
            if (_guideIndicator == null || !_guideIndicator.gameObject.activeSelf)
                return;
            _guideIndicator.localPosition = new Vector3(0f, IndicatorHeightMeters + Mathf.Sin(Time.time * 4f) * 0.06f, 0f);
            var cam = Camera.main;
            if (cam != null)
            {
                var f = cam.transform.forward;
                f.y = 0f;
                if (f.sqrMagnitude > 0.001f)
                    _guideIndicator.rotation = Quaternion.LookRotation(f.normalized, Vector3.up);
            }
        }

        void DisableCompetingAnimDrivers()
        {
            if (_cityPeopleDriver != null && _cityPeopleDriver.enabled)
            {
                _cityPeopleDriver.enabled = false;
                _disabledCityPeopleDriverForGuide = true;
            }
            if (_stylizedDriver != null && _stylizedDriver.enabled)
            {
                _stylizedDriver.enabled = false;
                _disabledStylizedDriverForGuide = true;
            }
        }

        void EnsureAnimationBindings()
        {
            _animator = GetComponent<Animator>() ?? GetComponentInChildren<Animator>(true);
            _cityPeopleDriver = GetComponent<CityPeopleLocomotionDriver>();
            _stylizedDriver = GetComponent<StylizedNpcAnimatorDriver>();
            if (_animator == null || _animator.runtimeAnimatorController == null)
                return;
            _speedHash = Animator.StringToHash("Speed");
            _sprintHash = Animator.StringToHash("Sprint");
            _groundedHash = Animator.StringToHash("Grounded");
            _verticalSpeedHash = Animator.StringToHash("VerticalSpeed");
            _hasSpeed = false;
            _hasSprint = false;
            _hasGrounded = false;
            _hasVerticalSpeed = false;
            foreach (var p in _animator.parameters)
            {
                if (p.nameHash == _speedHash) _hasSpeed = true;
                else if (p.nameHash == _sprintHash) _hasSprint = true;
                else if (p.nameHash == _groundedHash) _hasGrounded = true;
                else if (p.nameHash == _verticalSpeedHash) _hasVerticalSpeed = true;
            }
            PickIdleWalkStates(_animator, out _idleStateName, out _walkStateName);
            _hasSimpleWalkIdleStates = !string.IsNullOrWhiteSpace(_idleStateName) && !string.IsNullOrWhiteSpace(_walkStateName);
            _animShowingWalk = false;
        }

        void ApplyWalkingAnimationState()
        {
            if (_animator == null)
                return;
            if (_hasSimpleWalkIdleStates)
            {
                if (!_animShowingWalk)
                {
                    _animator.CrossFadeInFixedTime(_walkStateName, 0.12f);
                    _animShowingWalk = true;
                }
                _animator.speed = 1f;
            }
            if (_hasSpeed) _animator.SetFloat(_speedHash, WalkAnimSpeedParam);
            if (_hasSprint) _animator.SetBool(_sprintHash, false);
            if (_hasGrounded) _animator.SetBool(_groundedHash, true);
            if (_hasVerticalSpeed) _animator.SetFloat(_verticalSpeedHash, 0f);
        }

        void ApplyIdleAnimationState()
        {
            if (_animator == null)
                return;
            if (_hasSimpleWalkIdleStates)
            {
                if (_animShowingWalk)
                {
                    _animator.CrossFadeInFixedTime(_idleStateName, 0.12f);
                    _animShowingWalk = false;
                }
                _animator.speed = 1f;
            }
            if (_hasSpeed) _animator.SetFloat(_speedHash, 0f);
            if (_hasSprint) _animator.SetBool(_sprintHash, false);
            if (_hasGrounded) _animator.SetBool(_groundedHash, true);
            if (_hasVerticalSpeed) _animator.SetFloat(_verticalSpeedHash, 0f);
        }

        bool TryGetCurrentWaypoint(out Vector3 wp)
        {
            wp = default;
            if (_plannedWaypoints.Count == 0)
                return false;
            _waypointIndex = Mathf.Clamp(_waypointIndex, 0, _plannedWaypoints.Count - 1);
            wp = _plannedWaypoints[_waypointIndex];
            return true;
        }

        Vector3 ClampToTerrain(Vector3 p)
        {
            var y = SampleAbsoluteTerrainWorldY(p.x, p.z);
            if (!float.IsNaN(y))
                return new Vector3(p.x, y, p.z);
            if (_terrain != null)
            {
                var yy = _terrain.SampleHeight(p) + _terrain.transform.position.y;
                return new Vector3(p.x, yy, p.z);
            }
            return p;
        }

        static Terrain ResolveNearestTerrain(Vector3 worldPos)
        {
            Terrain best = null;
            var bestSqr = float.MaxValue;
            foreach (var t in UnityEngine.Object.FindObjectsByType<Terrain>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (t == null || t.terrainData == null)
                    continue;
                var o = t.transform.position;
                var c = o + new Vector3(t.terrainData.size.x * 0.5f, 0f, t.terrainData.size.z * 0.5f);
                var sqr = (new Vector2(worldPos.x - c.x, worldPos.z - c.z)).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = t;
                }
            }
            return best;
        }

        static void PickIdleWalkStates(Animator animator, out string idleName, out string walkName)
        {
            idleName = null;
            walkName = null;
            if (animator == null || animator.runtimeAnimatorController == null)
                return;
            foreach (var c in animator.runtimeAnimatorController.animationClips)
            {
                if (c == null || string.IsNullOrWhiteSpace(c.name))
                    continue;
                var n = c.name.ToLowerInvariant();
                if (idleName == null && n.Contains("idle"))
                    idleName = c.name;
                if (walkName == null && (n.Contains("walk") || n.Contains("locom")))
                    walkName = c.name;
            }
            if (idleName == null && animator.runtimeAnimatorController.animationClips.Length > 0)
                idleName = animator.runtimeAnimatorController.animationClips[0].name;
            if (walkName == null)
                walkName = idleName;
        }

        void NotifyGuideArrived()
        {
            if (DialogueManager.Instance == null || string.IsNullOrWhiteSpace(_npcId))
                return;
            DialogueManager.Instance.OnNpcGuideArrived(_npcId, string.IsNullOrWhiteSpace(_guideTargetId) ? "destination" : _guideTargetId);
        }

        void NotifyGuidePathTooDifficult()
        {
            if (DialogueManager.Instance == null || string.IsNullOrWhiteSpace(_npcId))
                return;
            DialogueManager.Instance.OnNpcGuidePathTooDifficult(_npcId);
        }
    }
}
