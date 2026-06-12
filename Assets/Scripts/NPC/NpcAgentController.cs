using System;
using System.Collections.Generic;
using Rpg.Dialogue;
using UnityEngine;

namespace Rpg.Npc
{
    public static class NpcPrimitiveTypes
    {
        public const string GotoLocation = "goto_location";
        public const string GotoNpc = "goto_npc";
        public const string WaitAt = "wait_at";
        public const string PerformWork = "perform_work";
        public const string ChatWithNpc = "chat_with_npc";
        public const string IdleHome = "idle_home";

        public static bool TryParse(string raw, out NpcPrimitiveKind kind)
        {
            kind = NpcPrimitiveKind.Unknown;
            if (string.IsNullOrWhiteSpace(raw))
                return false;

            switch (raw.Trim().ToLowerInvariant())
            {
                case GotoLocation:
                    kind = NpcPrimitiveKind.GotoLocation;
                    return true;
                case GotoNpc:
                    kind = NpcPrimitiveKind.GotoNpc;
                    return true;
                case WaitAt:
                    kind = NpcPrimitiveKind.WaitAt;
                    return true;
                case PerformWork:
                    kind = NpcPrimitiveKind.PerformWork;
                    return true;
                case ChatWithNpc:
                    kind = NpcPrimitiveKind.ChatWithNpc;
                    return true;
                case IdleHome:
                    kind = NpcPrimitiveKind.IdleHome;
                    return true;
                default:
                    return false;
            }
        }
    }

    public enum NpcPrimitiveKind
    {
        Unknown = 0,
        GotoLocation = 1,
        GotoNpc = 2,
        WaitAt = 3,
        PerformWork = 4,
        ChatWithNpc = 5,
        IdleHome = 6
    }

    public enum NpcAgentStepCompletionState
    {
        None = 0,
        Succeeded = 1,
        Failed = 2
    }

    [Serializable]
    public sealed class NpcPrimitiveStep
    {
        [SerializeField] string primitiveType = NpcPrimitiveTypes.WaitAt;
        [SerializeField] Vector3 worldLocation;
        [SerializeField] string targetNpcId = string.Empty;
        [SerializeField] float durationSeconds = 1f;
        [SerializeField] float stopDistanceMeters = 0.35f;
        [SerializeField] string workId = string.Empty;

        public string PrimitiveType
        {
            get => primitiveType;
            set => primitiveType = value ?? string.Empty;
        }

        public Vector3 WorldLocation
        {
            get => worldLocation;
            set => worldLocation = value;
        }

        public string TargetNpcId
        {
            get => targetNpcId;
            set => targetNpcId = value ?? string.Empty;
        }

        public float DurationSeconds
        {
            get => durationSeconds;
            set => durationSeconds = Mathf.Max(0f, value);
        }

        public float StopDistanceMeters
        {
            get => stopDistanceMeters;
            set => stopDistanceMeters = Mathf.Max(0.05f, value);
        }

        public string WorkId
        {
            get => workId;
            set => workId = value ?? string.Empty;
        }

        public NpcPrimitiveStep Clone()
        {
            return new NpcPrimitiveStep
            {
                PrimitiveType = primitiveType,
                WorldLocation = worldLocation,
                TargetNpcId = targetNpcId,
                DurationSeconds = durationSeconds,
                StopDistanceMeters = stopDistanceMeters,
                WorkId = workId
            };
        }
    }

    /// <summary>
    /// Executes authored/generated primitive plan steps and falls back to ambient villager behavior when no plan exists.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [DefaultExecutionOrder(17)]
    public sealed class NpcAgentController : MonoBehaviour
    {
        enum StepTickResult
        {
            Running = 0,
            Succeeded = 1,
            Failed = 2
        }

        [SerializeField] float moveSpeedMetersPerSecond = 2.2f;
        [SerializeField] float stopDistanceMeters = 0.35f;
        [SerializeField] float gravity = -18f;
        [SerializeField] bool fallbackToAmbientWhenIdle = true;
        [SerializeField] float defaultWaitSeconds = 2f;
        [SerializeField] float defaultWorkSeconds = 3f;
        [SerializeField] float defaultChatSeconds = 2f;

