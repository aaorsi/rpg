using System;
using Rpg.Core;
using Rpg.Player;
using UnityEngine;
using UnityEngine.UI;

namespace Rpg.UI
{
    /// <summary>
    /// Top-right HUD: health and hunger bars built at runtime (uGUI Image + procedural sprite) so player
    /// builds do not depend on InfinityPBR RawImage textures that can render magenta when stripped.
    /// Press <c>H</c> to show "Health" / "Food" labels.
    /// </summary>
    public sealed class HeroHealthBarHud : MonoBehaviour
    {
        public static HeroHealthBarHud Instance { get; private set; }

        [SerializeField]
        int canvasSortingOrder = 105;

        [SerializeField]
        float cornerMarginPixels = 20f;

        [SerializeField]
        float verticalGapBetweenBarsPixels = 8f;

        [SerializeField]
        Vector2 barReferenceSize = new Vector2(260f, 24f);

        [SerializeField]
        float legendLabelWidthPixels = 72f;

        static readonly Color HealthFillColor = new Color(0.18f, 0.82f, 0.32f, 1f);
        static readonly Color HungerFillColor = new Color(0.95f, 0.62f, 0.12f, 1f);
        static readonly Color TrackColor = new Color(0.12f, 0.12f, 0.14f, 0.92f);
        static readonly Color BorderColor = new Color(0.35f, 0.38f, 0.42f, 0.95f);

        SimpleFillBar _healthBar;
        SimpleFillBar _hungerBar;
        Text _healthLegend;
        Text _foodLegend;
        RectTransform _stackRt;
        bool _legendVisible;

        HeroHealth _hero;
        HeroHunger _hunger;

        static Sprite _whiteSprite;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }

