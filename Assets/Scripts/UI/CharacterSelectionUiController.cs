using System.Collections.Generic;
using Rpg.Core;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.UI;

namespace Rpg.UI
{
    /// <summary>
    /// Fullscreen startup avatar picker with a live upper-body preview.
    /// </summary>
    public sealed class CharacterSelectionUiController : MonoBehaviour
    {
        const int FontSize = 26;

        static Font BuiltinUiFont() => RuntimeUiBuildMaterials.BuiltinUiFont();

        Canvas _canvas;
        GameObject _panel;
        Transform _choicesRoot;
        RawImage _previewImage;
        Text _hint;

        readonly List<GameObject> _choices = new();
        GameObject _selected;
        bool _finished;

        RenderTexture _previewRt;
        GameObject _previewRoot;
        PreviewClipPlayer _previewPlayer;

        public bool IsOpen => _panel != null && _panel.activeSelf;
        public bool IsFinished => _finished;
        public GameObject Selected => _selected;

        void Awake()
        {
            BuildUi();
            Close();
        }

        public void Open(IReadOnlyList<GameObject> candidates)
        {
            _choices.Clear();
            if (candidates != null)
            {
                foreach (var c in candidates)
                {
                    if (c != null)
                        _choices.Add(c);
                }
            }

            _selected = null;
            _finished = false;
            RebuildChoiceButtons();
            _panel.SetActive(true);
            _hint.text = _choices.Count > 0
                ? "Choose your avatar"
                : "No mixamo characters found. Continuing with fallback.";
            if (_choices.Count > 0)
                ShowPreview(_choices[0], 0);
        }

        public void Close()
        {
            if (_panel != null)
                _panel.SetActive(false);
            DestroyPreviewScene();
        }

        public void ConfirmFallbackAndClose()
        {
            _selected = null;
            _finished = true;
            Close();
        }

        void RebuildChoiceButtons()
        {
            for (var i = _choicesRoot.childCount - 1; i >= 0; i--)
                Destroy(_choicesRoot.GetChild(i).gameObject);

            for (var i = 0; i < _choices.Count; i++)
            {
                var prefab = _choices[i];
                var idx = i;
                var go = new GameObject(prefab.name + "_Button");
                go.transform.SetParent(_choicesRoot, false);
                var img = go.AddComponent<Image>();
                img.color = new Color(0.16f, 0.19f, 0.24f, 1f);
                RuntimeUiBuildMaterials.TryApplySpriteImageMaterial(img);
                var btn = go.AddComponent<Button>();
                btn.targetGraphic = img;
                btn.onClick.AddListener(() =>
                {
                    ShowPreview(prefab, idx);
                    _selected = prefab;
                    _finished = true;
                    Close();
                });

                var txtGo = new GameObject("Text");
                txtGo.transform.SetParent(go.transform, false);
                var txt = txtGo.AddComponent<Text>();
                txt.font = BuiltinUiFont();
                txt.fontSize = 20;
                txt.alignment = TextAnchor.MiddleCenter;
                txt.color = Color.white;
                txt.text = prefab.name;
                RuntimeUiBuildMaterials.TryApplyLegacyTextMaterial(txt);
                var trt = txt.rectTransform;
                trt.anchorMin = Vector2.zero;
                trt.anchorMax = Vector2.one;
                trt.offsetMin = Vector2.zero;
                trt.offsetMax = Vector2.zero;

                var rt = go.GetComponent<RectTransform>();
                rt.sizeDelta = new Vector2(360f, 68f);
            }
        }

        void ShowPreview(GameObject prefab, int index)
        {
            if (prefab == null)
                return;

            EnsurePreviewScene();
            for (var i = _previewRoot.transform.childCount - 1; i >= 2; i--)
                Destroy(_previewRoot.transform.GetChild(i).gameObject);

            var instance = Instantiate(prefab, _previewRoot.transform, false);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.Euler(0f, 175f, 0f);
            instance.transform.localScale = Vector3.one;

            var anim = instance.GetComponentInChildren<Animator>(true);
            if (anim != null)
            {
                var selection = MixamoAnimationCatalog.GetSelection();
                var idle = index % 2 == 0 ? selection.IdleA : selection.IdleB;
                _previewPlayer.Set(anim, idle ?? selection.IdleA);
            }
        }