        CharacterController _cc;
        VillagerAmbientRoutine _ambientRoutine;
        NpcAmbientDrift _ambientDrift;
        readonly Queue<NpcPrimitiveStep> _queue = new Queue<NpcPrimitiveStep>();
        NpcPrimitiveStep _activeStep;
        Transform _activeNpcTarget;
        Vector3 _activeMoveTarget;
        Vector3 _homePosition;
        float _verticalVelocity;
        float _stepStartedAt;
        float _stepPhaseStartedAt;
        bool _stepAnchorReached;
        bool _initialized;
        bool _ownsLocomotion;
        bool _ambientWasEnabledBeforePlan;
        bool _driftWasEnabledBeforePlan;

        public float CurrentPlanarSpeed { get; private set; }
        public string CurrentPrimitiveType { get; private set; } = string.Empty;
        public NpcAgentStepCompletionState LastStepCompletionState { get; private set; }
        public int SuccessfulStepCount { get; private set; }
        public int FailedStepCount { get; private set; }
        public bool IsExecutingPlan => _activeStep != null || _queue.Count > 0;
        public int RemainingStepCount => _queue.Count + (_activeStep != null ? 1 : 0);

        void Awake()
        {
            EnsureInitialized();
        }

        public void InitializeForTests()
        {
            EnsureInitialized();
        }

        void Update()
        {
            TickAgent(Time.deltaTime, Time.time);
        }

        public void TickAgent(float deltaTime, float nowTime)
        {
            EnsureInitialized();
            if (_cc == null || !_cc.enabled || !_cc.gameObject.activeInHierarchy)
                return;

            if (DialogueManager.Instance != null && DialogueManager.Instance.IsDialogueOpen)
            {
                StepGravityOnly(deltaTime);
                return;
            }

            if (!IsExecutingPlan)
            {
                ReleaseLocomotionOwnershipIfNeeded();
                if (fallbackToAmbientWhenIdle && _ambientRoutine != null && _ambientRoutine.enabled)
                {
                    CurrentPlanarSpeed = 0f;
                    return;
                }

                StepGravityOnly(deltaTime);
                return;
            }

            AcquireLocomotionOwnershipIfNeeded();
            if (_activeStep == null && !TryBeginNextStep(nowTime))
            {
                ReleaseLocomotionOwnershipIfNeeded();
                return;
            }

            if (_activeStep == null)
                return;

            if (!NpcPrimitiveTypes.TryParse(_activeStep.PrimitiveType, out var kind))
            {
                CompleteStep(success: false);
                return;
            }

            var stepResult = TickStep(kind, deltaTime, nowTime);
            if (stepResult == StepTickResult.Succeeded)
                CompleteStep(success: true);
            else if (stepResult == StepTickResult.Failed)
                CompleteStep(success: false);
        }

        public void SetPlan(IEnumerable<NpcPrimitiveStep> steps)
        {
            _queue.Clear();
            _activeStep = null;
            CurrentPrimitiveType = string.Empty;
            LastStepCompletionState = NpcAgentStepCompletionState.None;

            if (steps == null)
                return;

            foreach (var step in steps)
                EnqueueStep(step);
        }

        public void EnqueueStep(NpcPrimitiveStep step)
        {
            if (step == null)
                return;
            _queue.Enqueue(step.Clone());
        }

        public void ClearPlan()
        {
            _queue.Clear();
            _activeStep = null;
            CurrentPrimitiveType = string.Empty;
            ReleaseLocomotionOwnershipIfNeeded();
        }

        void EnsureInitialized()
        {
            if (_initialized)
                return;
            _cc = GetComponent<CharacterController>();
            _ambientRoutine = GetComponent<VillagerAmbientRoutine>();
            _ambientDrift = GetComponent<NpcAmbientDrift>();
            _homePosition = transform.position;
            _initialized = true;
        }

        void AcquireLocomotionOwnershipIfNeeded()
        {
            if (_ownsLocomotion)
                return;
            _ambientWasEnabledBeforePlan = _ambientRoutine != null && _ambientRoutine.enabled;
            _driftWasEnabledBeforePlan = _ambientDrift != null && _ambientDrift.enabled;

            if (_ambientRoutine != null)
                _ambientRoutine.enabled = false;
            if (_ambientDrift != null)
                _ambientDrift.enabled = false;

            _ownsLocomotion = true;
        }

