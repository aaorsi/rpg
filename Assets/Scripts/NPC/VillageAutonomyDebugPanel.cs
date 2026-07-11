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
        [SerializeField] float width = 720f;
        [SerializeField] float maxHeight = 920f;
        [SerializeField] Vector2 anchorOffset = new Vector2(16f, 16f);

        VillageAgentSimulation _simulation;
        Vector2 _scroll;
        Vector2 _villagerScroll;
        bool _visible;
        GUIStyle _titleStyle;
        GUIStyle _lineStyle;
        GUIStyle _helpStyle;
        string _selectedNpcId = string.Empty;
        string _targetNpcId = string.Empty;
        string _selectedInteractionId = "romantic_relationship";
        string _statusLine = string.Empty;
        string _gossipTargetNpcId = string.Empty;
        string _forceChatTargetNpcId = string.Empty;
        string _groupAskId = "ask_run_for_mayor";
        string _groupAskResponderNpcId = string.Empty;
        string _groupAudienceNpcId = string.Empty;
        float _heroOpinionDelta = 10f;
        float _heroLeadershipDelta = 5f;
        bool _showNpcDropdownA;
        bool _showNpcDropdownB;
        bool _showInteractionDropdown;
        bool _showGroupAudienceDropdown;
        bool _useGroupInteractionStart;
        Vector2 _npcDropdownScrollA;
        Vector2 _npcDropdownScrollB;
        Vector2 _groupAudienceScroll;
        bool _showWorldCatalog = true;
        bool _showAtomicActions = true;
        string _catalogFilter = string.Empty;
        string _selectedLocationId = string.Empty;
        string _selectedItemId = string.Empty;
        string _itemExchangeMode = "transfer";
        int _itemExchangeQty = 1;
        int _coinTransferAmount = 5;
        string _coinTransferMode = "transfer";
        bool _showLocationDropdown;
        bool _showItemDropdown;
        Vector2 _catalogScroll;
        Vector2 _locationDropdownScroll;
        Vector2 _itemDropdownScroll;
        readonly List<VillageAgentSimulation.DebugLocationEntry> _locationOptions = new List<VillageAgentSimulation.DebugLocationEntry>();
        readonly List<VillageAgentSimulation.DebugItemEntry> _itemOptions = new List<VillageAgentSimulation.DebugItemEntry>();
        readonly List<string> _inventoryLines = new List<string>();

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
            simulation.BuildDebugNpcEntryList(_npcOptions);
            var telemetry = simulation.TelemetrySnapshot;
            var rect = new Rect(
                Screen.width - Mathf.Max(280f, width) - anchorOffset.x,
                anchorOffset.y,
                Mathf.Max(280f, width),
                Mathf.Clamp(maxHeight, 200f, Screen.height - (anchorOffset.y * 2f)));

            GUILayout.BeginArea(rect, GUI.skin.box);
            _scroll = GUILayout.BeginScrollView(_scroll);
            GUILayout.Label("Village Autonomy Debug", _titleStyle);
            GUILayout.Label(
                $"deliberations={telemetry.DeliberationCalls}, fallback={telemetry.FallbackCalls} ({telemetry.FallbackRate:P1}), plans ok={telemetry.PlanCompletionsSucceeded}, plans failed={telemetry.PlanCompletionsFailed}",
                _lineStyle);
            GUILayout.Label($"npcs={_npcOptions.Count}  (toggle: {toggleKey})", _lineStyle);
            DrawWorldCatalogSection(simulation);
            DrawPrimaryNpcSelectors(simulation);
            DrawAtomicActionControls(simulation);
            DrawInteractionControls(simulation);
            DrawPerNpcControls(simulation);
            DrawInteractionOverview(simulation);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        void DrawWorldCatalogSection(VillageAgentSimulation simulation)
        {
            _showWorldCatalog = GUILayout.Toggle(_showWorldCatalog, "World catalogs", _titleStyle);
            if (!_showWorldCatalog)
                return;

            GUILayout.Label("Filter lists by id or display name.", _helpStyle);
            _catalogFilter = GUILayout.TextField(_catalogFilter ?? string.Empty, GUILayout.MinWidth(240f));

            simulation.BuildDebugLocationEntryList(_locationOptions);
            simulation.BuildDebugItemEntryList(_itemOptions);
            EnsureLocationSelection(_locationOptions);
            EnsureItemSelection(_itemOptions);

            _catalogScroll = GUILayout.BeginScrollView(_catalogScroll, GUILayout.Height(160f));
            GUILayout.Label($"NPCs ({CountFilteredNpcEntries()})", _lineStyle);
            for (var i = 0; i < _npcOptions.Count; i++)
            {
                var entry = _npcOptions[i];
                if (!MatchesCatalogFilter(entry.NpcId, entry.DisplayName))
                    continue;
                GUILayout.Label("  " + BuildNpcOptionLabel(entry), _lineStyle);
            }

            GUILayout.Space(4f);
            GUILayout.Label($"Locations ({CountFilteredLocationEntries()})", _lineStyle);
            for (var i = 0; i < _locationOptions.Count; i++)
            {
                var entry = _locationOptions[i];
                if (!MatchesCatalogFilter(entry.LocationId, entry.DisplayName, entry.SceneAnchorName))
                    continue;
                var binding = entry.HasSceneBinding ? "bound" : "unbound";
                GUILayout.Label($"  {entry.LocationId} · {entry.DisplayName} [{binding}]", _lineStyle);
            }

            GUILayout.Space(4f);
            GUILayout.Label($"Items ({CountFilteredItemEntries()})", _lineStyle);
            for (var i = 0; i < _itemOptions.Count; i++)
            {
                var entry = _itemOptions[i];
                if (!MatchesCatalogFilter(entry.ItemId, entry.DisplayName))
                    continue;
                GUILayout.Label($"  {entry.ItemId} · {entry.DisplayName}", _lineStyle);
            }
            GUILayout.EndScrollView();

            if (!string.IsNullOrWhiteSpace(_selectedNpcId))
            {
                simulation.BuildDebugInventoryLinesForActor(_selectedNpcId, _inventoryLines);
                GUILayout.Label("Initiator inventory", _lineStyle);
                for (var i = 0; i < _inventoryLines.Count; i++)
                    GUILayout.Label("  " + _inventoryLines[i], _lineStyle);
            }

            GUILayout.Space(6f);
        }

        void DrawPrimaryNpcSelectors(VillageAgentSimulation simulation)
        {
            EnsureNpcSelections(_npcOptions);
            GUILayout.Label("Participants", _titleStyle);
            DrawNpcDropdownRow(_npcOptions, "Initiator", ref _selectedNpcId, ref _showNpcDropdownA, ref _npcDropdownScrollA);
            DrawNpcDropdownRow(_npcOptions, "Other", ref _targetNpcId, ref _showNpcDropdownB, ref _npcDropdownScrollB);
            GUILayout.Space(4f);
        }

        void DrawAtomicActionControls(VillageAgentSimulation simulation)
        {
            _showAtomicActions = GUILayout.Toggle(_showAtomicActions, "Atomic action tests", _titleStyle);
            if (!_showAtomicActions)
                return;

            GUILayout.Label(
                "Test interaction building blocks with initiator/other above. Exchange actions require actors in range.",
                _helpStyle);

            simulation.BuildDebugLocationEntryList(_locationOptions);
            simulation.BuildDebugItemEntryList(_itemOptions);
            EnsureLocationSelection(_locationOptions);
            EnsureItemSelection(_itemOptions);

            DrawLocationDropdownRow(_locationOptions);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Move initiator → location", GUILayout.Width(170f)))
            {
                if (simulation.TryMoveNpcToLocationForDebug(_selectedNpcId, _selectedLocationId, out var msg))
                    _statusLine = msg;
                else
                    _statusLine = "move_to_location failed: " + msg;
            }

            if (GUILayout.Button("Move initiator → other", GUILayout.Width(150f)))
            {
                if (simulation.TryMoveNpcToNpcForDebug(_selectedNpcId, _targetNpcId, out var msg))
                    _statusLine = msg;
                else
                    _statusLine = "move_to_npc failed: " + msg;
            }

            if (GUILayout.Button("Move initiator → hero", GUILayout.Width(150f)))
            {
                if (simulation.TryMoveNpcToHeroForDebug(_selectedNpcId, out var msg))
                    _statusLine = msg;
                else
                    _statusLine = "move_to_hero failed: " + msg;
            }
            GUILayout.EndHorizontal();

            DrawItemDropdownRow(_itemOptions);
            GUILayout.BeginHorizontal();
            GUILayout.Label("mode", GUILayout.Width(36f));
            _itemExchangeMode = GUILayout.TextField(_itemExchangeMode ?? "transfer", GUILayout.Width(120f));
            GUILayout.Label("qty", GUILayout.Width(24f));
            var qtyText = GUILayout.TextField(_itemExchangeQty.ToString(), GUILayout.Width(36f));
            if (int.TryParse(qtyText, out var parsedQty))
                _itemExchangeQty = Mathf.Max(1, parsedQty);
            if (GUILayout.Button("Exchange item (from→other)", GUILayout.Width(180f)))
            {
                if (simulation.TryExchangeItemForDebug(
                        _selectedNpcId,
                        _targetNpcId,
                        _itemExchangeMode,
                        _selectedItemId,
                        _itemExchangeQty,
                        out var msg))
                {
                    _statusLine = msg;
                }
                else
                {
                    _statusLine = "exchange_item failed: " + msg;
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("coins", GUILayout.Width(36f));
            var coinText = GUILayout.TextField(_coinTransferAmount.ToString(), GUILayout.Width(48f));
            if (int.TryParse(coinText, out var parsedCoins))
                _coinTransferAmount = Mathf.Max(1, parsedCoins);
            _coinTransferMode = GUILayout.TextField(_coinTransferMode ?? "transfer", GUILayout.Width(100f));
            if (GUILayout.Button("Exchange coins (initiator→other)", GUILayout.Width(210f)))
            {
                if (simulation.TryExchangeCoinsForDebug(
                        _selectedNpcId,
                        _targetNpcId,
                        _coinTransferAmount,
                        _coinTransferMode,
                        out var msg))
                {
                    _statusLine = msg;
                }
                else
                {
                    _statusLine = "exchange_coins failed: " + msg;
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(6f);
        }

        void DrawLocationDropdownRow(IReadOnlyList<VillageAgentSimulation.DebugLocationEntry> entries)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Location", GUILayout.Width(70f));
            var display = string.IsNullOrWhiteSpace(_selectedLocationId) ? "(none)" : _selectedLocationId;
            var selected = FindLocationEntry(entries, _selectedLocationId);
            if (selected.HasValue && !string.IsNullOrWhiteSpace(selected.Value.DisplayName))
                display = $"{selected.Value.LocationId} · {selected.Value.DisplayName}";
            if (GUILayout.Button(display, GUILayout.MinWidth(200f), GUILayout.Height(22f)))
                _showLocationDropdown = !_showLocationDropdown;
            GUILayout.EndHorizontal();

            if (!_showLocationDropdown || entries == null || entries.Count == 0)
                return;

            GUILayout.BeginHorizontal();
            GUILayout.Space(76f);
            GUILayout.BeginVertical(GUI.skin.box);
            _locationDropdownScroll = GUILayout.BeginScrollView(
                _locationDropdownScroll,
                GUILayout.Height(Mathf.Min(160f, entries.Count * 22f + 8f)));
            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (string.IsNullOrWhiteSpace(entry.LocationId))
                    continue;
                var label = string.IsNullOrWhiteSpace(entry.DisplayName)
                    ? entry.LocationId
                    : $"{entry.LocationId} · {entry.DisplayName}";
                if (GUILayout.Button(label, GUILayout.MinWidth(260f), GUILayout.Height(20f)))
                {
                    _selectedLocationId = entry.LocationId;
                    _showLocationDropdown = false;
                }
            }
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
        }

        void DrawItemDropdownRow(IReadOnlyList<VillageAgentSimulation.DebugItemEntry> entries)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Item", GUILayout.Width(70f));
            var display = string.IsNullOrWhiteSpace(_selectedItemId) ? "(none)" : _selectedItemId;
            var selected = FindItemEntry(entries, _selectedItemId);
            if (selected.HasValue && !string.IsNullOrWhiteSpace(selected.Value.DisplayName))
                display = $"{selected.Value.ItemId} · {selected.Value.DisplayName}";
            if (GUILayout.Button(display, GUILayout.MinWidth(200f), GUILayout.Height(22f)))
                _showItemDropdown = !_showItemDropdown;
            GUILayout.EndHorizontal();

            if (!_showItemDropdown || entries == null || entries.Count == 0)
                return;

            GUILayout.BeginHorizontal();
            GUILayout.Space(76f);
            GUILayout.BeginVertical(GUI.skin.box);
            _itemDropdownScroll = GUILayout.BeginScrollView(
                _itemDropdownScroll,
                GUILayout.Height(Mathf.Min(160f, entries.Count * 22f + 8f)));
            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (string.IsNullOrWhiteSpace(entry.ItemId))
                    continue;
                var label = string.IsNullOrWhiteSpace(entry.DisplayName)
                    ? entry.ItemId
                    : $"{entry.ItemId} · {entry.DisplayName}";
                if (GUILayout.Button(label, GUILayout.MinWidth(260f), GUILayout.Height(20f)))
                {
                    _selectedItemId = entry.ItemId;
                    _showItemDropdown = false;
                }
            }
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
        }

        void EnsureLocationSelection(IReadOnlyList<VillageAgentSimulation.DebugLocationEntry> entries)
        {
            if (entries == null || entries.Count == 0)
                return;
            if (!string.IsNullOrWhiteSpace(_selectedLocationId))
            {
                for (var i = 0; i < entries.Count; i++)
                {
                    if (string.Equals(entries[i].LocationId, _selectedLocationId, StringComparison.OrdinalIgnoreCase))
                        return;
                }
            }

            _selectedLocationId = entries[0].LocationId;
        }

        void EnsureItemSelection(IReadOnlyList<VillageAgentSimulation.DebugItemEntry> entries)
        {
            if (entries == null || entries.Count == 0)
                return;
            if (!string.IsNullOrWhiteSpace(_selectedItemId))
            {
                for (var i = 0; i < entries.Count; i++)
                {
                    if (string.Equals(entries[i].ItemId, _selectedItemId, StringComparison.OrdinalIgnoreCase))
                        return;
                }
            }

            _selectedItemId = entries[0].ItemId;
        }

        bool MatchesCatalogFilter(params string[] values)
        {
            if (string.IsNullOrWhiteSpace(_catalogFilter))
                return true;
            var filter = _catalogFilter.Trim();
            if (values == null)
                return false;
            for (var i = 0; i < values.Length; i++)
            {
                var value = values[i];
                if (string.IsNullOrWhiteSpace(value))
                    continue;
                if (value.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
        }

        int CountFilteredNpcEntries()
        {
            var count = 0;
            for (var i = 0; i < _npcOptions.Count; i++)
            {
                var entry = _npcOptions[i];
                if (MatchesCatalogFilter(entry.NpcId, entry.DisplayName))
                    count++;
            }

            return count;
        }

        int CountFilteredLocationEntries()
        {
            var count = 0;
            for (var i = 0; i < _locationOptions.Count; i++)
            {
                var entry = _locationOptions[i];
                if (MatchesCatalogFilter(entry.LocationId, entry.DisplayName, entry.SceneAnchorName))
                    count++;
            }

            return count;
        }

        int CountFilteredItemEntries()
        {
            var count = 0;
            for (var i = 0; i < _itemOptions.Count; i++)
            {
                var entry = _itemOptions[i];
                if (MatchesCatalogFilter(entry.ItemId, entry.DisplayName))
                    count++;
            }

            return count;
        }

        static VillageAgentSimulation.DebugLocationEntry? FindLocationEntry(
            IReadOnlyList<VillageAgentSimulation.DebugLocationEntry> entries,
            string locationId)
        {
            if (entries == null || string.IsNullOrWhiteSpace(locationId))
                return null;
            for (var i = 0; i < entries.Count; i++)
            {
                if (string.Equals(entries[i].LocationId, locationId, StringComparison.OrdinalIgnoreCase))
                    return entries[i];
            }

            return null;
        }

        static VillageAgentSimulation.DebugItemEntry? FindItemEntry(
            IReadOnlyList<VillageAgentSimulation.DebugItemEntry> entries,
            string itemId)
        {
            if (entries == null || string.IsNullOrWhiteSpace(itemId))
                return null;
            for (var i = 0; i < entries.Count; i++)
            {
                if (string.Equals(entries[i].ItemId, itemId, StringComparison.OrdinalIgnoreCase))
                    return entries[i];
            }

            return null;
        }

        void DrawInteractionControls(VillageAgentSimulation simulation)
        {
            EnsureNpcSelections(_npcOptions);
            EnsureInteractionSelection(simulation);
            GUILayout.Space(6f);
            GUILayout.Label("Start an interaction", _titleStyle);
            GUILayout.Label(
                "Press E near a running scene to join (hero-join types only).",
                _helpStyle);
            DrawInteractionDropdownRow(simulation);
            _useGroupInteractionStart = GUILayout.Toggle(_useGroupInteractionStart, "Group start (convener + extra audience)");
            if (_useGroupInteractionStart)
                DrawNpcDropdownRow(_npcOptions, "Audience+", ref _groupAudienceNpcId, ref _showGroupAudienceDropdown, ref _groupAudienceScroll);

            var preview = simulation.PreviewNextInteractionStage(_selectedInteractionId, _selectedNpcId, _targetNpcId);
            GUILayout.Label("next stage: " + preview, _helpStyle);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Start interaction", GUILayout.Width(140f)))
            {
                if (TryStartSelectedInteraction(simulation, out var message))
                    _statusLine = message;
            }

            if (GUILayout.Button("Re-deliberate all", GUILayout.Width(120f)))
            {
                simulation.RequestGlobalRedeliberation("debug_panel_global");
                _statusLine = "requested global re-deliberation";
            }
            GUILayout.EndHorizontal();

            GUILayout.Label(
                "Initiator walks to the other participant before dialogue. Coin balances shown below.",
                _helpStyle);
            if (!string.IsNullOrWhiteSpace(_selectedNpcId) || !string.IsNullOrWhiteSpace(_targetNpcId))
            {
                var initiatorCoins = simulation.GetActorCoinBalance(_selectedNpcId);
                var otherCoins = simulation.GetActorCoinBalance(_targetNpcId);
                GUILayout.Label($"coins — initiator: {initiatorCoins} | other: {otherCoins}", _lineStyle);
            }

            if (!string.IsNullOrWhiteSpace(_statusLine))
                GUILayout.Label("status: " + _statusLine, _lineStyle);
            GUILayout.Space(6f);
        }

        void DrawPerNpcControls(VillageAgentSimulation simulation)
        {
            EnsureNpcSelections(_npcOptions);
            if (string.IsNullOrWhiteSpace(_selectedNpcId))
                return;

            GUILayout.Label("Per-villager debug", _titleStyle);
            if (!IsHeroId(_selectedNpcId))
            {
                var villagerLines = simulation.BuildVillagerDebugLines(_selectedNpcId);
                for (var i = 0; i < villagerLines.Count; i++)
                    GUILayout.Label(villagerLines[i], _lineStyle);
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Re-deliberate initiator", GUILayout.Width(160f)))
            {
                if (IsHeroId(_selectedNpcId))
                    _statusLine = "hero cannot deliberate";
                else
                {
                    simulation.RequestRedeliberation(_selectedNpcId, "debug_panel_single");
                    _statusLine = "requested re-deliberation for " + _selectedNpcId;
                }
            }

            if (GUILayout.Button("Force chat → target", GUILayout.Width(130f)))
            {
                var chatTarget = string.IsNullOrWhiteSpace(_forceChatTargetNpcId) ? _targetNpcId : _forceChatTargetNpcId;
                if (simulation.TryForceChatPlanForDebug(_selectedNpcId, chatTarget, 3f, out var err))
                    _statusLine = $"force chat {_selectedNpcId} → {chatTarget}";
                else
                    _statusLine = "force chat failed: " + err;
            }
            GUILayout.EndHorizontal();

            _forceChatTargetNpcId = GUILayout.TextField(_forceChatTargetNpcId ?? string.Empty, GUILayout.Width(220f));

            if (!IsHeroId(_selectedNpcId))
            {
                GUILayout.Label("Hero impact on initiator", _lineStyle);
                _heroOpinionDelta = LabeledSlider("opinion", _heroOpinionDelta, -50f, 50f);
                _heroLeadershipDelta = LabeledSlider("leadership", _heroLeadershipDelta, -50f, 50f);
                if (GUILayout.Button("Apply hero impact", GUILayout.Width(140f)))
                {
                    if (simulation.TryApplyHeroImpactForDebug(
                            _selectedNpcId,
                            _heroOpinionDelta,
                            _heroLeadershipDelta,
                            0f,
                            0f,
                            0f,
                            out var err))
                    {
                        _statusLine = "applied hero impact to " + _selectedNpcId;
                    }
                    else
                    {
                        _statusLine = "hero impact failed: " + err;
                    }
                }
            }

            GUILayout.BeginHorizontal();
            _gossipTargetNpcId = GUILayout.TextField(
                string.IsNullOrWhiteSpace(_gossipTargetNpcId) ? _targetNpcId : _gossipTargetNpcId,
                GUILayout.Width(160f));
            if (GUILayout.Button("Queue gossip", GUILayout.Width(100f)))
            {
                var gossipB = string.IsNullOrWhiteSpace(_gossipTargetNpcId) ? _targetNpcId : _gossipTargetNpcId;
                if (simulation.TryQueueGossipForDebug(_selectedNpcId, gossipB, out var err))
                    _statusLine = $"gossip queued {_selectedNpcId} ↔ {gossipB}";
                else
                    _statusLine = "gossip queue failed: " + err;
            }

            if (GUILayout.Button("Process gossip", GUILayout.Width(110f)))
            {
                var n = simulation.ProcessGossipForDebug(2);
                _statusLine = $"processed {n} gossip event(s)";
            }
            GUILayout.EndHorizontal();

            DrawGroupAskControls(simulation);
            GUILayout.Space(6f);
        }

        void DrawGroupAskControls(VillageAgentSimulation simulation)
        {
            var openAsks = simulation.SnapshotOpenGroupAskIds();
            GUILayout.Label("Group ask", _lineStyle);
            if (openAsks.Count > 0 && string.IsNullOrWhiteSpace(_groupAskId))
                _groupAskId = openAsks[0];
            _groupAskId = GUILayout.TextField(_groupAskId ?? string.Empty, GUILayout.Width(220f));
            _groupAskResponderNpcId = string.IsNullOrWhiteSpace(_groupAskResponderNpcId)
                ? _selectedNpcId
                : _groupAskResponderNpcId;
            _groupAskResponderNpcId = GUILayout.TextField(_groupAskResponderNpcId ?? string.Empty, GUILayout.Width(220f));
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Accept ask", GUILayout.Width(90f)))
            {
                if (simulation.TryRespondToGroupAskForDebug(_groupAskId, true, _groupAskResponderNpcId, out var signals, out var err))
                    _statusLine = "ask accepted: " + string.Join(", ", signals);
                else
                    _statusLine = "ask accept failed: " + err;
            }

            if (GUILayout.Button("Reject ask", GUILayout.Width(90f)))
            {
                if (simulation.TryRespondToGroupAskForDebug(_groupAskId, false, _groupAskResponderNpcId, out _, out var err))
                    _statusLine = "ask rejected";
                else
                    _statusLine = "ask reject failed: " + err;
            }
            GUILayout.EndHorizontal();
        }

        static float LabeledSlider(string label, float value, float min, float max)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(72f));
            value = GUILayout.HorizontalSlider(value, min, max, GUILayout.Width(120f));
            GUILayout.Label(value.ToString("F0"), GUILayout.Width(36f));
            GUILayout.EndHorizontal();
            return value;
        }

        bool TryStartSelectedInteraction(VillageAgentSimulation simulation, out string message)
        {
            message = string.Empty;
            if (simulation == null)
            {
                message = "simulation unavailable";
                return false;
            }

            if (_useGroupInteractionStart)
            {
                var audience = new List<string>();
                if (!string.IsNullOrWhiteSpace(_targetNpcId))
                    audience.Add(_targetNpcId);
                if (!string.IsNullOrWhiteSpace(_groupAudienceNpcId)
                    && !string.Equals(_groupAudienceNpcId, _targetNpcId, StringComparison.OrdinalIgnoreCase))
                {
                    audience.Add(_groupAudienceNpcId);
                }

                if (simulation.TryStartGroupInteractionForDebug(_selectedInteractionId, _selectedNpcId, audience, out var groupError))
                {
                    message = $"started group {_selectedInteractionId}: {_selectedNpcId} + {audience.Count} audience";
                    return true;
                }

                message = $"group start failed: {groupError}";
                return false;
            }

            if (simulation.TryStartInteractionForDebug(_selectedInteractionId, _selectedNpcId, _targetNpcId, out var pairError))
            {
                message = $"started {_selectedInteractionId}: {_selectedNpcId} with {_targetNpcId}";
                return true;
            }

            message = $"start failed: {pairError}";
            return false;
        }

        void DrawInteractionOverview(VillageAgentSimulation simulation)
        {
            var active = simulation.ActiveInteractions;
            var visibleCount = 0;
            if (active != null)
            {
                for (var i = 0; i < active.Count; i++)
                {
                    var entry = active[i];
                    if (entry != null)
                        visibleCount++;
                }
            }

            GUILayout.Space(4f);
            GUILayout.Label($"Interactions: {visibleCount}", _titleStyle);
            if (active == null || active.Count == 0)
            {
                GUILayout.Label("No interaction instances yet.", _lineStyle);
                return;
            }

            _villagerScroll = GUILayout.BeginScrollView(_villagerScroll, GUILayout.Height(280f));
            for (var i = 0; i < active.Count; i++)
            {
                var entry = active[i];
                if (entry == null)
                    continue;

                GUILayout.Label($"— {simulation.FormatInteractionTypeForDebug(entry)} [{entry.status}]", _titleStyle);
                var lines = simulation.BuildInteractionDebugLines(entry);
                for (var j = 0; j < lines.Count; j++)
                    GUILayout.Label(lines[j], _lineStyle);

                if (entry.status == InteractionRuntimeStatus.Running)
                {
                    var nextStepIn = Mathf.Max(0f, entry.nextStepAtTime - Time.time).ToString("F1") + "s";
                    var nextStage = simulation.FormatInteractionNextStageForDebug(entry);
                    GUILayout.Label($"next: {nextStage} in {nextStepIn} | paused={entry.pausedByHero} | awaitingDialogue={entry.awaitingDialogueStep}", _lineStyle);
                }

                GUILayout.Space(6f);
            }
            GUILayout.EndScrollView();
        }

        void DrawInteractionDropdownRow(VillageAgentSimulation simulation)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Type", GUILayout.Width(70f));
            var display = string.IsNullOrWhiteSpace(_selectedInteractionId) ? "(none)" : _selectedInteractionId;
            if (GUILayout.Button(display, GUILayout.MinWidth(220f), GUILayout.Height(22f)))
                _showInteractionDropdown = !_showInteractionDropdown;
            GUILayout.EndHorizontal();

            if (!_showInteractionDropdown)
                return;
            var defs = simulation != null ? simulation.InteractionDefinitions : null;
            var interactions = defs != null ? defs.interactions : null;
            if (interactions == null || interactions.Count == 0)
            {
                GUILayout.Label("No interactions available.", _lineStyle);
                return;
            }

            GUILayout.BeginHorizontal();
            GUILayout.Space(74f);
            GUILayout.BeginVertical(GUI.skin.box);
            for (var i = 0; i < interactions.Count; i++)
            {
                var interaction = interactions[i];
                if (interaction == null || string.IsNullOrWhiteSpace(interaction.id))
                    continue;
                var label = interaction.id;
                if (!string.IsNullOrWhiteSpace(interaction.displayName))
                    label = interaction.displayName.Trim() + " (" + interaction.id + ")";
                if (!string.IsNullOrWhiteSpace(interaction.status))
                    label += $" [{interaction.status}]";
                if (!interaction.spawnFromChat)
                    label += " [manual]";
                if (GUILayout.Button(label, GUILayout.MinWidth(260f), GUILayout.Height(20f)))
                {
                    _selectedInteractionId = interaction.id.Trim();
                    _showInteractionDropdown = false;
                }
            }
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
        }

        void EnsureNpcSelections(IReadOnlyList<VillageAgentSimulation.DebugNpcEntry> entries)
        {
            if (entries == null || entries.Count == 0)
                return;

            if (string.IsNullOrWhiteSpace(_selectedNpcId) || !ContainsNpcId(entries, _selectedNpcId))
                _selectedNpcId = entries[0].NpcId;

            if (string.IsNullOrWhiteSpace(_targetNpcId) || !ContainsNpcId(entries, _targetNpcId))
            {
                for (var i = 0; i < entries.Count; i++)
                {
                    if (!string.Equals(entries[i].NpcId, _selectedNpcId, StringComparison.OrdinalIgnoreCase))
                    {
                        _targetNpcId = entries[i].NpcId;
                        break;
                    }
                }
            }
        }

        static bool IsHeroId(string npcId) =>
            string.Equals(npcId?.Trim(), InventoryService.HeroActorId, StringComparison.OrdinalIgnoreCase);

        static bool ContainsNpcId(IReadOnlyList<VillageAgentSimulation.DebugNpcEntry> entries, string npcId)
        {
            if (entries == null || string.IsNullOrWhiteSpace(npcId))
                return false;
            for (var i = 0; i < entries.Count; i++)
            {
                if (string.Equals(entries[i].NpcId, npcId, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        void EnsureInteractionSelection(VillageAgentSimulation simulation)
        {
            var defs = simulation != null ? simulation.InteractionDefinitions : null;
            var interactions = defs != null ? defs.interactions : null;
            if (interactions == null || interactions.Count == 0)
                return;
            if (!string.IsNullOrWhiteSpace(_selectedInteractionId))
            {
                for (var i = 0; i < interactions.Count; i++)
                {
                    var item = interactions[i];
                    if (item == null || string.IsNullOrWhiteSpace(item.id))
                        continue;
                    if (string.Equals(item.id, _selectedInteractionId, StringComparison.OrdinalIgnoreCase))
                        return;
                }
            }

            for (var i = 0; i < interactions.Count; i++)
            {
                var item = interactions[i];
                if (item == null || string.IsNullOrWhiteSpace(item.id))
                    continue;
                _selectedInteractionId = item.id.Trim();
                return;
            }
        }

        void DrawNpcDropdownRow(
            IReadOnlyList<VillageAgentSimulation.DebugNpcEntry> entries,
            string label,
            ref string selectedNpcId,
            ref bool showDropdown,
            ref Vector2 dropdownScroll)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(70f));
            var display = string.IsNullOrWhiteSpace(selectedNpcId) ? "(none)" : selectedNpcId;
            var selectedEntry = FindNpcEntry(entries, selectedNpcId);
            if (selectedEntry.HasValue)
                display = BuildNpcOptionLabel(selectedEntry.Value);
            if (GUILayout.Button(display, GUILayout.MinWidth(200f), GUILayout.Height(22f)))
                showDropdown = !showDropdown;
            GUILayout.EndHorizontal();

            if (!showDropdown || entries == null || entries.Count == 0)
                return;

            GUILayout.BeginHorizontal();
            GUILayout.Space(76f);
            GUILayout.BeginVertical(GUI.skin.box);
            dropdownScroll = GUILayout.BeginScrollView(dropdownScroll, GUILayout.Height(Mathf.Min(180f, entries.Count * 22f + 8f)));
            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (string.IsNullOrWhiteSpace(entry.NpcId))
                    continue;
                var option = BuildNpcOptionLabel(entry);
                if (GUILayout.Button(option, GUILayout.MinWidth(260f), GUILayout.Height(20f)))
                {
                    selectedNpcId = entry.NpcId;
                    showDropdown = false;
                }
            }
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
        }

        static VillageAgentSimulation.DebugNpcEntry? FindNpcEntry(
            IReadOnlyList<VillageAgentSimulation.DebugNpcEntry> entries,
            string npcId)
        {
            if (entries == null || string.IsNullOrWhiteSpace(npcId))
                return null;
            for (var i = 0; i < entries.Count; i++)
            {
                if (string.Equals(entries[i].NpcId, npcId, StringComparison.OrdinalIgnoreCase))
                    return entries[i];
            }

            return null;
        }

        static string BuildNpcOptionLabel(VillageAgentSimulation.DebugNpcEntry entry)
        {
            var npcId = string.IsNullOrWhiteSpace(entry.NpcId) ? "npc_unknown" : entry.NpcId.Trim();
            var displayName = string.IsNullOrWhiteSpace(entry.DisplayName) ? npcId : entry.DisplayName.Trim();
            var suffix = entry.IsHero ? " [hero]" : entry.IsSidekick ? " [sidekick]" : string.Empty;
            if (string.Equals(displayName, npcId, StringComparison.OrdinalIgnoreCase))
                return npcId + suffix;
            return $"{npcId} · {displayName}{suffix}";
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
            if (_titleStyle != null && _lineStyle != null && _helpStyle != null)
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
            _helpStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 10,
                wordWrap = true,
                fontStyle = FontStyle.Italic
            };
        }
    }
}