        void EnsurePreviewScene()
        {
            if (_previewRoot != null)
                return;

            _previewRt = new RenderTexture(1024, 1024, 24) { antiAliasing = 2 };
            _previewRoot = new GameObject("CharacterSelectionPreviewRoot");
            _previewRoot.hideFlags = HideFlags.HideAndDontSave;
            var lightGo = new GameObject("PreviewLight");
            lightGo.transform.SetParent(_previewRoot.transform, false);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.15f;
            lightGo.transform.rotation = Quaternion.Euler(45f, -28f, 0f);

            var camGo = new GameObject("PreviewCamera");
            camGo.transform.SetParent(_previewRoot.transform, false);
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.09f, 0.11f, 0.14f, 1f);
            cam.targetTexture = _previewRt;
            cam.orthographic = false;
            cam.fieldOfView = 28f;
            camGo.transform.localPosition = new Vector3(0f, 1.42f, -2.35f);
            camGo.transform.localRotation = Quaternion.Euler(9f, 0f, 0f);

            _previewPlayer = _previewRoot.AddComponent<PreviewClipPlayer>();
            _previewImage.texture = _previewRt;
        }

        void DestroyPreviewScene()
        {
            if (_previewRoot != null)
                Destroy(_previewRoot);
            _previewRoot = null;
            if (_previewRt != null)
                Destroy(_previewRt);
            _previewRt = null;
            if (_previewImage != null)
                _previewImage.texture = null;
        }

