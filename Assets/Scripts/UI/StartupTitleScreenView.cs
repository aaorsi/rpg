using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Rpg.UI
{
    /// <summary>
    /// Serialized wiring for <see cref="StartupTitleScreenStage"/> UI (built from Resources prefab).
    /// </summary>
    public sealed class StartupTitleScreenView : MonoBehaviour
    {
        [Header("Backdrop")]
        [SerializeField] Image backgroundImage;
        [SerializeField] Image shadeImage;

        [Header("Card")]
        [SerializeField] RectTransform cardRoot;
        [SerializeField] Image cardImage;
        [SerializeField] Image cardShadowImage;

        [Header("Copy")]
        [SerializeField] TextMeshProUGUI titleText;
        [SerializeField] TextMeshProUGUI subtitleText;
        [SerializeField] TextMeshProUGUI hintText;
        [SerializeField] TextMeshProUGUI statusText;

        [Header("Ollama")]
        [SerializeField] ToggleGroup modeToggleGroup;
        [SerializeField] Toggle localToggle;
        [SerializeField] Toggle cloudToggle;
        [SerializeField] GameObject tokenRow;
        [SerializeField] TextMeshProUGUI tokenLabel;
        [SerializeField] TextMeshProUGUI modelLabel;
        [SerializeField] TMP_InputField modelInput;
        [SerializeField] TMP_InputField tokenInput;
        [SerializeField] Button refreshModelsButton;
        [SerializeField] TextMeshProUGUI refreshModelsLabel;
        [SerializeField] ScrollRect modelScrollRect;
        [SerializeField] RectTransform modelListContent;
        [SerializeField] GameObject modelPickRowTemplate;

        [Header("Primary action")]
        [SerializeField] Button playButton;
        [SerializeField] TextMeshProUGUI playLabel;

        public Image BackgroundImage => backgroundImage;
        public Toggle LocalToggle => localToggle;
        public Toggle CloudToggle => cloudToggle;
        public GameObject TokenRow => tokenRow;
        public TMP_InputField ModelInput => modelInput;
        public TMP_InputField TokenInput => tokenInput;
        public Button RefreshModelsButton => refreshModelsButton;
        public Button PlayButton => playButton;
        public TextMeshProUGUI StatusText => statusText;
        public RectTransform ModelListContent => modelListContent;
        public GameObject ModelPickRowTemplate => modelPickRowTemplate;

        public void SetTitle(string gameTitle, string subtitle)
        {
            if (titleText != null)
                titleText.text = string.IsNullOrWhiteSpace(gameTitle) ? "RPG Island" : gameTitle.Trim();
            if (subtitleText != null)
                subtitleText.text = subtitle ?? string.Empty;
        }

        public void SetHint(string text)
        {
            if (hintText != null)
                hintText.text = text ?? string.Empty;
        }

        public void SetDefaultModel(string model)
        {
            if (modelInput != null)
                modelInput.text = model ?? string.Empty;
        }

        public void ApplyDarkTheme()
        {
            const string hint = "This machine uses your local Ollama daemon. Cloud uses an API key from ollama.com/settings/keys — same /api/chat as the desktop app.";
            if (hintText != null && string.IsNullOrEmpty(hintText.text))
                hintText.text = hint;

            var body = new Color(0.92f, 0.94f, 0.98f, 1f);
            var muted = new Color(0.78f, 0.82f, 0.9f, 0.72f);
            ApplyTmpColor(titleText, new Color(0.97f, 0.98f, 1f, 1f));
            ApplyTmpColor(subtitleText, muted);
            ApplyTmpColor(hintText, muted);
            ApplyTmpColor(statusText, new Color(0.75f, 0.82f, 0.95f, 0.9f));
            ApplyTmpColor(tokenLabel, body);
            ApplyTmpColor(modelLabel, body);
            ApplyTmpColor(refreshModelsLabel, body);
            ApplyTmpColor(playLabel, Color.white);

            if (modelInput != null)
                StyleTmpInput(modelInput, body);
            if (tokenInput != null)
                StyleTmpInput(tokenInput, body);

            StyleCard();
            StylePrimaryButton(playButton, new Color(0.28f, 0.52f, 0.88f, 1f));
            StyleSecondaryButton(refreshModelsButton, new Color(0.22f, 0.28f, 0.38f, 0.95f));
            StyleToggleSurface(localToggle, new Color(0.16f, 0.2f, 0.28f, 0.95f));
            StyleToggleSurface(cloudToggle, new Color(0.16f, 0.2f, 0.28f, 0.95f));

            if (shadeImage != null)
            {
                shadeImage.color = new Color(0f, 0f, 0f, 0.42f);
                shadeImage.raycastTarget = false;
            }

            if (backgroundImage != null)
                backgroundImage.color = Color.white;
        }

        static void ApplyTmpColor(TextMeshProUGUI tmp, Color c)
        {
            if (tmp == null)
                return;
            tmp.color = c;
        }

        static void StyleTmpInput(TMP_InputField field, Color textColor)
        {
            if (field == null)
                return;
            field.customCaretColor = true;
            field.caretColor = new Color(0.9f, 0.93f, 1f, 0.95f);
            if (field.textComponent != null)
            {
                field.textComponent.color = textColor;
                field.textComponent.fontSize = 20;
            }

            if (field.placeholder is TextMeshProUGUI ph)
            {
                var pc = textColor;
                pc.a *= 0.45f;
                ph.color = pc;
                ph.fontSize = 18;
            }

            var img = field.image;
            if (img != null)
                img.color = new Color(0.08f, 0.1f, 0.14f, 0.92f);
        }

        void StyleCard()
        {
            if (cardImage != null)
                cardImage.color = new Color(0.09f, 0.11f, 0.16f, 0.94f);
            if (cardShadowImage != null)
                cardShadowImage.color = new Color(0f, 0f, 0f, 0.45f);
        }

        static void StylePrimaryButton(Button btn, Color normal)
        {
            if (btn == null)
                return;
            var img = btn.targetGraphic as Graphic;
            if (img != null)
                img.color = normal;
            var colors = btn.colors;
            colors.normalColor = normal;
            colors.highlightedColor = new Color(normal.r * 1.08f, normal.g * 1.08f, normal.b * 1.08f, 1f);
            colors.pressedColor = new Color(normal.r * 0.85f, normal.g * 0.85f, normal.b * 0.9f, 1f);
            colors.disabledColor = new Color(0.35f, 0.38f, 0.45f, 0.55f);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.12f;
            btn.colors = colors;
        }

        static void StyleSecondaryButton(Button btn, Color normal)
        {
            if (btn == null)
                return;
            var img = btn.targetGraphic as Graphic;
            if (img != null)
                img.color = normal;
            var colors = btn.colors;
            colors.normalColor = normal;
            colors.highlightedColor = new Color(0.28f, 0.34f, 0.44f, 1f);
            colors.pressedColor = new Color(0.18f, 0.22f, 0.3f, 1f);
            colors.disabledColor = new Color(0.25f, 0.28f, 0.34f, 0.45f);
            btn.colors = colors;
        }

        static void StyleToggleSurface(Toggle t, Color bg)
        {
            if (t == null || t.targetGraphic == null)
                return;
            var g = t.targetGraphic as Graphic;
            if (g != null)
                g.color = bg;
        }
    }
}
