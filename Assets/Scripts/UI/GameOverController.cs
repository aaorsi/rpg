using System.Collections;
using System.Collections.Generic;
using Rpg.Player;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Rpg.UI
{
    /// <summary>
    /// Full-screen dim + "Game over" + Play again (reloads scene from avatar selection onward),
    /// or victory (Congratulations / escape message + Play again). Also hosts the drowning countdown strip.
    /// </summary>
    public sealed class GameOverController : MonoBehaviour
    {
        public static GameOverController Instance { get; private set; }

        const float GameOverDimAlpha = 0.5f;
        const string LightningAuraResourcesPath = "Vfx/LightningAura";
        const float DeathCharacterFadeSeconds = 1f;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");

        Canvas _hudCanvas;
        GameObject _drowningRoot;
        Text _drowningText;
        GameObject _gameOverRoot;
        Image _gameOverDim;
        Text _gameOverTitle;
        Button _playAgainButton;

        GameObject _victoryRoot;
        Image _victoryDim;
        Text _victoryTitle;
        Text _victorySubtitle;
        Text _victoryGameOverLine;
        Button _victoryPlayAgainButton;

        readonly List<MaterialFadeEntry> _deathFadeMats = new(32);

        struct MaterialFadeEntry
        {
            public Material Mat;
            public int PropId;
            public Color Orig;
        }

        public bool IsGameOver { get; private set; }

        /// <summary>True after <see cref="TriggerPlayerVictory"/>; false after death or fresh session.</summary>
        public bool IsVictory { get; private set; }

        static Font BuiltinUiFont() => RuntimeUiBuildMaterials.BuiltinUiFont();

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            BuildHud();
        }

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        void BuildHud()
        {
            var hudRoot = new GameObject("GameplayHud");
            hudRoot.transform.SetParent(transform, false);
            var stretch = hudRoot.AddComponent<RectTransform>();
            stretch.anchorMin = Vector2.zero;
            stretch.anchorMax = Vector2.one;
            stretch.offsetMin = Vector2.zero;
            stretch.offsetMax = Vector2.zero;

            _hudCanvas = hudRoot.AddComponent<Canvas>();
            _hudCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _hudCanvas.overrideSorting = true;
            _hudCanvas.sortingOrder = 400;
            hudRoot.AddComponent<GraphicRaycaster>();

            if (hudRoot.GetComponent<CanvasScaler>() == null)
            {
                var scaler = hudRoot.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.matchWidthOrHeight = 0.5f;
            }

            if (Object.FindFirstObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem");
                es.AddComponent<EventSystem>();
                es.AddComponent<InputSystemUIInputModule>();
            }

            _drowningRoot = new GameObject("DrowningCountdown");
            _drowningRoot.transform.SetParent(hudRoot.transform, false);
            var dRt = _drowningRoot.AddComponent<RectTransform>();
            dRt.anchorMin = new Vector2(0.25f, 0.9f);
            dRt.anchorMax = new Vector2(0.75f, 0.98f);
            dRt.offsetMin = Vector2.zero;
            dRt.offsetMax = Vector2.zero;
            var dBg = _drowningRoot.AddComponent<Image>();
            dBg.color = new Color(0f, 0.08f, 0.12f, 0.82f);
            dBg.raycastTarget = false;
            RuntimeUiBuildMaterials.TryApplySpriteImageMaterial(dBg);
            var dTxtGo = new GameObject("Text");
            dTxtGo.transform.SetParent(_drowningRoot.transform, false);
            _drowningText = dTxtGo.AddComponent<Text>();
            _drowningText.font = BuiltinUiFont();
            _drowningText.fontSize = 28;
            _drowningText.color = new Color(0.85f, 0.92f, 1f, 1f);
            _drowningText.alignment = TextAnchor.MiddleCenter;
            RuntimeUiBuildMaterials.TryApplyLegacyTextMaterial(_drowningText);
            var dTxtRt = dTxtGo.GetComponent<RectTransform>();
            dTxtRt.anchorMin = Vector2.zero;
            dTxtRt.anchorMax = Vector2.one;
            dTxtRt.offsetMin = new Vector2(8f, 4f);
            dTxtRt.offsetMax = new Vector2(-8f, -4f);
            _drowningRoot.SetActive(false);

            _gameOverRoot = new GameObject("GameOverPanel");
            _gameOverRoot.transform.SetParent(hudRoot.transform, false);
            var gRt = _gameOverRoot.AddComponent<RectTransform>();
            gRt.anchorMin = Vector2.zero;
            gRt.anchorMax = Vector2.one;
            gRt.offsetMin = Vector2.zero;
            gRt.offsetMax = Vector2.zero;

            var dimGo = new GameObject("Dim");
            dimGo.transform.SetParent(_gameOverRoot.transform, false);
            _gameOverDim = dimGo.AddComponent<Image>();
            _gameOverDim.color = new Color(0f, 0f, 0f, GameOverDimAlpha);
            _gameOverDim.raycastTarget = true;
            RuntimeUiBuildMaterials.TryApplySpriteImageMaterial(_gameOverDim);
            var dimRt = dimGo.GetComponent<RectTransform>();
            dimRt.anchorMin = Vector2.zero;
            dimRt.anchorMax = Vector2.one;
            dimRt.offsetMin = Vector2.zero;
            dimRt.offsetMax = Vector2.zero;

            var titleGo = new GameObject("GameOverTitle");
            titleGo.transform.SetParent(_gameOverRoot.transform, false);
            _gameOverTitle = titleGo.AddComponent<Text>();
            _gameOverTitle.font = BuiltinUiFont();
            _gameOverTitle.fontSize = 56;
            _gameOverTitle.color = Color.white;
            _gameOverTitle.alignment = TextAnchor.MiddleCenter;
            _gameOverTitle.text = "Game over";
            _gameOverTitle.raycastTarget = false;
            RuntimeUiBuildMaterials.TryApplyLegacyTextMaterial(_gameOverTitle);
            var titleRt = titleGo.GetComponent<RectTransform>();
            titleRt.anchorMin = new Vector2(0.1f, 0.55f);
            titleRt.anchorMax = new Vector2(0.9f, 0.72f);
            titleRt.offsetMin = Vector2.zero;
            titleRt.offsetMax = Vector2.zero;

            var btnGo = new GameObject("PlayAgainButton");
            btnGo.transform.SetParent(_gameOverRoot.transform, false);
            var btnRt = btnGo.AddComponent<RectTransform>();
            btnRt.anchorMin = new Vector2(0.38f, 0.38f);
            btnRt.anchorMax = new Vector2(0.62f, 0.48f);
            btnRt.offsetMin = Vector2.zero;
            btnRt.offsetMax = Vector2.zero;
            var btnImg = btnGo.AddComponent<Image>();
            btnImg.color = new Color(0.2f, 0.45f, 0.35f, 0.95f);
            RuntimeUiBuildMaterials.TryApplySpriteImageMaterial(btnImg);
            _playAgainButton = btnGo.AddComponent<Button>();
            _playAgainButton.targetGraphic = btnImg;
            _playAgainButton.onClick.AddListener(OnPlayAgainClicked);

            var btnLabelGo = new GameObject("Label");
            btnLabelGo.transform.SetParent(btnGo.transform, false);
            var btnLabel = btnLabelGo.AddComponent<Text>();
            btnLabel.font = BuiltinUiFont();
            btnLabel.fontSize = 30;
            btnLabel.color = Color.white;
            btnLabel.alignment = TextAnchor.MiddleCenter;
            btnLabel.text = "Play again";
            btnLabel.raycastTarget = false;
            RuntimeUiBuildMaterials.TryApplyLegacyTextMaterial(btnLabel);
            var btnLabelRt = btnLabelGo.GetComponent<RectTransform>();
            btnLabelRt.anchorMin = Vector2.zero;
            btnLabelRt.anchorMax = Vector2.one;
            btnLabelRt.offsetMin = Vector2.zero;
            btnLabelRt.offsetMax = Vector2.zero;

            _gameOverRoot.SetActive(false);

            _victoryRoot = new GameObject("VictoryPanel");
            _victoryRoot.transform.SetParent(hudRoot.transform, false);
            var vRt = _victoryRoot.AddComponent<RectTransform>();
            vRt.anchorMin = Vector2.zero;
            vRt.anchorMax = Vector2.one;
            vRt.offsetMin = Vector2.zero;
            vRt.offsetMax = Vector2.zero;

            var vDimGo = new GameObject("Dim");
            vDimGo.transform.SetParent(_victoryRoot.transform, false);
            _victoryDim = vDimGo.AddComponent<Image>();
            _victoryDim.color = new Color(0f, 0f, 0f, GameOverDimAlpha);
            _victoryDim.raycastTarget = true;
            RuntimeUiBuildMaterials.TryApplySpriteImageMaterial(_victoryDim);
            var vDimRt = vDimGo.GetComponent<RectTransform>();
            vDimRt.anchorMin = Vector2.zero;
            vDimRt.anchorMax = Vector2.one;
            vDimRt.offsetMin = Vector2.zero;
            vDimRt.offsetMax = Vector2.zero;

            var vTitleGo = new GameObject("Congratulations");
            vTitleGo.transform.SetParent(_victoryRoot.transform, false);
            _victoryTitle = vTitleGo.AddComponent<Text>();
            _victoryTitle.font = BuiltinUiFont();
            _victoryTitle.fontSize = 56;
            _victoryTitle.color = new Color(0.85f, 1f, 0.88f, 1f);
            _victoryTitle.alignment = TextAnchor.MiddleCenter;
            _victoryTitle.text = "Congratulations!";
            _victoryTitle.raycastTarget = false;
            RuntimeUiBuildMaterials.TryApplyLegacyTextMaterial(_victoryTitle);
            var vTitleRt = vTitleGo.GetComponent<RectTransform>();
            vTitleRt.anchorMin = new Vector2(0.08f, 0.58f);
            vTitleRt.anchorMax = new Vector2(0.92f, 0.72f);
            vTitleRt.offsetMin = Vector2.zero;
            vTitleRt.offsetMax = Vector2.zero;

            var vSubGo = new GameObject("EscapeSubtitle");
            vSubGo.transform.SetParent(_victoryRoot.transform, false);
            _victorySubtitle = vSubGo.AddComponent<Text>();
            _victorySubtitle.font = BuiltinUiFont();
            _victorySubtitle.fontSize = 34;
            _victorySubtitle.color = Color.white;
            _victorySubtitle.alignment = TextAnchor.MiddleCenter;
            _victorySubtitle.text = "You've escaped from the Island";
            _victorySubtitle.raycastTarget = false;
            RuntimeUiBuildMaterials.TryApplyLegacyTextMaterial(_victorySubtitle);
            var vSubRt = vSubGo.GetComponent<RectTransform>();
            vSubRt.anchorMin = new Vector2(0.1f, 0.48f);
            vSubRt.anchorMax = new Vector2(0.9f, 0.58f);
            vSubRt.offsetMin = Vector2.zero;
            vSubRt.offsetMax = Vector2.zero;

            var vGoLine = new GameObject("GameOverLine");
            vGoLine.transform.SetParent(_victoryRoot.transform, false);
            _victoryGameOverLine = vGoLine.AddComponent<Text>();
            _victoryGameOverLine.font = BuiltinUiFont();
            _victoryGameOverLine.fontSize = 40;
            _victoryGameOverLine.color = new Color(1f, 1f, 1f, 0.92f);
            _victoryGameOverLine.alignment = TextAnchor.MiddleCenter;
            _victoryGameOverLine.text = "Game over";
            _victoryGameOverLine.raycastTarget = false;
            RuntimeUiBuildMaterials.TryApplyLegacyTextMaterial(_victoryGameOverLine);
            var vGoRt = vGoLine.GetComponent<RectTransform>();
            vGoRt.anchorMin = new Vector2(0.1f, 0.38f);
            vGoRt.anchorMax = new Vector2(0.9f, 0.46f);
            vGoRt.offsetMin = Vector2.zero;
            vGoRt.offsetMax = Vector2.zero;

            var vBtnGo = new GameObject("PlayAgainButton");
            vBtnGo.transform.SetParent(_victoryRoot.transform, false);
            var vBtnRt = vBtnGo.AddComponent<RectTransform>();
            vBtnRt.anchorMin = new Vector2(0.38f, 0.26f);
            vBtnRt.anchorMax = new Vector2(0.62f, 0.34f);
            vBtnRt.offsetMin = Vector2.zero;
            vBtnRt.offsetMax = Vector2.zero;
            var vBtnImg = vBtnGo.AddComponent<Image>();
            vBtnImg.color = new Color(0.15f, 0.52f, 0.32f, 0.95f);
            RuntimeUiBuildMaterials.TryApplySpriteImageMaterial(vBtnImg);
            _victoryPlayAgainButton = vBtnGo.AddComponent<Button>();
            _victoryPlayAgainButton.targetGraphic = vBtnImg;
            _victoryPlayAgainButton.onClick.AddListener(OnPlayAgainClicked);

            var vBtnLabelGo = new GameObject("Label");
            vBtnLabelGo.transform.SetParent(vBtnGo.transform, false);
            var vBtnLabel = vBtnLabelGo.AddComponent<Text>();
            vBtnLabel.font = BuiltinUiFont();
            vBtnLabel.fontSize = 30;
            vBtnLabel.color = Color.white;
            vBtnLabel.alignment = TextAnchor.MiddleCenter;
            vBtnLabel.text = "Play again";
            vBtnLabel.raycastTarget = false;
            RuntimeUiBuildMaterials.TryApplyLegacyTextMaterial(vBtnLabel);
            var vBtnLabelRt = vBtnLabelGo.GetComponent<RectTransform>();
            vBtnLabelRt.anchorMin = Vector2.zero;
            vBtnLabelRt.anchorMax = Vector2.one;
            vBtnLabelRt.offsetMin = Vector2.zero;
            vBtnLabelRt.offsetMax = Vector2.zero;

            _victoryRoot.SetActive(false);
        }

        public void SetDrowningCountdownVisible(float secondsRemaining, float maxSeconds)
        {
            if (IsGameOver || _drowningRoot == null || _drowningText == null)
                return;
            _drowningRoot.SetActive(true);
            _drowningText.text =
                $"Under water: {Mathf.Max(0f, secondsRemaining):F1}s / {maxSeconds:F0}s";
        }

        public void HideDrowningCountdown()
        {
            if (_drowningRoot != null)
                _drowningRoot.SetActive(false);
        }

        public void TriggerPlayerVictory(GameObject player)
        {
            if (IsGameOver)
                return;
            IsGameOver = true;
            IsVictory = true;
            HideDrowningCountdown();
            DisablePlayerControlSystems(player);
            if (_gameOverRoot != null)
                _gameOverRoot.SetActive(false);
            if (_victoryRoot != null)
                _victoryRoot.SetActive(true);
        }

        public void TriggerPlayerDeath(GameObject player)
        {
            if (IsGameOver)
                return;
            IsVictory = false;
            IsGameOver = true;
            HideDrowningCountdown();
            DisablePlayerControlSystems(player);
            if (_victoryRoot != null)
                _victoryRoot.SetActive(false);

            StartCoroutine(CoPlayerDeathSpectacle(player));
        }

        static void DisablePlayerControlSystems(GameObject player)
        {
            if (player == null)
                return;
            if (player.TryGetComponent<PlayerClickMove>(out var move))
                move.enabled = false;
            if (player.TryGetComponent<CharacterController>(out var cc))
                cc.enabled = false;
            if (player.TryGetComponent<PlayerInteractor>(out var inter))
                inter.enabled = false;
            if (player.TryGetComponent<PlayerUnderwaterDeathController>(out var drown))
                drown.enabled = false;
            if (player.TryGetComponent<PlayerTigerProximityDeathController>(out var tigerDeath))
                tigerDeath.enabled = false;
            if (player.TryGetComponent<PlayerDefensiveSpellAttack>(out var spellAttack))
                spellAttack.enabled = false;
            if (player.TryGetComponent<HeroHealth>(out var heroHealth))
                heroHealth.enabled = false;
            if (player.TryGetComponent<HeroHunger>(out var heroHunger))
                heroHunger.enabled = false;
        }

        IEnumerator CoPlayerDeathSpectacle(GameObject player)
        {
            if (player != null)
            {
                DestroyExistingPlayerLightningChildren(player.transform);

                GameObject aura = null;
                var prefab = Resources.Load<GameObject>(LightningAuraResourcesPath);
                if (prefab != null)
                {
                    aura = Instantiate(prefab, player.transform, false);
                    aura.name = prefab.name + "_Death";
                    aura.transform.localPosition = Vector3.zero;
                    aura.transform.localRotation = Quaternion.identity;
                    foreach (var ps in aura.GetComponentsInChildren<ParticleSystem>(true))
                    {
                        if (ps != null)
                            ps.Play(true);
                    }
                }

                yield return null;

                if (aura != null)
                {
                    var anchor = player.transform.position;
                    aura.transform.SetParent(null, true);
                    aura.transform.position = anchor;
                }

                BuildDeathFadeMaterials(player);
                var t0 = Time.time;
                var dur = Mathf.Max(0.05f, DeathCharacterFadeSeconds);
                while (Time.time - t0 < dur)
                {
                    var vis = 1f - Mathf.Clamp01((Time.time - t0) / dur);
                    ApplyDeathCharacterFade(vis);
                    yield return null;
                }

                ApplyDeathCharacterFade(0f);
                foreach (var r in player.GetComponentsInChildren<Renderer>(true))
                {
                    if (r == null || r is ParticleSystemRenderer)
                        continue;
                    r.enabled = false;
                }
            }

            if (_gameOverRoot != null)
                _gameOverRoot.SetActive(true);
        }

        static void DestroyExistingPlayerLightningChildren(Transform playerTransform)
        {
            var spawnAura = playerTransform.GetComponent<PlayerSpawnLightningAura>();
            if (spawnAura != null)
                Destroy(spawnAura);

            for (var i = playerTransform.childCount - 1; i >= 0; i--)
            {
                var ch = playerTransform.GetChild(i);
                var n = ch.name;
                if (n.IndexOf("LightningAura", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.EndsWith("_Intro", System.StringComparison.Ordinal))
                    Destroy(ch.gameObject);
            }
        }

        void BuildDeathFadeMaterials(GameObject player)
        {
            _deathFadeMats.Clear();
            foreach (var r in player.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null || r is ParticleSystemRenderer)
                    continue;
                var mats = r.materials;
                for (var mi = 0; mi < mats.Length; mi++)
                {
                    var m = mats[mi];
                    if (m == null)
                        continue;
                    if (m.HasProperty(BaseColorId))
                    {
                        _deathFadeMats.Add(new MaterialFadeEntry
                        {
                            Mat = m,
                            PropId = BaseColorId,
                            Orig = m.GetColor(BaseColorId)
                        });
                    }
                    else if (m.HasProperty(ColorId))
                    {
                        _deathFadeMats.Add(new MaterialFadeEntry
                        {
                            Mat = m,
                            PropId = ColorId,
                            Orig = m.GetColor(ColorId)
                        });
                    }
                }
            }
        }

        void ApplyDeathCharacterFade(float visibility01)
        {
            var vis = Mathf.Clamp01(visibility01);
            for (var i = 0; i < _deathFadeMats.Count; i++)
            {
                var e = _deathFadeMats[i];
                if (e.Mat == null)
                    continue;
                var c = Color.Lerp(Color.black, e.Orig, vis);
                e.Mat.SetColor(e.PropId, c);
            }
        }

        void OnPlayAgainClicked()
        {
            var scene = SceneManager.GetActiveScene();
            SceneManager.LoadScene(scene.buildIndex);
        }
    }
}
