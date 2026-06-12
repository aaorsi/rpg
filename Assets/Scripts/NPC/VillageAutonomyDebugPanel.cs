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
            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.ExpandHeight(true));

            foreach (var kvp in simulation.States)
            {
                var state = kvp.Value;
                if (state == null)
                    continue;
                DrawVillagerRow(state, simulation.OpinionService);
                GUILayout.Space(8f);
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        void DrawVillagerRow(VillageAgentSimulation.VillagerRuntimeState state, VillageOpinionService opinionService)
        {
            var npcId = string.IsNullOrWhiteSpace(state.NpcId) ? "(unknown villager)" : state.NpcId;
            GUILayout.Label(npcId, _titleStyle);
            var persona = state.Binding != null ? state.Binding.Persona : null;
            GUILayout.Label(VillageAutonomyDebugFormatter.BuildPersonaSummary(persona), _lineStyle);
            GUILayout.Label(VillageAutonomyDebugFormatter.BuildGoalAndPlanLine(state), _lineStyle);

            var agreements = ResolveAgreementSummary(npcId);
            GUILayout.Label(VillageAutonomyDebugFormatter.BuildAgreementStatusLine(agreements), _lineStyle);

            var opinion = opinionService != null
                ? opinionService.GetSummary(npcId)
                : default;
            GUILayout.Label(VillageAutonomyDebugFormatter.BuildOpinionSnapshotLine(opinion), _lineStyle);
            if (!string.IsNullOrWhiteSpace(state.LastError))
                GUILayout.Label($"last_error: {state.LastError}", _lineStyle);
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
