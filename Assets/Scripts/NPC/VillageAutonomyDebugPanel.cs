using System;
using System.Collections.Generic;
using Rpg.Dialogue;
using UnityEngine;

namespace Rpg.Npc
{
    [DefaultExecutionOrder(19)]
    public sealed class VillageAutonomyDebugPanel : MonoBehaviour
    {
        [SerializeField] bool debugPanelEnabled = true;
        [SerializeField] bool developmentBuildsOnly = true;
        [SerializeField] bool startVisible = false;
        [SerializeField] KeyCode toggleKey = KeyCode.F8;
        [SerializeField] float width = 520f;
        [SerializeField] float maxHeight = 420f;
        [SerializeField] Vector2 anchorOffset = new Vector2(16f, 16f);

        VillageAgentSimulation _simulation;
        Vector2 _scroll;
        bool _visible;
        GUIStyle _titleStyle;
        GUIStyle _lineStyle;
        string _lastActionMessage = string.Empty;
        float _lastActionAt;

        void Awake()
        {
            _visible = startVisible;
        }

        public void Configure(VillageAgentSimulation simulation)
        {
            _simulation = simulation;
        }

        void Update()
        {
            if (!debugPanelEnabled || !IsBuildSupported())
                return;
            if (Input.GetKeyDown(toggleKey))
                _visible = !_visible;
        }

        void OnGUI()
        {
            if (!debugPanelEnabled || !_visible || !IsBuildSupported())
                return;

            var simulation = ResolveSimulation();
            if (simulation == null)
                return;

            EnsureStyles();
            var telemetry = simulation.TelemetrySnapshot;
            var rect = new Rect(
                Screen.width - Mathf.Max(240f, width) - anchorOffset.x,
                anchorOffset.y,
                Mathf.Max(240f, width),
                Mathf.Clamp(maxHeight, 160f, Screen.height - (anchorOffset.y * 2f)));

            GUILayout.BeginArea(rect, GUI.skin.box);
            GUILayout.Label("Village Autonomy Debug", _titleStyle);
            GUILayout.Label(
                $"deliberations={telemetry.DeliberationCalls}, fallback={telemetry.FallbackCalls} ({telemetry.FallbackRate:P1}), plans ok={telemetry.PlanCompletionsSucceeded}, plans failed={telemetry.PlanCompletionsFailed}",
                _lineStyle);
            GUILayout.Label($"villagers={simulation.States.Count}  (toggle: {toggleKey})", _lineStyle);
            DrawGlobalControls(simulation);
            if (!string.IsNullOrWhiteSpace(_lastActionMessage) && Time.time - _lastActionAt < 6f)
                GUILayout.Label($"last action: {_lastActionMessage}", _lineStyle);
            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.ExpandHeight(true));