            Instance = this;
            TryBuildHud();
        }

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
            UnsubscribeHero();
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.H))
            {
                _legendVisible = !_legendVisible;
                ApplyLegendVisibility();
            }
        }

        void LateUpdate() => TryBindHeroIfNeeded();

        void TryBuildHud()
        {
            var hudRoot = new GameObject("HeroHealthBarHudRoot");
            hudRoot.transform.SetParent(transform, false);
            var stretch = hudRoot.AddComponent<RectTransform>();
            stretch.anchorMin = Vector2.zero;
            stretch.anchorMax = Vector2.one;
            stretch.offsetMin = Vector2.zero;
            stretch.offsetMax = Vector2.zero;

            var canvas = hudRoot.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = canvasSortingOrder;
            hudRoot.AddComponent<GraphicRaycaster>();

            var scaler = hudRoot.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            var stack = new GameObject("VitalityBarsStack");
            stack.transform.SetParent(hudRoot.transform, false);
            _stackRt = stack.AddComponent<RectTransform>();
            _stackRt.anchorMin = new Vector2(1f, 1f);
            _stackRt.anchorMax = new Vector2(1f, 1f);
            _stackRt.pivot = new Vector2(1f, 1f);
            _stackRt.anchoredPosition = new Vector2(-cornerMarginPixels, -cornerMarginPixels);

            var rowH = CreateBarRow(stack.transform, "HealthRow", 0f, out _healthLegend, "Health");
            var rowF = CreateBarRow(stack.transform, "FoodRow", -(barReferenceSize.y + verticalGapBetweenBarsPixels), out _foodLegend, "Food");

            _healthBar = CreateRuntimeFillBar(rowH, HealthFillColor);
            _hungerBar = CreateRuntimeFillBar(rowF, HungerFillColor);

            ApplyLegendVisibility();
            UpdateStackWidthForLegendState();
        }

        Transform CreateBarRow(Transform parent, string rowName, float anchoredY, out Text legend, string legendText)
        {
            var row = new GameObject(rowName);
            row.transform.SetParent(parent, false);
            var rowRt = row.AddComponent<RectTransform>();
            rowRt.anchorMin = new Vector2(0f, 1f);
            rowRt.anchorMax = new Vector2(1f, 1f);
            rowRt.pivot = new Vector2(1f, 1f);
            rowRt.anchoredPosition = new Vector2(0f, anchoredY);
            rowRt.sizeDelta = new Vector2(0f, barReferenceSize.y);

            var legGo = new GameObject("Legend");
            legGo.transform.SetParent(row.transform, false);
            legend = legGo.AddComponent<Text>();
            legend.font = RuntimeUiBuildMaterials.BuiltinUiFont();
            legend.fontSize = 16;
            legend.color = new Color(0.92f, 0.94f, 0.98f, 1f);
            legend.alignment = TextAnchor.MiddleRight;
            legend.text = legendText;
            legend.raycastTarget = false;
            RuntimeUiBuildMaterials.TryApplyLegacyTextMaterial(legend);
            var legRt = legGo.GetComponent<RectTransform>();
            legRt.anchorMin = new Vector2(0f, 0f);
            legRt.anchorMax = new Vector2(0f, 1f);
            legRt.pivot = new Vector2(0f, 0.5f);
            legRt.sizeDelta = new Vector2(legendLabelWidthPixels, 0f);
            legRt.anchoredPosition = Vector2.zero;

            return row.transform;
        }

        SimpleFillBar CreateRuntimeFillBar(Transform row, Color fillColor)
        {
            var root = new GameObject("RuntimeVitalityBar");
            root.transform.SetParent(row, false);
            var rootRt = root.AddComponent<RectTransform>();
            rootRt.anchorMin = new Vector2(1f, 0.5f);
            rootRt.anchorMax = new Vector2(1f, 0.5f);
            rootRt.pivot = new Vector2(1f, 0.5f);
            rootRt.anchoredPosition = Vector2.zero;
            rootRt.sizeDelta = barReferenceSize;

            var sprite = GetOrCreateWhiteSprite();

            var borderGo = new GameObject("Border");
            borderGo.transform.SetParent(root.transform, false);
            var borderRt = borderGo.AddComponent<RectTransform>();
            borderRt.anchorMin = Vector2.zero;
            borderRt.anchorMax = Vector2.one;
            borderRt.offsetMin = new Vector2(-1.5f, -1.5f);
            borderRt.offsetMax = new Vector2(1.5f, 1.5f);
            var borderImg = borderGo.AddComponent<Image>();
            borderImg.sprite = sprite;
            borderImg.type = Image.Type.Simple;
            borderImg.color = BorderColor;
            borderImg.raycastTarget = false;
            RuntimeUiBuildMaterials.TryApplySpriteImageMaterial(borderImg);

            var trackGo = new GameObject("Track");
            trackGo.transform.SetParent(root.transform, false);
            var trackRt = trackGo.AddComponent<RectTransform>();
            trackRt.anchorMin = Vector2.zero;
            trackRt.anchorMax = Vector2.one;
            trackRt.offsetMin = Vector2.zero;
            trackRt.offsetMax = Vector2.zero;
            var trackImg = trackGo.AddComponent<Image>();
            trackImg.sprite = sprite;
            trackImg.type = Image.Type.Simple;
            trackImg.color = TrackColor;
            trackImg.raycastTarget = false;
            RuntimeUiBuildMaterials.TryApplySpriteImageMaterial(trackImg);

            var fillGo = new GameObject("Fill");
            fillGo.transform.SetParent(root.transform, false);
            var fillRt = fillGo.AddComponent<RectTransform>();
            fillRt.anchorMin = new Vector2(0f, 0.1f);
            fillRt.anchorMax = new Vector2(1f, 0.9f);
            fillRt.offsetMin = Vector2.zero;
            fillRt.offsetMax = Vector2.zero;
            var fillImg = fillGo.AddComponent<Image>();
            fillImg.sprite = sprite;
            fillImg.type = Image.Type.Simple;
            fillImg.color = fillColor;
            fillImg.raycastTarget = false;
            RuntimeUiBuildMaterials.TryApplySpriteImageMaterial(fillImg);

            return new SimpleFillBar(fillRt);
        }

        static Sprite GetOrCreateWhiteSprite()
        {
            if (_whiteSprite != null)
                return _whiteSprite;
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            for (var y = 0; y < 2; y++)
            for (var x = 0; x < 2; x++)
                tex.SetPixel(x, y, Color.white);
            tex.Apply(false, true);
            _whiteSprite = Sprite.Create(tex, new Rect(0f, 0f, 2f, 2f), new Vector2(0.5f, 0.5f), 100f);
            return _whiteSprite;
        }

        void ApplyLegendVisibility()
        {
            if (_healthLegend != null)
                _healthLegend.gameObject.SetActive(_legendVisible);
            if (_foodLegend != null)
                _foodLegend.gameObject.SetActive(_legendVisible);
            UpdateStackWidthForLegendState();
        }

        void UpdateStackWidthForLegendState()
        {
            if (_stackRt == null)
                return;
            var labelBlock = _legendVisible ? legendLabelWidthPixels + 6f : 0f;
            _stackRt.sizeDelta = new Vector2(labelBlock + barReferenceSize.x, barReferenceSize.y * 2f + verticalGapBetweenBarsPixels);
        }

        void TryBindHeroIfNeeded()
        {
            if (_healthBar == null || _hungerBar == null)
                return;

            var player = GameObject.FindGameObjectWithTag(GameConstants.PlayerTag);
            if (player == null)
            {
                UnsubscribeHero();
                return;
            }

            if (!player.TryGetComponent<HeroHealth>(out var health)
                || !player.TryGetComponent<HeroHunger>(out var hunger))
            {
                UnsubscribeHero();
                return;
            }

            if (_hero == health && _hunger == hunger)
                return;

            UnsubscribeHero();
            _hero = health;
            _hunger = hunger;
            _hero.HealthChanged += OnHeroHealthChanged;
            _hunger.HungerChanged += OnHungerChanged;
            OnHeroHealthChanged();
            OnHungerChanged();
        }

        void UnsubscribeHero()
        {
            if (_hero != null)
                _hero.HealthChanged -= OnHeroHealthChanged;
            if (_hunger != null)
                _hunger.HungerChanged -= OnHungerChanged;
            _hero = null;
            _hunger = null;
        }

        void OnHeroHealthChanged()
        {
            if (_healthBar == null || _hero == null)
                return;
            _healthBar.SetProgress(_hero.Normalized);
        }

        void OnHungerChanged()
        {
            if (_hungerBar == null || _hunger == null)
                return;
            _hungerBar.SetProgress(_hunger.Normalized);
        }

        sealed class SimpleFillBar
        {
            readonly RectTransform _fillRt;

            public SimpleFillBar(RectTransform fillRt) => _fillRt = fillRt;

            public void SetProgress(float progress)
            {
                progress = Mathf.Clamp01(progress);
                _fillRt.anchorMin = new Vector2(0f, 0.1f);
                _fillRt.anchorMax = new Vector2(progress, 0.9f);
                _fillRt.offsetMin = Vector2.zero;
                _fillRt.offsetMax = Vector2.zero;
            }
        }
    }
}
