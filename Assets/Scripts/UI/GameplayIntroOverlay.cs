using System.Collections;
using System.Collections.Generic;
using System.Text;
using Rpg.Core;
using Rpg.Dialogue;
using Rpg.Player;
using UnityEngine;
using UnityEngine.UI;

namespace Rpg.UI
{
    /// <summary>
    /// Shows run intro lines with typewriter, fade out, then delayed gameplay help.
    /// </summary>
    public sealed class GameplayIntroOverlay : MonoBehaviour
    {
        const float TypewriterCharsPerSecond = 40f;
        const float IntroStartDelayAfterSpawnSeconds = 3f;
        const float IntroHoldSeconds = 10f;
        const float IntroFadeSeconds = 2.5f;
        const float HelpDelayAfterIntroFadeSeconds = 5f;

        Canvas _canvas;
        Text _introText;
        Text _helpText;
        Image _helpBackdrop;
        Text _persistentHelpPrompt;
        Text _hungerWarningText;
        Image _introBackdrop;
        bool _helpPanelVisible;
        bool _hungerLowMessageShown;

        void Awake()
        {
            BuildUi();
            if (_introBackdrop != null)
                _introBackdrop.gameObject.SetActive(false);
            if (_introText != null)
                _introText.gameObject.SetActive(false);
            if (_helpText != null)
                _helpText.gameObject.SetActive(false);
            if (_helpBackdrop != null)
                _helpBackdrop.gameObject.SetActive(false);
            if (_hungerWarningText != null)
                _hungerWarningText.gameObject.SetActive(false);
        }

        void OnDisable()
        {
            CancelInvoke(nameof(HideHungerWarning));
        }

        void Update()
        {
            if (DialogueManager.Instance != null && DialogueManager.Instance.IsDialogueOpen)
                return;
            if (Input.GetKeyDown(KeyCode.H))
                ToggleHelpPanel();
            TryShowHungerLowOnce();
        }

        public void PlayForCurrentRun()
        {
            var lines = ResolveIntroLines();
            if (lines == null || lines.Count == 0)
                return;
            StopAllCoroutines();
            StartCoroutine(CoShowIntroThenHelp(lines));
        }

        public void ShowBookNarration(string narrationText)
        {
            if (string.IsNullOrWhiteSpace(narrationText))
                return;
            StopAllCoroutines();
            StartCoroutine(CoShowTransientIntroStyleText(narrationText.Trim(), holdSeconds: 8f));
        }

        /// <summary>Short NPC call-out (e.g. chicken theft) without implying a book discovery.</summary>
        public void ShowNpcShoutLine(string line, float holdSeconds = 3.25f)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;
            StopAllCoroutines();
            StartCoroutine(CoShowTransientIntroStyleText(line.Trim(), Mathf.Clamp(holdSeconds, 0.75f, 12f)));
        }

        List<string> ResolveIntroLines()
        {
            var canon = DialogueManager.Instance?.CurrentCanon;
            var lines = canon?.openingIntroLines;
            if (lines == null || lines.Count == 0)
            {
                return new List<string>
                {
                    "You awaken on a haunted island with no map and no allies.",
                    "Gather knowledge, recruit companions, and survive the monsters roaming the land.",
                    "Reach the castle, defeat the Ghoul, and open the portal home."
                };
            }

            var trimmed = new List<string>();
            foreach (var l in lines)
            {
                if (!string.IsNullOrWhiteSpace(l))
                    trimmed.Add(l.Trim());
            }
            return trimmed;
        }