            foreach (var kvp in simulation.States)
            {
                var state = kvp.Value;
                if (state == null)
                    continue;
                DrawVillagerRow(state, simulation);
                GUILayout.Space(8f);
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        void DrawVillagerRow(VillageAgentSimulation.VillagerRuntimeState state, VillageAgentSimulation simulation)
        {
            var npcId = string.IsNullOrWhiteSpace(state.NpcId) ? "(unknown villager)" : state.NpcId;
            GUILayout.Label(npcId, _titleStyle);
            var persona = state.Binding != null ? state.Binding.Persona : null;
            GUILayout.Label(VillageAutonomyDebugFormatter.BuildPersonaSummary(persona), _lineStyle);
            GUILayout.Label(VillageAutonomyDebugFormatter.BuildGoalAndPlanLine(state), _lineStyle);

            var agreements = ResolveAgreementSummary(npcId);
            GUILayout.Label(VillageAutonomyDebugFormatter.BuildAgreementStatusLine(agreements), _lineStyle);

            var opinion = simulation.OpinionService != null
                ? simulation.OpinionService.GetSummary(npcId)
                : default;
            GUILayout.Label(VillageAutonomyDebugFormatter.BuildOpinionSnapshotLine(opinion), _lineStyle);
            if (!string.IsNullOrWhiteSpace(state.LastError))
                GUILayout.Label($"last_error: {state.LastError}", _lineStyle);

            var peerNpcId = ResolvePeerNpcId(simulation, npcId);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Redeliberate", GUILayout.Height(22f)))
            {
                simulation.RequestRedeliberation(npcId, "debug_manual");
                SetActionMessage($"queued redeliberation for {npcId}");
            }

            if (GUILayout.Button("Hero +", GUILayout.Height(22f)))
            {
                if (simulation.TryDebugApplyHeroImpact(npcId, 12f, 6f, 0f, 0f, 4f))
                    SetActionMessage($"applied positive hero impact to {npcId}");
            }

            if (GUILayout.Button("Hero -", GUILayout.Height(22f)))
            {
                if (simulation.TryDebugApplyHeroImpact(npcId, -12f, -6f, 0f, 0f, -4f))
                    SetActionMessage($"applied negative hero impact to {npcId}");
            }

            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Force Chat", GUILayout.Height(22f)))
            {
                if (!string.IsNullOrWhiteSpace(peerNpcId) && simulation.TryDebugForceChat(npcId, peerNpcId, 2f))
                    SetActionMessage($"forced chat between {npcId} and {peerNpcId}");
            }

            if (GUILayout.Button("Queue Gossip", GUILayout.Height(22f)))
            {
                if (!string.IsNullOrWhiteSpace(peerNpcId) && simulation.TryDebugQueueGossip(npcId, peerNpcId))
                    SetActionMessage($"queued gossip between {npcId} and {peerNpcId}");
            }

            GUILayout.EndHorizontal();
        }

        void DrawGlobalControls(VillageAgentSimulation simulation)
        {
            GUILayout.Space(4f);
            GUILayout.Label("debug mode controls", _lineStyle);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Redeliberate All", GUILayout.Height(22f)))
            {
                simulation.RequestGlobalRedeliberation("debug_manual");
                SetActionMessage("queued global redeliberation");
            }

            if (GUILayout.Button("Process Gossip x4", GUILayout.Height(22f)))
            {
                var processed = simulation.OpinionService != null ? simulation.OpinionService.ProcessGossip(4) : 0;
                SetActionMessage($"processed gossip interactions: {processed}");
            }

            GUILayout.EndHorizontal();

            var asks = simulation.DebugSnapshotGroupAsks();
            for (var i = 0; i < asks.Count; i++)
            {
                var ask = asks[i];
                if (ask == null || !string.Equals(ask.state, "offered", StringComparison.OrdinalIgnoreCase))
                    continue;

                GUILayout.Label(VillageAutonomyDebugFormatter.BuildGroupAskLine(ask), _lineStyle);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Accept Ask", GUILayout.Height(22f)))
                {
                    if (simulation.TryDebugRespondToGroupAsk(ask.askId, true, "debug_tester", out var signals))
                        SetActionMessage($"accepted ask {ask.askId}; signals={signals.Count}");
                }

                if (GUILayout.Button("Decline Ask", GUILayout.Height(22f)))
                {
                    if (simulation.TryDebugRespondToGroupAsk(ask.askId, false, "debug_tester", out var signals))
                        SetActionMessage($"declined ask {ask.askId}; signals={signals.Count}");
                }

                GUILayout.EndHorizontal();
            }
        }

        static string ResolvePeerNpcId(VillageAgentSimulation simulation, string npcId)
        {
            if (simulation == null || simulation.States == null || string.IsNullOrWhiteSpace(npcId))
                return null;

            string fallback = null;
            foreach (var key in simulation.States.Keys)
            {
                if (string.IsNullOrWhiteSpace(key) || string.Equals(key, npcId, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (fallback == null)
                    fallback = key;
                if (string.Compare(key, npcId, StringComparison.OrdinalIgnoreCase) > 0)
                    return key;
            }

            return fallback;
        }

        void SetActionMessage(string message)
        {
            _lastActionMessage = string.IsNullOrWhiteSpace(message) ? string.Empty : message.Trim();
            _lastActionAt = Time.time;
        }

        IReadOnlyList<string> ResolveAgreementSummary(string npcId)
        {
            var manager = DialogueManager.Instance;
            if (manager == null || manager.Agreements == null || string.IsNullOrWhiteSpace(npcId))
                return null;
            return manager.Agreements.BuildActiveAgreementSummariesForNpc(npcId);
        }

        VillageAgentSimulation ResolveSimulation()
        {
            if (_simulation != null)
                return _simulation;
            _simulation = GetComponent<VillageAgentSimulation>();
            if (_simulation != null)
                return _simulation;
            _simulation = FindFirstObjectByType<VillageAgentSimulation>();
            return _simulation;
        }

        bool IsBuildSupported()
        {
            if (!developmentBuildsOnly)
                return true;
            return Application.isEditor || Debug.isDebugBuild;
        }

        void EnsureStyles()
        {
            if (_titleStyle != null && _lineStyle != null)
                return;
            _titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontStyle = FontStyle.Bold,
                wordWrap = true
            };
            _lineStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                wordWrap = true
            };
        }
    }
}
