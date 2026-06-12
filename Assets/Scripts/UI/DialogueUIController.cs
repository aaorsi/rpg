using System;
using System.Collections;
using System.Collections.Generic;
using Rpg.Audio;
using Rpg.Core;
using Rpg.Dialogue;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Rpg.UI
{
    public sealed class DialogueUIController : MonoBehaviour
    {
        const int FontSize = 18;
        const float TypewriterCharsPerSecond = 42f;

        static Font BuiltinUiFont() => RuntimeUiBuildMaterials.BuiltinUiFont();

        Canvas _canvas;
        GameObject _panelRoot;
        Text _title;
        Text _playerEcho;
        Text _npcBody;
        Text _systemStrip;
        Text _inventoryStrip;
        Image _inventoryStripBackdrop;
        Text _thinking;
        InputField _input;
        Button _send;
        Button _close;
        GameObject _quickActionsPanel;
        Text _quickActionsTitle;
        Text _quickHeroLabel;
        Text _quickNpcLabel;
        Text _quickSelectionLabel;
        Button _quickModeGiveBtn;
        Button _quickModeTakeBtn;
        Button _quickModeTradeBtn;
        Button _dropHeroItemBtn;
        Button _tradeExecuteButton;
        string _selectedHeroDropItemId;
        RectTransform _quickHeroListRoot;
        RectTransform _quickNpcListRoot;
        string _selectedHeroTradeItemId;
        string _selectedNpcTradeItemId;
        QuickActionMode _quickMode = QuickActionMode.Give;
        readonly List<GameObject> _quickDynamicRows = new List<GameObject>();
        InventoryItemIconRenderer _inventoryIconRenderer;
        GameObject _decisionPanel;
        Text _decisionText;
        Button _decisionYes;
        Button _decisionNo;
        ScrollRect _scroll;
        Coroutine _typewriterCo;
        AudioSource _uiSfxSource;
        AudioClip _dialogueOpenCloseSfx;
        AudioClip _inventoryOpenCloseSfx;
        GameObject _hudNoticeGo;
        Text _hudNoticeText;
        Coroutine _hudNoticeCo;

        enum QuickActionMode
        {
            Give,
            Take,
            Trade
        }

        void Awake()
        {
            _inventoryIconRenderer = new InventoryItemIconRenderer();
            _uiSfxSource = gameObject.AddComponent<AudioSource>();
            _uiSfxSource.playOnAwake = false;
            _uiSfxSource.spatialBlend = 0f;
            _dialogueOpenCloseSfx = RuntimeAudioClipLoader.LoadFromAssetPath("Assets/FREE SOUND PACK_TM(355)/UI(27)/Interface Button 20.wav");
            _inventoryOpenCloseSfx = RuntimeAudioClipLoader.LoadFromAssetPath("Assets/FREE SOUND PACK_TM(355)/UI(27)/Clicks-001.wav");
            BuildUi();
            Close();
        }

        void Update()
        {
            var isDialogueOpen = DialogueManager.Instance != null && DialogueManager.Instance.IsDialogueOpen;
            if ((_panelRoot == null || !_panelRoot.activeSelf) && IsDebugSlashPressed())
            {
                DialogueManager.Instance?.OpenDebugConsoleFromShortcut();
                return;
            }
            if (!isDialogueOpen && IsQuickActionsTogglePressed())
            {
                ToggleQuickActionsPanel();
                return;
            }
            if (_panelRoot != null && _panelRoot.activeSelf && Input.GetKeyDown(KeyCode.Return))
            {
                if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
                    return;
                Submit();
            }
        }

        static bool IsDebugSlashPressed()
        {
            if (Input.GetKeyDown(KeyCode.Slash) || Input.GetKeyDown(KeyCode.KeypadDivide))
                return true;
            var typed = Input.inputString;
            if (string.IsNullOrEmpty(typed))
                return false;
            return typed.IndexOf('/') >= 0;
        }

        static bool IsQuickActionsTogglePressed()
        {
            if (Input.GetKeyDown(KeyCode.I))
                return true;
            var typed = Input.inputString;
            if (string.IsNullOrEmpty(typed))
                return false;
            return typed.IndexOf('i') >= 0 || typed.IndexOf('I') >= 0;
        }

        public void Open(string npcDisplayName)
        {
            if (_panelRoot != null && !_panelRoot.activeSelf)
                PlayUiSfx(_dialogueOpenCloseSfx);
            if (_title != null)
                _title.text = string.IsNullOrWhiteSpace(npcDisplayName) ? "Conversation" : npcDisplayName;
            var showInventoryDebug =
                string.Equals((npcDisplayName ?? string.Empty).Trim(), "Debug Console", StringComparison.OrdinalIgnoreCase);
            if (_inventoryStrip != null)
            {
                _inventoryStrip.gameObject.SetActive(showInventoryDebug);
                _inventoryStrip.text = string.Empty;
            }
            if (_inventoryStripBackdrop != null)
                _inventoryStripBackdrop.gameObject.SetActive(showInventoryDebug);
            StopTypewriter();
            if (_playerEcho != null)
                _playerEcho.text = string.Empty;
            if (_npcBody != null)
                _npcBody.text = string.Empty;
            if (_systemStrip != null)
                _systemStrip.text = string.Empty;
            if (_input != null)
            {
                _input.text = string.Empty;
            }
            HideTransferDecision();
            HideQuickActionsPanel();

            _panelRoot.SetActive(true);
            RefreshQuickActionsPanel();
            if (_quickActionsPanel != null)
            {
                _quickActionsPanel.SetActive(true);
                PlayUiSfx(_inventoryOpenCloseSfx);
            }
            RefreshDialogueLayout();
            StartCoroutine(ResizeLogAfterFirstLayout());
            if (_input != null)
                StartCoroutine(SelectInputAfterPanelShown());
        }

        IEnumerator ResizeLogAfterFirstLayout()
        {
            yield return null;
            RefreshDialogueLayout();
        }

        IEnumerator SelectInputAfterPanelShown()
        {
            yield return null;
            if (_input == null || _panelRoot == null || !_panelRoot.activeSelf)
                yield break;
            EventSystem.current?.SetSelectedGameObject(_input.gameObject, null);
            _input.ActivateInputField();
        }

        public void Close()
        {
            var wasOpen = _panelRoot != null && _panelRoot.activeSelf;
            if (wasOpen)
                PlayUiSfx(_dialogueOpenCloseSfx);
            StopTypewriter();
            HideTransferDecision();
            HideQuickActionsPanel();
            if (_panelRoot != null)
                _panelRoot.SetActive(false);
            SetThinking(false);
        }

        public void ShowTransferDecision(string question, UnityEngine.Events.UnityAction onAccept, UnityEngine.Events.UnityAction onDecline)
        {
            if (_decisionPanel == null || _decisionText == null || _decisionYes == null || _decisionNo == null)
                return;
            _decisionPanel.transform.SetAsLastSibling();
            _decisionText.text = string.IsNullOrWhiteSpace(question) ? "Accept transfer?" : question.Trim();
            _decisionYes.onClick.RemoveAllListeners();
            _decisionNo.onClick.RemoveAllListeners();
            _decisionYes.onClick.AddListener(() => onAccept?.Invoke());
            _decisionNo.onClick.AddListener(() => onDecline?.Invoke());
            _decisionPanel.SetActive(true);
        }

        public void HideTransferDecision()
        {
            if (_decisionPanel == null)
                return;
            _decisionPanel.SetActive(false);
            if (_decisionYes != null) _decisionYes.onClick.RemoveAllListeners();
            if (_decisionNo != null) _decisionNo.onClick.RemoveAllListeners();
        }

        /// <summary>Latest player line (instant). Does not accumulate history.</summary>
        public void AppendPlayerLine(string text)
        {
            if (_systemStrip != null)
                _systemStrip.text = string.Empty;
            if (_playerEcho == null)
                return;
            var t = (text ?? string.Empty).Trim();
            _playerEcho.text = string.IsNullOrEmpty(t) ? string.Empty : $"You: {t}";
        }

        /// <summary>NPC line replaces the previous one and reveals letter-by-letter.</summary>
        public void AppendNpcLine(string text)
        {
            if (_npcBody == null)
                return;
            StopTypewriter();
            var full = (text ?? string.Empty).TrimEnd();
            // Rich-text lines (e.g. Ghoul <color=…>) must not be fed through per-character typewriter or tags break mid-reveal.
            if (full.Length > 0 && full.StartsWith("<color=", StringComparison.Ordinal))
            {
                _npcBody.text = full;
                RefreshDialogueLayout();
                return;
            }

            _typewriterCo = StartCoroutine(CoTypewriteNpc(full));
        }

        /// <summary>Status / debug / errors — separate strip so NPC typewriter does not erase it.</summary>
        public void AppendSystemLine(string text)
        {
            if (_systemStrip == null)
                return;
            var t = (text ?? string.Empty).TrimEnd();
            _systemStrip.text = string.IsNullOrEmpty(t) ? string.Empty : t;
        }

        public void SetThinking(bool on)
        {
            if (_thinking == null)
                return;
            _thinking.gameObject.SetActive(on);
        }

        /// <summary>Persistent debug block for hero/NPC inventory while panel is open.</summary>
        public void SetInventoryDebug(string text)
        {
            if (_inventoryStrip == null || !_inventoryStrip.gameObject.activeSelf)
                return;
            var t = (text ?? string.Empty).TrimEnd();
            _inventoryStrip.text = string.IsNullOrEmpty(t) ? string.Empty : t;
        }

        void StopTypewriter()
        {
            if (_typewriterCo == null)
                return;
            StopCoroutine(_typewriterCo);
            _typewriterCo = null;
        }

        IEnumerator CoTypewriteNpc(string full)
        {
            if (_npcBody == null)
                yield break;

            _npcBody.text = string.Empty;
            if (full.Length == 0)
            {
                RefreshDialogueLayout();
                _typewriterCo = null;
                yield break;
            }

            var delay = 1f / Mathf.Max(4f, TypewriterCharsPerSecond);
            for (var i = 1; i <= full.Length; i++)
            {
                _npcBody.text = full.Substring(0, i);
                if (i % 6 == 0 || i == full.Length)
                    RefreshDialogueLayout();
                yield return new WaitForSeconds(delay);
            }

            RefreshDialogueLayout();
            _typewriterCo = null;
        }

        /// <summary>
        /// NPC body is top-anchored inside scroll content; keep content tall enough and scroll so text is visible.
        /// </summary>
        void RefreshDialogueLayout()
        {
            if (_npcBody == null || _scroll == null || _scroll.content == null || _scroll.viewport == null)
                return;

            var logRt = _npcBody.rectTransform;
            var contentRt = _scroll.content;
            var vpRt = _scroll.viewport;

            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(vpRt);
            LayoutRebuilder.ForceRebuildLayoutImmediate(contentRt);
            LayoutRebuilder.ForceRebuildLayoutImmediate(logRt);

            const float pad = 12f;
            var preferred = Mathf.Max(_npcBody.preferredHeight, 24f);
            logRt.sizeDelta = new Vector2(logRt.sizeDelta.x, preferred);

            var vh = Mathf.Max(vpRt.rect.height, 80f);
            var contentHeight = preferred + pad;
            contentRt.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Max(contentHeight, vh));

            _scroll.verticalNormalizedPosition = contentHeight > vh + 1f ? 0f : 1f;
        }

        void Submit()
        {
            if (_input == null || DialogueManager.Instance == null)
                return;
            var t = _input.text;
            _input.text = string.Empty;
            DialogueManager.Instance.SubmitPlayerLineFromUi(t);
        }

        void ToggleQuickActionsPanel()
        {
            if (_quickActionsPanel == null)
                return;
            if (_panelRoot == null)
                return;
            if (_quickActionsPanel.activeSelf)
            {
                HideQuickActionsPanel();
                return;
            }
            RefreshQuickActionsPanel();
            _quickActionsPanel.SetActive(true);
            PlayUiSfx(_inventoryOpenCloseSfx);
        }

        void HideQuickActionsPanel()
        {
            if (_quickActionsPanel == null)
                return;
            var wasOpen = _quickActionsPanel.activeSelf;
            _quickActionsPanel.SetActive(false);
            if (wasOpen)
                PlayUiSfx(_inventoryOpenCloseSfx);
            _selectedHeroTradeItemId = null;
            _selectedNpcTradeItemId = null;
            _quickMode = QuickActionMode.Give;
            _selectedHeroDropItemId = null;
            ClearQuickActionRows();
        }

        void PlayUiSfx(AudioClip clip)
        {
            if (_uiSfxSource == null || clip == null)
                return;
            _uiSfxSource.PlayOneShot(clip);
        }

        public void ShowTransientHudMessage(string text)
        {
            if (_hudNoticeText == null || _hudNoticeGo == null)
                return;
            if (_hudNoticeCo != null)
            {
                StopCoroutine(_hudNoticeCo);
                _hudNoticeCo = null;
            }

            _hudNoticeCo = StartCoroutine(CoTransientHudMessage(text));
        }

        IEnumerator CoTransientHudMessage(string text)
        {
            _hudNoticeGo.SetActive(true);
            _hudNoticeText.text = string.IsNullOrWhiteSpace(text) ? string.Empty : text.Trim();
            yield return new WaitForSeconds(2.6f);
            _hudNoticeText.text = string.Empty;
            _hudNoticeGo.SetActive(false);
            _hudNoticeCo = null;
        }

        void RefreshQuickActionsPanel()
        {
            if (_quickActionsPanel == null || _quickHeroListRoot == null || _quickNpcListRoot == null)
                return;
            ClearQuickActionRows();
            if (DialogueManager.Instance == null || !DialogueManager.Instance.TryBuildQuickActionState(out var state) || state == null)
            {
                if (_quickActionsTitle != null)
                    _quickActionsTitle.text = "Quick Actions (requires active NPC dialogue)";
                AddQuickRow(_quickHeroListRoot, "(no data)", null, null);
                AddQuickRow(_quickNpcListRoot, "(no data)", null, null);
                return;
            }

            var hasNpcContext = !string.IsNullOrWhiteSpace(state.npcId) && state.npcId != "(none)";
            if (_quickActionsTitle != null)
                _quickActionsTitle.text = hasNpcContext
                    ? $"Quick Actions - NPC: {state.npcId}"
                    : "Inventory";
            if (_quickHeroLabel != null)
                _quickHeroLabel.text = _quickMode == QuickActionMode.Take ? "Hero items (reference)" : "Hero items";
            if (_quickNpcLabel != null)
                _quickNpcLabel.text = hasNpcContext
                    ? (_quickMode == QuickActionMode.Give ? "NPC items (reference)" : "NPC items")
                    : string.Empty;
            if (_quickSelectionLabel != null)
            {
                if (_quickMode == QuickActionMode.Trade)
                {
                    var heroSel = string.IsNullOrWhiteSpace(_selectedHeroTradeItemId)
                        ? "(pick hero item)"
                        : state.heroItems.Find(x => x.itemId == _selectedHeroTradeItemId)?.displayName ?? _selectedHeroTradeItemId;
                    var npcSel = string.IsNullOrWhiteSpace(_selectedNpcTradeItemId)
                        ? "(pick npc item)"
                        : state.npcItems.Find(x => x.itemId == _selectedNpcTradeItemId)?.displayName ?? _selectedNpcTradeItemId;
                    _quickSelectionLabel.text = $"Trade selection: You give {heroSel} <-> You take {npcSel}";
                }
                else
                    _quickSelectionLabel.text = _quickMode == QuickActionMode.Give
                        ? "Mode: Give (click hero item)"
                        : "Mode: Take (click npc item)";
            }
            UpdateQuickModeButtonVisuals();
            SetNpcContextUiVisible(hasNpcContext);

            if (state.heroItems == null || state.heroItems.Count == 0)
                AddQuickRow(_quickHeroListRoot, "(hero inventory empty)", null, null);
            else
            {
                foreach (var e in state.heroItems)
                {
                    if (e == null) continue;
                    var itemId = e.itemId;
                    var txt = $"{e.displayName} x{e.quantity}";
                    var icon = _inventoryIconRenderer != null ? _inventoryIconRenderer.GetOrCreate(itemId) : null;
                    UnityEngine.Events.UnityAction onHeroRow = null;
                    var isChicken = string.Equals(itemId, GameConstants.LiveChickenItemId, System.StringComparison.OrdinalIgnoreCase);
                    if (hasNpcContext)
                    {
                        if (isChicken && _quickMode != QuickActionMode.Trade)
                            onHeroRow = PromptEatChickenFromInventoryRow;
                        else
                        {
                            var heroItemId = itemId;
                            onHeroRow = () =>
                            {
                                if (_quickMode == QuickActionMode.Trade)
                                {
                                    _selectedHeroTradeItemId = heroItemId;
                                    RefreshQuickActionsPanel();
                                    return;
                                }
                                DialogueManager.Instance?.ExecuteQuickGiveToNpc(heroItemId);
                                RefreshQuickActionsPanel();
                            };
                        }
                    }
                    else if (isChicken)
                        onHeroRow = PromptEatChickenFromInventoryRow;
                    else
                    {
                        var selId = itemId;
                        onHeroRow = () =>
                        {
                            _selectedHeroDropItemId = selId;
                            RefreshQuickActionsPanel();
                        };
                    }

                    var highlightDrop = !hasNpcContext && !isChicken
                        && string.Equals(_selectedHeroDropItemId, itemId, StringComparison.OrdinalIgnoreCase);
                    AddQuickRow(_quickHeroListRoot, txt, onHeroRow, icon, highlightDrop);
                }
            }

            if (state.npcItems == null || state.npcItems.Count == 0)
                AddQuickRow(_quickNpcListRoot, "(npc inventory empty)", null, null);
            else
            {
                foreach (var e in state.npcItems)
                {
                    if (e == null) continue;
                    var itemId = e.itemId;
                    var txt = $"{e.displayName} x{e.quantity}";
                    var icon = _inventoryIconRenderer != null ? _inventoryIconRenderer.GetOrCreate(itemId) : null;
                    AddQuickRow(_quickNpcListRoot, txt, hasNpcContext ? (() =>
                    {
                        if (_quickMode == QuickActionMode.Trade)
                        {
                            _selectedNpcTradeItemId = itemId;
                            RefreshQuickActionsPanel();
                            return;
                        }
                        DialogueManager.Instance?.ExecuteQuickTakeFromNpc(itemId);
                        RefreshQuickActionsPanel();
                    }) : null, icon);
                }
            }
        }

        void PromptEatChickenFromInventoryRow()
        {
            if (_quickActionsPanel == null || !_quickActionsPanel.activeSelf)
                return;
            ShowTransferDecision(
                "Eat chicken?",
                () =>
                {
                    HideTransferDecision();
                    if (DialogueManager.Instance != null && DialogueManager.Instance.TryConsumeLiveChickenForFood())
                        AppendSystemLine("You ate the chicken. Food restored.");
                    else
                        AppendSystemLine("You could not eat the chicken.");
                    RefreshQuickActionsPanel();
                },
                HideTransferDecision);
        }

        void SetNpcContextUiVisible(bool visible)
        {
            if (_quickNpcLabel != null)
                _quickNpcLabel.gameObject.SetActive(visible);
            if (_quickSelectionLabel != null)
                _quickSelectionLabel.gameObject.SetActive(visible);
            if (_quickNpcListRoot != null)
                _quickNpcListRoot.gameObject.SetActive(visible);
            if (_quickModeGiveBtn != null)
                _quickModeGiveBtn.gameObject.SetActive(visible);
            if (_quickModeTakeBtn != null)
                _quickModeTakeBtn.gameObject.SetActive(visible);
            if (_quickModeTradeBtn != null)
                _quickModeTradeBtn.gameObject.SetActive(visible);
            if (_dropHeroItemBtn != null)
                _dropHeroItemBtn.gameObject.SetActive(!visible);
            if (_tradeExecuteButton != null)
                _tradeExecuteButton.gameObject.SetActive(visible);
        }

        void UpdateQuickModeButtonVisuals()
        {
            SetQuickModeButtonColor(_quickModeGiveBtn, _quickMode == QuickActionMode.Give);
            SetQuickModeButtonColor(_quickModeTakeBtn, _quickMode == QuickActionMode.Take);
            SetQuickModeButtonColor(_quickModeTradeBtn, _quickMode == QuickActionMode.Trade);
        }

        static void SetQuickModeButtonColor(Button btn, bool active)
        {
            if (btn == null || btn.targetGraphic == null)
                return;
            if (btn.targetGraphic is Image img)
                img.color = active ? new Color(0.24f, 0.38f, 0.5f, 1f) : new Color(0.18f, 0.22f, 0.3f, 1f);
        }

        void ClearQuickActionRows()
        {
            foreach (var go in _quickDynamicRows)
            {
                if (go != null)
                    Destroy(go);
            }
            _quickDynamicRows.Clear();
        }

        void AddQuickRow(Transform parent, string label, UnityEngine.Events.UnityAction onClick, Sprite icon, bool highlightSelection = false)
        {
            if (parent == null)
                return;
            var go = new GameObject("Row");
            go.transform.SetParent(parent, false);
            _quickDynamicRows.Add(go);
            var rt = go.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(0f, 26f);
            var img = go.AddComponent<Image>();
            if (onClick == null)
                img.color = new Color(0.16f, 0.16f, 0.16f, 0.75f);
            else if (highlightSelection)
                img.color = new Color(0.26f, 0.42f, 0.3f, 0.98f);
            else
                img.color = new Color(0.2f, 0.26f, 0.34f, 0.95f);
            RuntimeUiBuildMaterials.TryApplySpriteImageMaterial(img);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            if (onClick != null)
                btn.onClick.AddListener(onClick);
            else
                btn.interactable = false;

            var iconGo = new GameObject("Icon");
            iconGo.transform.SetParent(go.transform, false);
            var iconImage = iconGo.AddComponent<Image>();
            iconImage.sprite = icon;
            iconImage.color = icon != null ? Color.white : new Color(1f, 1f, 1f, 0f);
            iconImage.preserveAspect = true;
            iconImage.raycastTarget = false;
            RuntimeUiBuildMaterials.TryApplySpriteImageMaterial(iconImage);
            var iconRt = iconGo.GetComponent<RectTransform>();
            iconRt.anchorMin = new Vector2(0.02f, 0.12f);
            iconRt.anchorMax = new Vector2(0.13f, 0.88f);
            iconRt.offsetMin = Vector2.zero;
            iconRt.offsetMax = Vector2.zero;

            var textGo = new GameObject("Text");
            textGo.transform.SetParent(go.transform, false);
            var txt = textGo.AddComponent<Text>();
            txt.font = BuiltinUiFont();
            txt.fontSize = FontSize - 5;
            txt.color = Color.white;
            txt.alignment = TextAnchor.MiddleLeft;
            txt.text = label;
            txt.raycastTarget = false;
            RuntimeUiBuildMaterials.TryApplyLegacyTextMaterial(txt);
            var tr = textGo.GetComponent<RectTransform>();
            tr.anchorMin = new Vector2(0.15f, 0f);
            tr.anchorMax = new Vector2(0.96f, 1f);
            tr.offsetMin = Vector2.zero;
            tr.offsetMax = Vector2.zero;
        }

        void BuildUi()
        {
            _canvas = gameObject.GetComponent<Canvas>();
            if (_canvas == null)
                _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 100;
            if (gameObject.GetComponent<GraphicRaycaster>() == null)
                gameObject.AddComponent<GraphicRaycaster>();
            if (gameObject.GetComponent<CanvasScaler>() == null)
            {
                var scaler = gameObject.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.matchWidthOrHeight = 0.5f;
            }

            if (UnityEngine.Object.FindFirstObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem");
                es.AddComponent<EventSystem>();
                es.AddComponent<InputSystemUIInputModule>();
            }

            _panelRoot = new GameObject("DialoguePanel");
            _panelRoot.transform.SetParent(transform, false);
            var panelRect = _panelRoot.AddComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.02f, 0.02f);
            panelRect.anchorMax = new Vector2(0.56f, 0.58f);
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;
            var bg = _panelRoot.AddComponent<Image>();
            bg.color = new Color(0.05f, 0.06f, 0.08f, 0.92f);
            bg.raycastTarget = true;
            RuntimeUiBuildMaterials.TryApplySpriteImageMaterial(bg);
            var uiFont = BuiltinUiFont();

            var panelShadow = new GameObject("DialoguePanelShadow");
            panelShadow.transform.SetParent(_panelRoot.transform, false);
            panelShadow.transform.SetAsFirstSibling();
            var panelShadowRt = panelShadow.AddComponent<RectTransform>();
            panelShadowRt.anchorMin = new Vector2(-0.01f, -0.015f);
            panelShadowRt.anchorMax = new Vector2(1.015f, 1.01f);
            panelShadowRt.offsetMin = Vector2.zero;
            panelShadowRt.offsetMax = Vector2.zero;
            var panelShadowImg = panelShadow.AddComponent<Image>();
            panelShadowImg.color = new Color(0f, 0f, 0f, 0.36f);
            panelShadowImg.raycastTarget = false;
            RuntimeUiBuildMaterials.TryApplySpriteImageMaterial(panelShadowImg);

            var titleGo = new GameObject("Title");
            titleGo.transform.SetParent(_panelRoot.transform, false);
            _title = titleGo.AddComponent<Text>();
            _title.font = uiFont;
            _title.fontSize = FontSize + 2;
            _title.color = Color.white;
            _title.alignment = TextAnchor.MiddleLeft;
            var titleRt = titleGo.GetComponent<RectTransform>();
            titleRt.anchorMin = new Vector2(0.02f, 0.86f);
            titleRt.anchorMax = new Vector2(0.52f, 0.98f);
            titleRt.offsetMin = Vector2.zero;
            titleRt.offsetMax = Vector2.zero;
            RuntimeUiBuildMaterials.TryApplyLegacyTextMaterial(_title);

            var playerEchoGo = new GameObject("PlayerEcho");
            playerEchoGo.transform.SetParent(_panelRoot.transform, false);
            _playerEcho = playerEchoGo.AddComponent<Text>();
            _playerEcho.font = uiFont;
            _playerEcho.fontSize = FontSize - 1;
            _playerEcho.color = new Color(0.75f, 0.82f, 0.95f, 1f);
            _playerEcho.alignment = TextAnchor.UpperLeft;
            _playerEcho.horizontalOverflow = HorizontalWrapMode.Wrap;
            _playerEcho.verticalOverflow = VerticalWrapMode.Overflow;
            var peRt = playerEchoGo.GetComponent<RectTransform>();
            peRt.anchorMin = new Vector2(0.02f, 0.73f);
            peRt.anchorMax = new Vector2(0.98f, 0.84f);
            peRt.offsetMin = Vector2.zero;
            peRt.offsetMax = Vector2.zero;
            RuntimeUiBuildMaterials.TryApplyLegacyTextMaterial(_playerEcho);

            var scrollGo = new GameObject("Scroll");
            scrollGo.transform.SetParent(_panelRoot.transform, false);
            _scroll = scrollGo.AddComponent<ScrollRect>();
            _scroll.horizontal = false;
            _scroll.vertical = true;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            var scrollRt = scrollGo.GetComponent<RectTransform>();
            scrollRt.anchorMin = new Vector2(0.02f, 0.22f);
            scrollRt.anchorMax = new Vector2(0.98f, 0.72f);
            scrollRt.offsetMin = Vector2.zero;
            scrollRt.offsetMax = Vector2.zero;

            var viewport = new GameObject("Viewport");
            viewport.transform.SetParent(scrollGo.transform, false);
            var vpRt = viewport.AddComponent<RectTransform>();
            vpRt.anchorMin = Vector2.zero;
            vpRt.anchorMax = Vector2.one;
            vpRt.offsetMin = Vector2.zero;
            vpRt.offsetMax = Vector2.zero;
            viewport.AddComponent<RectMask2D>();
            _scroll.viewport = vpRt;

            var content = new GameObject("Content");
            content.transform.SetParent(viewport.transform, false);
            var contentRt = content.AddComponent<RectTransform>();
            contentRt.anchorMin = new Vector2(0f, 1f);
            contentRt.anchorMax = new Vector2(1f, 1f);
            contentRt.pivot = new Vector2(0.5f, 1f);
            contentRt.sizeDelta = new Vector2(0f, 64f);
            _scroll.content = contentRt;

            var npcGo = new GameObject("NpcBody");
            npcGo.transform.SetParent(content.transform, false);
            _npcBody = npcGo.AddComponent<Text>();
            _npcBody.font = uiFont;
            _npcBody.fontSize = FontSize;
            _npcBody.color = new Color(0.92f, 0.94f, 0.98f, 1f);
            _npcBody.alignment = TextAnchor.UpperLeft;
            _npcBody.horizontalOverflow = HorizontalWrapMode.Wrap;
            _npcBody.verticalOverflow = VerticalWrapMode.Overflow;
            _npcBody.supportRichText = true;
            var logRt = npcGo.GetComponent<RectTransform>();
            logRt.anchorMin = new Vector2(0f, 1f);
            logRt.anchorMax = new Vector2(1f, 1f);
            logRt.pivot = new Vector2(0.5f, 1f);
            logRt.anchoredPosition = Vector2.zero;
            logRt.sizeDelta = new Vector2(-16f, 32f);
            RuntimeUiBuildMaterials.TryApplyLegacyTextMaterial(_npcBody);

            _thinking = CreateTextLine(_panelRoot.transform, "Thinking", new Vector2(0.02f, 0.14f), new Vector2(0.98f, 0.182f), new Color(0.8f, 0.85f, 1f));
            _thinking.gameObject.SetActive(false);

            var inputGo = new GameObject("Input");
            inputGo.transform.SetParent(_panelRoot.transform, false);
            var inputRt = inputGo.AddComponent<RectTransform>();
            inputRt.anchorMin = new Vector2(0.02f, 0.06f);
            inputRt.anchorMax = new Vector2(0.72f, 0.18f);
            inputRt.offsetMin = Vector2.zero;
            inputRt.offsetMax = Vector2.zero;
            _input = inputGo.AddComponent<InputField>();
            var inputBg = inputGo.AddComponent<Image>();
            inputBg.color = new Color(0.12f, 0.14f, 0.18f, 1f);
            inputBg.raycastTarget = true;
            RuntimeUiBuildMaterials.TryApplySpriteImageMaterial(inputBg);
            _input.targetGraphic = inputBg;
            _input.interactable = true;

            var inputTextGo = new GameObject("Text");
            inputTextGo.transform.SetParent(inputGo.transform, false);
            var itRt = inputTextGo.AddComponent<RectTransform>();
            itRt.anchorMin = new Vector2(0.02f, 0.1f);
            itRt.anchorMax = new Vector2(0.98f, 0.9f);
            itRt.offsetMin = Vector2.zero;
            itRt.offsetMax = Vector2.zero;
            var it = inputTextGo.AddComponent<Text>();
            it.font = uiFont;
            it.fontSize = FontSize;
            it.color = Color.white;
            it.supportRichText = false;
            it.raycastTarget = true;
            RuntimeUiBuildMaterials.TryApplyLegacyTextMaterial(it);
            _input.textComponent = it;
            _input.lineType = InputField.LineType.SingleLine;

            var placeholderGo = new GameObject("Placeholder");
            placeholderGo.transform.SetParent(inputGo.transform, false);
            var phRt = placeholderGo.AddComponent<RectTransform>();
            phRt.anchorMin = new Vector2(0.02f, 0.1f);
            phRt.anchorMax = new Vector2(0.98f, 0.9f);
            phRt.offsetMin = Vector2.zero;
            phRt.offsetMax = Vector2.zero;
            var ph = placeholderGo.AddComponent<Text>();
            ph.font = uiFont;
            ph.fontSize = FontSize - 2;
            ph.color = new Color(1f, 1f, 1f, 0.35f);
            ph.text = "Type a reply… (Enter to send, /inv help for inventory commands)";
            ph.raycastTarget = false;
            RuntimeUiBuildMaterials.TryApplyLegacyTextMaterial(ph);
            _input.placeholder = ph;

            var systemGo = new GameObject("SystemStrip");
            systemGo.transform.SetParent(_panelRoot.transform, false);
            _systemStrip = systemGo.AddComponent<Text>();
            _systemStrip.font = uiFont;
            _systemStrip.fontSize = FontSize - 3;
            _systemStrip.color = new Color(0.95f, 0.78f, 0.55f, 1f);
            _systemStrip.alignment = TextAnchor.MiddleLeft;
            _systemStrip.horizontalOverflow = HorizontalWrapMode.Wrap;
            _systemStrip.verticalOverflow = VerticalWrapMode.Truncate;
            var sysRt = systemGo.GetComponent<RectTransform>();
            sysRt.anchorMin = new Vector2(0.02f, 0.192f);
            sysRt.anchorMax = new Vector2(0.72f, 0.218f);
            sysRt.offsetMin = Vector2.zero;
            sysRt.offsetMax = Vector2.zero;
            RuntimeUiBuildMaterials.TryApplyLegacyTextMaterial(_systemStrip);

            var invGo = new GameObject("InventoryStrip");
            invGo.transform.SetParent(_panelRoot.transform, false);
            _inventoryStripBackdrop = invGo.AddComponent<Image>();
            _inventoryStripBackdrop.color = new Color(0.07f, 0.1f, 0.13f, 0.9f);
            _inventoryStripBackdrop.raycastTarget = false;
            RuntimeUiBuildMaterials.TryApplySpriteImageMaterial(_inventoryStripBackdrop);
            var invTextGo = new GameObject("InventoryText");
            invTextGo.transform.SetParent(invGo.transform, false);
            _inventoryStrip = invTextGo.AddComponent<Text>();
            _inventoryStrip.font = uiFont;
            _inventoryStrip.fontSize = FontSize - 4;
            _inventoryStrip.color = new Color(0.72f, 0.95f, 0.78f, 1f);
            _inventoryStrip.alignment = TextAnchor.UpperLeft;
            _inventoryStrip.horizontalOverflow = HorizontalWrapMode.Wrap;
            _inventoryStrip.verticalOverflow = VerticalWrapMode.Overflow;
            var invRt = invGo.GetComponent<RectTransform>();
            invRt.anchorMin = new Vector2(0.74f, 0.192f);
            invRt.anchorMax = new Vector2(0.98f, 0.72f);
            invRt.offsetMin = Vector2.zero;
            invRt.offsetMax = Vector2.zero;
            var invTextRt = invTextGo.GetComponent<RectTransform>();
            invTextRt.anchorMin = new Vector2(0.04f, 0.04f);
            invTextRt.anchorMax = new Vector2(0.96f, 0.96f);
            invTextRt.offsetMin = Vector2.zero;
            invTextRt.offsetMax = Vector2.zero;
            RuntimeUiBuildMaterials.TryApplyLegacyTextMaterial(_inventoryStrip);

            _send = CreateButton(_panelRoot.transform, "Send", new Vector2(0.74f, 0.06f), new Vector2(0.86f, 0.18f), Submit);
            _close = CreateButton(_panelRoot.transform, "Close", new Vector2(0.88f, 0.06f), new Vector2(0.98f, 0.18f), () => DialogueManager.Instance?.EndDialogue());

            _quickActionsPanel = new GameObject("QuickActionsPanel");
            _quickActionsPanel.transform.SetParent(transform, false);
            var qaRt = _quickActionsPanel.AddComponent<RectTransform>();
            qaRt.anchorMin = new Vector2(0.78f, 0.02f);
            qaRt.anchorMax = new Vector2(0.98f, 0.72f);
            qaRt.offsetMin = Vector2.zero;
            qaRt.offsetMax = Vector2.zero;
            var qaBg = _quickActionsPanel.AddComponent<Image>();
            qaBg.color = new Color(0.06f, 0.08f, 0.11f, 0.94f);
            RuntimeUiBuildMaterials.TryApplySpriteImageMaterial(qaBg);

            var qaShadow = new GameObject("QuickActionsShadow");
            qaShadow.transform.SetParent(_quickActionsPanel.transform, false);
            qaShadow.transform.SetAsFirstSibling();
            var qaShadowRt = qaShadow.AddComponent<RectTransform>();
            qaShadowRt.anchorMin = new Vector2(-0.02f, -0.015f);
            qaShadowRt.anchorMax = new Vector2(1.02f, 1.01f);
            qaShadowRt.offsetMin = Vector2.zero;
            qaShadowRt.offsetMax = Vector2.zero;
            var qaShadowImg = qaShadow.AddComponent<Image>();
            qaShadowImg.color = new Color(0f, 0f, 0f, 0.34f);
            qaShadowImg.raycastTarget = false;
            RuntimeUiBuildMaterials.TryApplySpriteImageMaterial(qaShadowImg);

            var qaTitleGo = new GameObject("Title");
            qaTitleGo.transform.SetParent(_quickActionsPanel.transform, false);
            _quickActionsTitle = qaTitleGo.AddComponent<Text>();
            _quickActionsTitle.font = uiFont;
            _quickActionsTitle.fontSize = FontSize - 2;
            _quickActionsTitle.color = Color.white;
            _quickActionsTitle.alignment = TextAnchor.MiddleLeft;
            _quickActionsTitle.text = "Quick Actions";
            var qaTitleRt = qaTitleGo.GetComponent<RectTransform>();
            qaTitleRt.anchorMin = new Vector2(0.04f, 0.93f);
            qaTitleRt.anchorMax = new Vector2(0.96f, 0.995f);
            qaTitleRt.offsetMin = Vector2.zero;
            qaTitleRt.offsetMax = Vector2.zero;
            RuntimeUiBuildMaterials.TryApplyLegacyTextMaterial(_quickActionsTitle);

            _quickHeroLabel = CreateTextLine(_quickActionsPanel.transform, "HeroItemsLabel", new Vector2(0.04f, 0.87f), new Vector2(0.96f, 0.92f), new Color(0.74f, 0.89f, 1f));
            _quickHeroLabel.text = "Hero items";
            _quickHeroLabel.fontSize = FontSize - 4;
            _quickHeroLabel.alignment = TextAnchor.MiddleLeft;

            _quickModeGiveBtn = CreateButton(_quickActionsPanel.transform, "Give", new Vector2(0.04f, 0.80f), new Vector2(0.30f, 0.86f), () =>
            {
                _quickMode = QuickActionMode.Give;
                RefreshQuickActionsPanel();
            });
            _quickModeTakeBtn = CreateButton(_quickActionsPanel.transform, "Take", new Vector2(0.34f, 0.80f), new Vector2(0.60f, 0.86f), () =>
            {
                _quickMode = QuickActionMode.Take;
                RefreshQuickActionsPanel();
            });
            _quickModeTradeBtn = CreateButton(_quickActionsPanel.transform, "Trade", new Vector2(0.64f, 0.80f), new Vector2(0.96f, 0.86f), () =>
            {
                _quickMode = QuickActionMode.Trade;
                RefreshQuickActionsPanel();
            });

            var heroListGo = new GameObject("HeroList");
            heroListGo.transform.SetParent(_quickActionsPanel.transform, false);
            _quickHeroListRoot = heroListGo.AddComponent<RectTransform>();
            _quickHeroListRoot.anchorMin = new Vector2(0.04f, 0.57f);
            _quickHeroListRoot.anchorMax = new Vector2(0.96f, 0.79f);
            _quickHeroListRoot.offsetMin = Vector2.zero;
            _quickHeroListRoot.offsetMax = Vector2.zero;
            var heroLayout = heroListGo.AddComponent<VerticalLayoutGroup>();
            heroLayout.spacing = 4f;
            heroLayout.childControlHeight = false;
            heroLayout.childControlWidth = true;
            heroLayout.childForceExpandHeight = false;
            heroLayout.childForceExpandWidth = true;
            heroLayout.padding = new RectOffset(0, 0, 0, 0);
            heroListGo.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _quickNpcLabel = CreateTextLine(_quickActionsPanel.transform, "NpcItemsLabel", new Vector2(0.04f, 0.47f), new Vector2(0.96f, 0.52f), new Color(0.78f, 1f, 0.8f));
            _quickNpcLabel.text = "NPC items";
            _quickNpcLabel.fontSize = FontSize - 4;
            _quickNpcLabel.alignment = TextAnchor.MiddleLeft;

            _quickSelectionLabel = CreateTextLine(_quickActionsPanel.transform, "TradeSelection", new Vector2(0.04f, 0.50f), new Vector2(0.96f, 0.56f), new Color(0.95f, 0.88f, 0.66f, 1f));
            _quickSelectionLabel.fontSize = FontSize - 5;
            _quickSelectionLabel.alignment = TextAnchor.MiddleLeft;
            _quickSelectionLabel.text = "Mode: Give (click hero item)";

            _tradeExecuteButton = CreateButton(_quickActionsPanel.transform, "Execute Trade", new Vector2(0.04f, 0.44f), new Vector2(0.96f, 0.49f), () =>
            {
                if (_quickMode != QuickActionMode.Trade)
                    return;
                if (string.IsNullOrWhiteSpace(_selectedHeroTradeItemId) || string.IsNullOrWhiteSpace(_selectedNpcTradeItemId))
                    return;
                DialogueManager.Instance?.ExecuteQuickTrade(_selectedHeroTradeItemId, _selectedNpcTradeItemId);
                _selectedHeroTradeItemId = null;
                _selectedNpcTradeItemId = null;
                RefreshQuickActionsPanel();
            });
            _tradeExecuteButton.GetComponentInChildren<Text>().fontSize = FontSize - 5;

            _dropHeroItemBtn = CreateButton(_quickActionsPanel.transform, "Drop Item", new Vector2(0.04f, 0.50f), new Vector2(0.96f, 0.56f), () =>
            {
                if (string.IsNullOrWhiteSpace(_selectedHeroDropItemId))
                {
                    ShowTransientHudMessage("Select an item row first.");
                    return;
                }

                if (DialogueManager.Instance != null && DialogueManager.Instance.TryDropHeroItemToWorld(_selectedHeroDropItemId))
                    _selectedHeroDropItemId = null;
                RefreshQuickActionsPanel();
            });
            _dropHeroItemBtn.GetComponentInChildren<Text>().fontSize = FontSize - 4;
            _dropHeroItemBtn.gameObject.SetActive(false);

            var npcListGo = new GameObject("NpcList");
            npcListGo.transform.SetParent(_quickActionsPanel.transform, false);
            _quickNpcListRoot = npcListGo.AddComponent<RectTransform>();
            _quickNpcListRoot.anchorMin = new Vector2(0.04f, 0.08f);
            _quickNpcListRoot.anchorMax = new Vector2(0.96f, 0.43f);
            _quickNpcListRoot.offsetMin = Vector2.zero;
            _quickNpcListRoot.offsetMax = Vector2.zero;
            var npcLayout = npcListGo.AddComponent<VerticalLayoutGroup>();
            npcLayout.spacing = 4f;
            npcLayout.childControlHeight = false;
            npcLayout.childControlWidth = true;
            npcLayout.childForceExpandHeight = false;
            npcLayout.childForceExpandWidth = true;
            npcLayout.padding = new RectOffset(0, 0, 0, 0);
            npcListGo.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _quickActionsPanel.SetActive(false);
            UpdateQuickModeButtonVisuals();

            _decisionPanel = new GameObject("TransferDecisionPanel");
            // Parent under canvas root, not the dialogue panel: when inventory (I) is open alone, _panelRoot is inactive
            // and any child would stay hidden — NPC transfer prompts and "Eat chicken?" must still show.
            _decisionPanel.transform.SetParent(transform, false);
            var dRt = _decisionPanel.AddComponent<RectTransform>();
            dRt.anchorMin = new Vector2(0.28f, 0.38f);
            dRt.anchorMax = new Vector2(0.72f, 0.62f);
            dRt.offsetMin = Vector2.zero;
            dRt.offsetMax = Vector2.zero;
            var dBg = _decisionPanel.AddComponent<Image>();
            dBg.color = new Color(0.1f, 0.13f, 0.18f, 0.96f);
            RuntimeUiBuildMaterials.TryApplySpriteImageMaterial(dBg);

            var qGo = new GameObject("Question");
            qGo.transform.SetParent(_decisionPanel.transform, false);
            _decisionText = qGo.AddComponent<Text>();
            _decisionText.font = uiFont;
            _decisionText.fontSize = FontSize - 1;
            _decisionText.color = Color.white;
            _decisionText.alignment = TextAnchor.UpperLeft;
            _decisionText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _decisionText.verticalOverflow = VerticalWrapMode.Overflow;
            var qRt = qGo.GetComponent<RectTransform>();
            qRt.anchorMin = new Vector2(0.04f, 0.42f);
            qRt.anchorMax = new Vector2(0.96f, 0.94f);
            qRt.offsetMin = Vector2.zero;
            qRt.offsetMax = Vector2.zero;
            RuntimeUiBuildMaterials.TryApplyLegacyTextMaterial(_decisionText);

            _decisionYes = CreateButton(_decisionPanel.transform, "Yes", new Vector2(0.12f, 0.08f), new Vector2(0.42f, 0.34f), null);
            _decisionNo = CreateButton(_decisionPanel.transform, "No", new Vector2(0.58f, 0.08f), new Vector2(0.88f, 0.34f), null);
            _decisionPanel.SetActive(false);

            _hudNoticeGo = new GameObject("HudNotice");
            _hudNoticeGo.transform.SetParent(transform, false);
            var hnRt = _hudNoticeGo.AddComponent<RectTransform>();
            hnRt.anchorMin = new Vector2(0.22f, 0.06f);
            hnRt.anchorMax = new Vector2(0.78f, 0.11f);
            hnRt.offsetMin = Vector2.zero;
            hnRt.offsetMax = Vector2.zero;
            var hnBg = _hudNoticeGo.AddComponent<Image>();
            hnBg.color = new Color(0.08f, 0.1f, 0.14f, 0.88f);
            RuntimeUiBuildMaterials.TryApplySpriteImageMaterial(hnBg);
            var hnTextGo = new GameObject("Text");
            hnTextGo.transform.SetParent(_hudNoticeGo.transform, false);
            _hudNoticeText = hnTextGo.AddComponent<Text>();
            _hudNoticeText.font = uiFont;
            _hudNoticeText.fontSize = FontSize - 1;
            _hudNoticeText.color = new Color(0.95f, 0.82f, 0.58f, 1f);
            _hudNoticeText.alignment = TextAnchor.MiddleCenter;
            _hudNoticeText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _hudNoticeText.verticalOverflow = VerticalWrapMode.Truncate;
            RuntimeUiBuildMaterials.TryApplyLegacyTextMaterial(_hudNoticeText);
            var hnTextRt = hnTextGo.GetComponent<RectTransform>();
            hnTextRt.anchorMin = new Vector2(0.04f, 0.1f);
            hnTextRt.anchorMax = new Vector2(0.96f, 0.9f);
            hnTextRt.offsetMin = Vector2.zero;
            hnTextRt.offsetMax = Vector2.zero;
            _hudNoticeGo.SetActive(false);
        }

        static Text CreateTextLine(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = BuiltinUiFont();
            t.fontSize = FontSize;
            t.color = color;
            t.alignment = TextAnchor.MiddleLeft;
            t.text = "Thinking…";
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            RuntimeUiBuildMaterials.TryApplyLegacyTextMaterial(t);
            return t;
        }

        static Button CreateButton(Transform parent, string label, Vector2 anchorMin, Vector2 anchorMax, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject(label + "Button");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var img = go.AddComponent<Image>();
            img.color = new Color(0.18f, 0.22f, 0.3f, 1f);
            img.raycastTarget = true;
            RuntimeUiBuildMaterials.TryApplySpriteImageMaterial(img);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            if (onClick != null)
                btn.onClick.AddListener(onClick);

            var textGo = new GameObject("Text");
            textGo.transform.SetParent(go.transform, false);
            var txt = textGo.AddComponent<Text>();
            txt.font = BuiltinUiFont();
            txt.fontSize = FontSize;
            txt.color = Color.white;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.text = label;
            txt.raycastTarget = false;
            RuntimeUiBuildMaterials.TryApplyLegacyTextMaterial(txt);
            var trt = textGo.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero;
            trt.offsetMax = Vector2.zero;

            return btn;
        }

    }
}
