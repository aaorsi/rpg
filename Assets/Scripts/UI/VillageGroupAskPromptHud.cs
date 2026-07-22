using System;
using System.Collections.Generic;
using Rpg.Dialogue;
using Rpg.Npc;
using UnityEngine;
using UnityEngine.UI;

namespace Rpg.UI
{
    /// <summary>
    /// Surfaces open village group asks to the player (Option A Phase 5 optional prompt).
    /// </summary>
    public sealed class VillageGroupAskPromptHud : MonoBehaviour
    {
        const int FontSize = 15;
        const float PollSeconds = 2f;

        GameObject _panelRoot;
        Text _body;
        float _nextPollAt;
        readonly HashSet<string> _announcedAskIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Awake()
        {
            BuildUi();
            SetVisible(false);
        }

        void Update()
        {
            if (Time.unscaledTime < _nextPollAt)
                return;
            _nextPollAt = Time.unscaledTime + PollSeconds;
            Refresh();
        }

        public void Refresh()
        {
            var simulation = FindFirstObjectByType<VillageAgentSimulation>(FindObjectsInactive.Exclude);
            if (simulation == null)
            {
                SetVisible(false);
                return;
            }

            var offered = simulation.SnapshotOfferedGroupAsksForPlayer();
            if (offered == null || offered.Count == 0)
            {
                SetVisible(false);
                return;
            }

            var primary = offered[0];
            if (primary == null || string.IsNullOrWhiteSpace(primary.askId))
            {
                SetVisible(false);
                return;
            }

            if (_announcedAskIds.Add(primary.askId))
            {
                var toast = string.IsNullOrWhiteSpace(primary.title)
                    ? "Village leadership is seeking your response."
                    : primary.title.Trim();
                DialogueManager.Instance?.ShowHudMessage(toast);
            }

            var title = string.IsNullOrWhiteSpace(primary.title) ? "Village ask" : primary.title.Trim();
            var summary = string.IsNullOrWhiteSpace(primary.summary)
                ? "Talk with villagers to respond."
                : primary.summary.Trim();
            _body.text = title + "\n" + summary + "\n(Speak with a villager to accept or decline.)";
            SetVisible(true);
        }

        void SetVisible(bool visible)
        {
            if (_panelRoot != null)
                _panelRoot.SetActive(visible);
        }

        void BuildUi()
        {
            var uiFont = RuntimeUiBuildMaterials.BuiltinUiFont();
            _panelRoot = new GameObject("VillageGroupAskPrompt");
            _panelRoot.transform.SetParent(transform, false);
            var rt = _panelRoot.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.28f, 0.88f);
            rt.anchorMax = new Vector2(0.72f, 0.98f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var bg = _panelRoot.AddComponent<Image>();
            bg.color = new Color(0.1f, 0.12f, 0.18f, 0.9f);
            RuntimeUiBuildMaterials.TryApplySpriteImageMaterial(bg);

            var bodyGo = new GameObject("Body");
            bodyGo.transform.SetParent(_panelRoot.transform, false);
            _body = bodyGo.AddComponent<Text>();
            _body.font = uiFont;
            _body.fontSize = FontSize;
            _body.color = new Color(0.95f, 0.9f, 0.78f);
            _body.alignment = TextAnchor.UpperCenter;
            _body.horizontalOverflow = HorizontalWrapMode.Wrap;
            _body.verticalOverflow = VerticalWrapMode.Overflow;
            var bodyRt = bodyGo.GetComponent<RectTransform>();
            bodyRt.anchorMin = new Vector2(0.04f, 0.08f);
            bodyRt.anchorMax = new Vector2(0.96f, 0.92f);
            bodyRt.offsetMin = Vector2.zero;
            bodyRt.offsetMax = Vector2.zero;
            RuntimeUiBuildMaterials.TryApplyLegacyTextMaterial(_body);
        }
    }
}
