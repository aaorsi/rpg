using System.Collections.Generic;
using System.Text;
using Rpg.Dialogue;
using Rpg.Npc;
using UnityEngine;
using UnityEngine.UI;

namespace Rpg.UI
{
    /// <summary>
    /// Player journal: active milestones + recent village rumors (Option A Phase 5).
    /// Toggle with J.
    /// </summary>
    public sealed class VillageJournalPanel : MonoBehaviour
    {
        const int FontSize = 16;

        GameObject _panelRoot;
        Text _body;
        bool _visible;

        void Awake()
        {
            BuildUi();
            SetVisible(false);
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.J))
                SetVisible(!_visible);
        }

        public void SetVisible(bool visible)
        {
            _visible = visible;
            if (_panelRoot != null)
                _panelRoot.SetActive(visible);
            if (visible)
                Refresh();
        }

        public void Refresh()
        {
            if (_body == null)
                return;
            var dm = DialogueManager.Instance;
            if (dm == null)
            {
                _body.text = "(Journal unavailable.)";
                return;
            }

            _body.text = dm.BuildJournalText();
        }

        void BuildUi()
        {
            var uiFont = RuntimeUiBuildMaterials.BuiltinUiFont();
            _panelRoot = new GameObject("VillageJournalPanel");
            _panelRoot.transform.SetParent(transform, false);
            var rt = _panelRoot.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.02f, 0.12f);
            rt.anchorMax = new Vector2(0.36f, 0.88f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var bg = _panelRoot.AddComponent<Image>();
            bg.color = new Color(0.06f, 0.08f, 0.12f, 0.92f);
            RuntimeUiBuildMaterials.TryApplySpriteImageMaterial(bg);

            var titleGo = new GameObject("Title");
            titleGo.transform.SetParent(_panelRoot.transform, false);
            var title = titleGo.AddComponent<Text>();
            title.font = uiFont;
            title.fontSize = FontSize + 2;
            title.color = new Color(0.92f, 0.86f, 0.72f);
            title.alignment = TextAnchor.UpperLeft;
            title.text = "Journal (J)";
            var titleRt = titleGo.GetComponent<RectTransform>();
            titleRt.anchorMin = new Vector2(0.05f, 0.92f);
            titleRt.anchorMax = new Vector2(0.95f, 0.99f);
            titleRt.offsetMin = Vector2.zero;
            titleRt.offsetMax = Vector2.zero;
            RuntimeUiBuildMaterials.TryApplyLegacyTextMaterial(title);

            var bodyGo = new GameObject("Body");
            bodyGo.transform.SetParent(_panelRoot.transform, false);
            _body = bodyGo.AddComponent<Text>();
            _body.font = uiFont;
            _body.fontSize = FontSize;
            _body.color = Color.white;
            _body.alignment = TextAnchor.UpperLeft;
            _body.horizontalOverflow = HorizontalWrapMode.Wrap;
            _body.verticalOverflow = VerticalWrapMode.Overflow;
            var bodyRt = bodyGo.GetComponent<RectTransform>();
            bodyRt.anchorMin = new Vector2(0.05f, 0.05f);
            bodyRt.anchorMax = new Vector2(0.95f, 0.9f);
            bodyRt.offsetMin = Vector2.zero;
            bodyRt.offsetMax = Vector2.zero;
            RuntimeUiBuildMaterials.TryApplyLegacyTextMaterial(_body);
        }
    }
}
