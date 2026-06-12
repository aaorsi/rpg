#if UNITY_EDITOR
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Rpg.UI;

namespace RpgEditor
{
    /// <summary>
    /// Builds <see cref="StartupTitleScreenView"/> prefab under Resources (TMP + layout). Run after pulling or when UI references change.
    /// </summary>
    public static class StartupTitleScreenPrefabBuilder
    {
        const string PrefabPath = "Assets/Resources/UI/StartupTitleScreenPanel.prefab";

        const string kStandardSpritePath = "UI/Skin/UISprite.psd";
        const string kBackgroundSpritePath = "UI/Skin/Background.psd";
        const string kInputFieldBackgroundPath = "UI/Skin/InputFieldBackground.psd";
        const string kKnobPath = "UI/Skin/Knob.psd";
        const string kCheckmarkPath = "UI/Skin/Checkmark.psd";
        const string kDropdownArrowPath = "UI/Skin/DropdownArrow.psd";
        const string kMaskPath = "UI/Skin/UIMask.psd";

        static TMP_DefaultControls.Resources _uiRes;

        [MenuItem("RPG/Build Startup Title Screen Prefab")]
        public static void Build()
        {
            EnsureUiFolder();
            _uiRes = GetStandardResources();

            var root = new GameObject("StartupTitleScreenPanel", typeof(RectTransform));
            var rootRt = root.GetComponent<RectTransform>();
            StretchFull(rootRt);
            var rootDim = root.AddComponent<Image>();
            rootDim.color = new Color(0.04f, 0.05f, 0.08f, 1f);
            rootDim.raycastTarget = true;
            var view = root.AddComponent<StartupTitleScreenView>();

            var bg = CreateChildImage(root.transform, "TitleBackground", Color.white, false);
            var shade = CreateChildImage(root.transform, "Shade", new Color(0f, 0f, 0f, 0.42f), false);

            var cardShadowGo = new GameObject("CardShadow", typeof(RectTransform), typeof(Image));
            cardShadowGo.transform.SetParent(root.transform, false);
            var csRt = cardShadowGo.GetComponent<RectTransform>();
            csRt.anchorMin = new Vector2(0.5f, 0.5f);
            csRt.anchorMax = new Vector2(0.5f, 0.5f);
            csRt.pivot = new Vector2(0.5f, 0.5f);
            csRt.sizeDelta = new Vector2(788f, 588f);
            csRt.anchoredPosition = new Vector2(8f, 58f);
            var cardShadowImg = cardShadowGo.GetComponent<Image>();
            cardShadowImg.sprite = _uiRes.standard;
            cardShadowImg.type = Image.Type.Sliced;
            cardShadowImg.color = new Color(0f, 0f, 0f, 0.5f);
            cardShadowImg.raycastTarget = false;

            var cardGo = new GameObject("Card", typeof(RectTransform), typeof(Image));
            cardGo.transform.SetParent(root.transform, false);
            var cardRt = cardGo.GetComponent<RectTransform>();
            cardRt.anchorMin = new Vector2(0.5f, 0.5f);
            cardRt.anchorMax = new Vector2(0.5f, 0.5f);
            cardRt.pivot = new Vector2(0.5f, 0.5f);
            cardRt.sizeDelta = new Vector2(780f, 560f);
            cardRt.anchoredPosition = new Vector2(0f, 68f);
            var cardImg = cardGo.GetComponent<Image>();
            cardImg.sprite = _uiRes.standard;
            cardImg.type = Image.Type.Sliced;
            cardImg.color = new Color(0.09f, 0.11f, 0.16f, 0.96f);
            cardImg.raycastTarget = true;
            var card = cardGo;

            var vlg = cardGo.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(28, 28, 24, 24);
            vlg.spacing = 12f;
            vlg.childAlignment = TextAnchor.UpperCenter;
            vlg.childControlHeight = true;
            vlg.childControlWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childForceExpandWidth = true;

            var titleTmp = CreateTmpText(cardGo.transform, "Title", 46, FontStyles.Bold, TextAlignmentOptions.Center);
            titleTmp.color = new Color(0.97f, 0.98f, 1f, 1f);
            AddLayoutElement(titleTmp.gameObject, 56f, -1f);

            var subtitleTmp = CreateTmpText(cardGo.transform, "Subtitle", 20, FontStyles.Normal, TextAlignmentOptions.Center);
            subtitleTmp.text = "Language model connection";
            AddLayoutElement(subtitleTmp.gameObject, 28f, -1f);

            var hintTmp = CreateTmpText(cardGo.transform, "Hint", 17, FontStyles.Normal, TextAlignmentOptions.Top);
            hintTmp.text =
                "This machine uses your local Ollama daemon. Cloud uses an API key from ollama.com/settings/keys — same /api/chat as the desktop app.";
            hintTmp.enableWordWrapping = true;
            hintTmp.alignment = TextAlignmentOptions.Top;
            AddLayoutElement(hintTmp.gameObject, 64f, -1f);

            var modeRow = new GameObject("ModeRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            modeRow.transform.SetParent(cardGo.transform, false);
            var modeH = modeRow.GetComponent<HorizontalLayoutGroup>();
            modeH.spacing = 12f;
            modeH.childAlignment = TextAnchor.MiddleCenter;
            modeH.childControlWidth = true;
            modeH.childForceExpandWidth = true;
            modeH.childControlHeight = true;
            modeH.childForceExpandHeight = true;
            AddLayoutElement(modeRow, 48f, -1f);
            var modeGroup = modeRow.AddComponent<ToggleGroup>();
            modeGroup.allowSwitchOff = false;

            var localT = CreateSegmentToggle(modeRow.transform, "ToggleLocal", "This machine", modeGroup, true);
            var cloudT = CreateSegmentToggle(modeRow.transform, "ToggleCloud", "Ollama Cloud", modeGroup, false);

            var tokenBlock = new GameObject("TokenBlock", typeof(RectTransform), typeof(VerticalLayoutGroup));
            tokenBlock.transform.SetParent(cardGo.transform, false);
            var tokenV = tokenBlock.GetComponent<VerticalLayoutGroup>();
            tokenV.spacing = 6f;
            tokenV.childAlignment = TextAnchor.UpperLeft;
            tokenV.childControlWidth = true;
            tokenV.childForceExpandWidth = true;
            tokenV.childControlHeight = true;
            tokenV.childForceExpandHeight = false;
            AddLayoutElement(tokenBlock, 88f, -1f);

            var tokenLabel = CreateTmpText(tokenBlock.transform, "TokenLabel", 18, FontStyles.Bold, TextAlignmentOptions.Left);
            tokenLabel.text = "API token (cloud only)";
            AddLayoutElement(tokenLabel.gameObject, 22f, -1f);

            var tokenFieldGo = TMP_DefaultControls.CreateInputField(_uiRes);
            tokenFieldGo.name = "TokenInput";
            tokenFieldGo.transform.SetParent(tokenBlock.transform, false);
            var tokenField = tokenFieldGo.GetComponent<TMP_InputField>();
            tokenField.contentType = TMP_InputField.ContentType.Password;
            tokenField.pointSize = 18;
            StyleInputFieldColors(tokenField);
            AssignTmpFontToInput(tokenField);
            if (tokenField.placeholder is TextMeshProUGUI tph)
                tph.text = "Paste your Ollama Cloud API key";
            AddLayoutElement(tokenFieldGo, 44f, -1f);

            var modelRow = new GameObject("ModelRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            modelRow.transform.SetParent(cardGo.transform, false);
            var modelH = modelRow.GetComponent<HorizontalLayoutGroup>();
            modelH.spacing = 12f;
            modelH.childAlignment = TextAnchor.MiddleLeft;
            modelH.childControlWidth = true;
            modelH.childForceExpandWidth = false;
            modelH.childControlHeight = true;
            modelH.childForceExpandHeight = true;
            AddLayoutElement(modelRow, 48f, -1f);

            var modelLab = CreateTmpText(modelRow.transform, "ModelLabel", 18, FontStyles.Bold, TextAlignmentOptions.Left);
            modelLab.text = "Model";
            var modelLabLe = AddLayoutElement(modelLab.gameObject, 44f, 100f);
            modelLabLe.flexibleWidth = 0f;

            var modelFieldGo = TMP_DefaultControls.CreateInputField(_uiRes);
            modelFieldGo.name = "ModelInput";
            modelFieldGo.transform.SetParent(modelRow.transform, false);
            var modelField = modelFieldGo.GetComponent<TMP_InputField>();
            modelField.contentType = TMP_InputField.ContentType.Standard;
            modelField.pointSize = 20;
            StyleInputFieldColors(modelField);
            AssignTmpFontToInput(modelField);
            if (modelField.placeholder is TextMeshProUGUI mph)
                mph.text = "e.g. gemma3:4b";
            var modelFieldLe = AddLayoutElement(modelFieldGo, 44f, -1f);
            modelFieldLe.flexibleWidth = 1f;

            var refreshRow = new GameObject("RefreshRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            refreshRow.transform.SetParent(cardGo.transform, false);
            var refreshH = refreshRow.GetComponent<HorizontalLayoutGroup>();
            refreshH.spacing = 14f;
            refreshH.childAlignment = TextAnchor.MiddleLeft;
            refreshH.childControlWidth = true;
            refreshH.childForceExpandWidth = false;
            refreshH.childControlHeight = true;
            refreshH.childForceExpandHeight = true;
            AddLayoutElement(refreshRow, 52f, -1f);

            var refreshBtnGo = TMP_DefaultControls.CreateButton(_uiRes);
            refreshBtnGo.name = "RefreshModelsButton";
            refreshBtnGo.transform.SetParent(refreshRow.transform, false);
            var refreshBtn = refreshBtnGo.GetComponent<Button>();
            var refreshImg = refreshBtnGo.GetComponent<Image>();
            refreshImg.color = new Color(0.22f, 0.28f, 0.38f, 0.98f);
            var refreshLbl = refreshBtnGo.GetComponentInChildren<TextMeshProUGUI>();
            refreshLbl.text = "Refresh model list";
            refreshLbl.fontSize = 18;
            refreshLbl.color = Color.white;
            refreshLbl.alignment = TextAlignmentOptions.Center;
            if (TMP_Settings.defaultFontAsset != null)
                refreshLbl.font = TMP_Settings.defaultFontAsset;
            AddLayoutElement(refreshBtnGo, 44f, 220f);

            var statusTmp = CreateTmpText(refreshRow.transform, "Status", 15, FontStyles.Normal, TextAlignmentOptions.TopLeft);
            statusTmp.enableWordWrapping = true;
            statusTmp.text = string.Empty;
            var statusLe = AddLayoutElement(statusTmp.gameObject, 44f, -1f);
            statusLe.flexibleWidth = 1f;

            var scrollRoot = new GameObject("ModelScroll", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
            scrollRoot.transform.SetParent(cardGo.transform, false);
            var scrollImg = scrollRoot.GetComponent<Image>();
            scrollImg.sprite = _uiRes.standard;
            scrollImg.type = Image.Type.Sliced;
            scrollImg.color = new Color(0.06f, 0.08f, 0.11f, 0.95f);
            var scroll = scrollRoot.GetComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 28f;
            var scrollLe = AddLayoutElement(scrollRoot, 200f, -1f);
            scrollLe.minHeight = 160f;
            scrollLe.preferredHeight = 200f;

            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
            viewport.transform.SetParent(scrollRoot.transform, false);
            var vpRt = viewport.GetComponent<RectTransform>();
            StretchFull(vpRt);
            vpRt.offsetMin = new Vector2(6f, 6f);
            vpRt.offsetMax = new Vector2(-6f, -6f);
            var vpImg = viewport.GetComponent<Image>();
            vpImg.sprite = _uiRes.mask;
            vpImg.type = Image.Type.Sliced;
            vpImg.color = new Color(0f, 0f, 0f, 0.02f);
            viewport.GetComponent<Mask>().showMaskGraphic = false;

            var content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            content.transform.SetParent(viewport.transform, false);
            var contentRt = content.GetComponent<RectTransform>();
            contentRt.anchorMin = new Vector2(0f, 1f);
            contentRt.anchorMax = new Vector2(1f, 1f);
            contentRt.pivot = new Vector2(0.5f, 1f);
            contentRt.sizeDelta = new Vector2(0f, 0f);
            contentRt.anchoredPosition = Vector2.zero;
            var contentV = content.GetComponent<VerticalLayoutGroup>();
            contentV.spacing = 4f;
            contentV.padding = new RectOffset(6, 6, 6, 6);
            contentV.childAlignment = TextAnchor.UpperCenter;
            contentV.childControlHeight = true;
            contentV.childControlWidth = true;
            contentV.childForceExpandWidth = true;
            contentV.childForceExpandHeight = false;
            content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.viewport = vpRt;
            scroll.content = contentRt;

            var pickTemplate = TMP_DefaultControls.CreateButton(_uiRes);
            pickTemplate.name = "ModelPickRowTemplate";
            pickTemplate.transform.SetParent(root.transform, false);
            pickTemplate.SetActive(false);
            var pickBtn = pickTemplate.GetComponent<Button>();
            var pickImg = pickTemplate.GetComponent<Image>();
            pickImg.color = new Color(0.12f, 0.15f, 0.2f, 0.92f);
            var pickTxt = pickTemplate.GetComponentInChildren<TextMeshProUGUI>();
            pickTxt.text = "model:name";
            pickTxt.fontSize = 16;
            pickTxt.alignment = TextAlignmentOptions.MidlineLeft;
            pickTxt.margin = new Vector4(12f, 0f, 8f, 0f);
            if (TMP_Settings.defaultFontAsset != null)
                pickTxt.font = TMP_Settings.defaultFontAsset;
            var pickLe = pickTemplate.GetComponent<LayoutElement>();
            if (pickLe == null)
                pickLe = pickTemplate.AddComponent<LayoutElement>();
            pickLe.minHeight = 30f;
            pickLe.preferredHeight = 30f;

            var playGo = TMP_DefaultControls.CreateButton(_uiRes);
            playGo.name = "PlayButton";
            playGo.transform.SetParent(root.transform, false);
            var playRt = playGo.GetComponent<RectTransform>();
            playRt.anchorMin = new Vector2(0.5f, 0f);
            playRt.anchorMax = new Vector2(0.5f, 0f);
            playRt.pivot = new Vector2(0.5f, 0f);
            playRt.sizeDelta = new Vector2(300f, 52f);
            playRt.anchoredPosition = new Vector2(0f, 36f);
            var playBtn = playGo.GetComponent<Button>();
            var playImg = playGo.GetComponent<Image>();
            playImg.color = new Color(0.28f, 0.52f, 0.88f, 1f);
            var playLbl = playGo.GetComponentInChildren<TextMeshProUGUI>();
            playLbl.text = "Play";
            playLbl.fontSize = 26;
            playLbl.fontStyle = FontStyles.Bold;
            if (TMP_Settings.defaultFontAsset != null)
                playLbl.font = TMP_Settings.defaultFontAsset;

            var so = new SerializedObject(view);
            so.FindProperty("backgroundImage").objectReferenceValue = bg;
            so.FindProperty("shadeImage").objectReferenceValue = shade;
            so.FindProperty("cardRoot").objectReferenceValue = cardRt;
            so.FindProperty("cardImage").objectReferenceValue = cardImg;
            so.FindProperty("cardShadowImage").objectReferenceValue = cardShadowImg;
            so.FindProperty("titleText").objectReferenceValue = titleTmp;
            so.FindProperty("subtitleText").objectReferenceValue = subtitleTmp;
            so.FindProperty("hintText").objectReferenceValue = hintTmp;
            so.FindProperty("statusText").objectReferenceValue = statusTmp;
            so.FindProperty("modeToggleGroup").objectReferenceValue = modeGroup;
            so.FindProperty("localToggle").objectReferenceValue = localT;
            so.FindProperty("cloudToggle").objectReferenceValue = cloudT;
            so.FindProperty("tokenRow").objectReferenceValue = tokenBlock;
            so.FindProperty("tokenLabel").objectReferenceValue = tokenLabel;
            so.FindProperty("modelLabel").objectReferenceValue = modelLab;
            so.FindProperty("modelInput").objectReferenceValue = modelField;
            so.FindProperty("tokenInput").objectReferenceValue = tokenField;
            so.FindProperty("refreshModelsButton").objectReferenceValue = refreshBtn;
            so.FindProperty("refreshModelsLabel").objectReferenceValue = refreshLbl;
            so.FindProperty("modelScrollRect").objectReferenceValue = scroll;
            so.FindProperty("modelListContent").objectReferenceValue = contentRt;
            so.FindProperty("modelPickRowTemplate").objectReferenceValue = pickTemplate;
            so.FindProperty("playButton").objectReferenceValue = playBtn;
            so.FindProperty("playLabel").objectReferenceValue = playLbl;
            so.ApplyModifiedProperties();

            Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath) ?? "Assets/Resources/UI");
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            AssetDatabase.Refresh();
            Debug.Log($"[StartupTitleScreenPrefabBuilder] Saved {PrefabPath}");
        }

        static void EnsureUiFolder()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Resources"))
                AssetDatabase.CreateFolder("Assets", "Resources");
            if (!AssetDatabase.IsValidFolder("Assets/Resources/UI"))
                AssetDatabase.CreateFolder("Assets/Resources", "UI");
        }

        static TMP_DefaultControls.Resources GetStandardResources()
        {
            var r = new TMP_DefaultControls.Resources
            {
                standard = AssetDatabase.GetBuiltinExtraResource<Sprite>(kStandardSpritePath),
                background = AssetDatabase.GetBuiltinExtraResource<Sprite>(kBackgroundSpritePath),
                inputField = AssetDatabase.GetBuiltinExtraResource<Sprite>(kInputFieldBackgroundPath),
                knob = AssetDatabase.GetBuiltinExtraResource<Sprite>(kKnobPath),
                checkmark = AssetDatabase.GetBuiltinExtraResource<Sprite>(kCheckmarkPath),
                dropdown = AssetDatabase.GetBuiltinExtraResource<Sprite>(kDropdownArrowPath),
                mask = AssetDatabase.GetBuiltinExtraResource<Sprite>(kMaskPath)
            };
            return r;
        }

        static Image CreateChildImage(Transform parent, string name, Color color, bool raycast)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            StretchFull(rt);
            var img = go.GetComponent<Image>();
            img.sprite = _uiRes.standard;
            img.type = Image.Type.Sliced;
            img.color = color;
            img.raycastTarget = raycast;
            return img;
        }