        IEnumerator CoShowIntroThenHelp(IReadOnlyList<string> lines)
        {
            if (_introText == null || _helpText == null)
                yield break;
            _helpText.gameObject.SetActive(false);
            yield return new WaitForSeconds(IntroStartDelayAfterSpawnSeconds);
            if (_introBackdrop != null)
                _introBackdrop.gameObject.SetActive(true);
            _introText.gameObject.SetActive(true);
            SetTextAlpha(_introText, 1f);
            var full = BuildIntroBlock(lines);
            var delay = 1f / Mathf.Max(8f, TypewriterCharsPerSecond);
            _introText.text = string.Empty;
            for (var i = 1; i <= full.Length; i++)
            {
                _introText.text = full.Substring(0, i);
                yield return new WaitForSeconds(delay);
            }

            yield return new WaitForSeconds(IntroHoldSeconds);
            var t = 0f;
            while (t < IntroFadeSeconds)
            {
                t += Time.deltaTime;
                SetTextAlpha(_introText, 1f - Mathf.Clamp01(t / IntroFadeSeconds));
                yield return null;
            }
            _introText.gameObject.SetActive(false);
            if (_introBackdrop != null)
                _introBackdrop.gameObject.SetActive(false);

            yield return new WaitForSeconds(HelpDelayAfterIntroFadeSeconds);
        }

        IEnumerator CoShowTransientIntroStyleText(string text, float holdSeconds)
        {
            if (_introText == null)
                yield break;
            if (_helpText != null)
                _helpText.gameObject.SetActive(false);
            if (_introBackdrop != null)
                _introBackdrop.gameObject.SetActive(true);
            _introText.gameObject.SetActive(true);
            SetTextAlpha(_introText, 1f);
            _introText.text = string.Empty;
            var delay = 1f / Mathf.Max(8f, TypewriterCharsPerSecond);
            for (var i = 1; i <= text.Length; i++)
            {
                _introText.text = text.Substring(0, i);
                yield return new WaitForSeconds(delay);
            }

            yield return new WaitForSeconds(Mathf.Max(0.5f, holdSeconds));
            var t = 0f;
            while (t < IntroFadeSeconds)
            {
                t += Time.deltaTime;
                SetTextAlpha(_introText, 1f - Mathf.Clamp01(t / IntroFadeSeconds));
                yield return null;
            }

            _introText.gameObject.SetActive(false);
            if (_introBackdrop != null)
                _introBackdrop.gameObject.SetActive(false);
        }

        void ToggleHelpPanel()
        {
            _helpPanelVisible = !_helpPanelVisible;
            if (!_helpPanelVisible)
            {
                if (_helpText != null)
                    _helpText.gameObject.SetActive(false);
                if (_helpBackdrop != null)
                    _helpBackdrop.gameObject.SetActive(false);
                return;
            }

            _helpText.text =
                "Controls:\n" +
                "WASD - Move\n" +
                "Shift + WASD - Run\n" +
                "Space - Jump\n" +
                "1 - Change view\n" +
                "Left click - Collect item\n" +
                "Left click + mouse move - Move camera view\n" +
                "E - Talk / interact with NPCs\n" +
                "K - Cast magic spell\n" +
                "I - Open inventory panel\n" +
                "[h] - Toggle this help panel\n\n" +
                "Hints to get started:\n" +
                "* Interact with NPCs to learn about this world, trade items and gain followers.\n" +
                "* There are dangerous creatures roaming the land.\n" +
                "* You can eat chickens.\n" +
                "* Books contain magic spells to attack.";
            SetTextAlpha(_helpText, 1f);
            if (_helpBackdrop != null)
                _helpBackdrop.gameObject.SetActive(true);
            _helpText.gameObject.SetActive(true);
        }

        void TryShowHungerLowOnce()
        {
            if (_hungerLowMessageShown || _hungerWarningText == null)
                return;
            if (GameOverController.Instance != null && GameOverController.Instance.IsGameOver)
                return;
            var go = GameObject.FindGameObjectWithTag(GameConstants.PlayerTag);
            if (go == null || !go.TryGetComponent<HeroHunger>(out var hunger))
                return;
            if (hunger.Normalized > 0.1f)
                return;

            _hungerLowMessageShown = true;
            _hungerWarningText.text = "I'm feeling weak and hungry";
            SetTextAlpha(_hungerWarningText, 1f);
            _hungerWarningText.gameObject.SetActive(true);
            CancelInvoke(nameof(HideHungerWarning));
            Invoke(nameof(HideHungerWarning), 5f);
        }