        void BuildUi()
        {
            _canvas = gameObject.GetComponent<Canvas>();
            if (_canvas == null)
                _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 300;
            if (gameObject.GetComponent<CanvasScaler>() == null)
            {
                var scaler = gameObject.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.matchWidthOrHeight = 0.5f;
            }

            if (gameObject.GetComponent<GraphicRaycaster>() == null)
                gameObject.AddComponent<GraphicRaycaster>();

            _panel = new GameObject("CharacterSelectionPanel");
            _panel.transform.SetParent(transform, false);
            var panelImg = _panel.AddComponent<Image>();
            panelImg.color = new Color(0.03f, 0.04f, 0.06f, 0.98f);
            RuntimeUiBuildMaterials.TryApplySpriteImageMaterial(panelImg);
            var prt = _panel.GetComponent<RectTransform>();
            prt.anchorMin = Vector2.zero;
            prt.anchorMax = Vector2.one;
            prt.offsetMin = Vector2.zero;
            prt.offsetMax = Vector2.zero;

            var title = new GameObject("Title");
            title.transform.SetParent(_panel.transform, false);
            var titleText = title.AddComponent<Text>();
            titleText.font = BuiltinUiFont();
            titleText.fontSize = FontSize + 6;
            titleText.color = Color.white;
            titleText.alignment = TextAnchor.MiddleCenter;
            titleText.text = "Choose Your Avatar";
            RuntimeUiBuildMaterials.TryApplyLegacyTextMaterial(titleText);
            var trt = title.GetComponent<RectTransform>();
            trt.anchorMin = new Vector2(0.2f, 0.89f);
            trt.anchorMax = new Vector2(0.8f, 0.98f);
            trt.offsetMin = Vector2.zero;
            trt.offsetMax = Vector2.zero;

            var previewGo = new GameObject("Preview");
            previewGo.transform.SetParent(_panel.transform, false);
            _previewImage = previewGo.AddComponent<RawImage>();
            RuntimeUiBuildMaterials.TryApplySpriteImageMaterial(_previewImage);
            var pr = previewGo.GetComponent<RectTransform>();
            pr.anchorMin = new Vector2(0.25f, 0.36f);
            pr.anchorMax = new Vector2(0.75f, 0.86f);
            pr.offsetMin = Vector2.zero;
            pr.offsetMax = Vector2.zero;

            var hintGo = new GameObject("Hint");
            hintGo.transform.SetParent(_panel.transform, false);
            _hint = hintGo.AddComponent<Text>();
            _hint.font = BuiltinUiFont();
            _hint.fontSize = 20;
            _hint.color = new Color(0.82f, 0.9f, 1f, 1f);
            _hint.alignment = TextAnchor.MiddleCenter;
            var hrt = hintGo.GetComponent<RectTransform>();
            hrt.anchorMin = new Vector2(0.2f, 0.31f);
            hrt.anchorMax = new Vector2(0.8f, 0.35f);
            hrt.offsetMin = Vector2.zero;
            hrt.offsetMax = Vector2.zero;
            RuntimeUiBuildMaterials.TryApplyLegacyTextMaterial(_hint);

            var scroll = new GameObject("ChoiceScroll");
            scroll.transform.SetParent(_panel.transform, false);
            var srt = scroll.AddComponent<RectTransform>();
            srt.anchorMin = new Vector2(0.14f, 0.05f);
            srt.anchorMax = new Vector2(0.86f, 0.28f);
            srt.offsetMin = Vector2.zero;
            srt.offsetMax = Vector2.zero;
            var sr = scroll.AddComponent<ScrollRect>();
            sr.horizontal = false;
            sr.vertical = true;
            sr.movementType = ScrollRect.MovementType.Clamped;

            var viewport = new GameObject("Viewport");
            viewport.transform.SetParent(scroll.transform, false);
            var vprt = viewport.AddComponent<RectTransform>();
            vprt.anchorMin = Vector2.zero;
            vprt.anchorMax = Vector2.one;
            vprt.offsetMin = Vector2.zero;
            vprt.offsetMax = Vector2.zero;
            viewport.AddComponent<RectMask2D>();
            var vpBg = viewport.AddComponent<Image>();
            vpBg.color = new Color(0f, 0f, 0f, 0.25f);
            RuntimeUiBuildMaterials.TryApplySpriteImageMaterial(vpBg);
            sr.viewport = vprt;

            var content = new GameObject("Content");
            content.transform.SetParent(viewport.transform, false);
            var crt = content.AddComponent<RectTransform>();
            crt.anchorMin = new Vector2(0f, 1f);
            crt.anchorMax = new Vector2(1f, 1f);
            crt.pivot = new Vector2(0.5f, 1f);
            sr.content = crt;
            _choicesRoot = content.transform;

            var vlg = content.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 10f;
            vlg.childControlHeight = false;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth = true;
            vlg.childForceExpandWidth = true;
            var fitter = content.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var skipGo = new GameObject("SkipButton");
            skipGo.transform.SetParent(_panel.transform, false);
            var sbtnImg = skipGo.AddComponent<Image>();
            sbtnImg.color = new Color(0.25f, 0.2f, 0.2f, 1f);
            RuntimeUiBuildMaterials.TryApplySpriteImageMaterial(sbtnImg);
            var sbtn = skipGo.AddComponent<Button>();
            sbtn.targetGraphic = sbtnImg;
            sbtn.onClick.AddListener(ConfirmFallbackAndClose);
            var sbrt = skipGo.GetComponent<RectTransform>();
            sbrt.anchorMin = new Vector2(0.82f, 0.91f);
            sbrt.anchorMax = new Vector2(0.96f, 0.975f);
            sbrt.offsetMin = Vector2.zero;
            sbrt.offsetMax = Vector2.zero;
            var sbtnTxt = new GameObject("Text");
            sbtnTxt.transform.SetParent(skipGo.transform, false);
            var sbt = sbtnTxt.AddComponent<Text>();
            sbt.font = BuiltinUiFont();
            sbt.fontSize = 18;
            sbt.alignment = TextAnchor.MiddleCenter;
            sbt.color = Color.white;
            sbt.text = "Skip";
            RuntimeUiBuildMaterials.TryApplyLegacyTextMaterial(sbt);
            var sbtr = sbtnTxt.GetComponent<RectTransform>();
            sbtr.anchorMin = Vector2.zero;
            sbtr.anchorMax = Vector2.one;
            sbtr.offsetMin = Vector2.zero;
            sbtr.offsetMax = Vector2.zero;
        }

        sealed class PreviewClipPlayer : MonoBehaviour
        {
            PlayableGraph _graph;

            public void Set(Animator animator, AnimationClip clip)
            {
                if (animator == null || clip == null)
                    return;
                if (_graph.IsValid())
                    _graph.Destroy();

                animator.runtimeAnimatorController = null;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                _graph = PlayableGraph.Create("CharacterPreviewGraph");
                _graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
                var output = AnimationPlayableOutput.Create(_graph, "Preview", animator);
                var playable = AnimationClipPlayable.Create(_graph, clip);
                playable.SetApplyFootIK(true);
                playable.SetTime(0f);
                output.SetSourcePlayable(playable);
                _graph.Play();
            }

            void OnDestroy()
            {
                if (_graph.IsValid())
                    _graph.Destroy();
            }
        }
    }
}