        static void StretchFull(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        static TextMeshProUGUI CreateTmpText(Transform parent, string name, float size, FontStyles style, TextAlignmentOptions align)
        {
            var go = TMP_DefaultControls.CreateText(_uiRes);
            go.name = name;
            go.transform.SetParent(parent, false);
            var tmp = go.GetComponent<TextMeshProUGUI>();
            tmp.text = name;
            tmp.fontSize = size;
            tmp.fontStyle = style;
            tmp.alignment = align;
            tmp.color = Color.white;
            if (TMP_Settings.defaultFontAsset != null)
                tmp.font = TMP_Settings.defaultFontAsset;
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(0f, size + 8f);
            return tmp;
        }

        static LayoutElement AddLayoutElement(GameObject go, float minH, float prefW)
        {
            var le = go.GetComponent<LayoutElement>();
            if (le == null)
                le = go.AddComponent<LayoutElement>();
            le.minHeight = minH;
            if (prefW > 0f)
                le.preferredWidth = prefW;
            return le;
        }

        static Toggle CreateSegmentToggle(Transform parent, string name, string caption, ToggleGroup group, bool isOn)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Toggle));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(0f, 44f);
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = 44f;
            le.flexibleWidth = 1f;

            var bg = go.GetComponent<Image>();
            bg.sprite = _uiRes.standard;
            bg.type = Image.Type.Sliced;
            bg.color = new Color(0.14f, 0.18f, 0.25f, 0.96f);