        void HideHungerWarning()
        {
            if (_hungerWarningText != null)
                _hungerWarningText.gameObject.SetActive(false);
        }

        static string BuildIntroBlock(IReadOnlyList<string> lines)
        {
            var sb = new StringBuilder(256);
            for (var i = 0; i < lines.Count; i++)
            {
                if (i > 0)
                    sb.AppendLine();
                sb.Append(lines[i]);
            }
            return sb.ToString();
        }

        static void SetTextAlpha(Text text, float alpha)
        {
            if (text == null)
                return;
            var c = text.color;
            c.a = Mathf.Clamp01(alpha);
            text.color = c;
        }

        void BuildUi()
        {
            _canvas = gameObject.GetComponent<Canvas>();
            if (_canvas == null)
                _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 380;
            if (gameObject.GetComponent<CanvasScaler>() == null)
            {
                var scaler = gameObject.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.matchWidthOrHeight = 0.5f;
            }
            if (gameObject.GetComponent<GraphicRaycaster>() == null)
                gameObject.AddComponent<GraphicRaycaster>();

            var introBgGo = new GameObject("RunIntroBackdrop");
            introBgGo.transform.SetParent(transform, false);
            _introBackdrop = introBgGo.AddComponent<Image>();
            _introBackdrop.color = new Color(0.28f, 0.28f, 0.28f, 0.55f);
            RuntimeUiBuildMaterials.TryApplySpriteImageMaterial(_introBackdrop);
            var brt = introBgGo.GetComponent<RectTransform>();
            brt.anchorMin = new Vector2(0.08f, 0.02f);
            brt.anchorMax = new Vector2(0.92f, 0.26f);
            brt.offsetMin = Vector2.zero;
            brt.offsetMax = Vector2.zero;

            var introGo = new GameObject("RunIntroText");
            introGo.transform.SetParent(transform, false);
            _introText = introGo.AddComponent<Text>();
            _introText.font = RuntimeUiBuildMaterials.BuiltinUiFont();
            _introText.fontSize = 24;
            _introText.color = new Color(0.96f, 0.96f, 1f, 1f);
            _introText.alignment = TextAnchor.UpperLeft;
            _introText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _introText.verticalOverflow = VerticalWrapMode.Overflow;
            var introOutline = introGo.AddComponent<Outline>();
            introOutline.effectColor = new Color(0f, 0f, 0f, 0.55f);
            introOutline.effectDistance = new Vector2(0.75f, -0.75f);
            RuntimeUiBuildMaterials.TryApplyLegacyTextMaterial(_introText);
            var irt = introGo.GetComponent<RectTransform>();
            irt.anchorMin = new Vector2(0.1f, 0.03f);
            irt.anchorMax = new Vector2(0.9f, 0.24f);
            irt.offsetMin = Vector2.zero;
            irt.offsetMax = Vector2.zero;

            var helpGo = new GameObject("GameplayHelpText");
            helpGo.transform.SetParent(transform, false);
            var helpBgGo = new GameObject("GameplayHelpBackdrop");
            helpBgGo.transform.SetParent(transform, false);
            helpBgGo.transform.SetAsFirstSibling();
            _helpBackdrop = helpBgGo.AddComponent<Image>();
            _helpBackdrop.color = new Color(0.06f, 0.08f, 0.11f, 0.9f);
            RuntimeUiBuildMaterials.TryApplySpriteImageMaterial(_helpBackdrop);
            var hbgRt = helpBgGo.GetComponent<RectTransform>();
            hbgRt.anchorMin = new Vector2(0.02f, 0.5f);
            hbgRt.anchorMax = new Vector2(0.44f, 0.9f);
            hbgRt.offsetMin = Vector2.zero;
            hbgRt.offsetMax = Vector2.zero;

            _helpText = helpGo.AddComponent<Text>();
            _helpText.font = RuntimeUiBuildMaterials.BuiltinUiFont();
            _helpText.fontSize = 18;
            _helpText.color = new Color(0.88f, 0.95f, 1f, 1f);
            _helpText.alignment = TextAnchor.UpperLeft;
            _helpText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _helpText.verticalOverflow = VerticalWrapMode.Overflow;
            var helpOutline = helpGo.AddComponent<Outline>();
            helpOutline.effectColor = new Color(0f, 0f, 0f, 0.5f);
            helpOutline.effectDistance = new Vector2(0.75f, -0.75f);
            RuntimeUiBuildMaterials.TryApplyLegacyTextMaterial(_helpText);
            var hrt = helpGo.GetComponent<RectTransform>();
            hrt.anchorMin = new Vector2(0.03f, 0.52f);
            hrt.anchorMax = new Vector2(0.43f, 0.88f);
            hrt.offsetMin = Vector2.zero;
            hrt.offsetMax = Vector2.zero;

            var promptGo = new GameObject("PersistentHelpPrompt");
            promptGo.transform.SetParent(transform, false);
            _persistentHelpPrompt = promptGo.AddComponent<Text>();
            _persistentHelpPrompt.font = RuntimeUiBuildMaterials.BuiltinUiFont();
            _persistentHelpPrompt.fontSize = 17;
            _persistentHelpPrompt.color = new Color(0.9f, 0.93f, 1f, 0.92f);
            _persistentHelpPrompt.alignment = TextAnchor.UpperLeft;
            _persistentHelpPrompt.horizontalOverflow = HorizontalWrapMode.Wrap;
            _persistentHelpPrompt.verticalOverflow = VerticalWrapMode.Overflow;
            _persistentHelpPrompt.text = "[h] toggles help/hints";
            var promptOutline = promptGo.AddComponent<Outline>();
            promptOutline.effectColor = new Color(0f, 0f, 0f, 0.55f);
            promptOutline.effectDistance = new Vector2(0.75f, -0.75f);
            RuntimeUiBuildMaterials.TryApplyLegacyTextMaterial(_persistentHelpPrompt);
            var prt = promptGo.GetComponent<RectTransform>();
            prt.anchorMin = new Vector2(0.02f, 0.91f);
            prt.anchorMax = new Vector2(0.48f, 0.99f);
            prt.offsetMin = Vector2.zero;
            prt.offsetMax = Vector2.zero;

            var hungerGo = new GameObject("HungerLowWarning");
            hungerGo.transform.SetParent(transform, false);
            _hungerWarningText = hungerGo.AddComponent<Text>();
            _hungerWarningText.font = RuntimeUiBuildMaterials.BuiltinUiFont();
            _hungerWarningText.fontSize = 20;
            _hungerWarningText.color = new Color(1f, 0.78f, 0.45f, 1f);
            _hungerWarningText.alignment = TextAnchor.MiddleCenter;
            _hungerWarningText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _hungerWarningText.verticalOverflow = VerticalWrapMode.Overflow;
            var hungerOutline = hungerGo.AddComponent<Outline>();
            hungerOutline.effectColor = new Color(0f, 0f, 0f, 0.6f);
            hungerOutline.effectDistance = new Vector2(1f, -1f);
            RuntimeUiBuildMaterials.TryApplyLegacyTextMaterial(_hungerWarningText);
            var wrt = hungerGo.GetComponent<RectTransform>();
            wrt.anchorMin = new Vector2(0.2f, 0.84f);
            wrt.anchorMax = new Vector2(0.8f, 0.905f);
            wrt.offsetMin = Vector2.zero;
            wrt.offsetMax = Vector2.zero;
            _hungerWarningText.gameObject.SetActive(false);
        }
    }
}
