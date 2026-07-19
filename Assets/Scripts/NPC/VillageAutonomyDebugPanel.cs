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
        readonly List<VillageAgentSimulation.DebugLocationEntry> _locationOptions = new List<VillageAgentSimulation.DebugLocationEntry>();
        readonly List<VillageAgentSimulation.DebugItemEntry> _itemOptions = new List<VillageAgentSimulation.DebugItemEntry>();
        readonly List<string> _inventoryLines = new List<string>();
        readonly List<string> _proposedInteractionIds = new List<string>();
        string _interactionFilterNpc = string.Empty;
        string _interactionFilterType = string.Empty;
        string _proposedPromoteId = string.Empty;
        bool _showInvalidInteractionsOnly;
        readonly List<VillageAgentSimulation.DebugNpcEntry> _npcOptions = new List<VillageAgentSimulation.DebugNpcEntry>();
        Rect _windowRect;

        void Awake()
        {
            _visible = startVisible;
            _windowRect = BuildDefaultWindowRect();
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
            {
                _visible = !_visible;
                if (!_visible)
                    CloseAllDropdowns();
            }
        }

        void CloseAllDropdowns()
        {
            _showNpcDropdownA = false;
            _showNpcDropdownB = false;
            _showInteractionDropdown = false;
            _showGroupAudienceDropdown = false;
            _showLocationDropdown = false;
            _showItemDropdown = false;
        }

        void OnGUI()
        {
            if (!debugPanelEnabled || !_visible || !IsBuildSupported())
                return;

            if (ResolveSimulation() == null)
                return;

            if (_windowRect.width <= 1f || _windowRect.height <= 1f)
                _windowRect = BuildDefaultWindowRect();

            _windowRect = ClampWindowRect(_windowRect);
            _windowRect = GUI.Window(GetInstanceID(), _windowRect, DrawDebugWindow, "Village Autonomy Debug");
        }

        void DrawDebugWindow(int windowId)
        {
            var simulation = ResolveSimulation();
            if (simulation == null)
                return;

            EnsureStyles();
            simulation.BuildDebugNpcEntryList(_npcOptions);
            var telemetry = simulation.TelemetrySnapshot;
            var scrollHeight = Mathf.Max(120f, _windowRect.height - 48f);

            GUILayout.Label(
                $"deliberations={telemetry.DeliberationCalls}, fallback={telemetry.FallbackCalls} ({telemetry.FallbackRate:P1}), plans ok={telemetry.PlanCompletionsSucceeded}, plans failed={telemetry.PlanCompletionsFailed}",
                _lineStyle);
            GUILayout.Label($"npcs={_npcOptions.Count}  (toggle: {toggleKey})", _lineStyle);
            DrawSystemicModeBanner(simulation);

            _scroll = GUILayout.BeginScrollView(
                _scroll,
                false,
                true,
                GUILayout.ExpandWidth(true),
                GUILayout.Height(scrollHeight));
            try
            {
                DrawWorldCatalogSection(simulation);
                DrawPrimaryNpcSelectors(simulation);
                DrawAtomicActionControls(simulation);
                if (!simulation.IsSystemicOnlyMode)
                {
                    DrawInteractionControls(simulation);
                    DrawProposedInteractionControls(simulation);
                    DrawInteractionOverview(simulation);
                }
                DrawPerNpcControls(simulation);
                DrawRejectEventLog(simulation);
            }
            finally
            {
                GUILayout.EndScrollView();
            }

            GUI.DragWindow(new Rect(0f, 0f, _windowRect.width, 24f));
        }

        Rect BuildDefaultWindowRect()
        {
            var panelWidth = Mathf.Max(280f, width);
            var panelHeight = Mathf.Clamp(maxHeight, 200f, Screen.height - (anchorOffset.y * 2f));
            return new Rect(
                Screen.width - panelWidth - anchorOffset.x,
                anchorOffset.y,
                panelWidth,
                panelHeight);
        }

        Rect ClampWindowRect(Rect rect)
        {
            var panelWidth = Mathf.Max(280f, width);
            var panelHeight = Mathf.Clamp(maxHeight, 200f, Screen.height - (anchorOffset.y * 2f));
            rect.width = Mathf.Clamp(rect.width, 280f, Screen.width - 16f);
            rect.height = Mathf.Clamp(rect.height, 200f, panelHeight);
            rect.x = Mathf.Clamp(rect.x, 8f, Mathf.Max(8f, Screen.width - rect.width - 8f));
            rect.y = Mathf.Clamp(rect.y, 8f, Mathf.Max(8f, Screen.height - rect.height - 8f));
            if (rect.width < panelWidth * 0.5f)
                rect.width = panelWidth;
            return rect;
        }

        void DrawSystemicModeBanner(VillageAgentSimulation simulation)
        {
            if (simulation == null || !simulation.IsSystemicOnlyMode)
                return;

            GUILayout.Label(
                "Option A: Interaction FSM disabled (SystemicOnly). Use 1:1 hero dialogue; systemic gossip/opinion debug below.",
                _helpStyle);
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

        void DrawProposedInteractionControls(VillageAgentSimulation simulation)
        {
            GUILayout.Label("Proposed interactions", _titleStyle);
            simulation.AutoPromoteProposedInteractionsInDevelopment = GUILayout.Toggle(
                simulation.AutoPromoteProposedInteractionsInDevelopment,
                "Auto-promote proposed (dev/editor only)");
            simulation.BuildProposedInteractionIdList(_proposedInteractionIds);
            if (_proposedInteractionIds.Count == 0)
            {
                GUILayout.Label("No proposed interaction types.", _lineStyle);
                return;
            }

            if (string.IsNullOrWhiteSpace(_proposedPromoteId))
                _proposedPromoteId = _proposedInteractionIds[0];
            _proposedPromoteId = GUILayout.TextField(_proposedPromoteId ?? string.Empty, GUILayout.Width(220f));
            if (GUILayout.Button("Promote proposed → active", GUILayout.Width(180f)))
            {
                if (simulation.TryPromoteProposedInteractionForDebug(_proposedPromoteId, out var err))
                    _statusLine = "promoted " + _proposedPromoteId;
                else
                    _statusLine = "promote failed: " + err;
            }

            GUILayout.Space(4f);
        }

        void DrawRejectEventLog(VillageAgentSimulation simulation)
        {
            var events = simulation.InteractionRejectEvents;
            GUILayout.Label($"Reject / replan events ({events?.Count ?? 0})", _titleStyle);
            if (events == null || events.Count == 0)
            {
                GUILayout.Label("No interaction reject events.", _lineStyle);
                return;
            }

            for (var i = events.Count - 1; i >= 0; i--)
            {
                var entry = events[i];
                GUILayout.Label(
                    $"{entry.InteractionId} [{entry.InteractionInstanceId}] actor={entry.ActorNpcId} reason={entry.Reason}",
                    _lineStyle);
            }
            GUILayout.Space(4f);
        }

        void DrawInteractionOverview(VillageAgentSimulation simulation)
        {
            var active = simulation.ActiveInteractions;
            GUILayout.Space(4f);
            GUILayout.Label("Active interactions", _titleStyle);
            GUILayout.BeginHorizontal();
            GUILayout.Label("filter npc", GUILayout.Width(64f));
            _interactionFilterNpc = GUILayout.TextField(_interactionFilterNpc ?? string.Empty, GUILayout.Width(100f));
            GUILayout.Label("type", GUILayout.Width(32f));
            _interactionFilterType = GUILayout.TextField(_interactionFilterType ?? string.Empty, GUILayout.Width(100f));
            _showInvalidInteractionsOnly = GUILayout.Toggle(_showInvalidInteractionsOnly, "invalid only");
            GUILayout.EndHorizontal();

            var visibleCount = 0;
            if (active != null)
            {
                for (var i = 0; i < active.Count; i++)
                {
                    if (ShouldShowInteractionRow(active[i]))
                        visibleCount++;
                }
            }

            GUILayout.Label($"visible: {visibleCount} / {active?.Count ?? 0}", _lineStyle);
            if (active == null || active.Count == 0)
            {
                GUILayout.Label("No interaction instances yet.", _lineStyle);
                return;
            }

            for (var i = 0; i < active.Count; i++)
            {
                var entry = active[i];
                if (entry == null || !ShouldShowInteractionRow(entry))
                    continue;

                var validity = VillageAutonomyDebugFormatter.BuildValidityLineForInstance(entry);
                GUILayout.Label(
                    $"— {simulation.FormatInteractionTypeForDebug(entry)} [{entry.status}] {validity}",
                    _titleStyle);
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
        }

        bool ShouldShowInteractionRow(InteractionRuntimeInstance entry)
        {
            if (entry == null)
                return false;
            if (_showInvalidInteractionsOnly)
            {
                var validity = VillageAutonomyDebugFormatter.BuildValidityLineForInstance(entry);
                if (validity.IndexOf("invalid", StringComparison.OrdinalIgnoreCase) < 0)
                    return false;
            }

            if (!string.IsNullOrWhiteSpace(_interactionFilterNpc))
            {
                var filter = _interactionFilterNpc.Trim();
                if ((entry.actorNpcId ?? string.Empty).IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0
                    && (entry.targetNpcId ?? string.Empty).IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    return false;
                }
            }

            if (!string.IsNullOrWhiteSpace(_interactionFilterType))
            {
                var filter = _interactionFilterType.Trim();
                if ((entry.interactionId ?? string.Empty).IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0
                    && (entry.interactionDisplayName ?? string.Empty).IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    return false;
                }
            }

            return true;
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