            var checkGo = new GameObject("Checkmark", typeof(RectTransform), typeof(Image));
            checkGo.transform.SetParent(go.transform, false);
            var ckRt = checkGo.GetComponent<RectTransform>();
            ckRt.anchorMin = new Vector2(0.04f, 0.25f);
            ckRt.anchorMax = new Vector2(0.14f, 0.75f);
            ckRt.offsetMin = Vector2.zero;
            ckRt.offsetMax = Vector2.zero;
            var ckImg = checkGo.GetComponent<Image>();
            ckImg.sprite = _uiRes.checkmark;
            ckImg.color = new Color(0.35f, 0.85f, 0.55f, 1f);

            var tgl = go.GetComponent<Toggle>();
            tgl.targetGraphic = bg;
            tgl.graphic = ckImg;
            tgl.isOn = isOn;
            tgl.group = group;
            tgl.toggleTransition = Toggle.ToggleTransition.Fade;

            var capGo = new GameObject("Caption", typeof(RectTransform));
            capGo.transform.SetParent(go.transform, false);
            var capRt = capGo.GetComponent<RectTransform>();
            capRt.anchorMin = new Vector2(0.16f, 0f);
            capRt.anchorMax = new Vector2(0.98f, 1f);
            capRt.offsetMin = new Vector2(4f, 2f);
            capRt.offsetMax = new Vector2(-8f, -2f);
            var cap = capGo.AddComponent<TextMeshProUGUI>();
            cap.text = caption;
            cap.fontSize = 19;
            cap.color = Color.white;
            cap.alignment = TextAlignmentOptions.MidlineLeft;
            cap.raycastTarget = false;
            if (TMP_Settings.defaultFontAsset != null)
                cap.font = TMP_Settings.defaultFontAsset;

            return tgl;
        }

        static void StyleInputFieldColors(TMP_InputField field)
        {
            if (field.textComponent != null)
                field.textComponent.color = new Color(0.93f, 0.95f, 1f, 1f);
            if (field.placeholder is TextMeshProUGUI ph)
            {
                var c = ph.color;
                c.a = 0.45f;
                ph.color = c;
            }
        }

        static void AssignTmpFontToInput(TMP_InputField field)
        {
            if (TMP_Settings.defaultFontAsset == null)
                return;
            if (field.textComponent != null)
                field.textComponent.font = TMP_Settings.defaultFontAsset;
            if (field.placeholder is TextMeshProUGUI ph)
                ph.font = TMP_Settings.defaultFontAsset;
        }
    }
}
#endif
