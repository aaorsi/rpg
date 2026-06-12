using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Rpg.Audio;
using Rpg.Core;
using Rpg.Dialogue;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Rpg.UI
{
    /// <summary>
    /// Fullscreen title splash with Play button shown before avatar selection/game bootstrap.
    /// UI layout comes from Resources prefab <c>UI/StartupTitleScreenPanel</c> (TMP); build it via <b>RPG → Build Startup Title Screen Prefab</b> in the Editor.
    /// </summary>
    public sealed class StartupTitleScreenStage : MonoBehaviour
    {
        public const string TitlePanelPrefabResource = "UI/StartupTitleScreenPanel";

        Canvas _canvas;
        GameObject _panelRoot;
        StartupTitleScreenView _view;
        Image _backgroundImage;
        Button _playButton;
        bool _finished;
        Texture2D _loadedTexture;
        Sprite _titleSprite;
        AudioSource _uiSfxSource;
        AudioClip _playClickSfx;

        bool _refreshRunning;

        readonly List<(Camera cam, int mask, CameraClearFlags flags, Color bg)> _stashedCameras = new List<(Camera, int, CameraClearFlags, Color)>(4);

        static Sprite _cachedUiWhiteSprite;

        static Sprite GetOrCreateUiWhiteSprite()
        {
            if (_cachedUiWhiteSprite != null)
                return _cachedUiWhiteSprite;
            var t = Texture2D.whiteTexture;
            if (t == null)
                return null;
            _cachedUiWhiteSprite = Sprite.Create(
                t,
                new Rect(0f, 0f, t.width, t.height),
                new Vector2(0.5f, 0.5f),
                100f,
                0u,
                SpriteMeshType.FullRect);
            return _cachedUiWhiteSprite;
        }

        static void AssignWhiteSprite(Image graphic)
        {
            if (graphic == null)
                return;
            var s = GetOrCreateUiWhiteSprite();
            if (s != null)
                graphic.sprite = s;
        }

        static void ConfigureTitleImage(Image graphic)
        {
            AssignWhiteSprite(graphic);
            RuntimeUiBuildMaterials.TryApplySpriteImageMaterial(graphic);
        }

        void PushCamerasForTitleOverlay()
        {
            if (_stashedCameras.Count > 0)
                return;
            foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (cam == null || !cam.enabled || cam.cameraType != CameraType.Game)
                    continue;
                _stashedCameras.Add((cam, cam.cullingMask, cam.clearFlags, cam.backgroundColor));
                cam.cullingMask = 0;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.02f, 0.03f, 0.05f, 1f);
            }
        }

        void PopCamerasForTitleOverlay()
        {
            for (var i = 0; i < _stashedCameras.Count; i++)
            {
                var (cam, mask, flags, bg) = _stashedCameras[i];
                if (cam == null)
                    continue;
                cam.cullingMask = mask;
                cam.clearFlags = flags;
                cam.backgroundColor = bg;
            }
            _stashedCameras.Clear();
        }

        public IEnumerator Run(string windowTitle, string imagePath)
        {
            var title = string.IsNullOrWhiteSpace(windowTitle) ? "RPG Island" : windowTitle.Trim();
            try
            {
                BuildUi(title);
                TryLoadBackground(imagePath);
                PushCamerasForTitleOverlay();
                _finished = false;
                if (_panelRoot != null)
                    _panelRoot.SetActive(true);
                if (_uiSfxSource == null)
                {
                    _uiSfxSource = gameObject.AddComponent<AudioSource>();
                    _uiSfxSource.playOnAwake = false;
                    _uiSfxSource.spatialBlend = 0f;
                }
                if (_playClickSfx == null)
                    _playClickSfx = RuntimeAudioClipLoader.LoadFromAssetPath("Assets/FREE SOUND PACK_TM(355)/UI(27)/Clicks.wav");
                while (!_finished)
                {
                    UpdatePlayInteractable();
                    yield return null;
                }
            }
            finally
            {
                PopCamerasForTitleOverlay();
                Teardown();
            }
        }

        void UpdatePlayInteractable()
        {
            if (_playButton == null)
                return;
            if (_view != null)
            {
                var cloud = _view.CloudToggle != null && _view.CloudToggle.isOn;
                var tokenOk = !cloud || (_view.TokenInput != null && !string.IsNullOrWhiteSpace(_view.TokenInput.text));
                var modelOk = _view.ModelInput != null && !string.IsNullOrWhiteSpace(_view.ModelInput.text);
                _playButton.interactable = tokenOk && modelOk;
            }
        }

        void BuildUi(string gameTitle)
        {
            _canvas = gameObject.GetComponent<Canvas>();
            if (_canvas == null)
                _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 40000;
            if (gameObject.GetComponent<CanvasScaler>() == null)
            {
                var scaler = gameObject.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.matchWidthOrHeight = 0.5f;
            }
            if (gameObject.GetComponent<GraphicRaycaster>() == null)
                gameObject.AddComponent<GraphicRaycaster>();

            if (Object.FindFirstObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem");
                es.AddComponent<EventSystem>();
                es.AddComponent<InputSystemUIInputModule>();
            }

            var prefab = Resources.Load<GameObject>(TitlePanelPrefabResource);
            if (prefab == null)
            {
                Debug.LogError(
                    $"Missing Resources/{TitlePanelPrefabResource}.prefab. In Unity use menu <b>RPG → Build Startup Title Screen Prefab</b>, then save the project.");
                BuildMinimalFallbackTitle(gameTitle);
                return;
            }

            _panelRoot = Instantiate(prefab, transform, false);
            _panelRoot.name = "StartupTitleScreenPanel";
            var prt = _panelRoot.GetComponent<RectTransform>();
            StretchFull(prt);

            _view = _panelRoot.GetComponent<StartupTitleScreenView>();
            if (_view == null)
            {
                Debug.LogError("StartupTitleScreenView component missing on title prefab.");
                Destroy(_panelRoot);
                _panelRoot = null;
                BuildMinimalFallbackTitle(gameTitle);
                return;
            }

            _view.SetTitle(gameTitle, "Language model connection");
            _view.ApplyDarkTheme();

            var defaultModel = OllamaStartupSelection.DefaultModelId;
            var asset = Resources.Load<OllamaSettings>(GameConstants.DefaultOllamaSettingsResource);
            if (asset != null && !string.IsNullOrWhiteSpace(asset.model))
                defaultModel = asset.model.Trim();
            _view.SetDefaultModel(defaultModel);

            if (_view.LocalToggle != null)
                _view.LocalToggle.onValueChanged.AddListener(_ => RefreshCloudUi());
            if (_view.CloudToggle != null)
                _view.CloudToggle.onValueChanged.AddListener(_ => RefreshCloudUi());

            if (_view.RefreshModelsButton != null)
            {
                _view.RefreshModelsButton.onClick.RemoveAllListeners();
                _view.RefreshModelsButton.onClick.AddListener(() => StartCoroutine(CoRefreshModelTags()));
            }

            if (_view.PlayButton != null)
            {
                _playButton = _view.PlayButton;
                _playButton.onClick.RemoveAllListeners();
                _playButton.onClick.AddListener(OnPlayClicked);
            }

            _backgroundImage = _view.BackgroundImage;
            RefreshCloudUi();
        }

        /// <summary>Lets the player continue with local defaults if the prefab was not generated yet.</summary>
        void BuildMinimalFallbackTitle(string gameTitle)
        {
            _panelRoot = new GameObject("StartupTitleFallback");
            _panelRoot.transform.SetParent(transform, false);
            var prt = _panelRoot.AddComponent<RectTransform>();
            StretchFull(prt);
            var dim = _panelRoot.AddComponent<Image>();
            dim.color = new Color(0.05f, 0.06f, 0.09f, 1f);
            ConfigureTitleImage(dim);

            var msgGo = new GameObject("Message");
            msgGo.transform.SetParent(_panelRoot.transform, false);
            var msgRt = msgGo.AddComponent<RectTransform>();
            msgRt.anchorMin = new Vector2(0.08f, 0.45f);
            msgRt.anchorMax = new Vector2(0.92f, 0.62f);
            msgRt.offsetMin = Vector2.zero;
            msgRt.offsetMax = Vector2.zero;
            var msg = msgGo.AddComponent<TextMeshProUGUI>();
            msg.fontSize = 22;
            msg.alignment = TextAlignmentOptions.Center;
            msg.enableWordWrapping = true;
            msg.color = new Color(0.95f, 0.96f, 1f, 1f);
            if (TMP_Settings.defaultFontAsset != null)
                msg.font = TMP_Settings.defaultFontAsset;
            msg.text =
                $"<b>{gameTitle}</b>\n\nTitle UI prefab is missing.\nUse menu <b>RPG → Build Startup Title Screen Prefab</b> in Unity, then press Play again.\n\n" +
                "You can still continue with <b>local Ollama</b> and model <b>gemma3:4b</b>.";

            var playGo = new GameObject("PlayButton");
            playGo.transform.SetParent(_panelRoot.transform, false);
            var playImg = playGo.AddComponent<Image>();
            playImg.color = new Color(0.28f, 0.52f, 0.88f, 1f);
            ConfigureTitleImage(playImg);
            _playButton = playGo.AddComponent<Button>();
            _playButton.targetGraphic = playImg;
            var brt = playGo.GetComponent<RectTransform>();
            brt.anchorMin = new Vector2(0.38f, 0.18f);
            brt.anchorMax = new Vector2(0.62f, 0.26f);
            brt.offsetMin = Vector2.zero;
            brt.offsetMax = Vector2.zero;
            var plGo = new GameObject("Label");
            plGo.transform.SetParent(playGo.transform, false);
            var pl = plGo.AddComponent<TextMeshProUGUI>();
            pl.text = "Continue (defaults)";
            pl.fontSize = 24;
            pl.alignment = TextAlignmentOptions.Center;
            pl.color = Color.white;
            if (TMP_Settings.defaultFontAsset != null)
                pl.font = TMP_Settings.defaultFontAsset;
            var plRt = plGo.GetComponent<RectTransform>();
            plRt.anchorMin = Vector2.zero;
            plRt.anchorMax = Vector2.one;
            plRt.offsetMin = Vector2.zero;
            plRt.offsetMax = Vector2.zero;
            _playButton.onClick.AddListener(() =>
            {
                OllamaStartupSelection.CaptureFromTitle(OllamaHostKind.Local, OllamaStartupSelection.DefaultModelId, string.Empty);
                if (_uiSfxSource != null && _playClickSfx != null)
                    _uiSfxSource.PlayOneShot(_playClickSfx);
                _finished = true;
            });
        }

        static void StretchFull(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        void OnPlayClicked()
        {
            if (_view == null)
                return;
            if (_view.CloudToggle != null && _view.CloudToggle.isOn &&
                (_view.TokenInput == null || string.IsNullOrWhiteSpace(_view.TokenInput.text)))
                return;
            if (_view.ModelInput == null || string.IsNullOrWhiteSpace(_view.ModelInput.text))
                return;

            var mode = _view.CloudToggle != null && _view.CloudToggle.isOn ? OllamaHostKind.Cloud : OllamaHostKind.Local;
            var model = _view.ModelInput.text.Trim();
            var token = _view.TokenInput != null ? _view.TokenInput.text.Trim() : string.Empty;
            OllamaStartupSelection.CaptureFromTitle(mode, model, token);

            if (_uiSfxSource != null && _playClickSfx != null)
                _uiSfxSource.PlayOneShot(_playClickSfx);
            _finished = true;
        }

        void RefreshCloudUi()
        {
            if (_view == null)
                return;
            var cloud = _view.CloudToggle != null && _view.CloudToggle.isOn;
            if (_view.TokenRow != null)
                _view.TokenRow.SetActive(cloud);
            if (cloud && _view.ModelInput != null && string.IsNullOrWhiteSpace(_view.ModelInput.text))
                _view.ModelInput.text = OllamaStartupSelection.DefaultModelId;
            UpdatePlayInteractable();
        }

        IEnumerator CoRefreshModelTags()
        {
            if (_refreshRunning || _view == null)
                yield break;
            _refreshRunning = true;
            if (_view.RefreshModelsButton != null)
                _view.RefreshModelsButton.interactable = false;
            if (_view.StatusText != null)
                _view.StatusText.text = "Loading models…";

            var probe = ScriptableObject.CreateInstance<OllamaSettings>();
            try
            {
                if (_view.CloudToggle != null && _view.CloudToggle.isOn)
                {
                    probe.baseUrl = OllamaStartupSelection.OllamaCloudBaseUrl;
                    probe.ollamaApiBearerToken = _view.TokenInput != null ? _view.TokenInput.text : string.Empty;
                }
                else
                    probe.baseUrl = "http://127.0.0.1:11434";
                probe.timeoutSeconds = 30;

                var task = OllamaClient.FetchTagModelNamesAsync(probe, CancellationToken.None);
                while (!task.IsCompleted)
                    yield return null;

                ClearModelPickRows();
                if (task.IsFaulted)
                {
                    if (_view.StatusText != null)
                        _view.StatusText.text = task.Exception != null ? task.Exception.GetBaseException().Message : "Request failed.";
                    yield break;
                }

                var r = task.Result;
                if (!r.IsSuccess)
                {
                    if (_view.StatusText != null)
                        _view.StatusText.text = r.Error ?? "Failed to load tags.";
                    yield break;
                }

                if (_view.StatusText != null)
                    _view.StatusText.text = $"Found {r.ModelNames.Count} model(s). Tap a name to copy into the field.";

                foreach (var name in r.ModelNames)
                    AddModelPickButton(name);
            }
            finally
            {
                if (probe != null)
                    Object.Destroy(probe);
                _refreshRunning = false;
                if (_view != null && _view.RefreshModelsButton != null)
                    _view.RefreshModelsButton.interactable = true;
            }
        }

        void ClearModelPickRows()
        {
            if (_view?.ModelListContent == null)
                return;
            for (var i = _view.ModelListContent.childCount - 1; i >= 0; i--)
                Destroy(_view.ModelListContent.GetChild(i).gameObject);
        }

        void AddModelPickButton(string modelName)
        {
            if (_view == null || _view.ModelListContent == null || string.IsNullOrEmpty(modelName))
                return;

            var row = new GameObject("PickRow", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            row.transform.SetParent(_view.ModelListContent, false);
            row.name = "Pick_" + modelName;
            row.SetActive(true);

            var rowRt = row.GetComponent<RectTransform>();
            rowRt.anchorMin = new Vector2(0f, 1f);
            rowRt.anchorMax = new Vector2(1f, 1f);
            rowRt.pivot = new Vector2(0.5f, 1f);
            rowRt.sizeDelta = new Vector2(0f, 32f);

            var rowLe = row.GetComponent<LayoutElement>();
            rowLe.minHeight = 30f;
            rowLe.preferredHeight = 32f;

            var rowImg = row.GetComponent<Image>();
            rowImg.color = new Color(0.13f, 0.17f, 0.24f, 0.96f);
            RuntimeUiBuildMaterials.TryApplySpriteImageMaterial(rowImg);

            var btn = row.GetComponent<Button>();
            if (btn != null)
            {
                btn.targetGraphic = rowImg;
                btn.onClick.RemoveAllListeners();
            }

            var labelGo = new GameObject("Label", typeof(RectTransform));
            labelGo.transform.SetParent(row.transform, false);
            var labelRt = labelGo.GetComponent<RectTransform>();
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.one;
            labelRt.offsetMin = new Vector2(10f, 0f);
            labelRt.offsetMax = new Vector2(-8f, 0f);

            var label = labelGo.AddComponent<Text>();
            label.font = RuntimeUiBuildMaterials.BuiltinUiFont();
            label.fontSize = 17;
            label.color = new Color(0.96f, 0.98f, 1f, 1f);
            label.alignment = TextAnchor.MiddleLeft;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            label.supportRichText = false;
            label.raycastTarget = false;
            label.text = "  " + modelName;
            RuntimeUiBuildMaterials.TryApplyLegacyTextMaterial(label);

            if (_view.ModelPickRowTemplate != null)
            {
                var templateBtn = _view.ModelPickRowTemplate.GetComponent<Button>();
                if (templateBtn != null && btn != null)
                {
                    var colors = templateBtn.colors;
                    colors.normalColor = new Color(0.13f, 0.17f, 0.24f, 0.96f);
                    colors.highlightedColor = new Color(0.2f, 0.25f, 0.34f, 1f);
                    colors.pressedColor = new Color(0.09f, 0.12f, 0.18f, 1f);
                    colors.disabledColor = new Color(0.1f, 0.1f, 0.1f, 0.4f);
                    btn.colors = colors;
                }
            }

            var captured = modelName;
            if (btn != null)
            {
                btn.onClick.AddListener(() =>
                {
                    if (_view.ModelInput != null)
                        _view.ModelInput.text = captured;
                    UpdatePlayInteractable();
                });
            }
        }

        void TryLoadBackground(string path)
        {
            if (_backgroundImage == null)
                return;

            string resolved = null;
            var trimmed = path?.Trim() ?? string.Empty;
            if (!string.IsNullOrEmpty(trimmed) && File.Exists(trimmed))
                resolved = trimmed;
            else
            {
                var streamPath = Path.Combine(Application.streamingAssetsPath, "IslandTitle.png");
                if (File.Exists(streamPath))
                    resolved = streamPath;
                else
                {
                    var parentOfData = Directory.GetParent(Application.dataPath)?.FullName ?? string.Empty;
                    var nextToData = Path.Combine(parentOfData, "IslandTitle.png");
                    if (File.Exists(nextToData))
                        resolved = nextToData;
                }
            }

            if (string.IsNullOrEmpty(resolved) || !File.Exists(resolved))
                return;

            var bytes = File.ReadAllBytes(resolved);
            if (bytes == null || bytes.Length == 0)
                return;
            var loaded = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!loaded.LoadImage(bytes, false))
            {
                Destroy(loaded);
                return;
            }

            loaded.wrapMode = TextureWrapMode.Clamp;
            loaded.filterMode = FilterMode.Bilinear;
            if (_titleSprite != null)
                Destroy(_titleSprite);
            if (_loadedTexture != null)
                Destroy(_loadedTexture);
            _loadedTexture = loaded;
            _titleSprite = Sprite.Create(
                _loadedTexture,
                new Rect(0f, 0f, _loadedTexture.width, _loadedTexture.height),
                new Vector2(0.5f, 0.5f),
                100f,
                0u,
                SpriteMeshType.FullRect);
            _backgroundImage.sprite = _titleSprite;
        }

        void Teardown()
        {
            if (_titleSprite != null)
            {
                Destroy(_titleSprite);
                _titleSprite = null;
            }
            if (_loadedTexture != null)
            {
                Destroy(_loadedTexture);
                _loadedTexture = null;
            }
            if (_panelRoot != null)
                Destroy(_panelRoot);
            _panelRoot = null;
            _view = null;
            _playButton = null;
            _backgroundImage = null;
        }
    }
}