        void ReleaseLocomotionOwnershipIfNeeded()
        {
            if (!_ownsLocomotion)
                return;
            _ownsLocomotion = false;
            _activeNpcTarget = null;
            _stepAnchorReached = false;
            CurrentPlanarSpeed = 0f;

            if (!fallbackToAmbientWhenIdle)
                return;

            if (_ambientRoutine != null)
                _ambientRoutine.enabled = _ambientWasEnabledBeforePlan;
            if (_ambientDrift != null)
                _ambientDrift.enabled = _driftWasEnabledBeforePlan;
        }

        bool TryBeginNextStep(float nowTime)
        {
            if (_queue.Count == 0)
            {
                CurrentPrimitiveType = string.Empty;
                return false;
            }

            _activeStep = _queue.Dequeue();
            _stepStartedAt = nowTime;
            _stepPhaseStartedAt = nowTime;
            _stepAnchorReached = false;
            _activeNpcTarget = null;
            CurrentPrimitiveType = _activeStep.PrimitiveType ?? string.Empty;
            _activeMoveTarget = SnapToGround(_activeStep.WorldLocation, transform.position.y);
            return true;
        }

        StepTickResult TickStep(NpcPrimitiveKind kind, float deltaTime, float nowTime)
        {
            switch (kind)
            {
                case NpcPrimitiveKind.GotoLocation:
                    return TickMoveTo(_activeMoveTarget, ResolveStopDistance(_activeStep), deltaTime)
                        ? StepTickResult.Succeeded
                        : StepTickResult.Running;
                case NpcPrimitiveKind.GotoNpc:
                    if (!TryResolveNpcTarget(_activeStep.TargetNpcId, out var npcTarget))
                        return StepTickResult.Failed;
                    return TickMoveTo(npcTarget.position, ResolveStopDistance(_activeStep), deltaTime)
                        ? StepTickResult.Succeeded
                        : StepTickResult.Running;
                case NpcPrimitiveKind.WaitAt:
                    return TickTimedAtLocation(_activeMoveTarget, ResolveStopDistance(_activeStep), ResolveDuration(_activeStep, defaultWaitSeconds), deltaTime, nowTime)
                        ? StepTickResult.Succeeded
                        : StepTickResult.Running;
                case NpcPrimitiveKind.PerformWork:
                    return TickTimedAtLocation(_activeMoveTarget, ResolveStopDistance(_activeStep), ResolveDuration(_activeStep, defaultWorkSeconds), deltaTime, nowTime)
                        ? StepTickResult.Succeeded
                        : StepTickResult.Running;
                case NpcPrimitiveKind.ChatWithNpc:
                    if (!TryResolveNpcTarget(_activeStep.TargetNpcId, out var chatTarget))
                        return StepTickResult.Failed;
                    return TickTimedAtLocation(chatTarget.position, Mathf.Max(0.35f, ResolveStopDistance(_activeStep)), ResolveDuration(_activeStep, defaultChatSeconds), deltaTime, nowTime)
                        ? StepTickResult.Succeeded
                        : StepTickResult.Running;
                case NpcPrimitiveKind.IdleHome:
                    return TickTimedAtLocation(_homePosition, ResolveStopDistance(_activeStep), ResolveDuration(_activeStep, defaultWaitSeconds), deltaTime, nowTime)
                        ? StepTickResult.Succeeded
                        : StepTickResult.Running;
                default:
                    return StepTickResult.Failed;
            }
        }

        bool TickTimedAtLocation(Vector3 target, float stopDistance, float durationSeconds, float deltaTime, float nowTime)
        {
            if (!_stepAnchorReached)
            {
                if (!TickMoveTo(target, stopDistance, deltaTime))
                    return false;
                _stepAnchorReached = true;
                _stepPhaseStartedAt = nowTime;
            }

            StepGravityOnly(deltaTime);
            return nowTime - _stepPhaseStartedAt >= Mathf.Max(0f, durationSeconds);
        }

        bool TickMoveTo(Vector3 worldTarget, float stopDistance, float deltaTime)
        {
            var target = SnapToGround(worldTarget, transform.position.y);
            var to = target - transform.position;
            to.y = 0f;
            var dist = to.magnitude;
            if (dist <= Mathf.Max(0.1f, stopDistance))
            {
                StepGravityOnly(deltaTime);
                return true;
            }

            var dir = to / dist;
            var planar = dir * Mathf.Max(0.2f, moveSpeedMetersPerSecond);
            if (planar.sqrMagnitude > 1e-6f)
            {
                var look = Quaternion.LookRotation(planar.normalized, Vector3.up);
                transform.rotation = Quaternion.Slerp(transform.rotation, look, 1f - Mathf.Exp(-10f * deltaTime));
            }

            Step(planar, deltaTime);
            return false;
        }

        void CompleteStep(bool success)
        {
            LastStepCompletionState = success ? NpcAgentStepCompletionState.Succeeded : NpcAgentStepCompletionState.Failed;
            if (success)
                SuccessfulStepCount++;
            else
                FailedStepCount++;
            _activeStep = null;
            _activeNpcTarget = null;
            _stepAnchorReached = false;
            CurrentPrimitiveType = string.Empty;
        }

        bool TryResolveNpcTarget(string npcId, out Transform target)
        {
            if (_activeNpcTarget != null && _activeNpcTarget.gameObject.activeInHierarchy)
            {
                target = _activeNpcTarget;
                return true;
            }

            target = null;
            if (string.IsNullOrWhiteSpace(npcId))
                return false;

            foreach (var b in FindObjectsByType<NpcDialogueBinding>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (b == null || b.gameObject == null || b.Definition == null)
                    continue;
                var key = npcId.Trim();
                if (string.Equals(b.Definition.npcId, key, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(b.Definition.displayName, key, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(b.gameObject.name, key, StringComparison.OrdinalIgnoreCase))
                {
                    _activeNpcTarget = b.transform;
                    target = _activeNpcTarget;
                    return target != null;
                }
            }

            return false;
        }

        float ResolveStopDistance(NpcPrimitiveStep step)
        {
            if (step == null)
                return Mathf.Max(0.1f, stopDistanceMeters);
            return Mathf.Max(0.1f, step.StopDistanceMeters > 0f ? step.StopDistanceMeters : stopDistanceMeters);
        }

        static float ResolveDuration(NpcPrimitiveStep step, float fallbackSeconds)
        {
            if (step == null)
                return Mathf.Max(0f, fallbackSeconds);
            if (step.DurationSeconds > 0f)
                return step.DurationSeconds;
            return Mathf.Max(0f, fallbackSeconds);
        }

        void StepGravityOnly(float deltaTime)
        {
            Step(Vector3.zero, deltaTime);
        }

        void Step(Vector3 planarVelocity, float deltaTime)
        {
            CurrentPlanarSpeed = new Vector3(planarVelocity.x, 0f, planarVelocity.z).magnitude;
            if (_cc.isGrounded && _verticalVelocity < 0f)
                _verticalVelocity = -1f;
            _verticalVelocity += gravity * deltaTime;
            var motion = planarVelocity + Vector3.up * _verticalVelocity;
            _cc.Move(motion * deltaTime);
        }

        static Vector3 SnapToGround(Vector3 near, float fallbackY)
        {
            var t = Terrain.activeTerrain;
            if (t != null && t.terrainData != null)
            {
                var o = t.transform.position;
                var td = t.terrainData;
                if (near.x >= o.x && near.x <= o.x + td.size.x &&
                    near.z >= o.z && near.z <= o.z + td.size.z)
                {
                    var y = t.SampleHeight(new Vector3(near.x, o.y, near.z)) + o.y;
                    return new Vector3(near.x, y, near.z);
                }
            }

            var origin = new Vector3(near.x, near.y + 50f, near.z);
            if (Physics.Raycast(origin, Vector3.down, out var hit, 150f, ~0, QueryTriggerInteraction.Ignore))
                return hit.point;
            return new Vector3(near.x, fallbackY, near.z);
        }
    }
}
