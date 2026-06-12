using System;
using System.Collections;
using Rpg.Core;
using Rpg.Dialogue;
using Rpg.Player;
using Rpg.UI;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Rpg.Npc
{
    /// <summary>
    /// Scene Ghoul: proximity menace dialogue (planar distance = 6 × (<see cref="BossAi.MeleeKillRadiusMeters"/> + 10 m)),
    /// smooth sky fade to black over <see cref="SkyFadeDurationSeconds"/> and smooth restore on exit/end.
    /// </summary>
    [DefaultExecutionOrder(13)]
    public sealed class GhoulMenaceController : MonoBehaviour
    {
        public const string GhoulNpcId = "scene_ghoul";

        const float SkyFadeDurationSeconds = 10f;
        const float DialogueRadiusBeyondMeleeMeters = 10f;
        const float MenaceInteractionRadiusMultiplier = 6f;

        [SerializeField, Min(2f)]
        float distanceCheckIntervalSeconds = 2f;

        NpcDialogueBinding _binding;
        CharacterController _cc;
        BossAi _bossAi;

        Material _savedSkyboxReference;
        Material _fadeWorkingMaterial;
        Coroutine _skyFadeRoutine;
        SkySnapshot _capturedBaseline;

        bool _menaceDialogueOpened;
        bool _aggressionUnleashed;
        int _snapAfterExchanges;
        int _completedPlayerNpcCycles;
        float _nextDistanceCheckTime = -999f;

        enum SkyFadePhase
        {
            Idle,
            FadingToMenace,
            FadingRestore
        }

        SkyFadePhase _skyPhase = SkyFadePhase.Idle;

        public bool IsAggressionUnleashed => _aggressionUnleashed;

        /// <summary>
        /// <see cref="Player.PlayerVisibilityRange"/> clones and drives <see cref="RenderSettings.skybox"/> each frame;
        /// while the Ghoul owns the working fade material it must not be replaced or the fade (and dialogue trigger) breaks.
        /// </summary>
        public bool BlocksPlayerVisibilitySkyDriver()
        {
            if (_skyPhase != SkyFadePhase.Idle)
                return true;
            return _fadeWorkingMaterial != null && ReferenceEquals(RenderSettings.skybox, _fadeWorkingMaterial);
        }

        public static bool AnyInstanceBlocksPlayerVisibilitySkyDriver()
        {
            foreach (var g in Object.FindObjectsByType<GhoulMenaceController>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (g != null && g.BlocksPlayerVisibilitySkyDriver())
                    return true;
            }

            return false;
        }

        public static bool IsGhoulStoryNpcId(string npcId) =>
            !string.IsNullOrWhiteSpace(npcId)
            && string.Equals(npcId.Trim(), GhoulNpcId, StringComparison.OrdinalIgnoreCase);

        public static NpcDefinition CreateRuntimeDefinition()
        {
            var template = Resources.Load<NpcDefinition>(GameConstants.DefaultNpcResource);
            if (template == null)
            {
                Debug.LogError($"[{nameof(GhoulMenaceController)}] Missing Resources asset '{GameConstants.DefaultNpcResource}.asset'.");
                return null;
            }

            var def = Object.Instantiate(template);
            def.npcId = GhoulNpcId;
            def.displayName = "Ghoul";
            def.roleSummary =
                "You are an ancient ruin-bound horror that speaks in riddles of total annihilation and the end of all things. "
                + "You enjoy implying cosmic doom and the hero's futility. You may hint at old world backstory, ruins, and curses. "
                + "This talk is atmospheric only: you do not form alliances, give quests, trade items, or change the world through JSON actions.";
            def.toneAndVocabulary =
                "Write entirely in ALL CAPS. Short, venomous lines. Threatening, theatrical, sometimes darkly curious about the hero. "
                + "No emojis. No markdown.";
            def.safetyRules =
                "Output only the required JSON object.\n"
                + "NEVER include proposedNpcActions, give_object, receive_object, trade, move_to_location, refer_to_npc, follow_hero, milestoneSignals, or stateDeltas.\n"
                + "Use interactionOutcome \"menace_flavor\" every turn until the runtime ends this session.\n"
                + "Stay in character; do not break the fourth wall about being an AI.";
            def.openingLine =
                "YOUR FOOTSTEPS ARE LOUD IN MY DARK. THE SKY BLEEDS FOR YOU — AND I AM WHAT COMES AFTER THE LAST STAR.";
            def.fallbackLines = new[]
            {
                "THE VOID DOES NOT STUTTER. ASK YOUR LITTLE QUESTIONS WHILE TIME STILL PRETENDS TO MOVE.",
                "I TASTE STORIES IN THE RAIN. YOURS ENDS THE SAME WAY AS EMPIRES: QUIETLY, THEN ALL AT ONCE."
            };
            return def;
        }

        public static void EnsureOnGhoulHost(GameObject host)
        {
            if (host == null)
                return;
            var binding = host.GetComponent<NpcDialogueBinding>() ?? host.AddComponent<NpcDialogueBinding>();
            if (binding.Definition == null || !IsGhoulStoryNpcId(binding.Definition.npcId))
                binding.SetDefinition(CreateRuntimeDefinition());
            if (host.GetComponent<GhoulMenaceController>() == null)
                host.AddComponent<GhoulMenaceController>();
        }

        void Awake()
        {
            _snapAfterExchanges = UnityEngine.Random.Range(3, 6);
            _cc = GetComponent<CharacterController>();
            _bossAi = GetComponent<BossAi>();
            ResolveBindingIfNeeded();
        }

        void Start()
        {
            distanceCheckIntervalSeconds = Mathf.Max(2f, distanceCheckIntervalSeconds);
            if (_bossAi == null)
                _bossAi = GetComponent<BossAi>();
            ResolveBindingIfNeeded();
            if (_cc == null)
                _cc = GetComponent<CharacterController>();
            _nextDistanceCheckTime = Time.time;
        }

        void ResolveBindingIfNeeded()
        {
            if (_binding == null)
                _binding = GetComponent<NpcDialogueBinding>();
        }

        float MenaceDialogueRadiusMeters()
        {
            if (_bossAi == null)
                _bossAi = GetComponent<BossAi>();
            if (_bossAi != null)
            {
                var baseRingMeters = _bossAi.MeleeKillRadiusMeters + DialogueRadiusBeyondMeleeMeters;
                return Mathf.Max(0.5f, MenaceInteractionRadiusMultiplier * baseRingMeters);
            }

            return Mathf.Max(0.5f, MenaceInteractionRadiusMultiplier * 15f);
        }

        Vector3 WorldAnchorPosition()
        {
            if (_cc == null)
                _cc = GetComponent<CharacterController>();
            return _cc != null ? transform.TransformPoint(_cc.center) : transform.position;
        }

        void OnDestroy()
        {
            StopSkyFadeRoutine();
            if (_fadeWorkingMaterial != null)
            {
                if (Application.isPlaying)
                    Destroy(_fadeWorkingMaterial);
                else
                    DestroyImmediate(_fadeWorkingMaterial);
                _fadeWorkingMaterial = null;
            }
        }

        void OnEnable()
        {
            DialogueManager.DialogueClosed += OnDialogueClosed;
            DialogueManager.AfterNpcDialogueTurnCommitted += OnAfterNpcDialogueTurnCommitted;
        }

        void OnDisable()
        {
            DialogueManager.DialogueClosed -= OnDialogueClosed;
            DialogueManager.AfterNpcDialogueTurnCommitted -= OnAfterNpcDialogueTurnCommitted;
            StopSkyFadeRoutine();
            if (_fadeWorkingMaterial != null && _savedSkyboxReference != null)
                RenderSettings.skybox = _savedSkyboxReference;
        }

        void OnDialogueClosed(string closingNpcId)
        {
            if (!IsGhoulStoryNpcId(closingNpcId))
                return;
            BeginFadeSkyToBaseline();
        }

        void OnAfterNpcDialogueTurnCommitted(NpcDefinition activeNpc)
        {
            if (_aggressionUnleashed || activeNpc == null || !IsGhoulStoryNpcId(activeNpc.npcId))
                return;
            _completedPlayerNpcCycles++;
            if (_completedPlayerNpcCycles < _snapAfterExchanges)
                return;
            var dm = DialogueManager.Instance;
            if (dm == null || !dm.IsDialogueOpen)
                return;
            dm.AppendGhoulSnapOutroThenEnd(
                "ENOUGH WORDS. I WILL TEAR THE LIGHT FROM YOUR CHEST — RUN, LITTLE THING, RUN!");
        }

        void Update()
        {
            if (_aggressionUnleashed || !isActiveAndEnabled)
                return;
            if (GameOverController.Instance != null && GameOverController.Instance.IsGameOver)
                return;
            ResolveBindingIfNeeded();

            if (_skyPhase == SkyFadePhase.FadingToMenace && !_menaceDialogueOpened)
            {
                if (!IsHeroWithinMenaceDialogueRadius(out _))
                    BeginFadeSkyToBaseline();
            }

            if (_skyPhase != SkyFadePhase.Idle)
                return;

            if (Time.time < _nextDistanceCheckTime)
                return;
            _nextDistanceCheckTime = Time.time + distanceCheckIntervalSeconds;

            if (!IsHeroWithinMenaceDialogueRadius(out _))
                return;
            if (_menaceDialogueOpened)
                return;
            var dm = DialogueManager.Instance;
            if (dm == null || dm.IsDialogueOpen || _binding == null || _binding.Definition == null)
                return;

            StopSkyFadeRoutine();
            _skyFadeRoutine = StartCoroutine(CoFadeSkyToMenaceThenTryOpenDialogue());
        }

        public void NotifyAggressionUnleashedFromDialogue()
        {
            _aggressionUnleashed = true;
        }

        bool IsHeroWithinMenaceDialogueRadius(out float planarDistance)
        {
            planarDistance = float.MaxValue;
            if (!TryPlanarDistanceToHero(out planarDistance))
                return false;
            return planarDistance <= MenaceDialogueRadiusMeters();
        }

        bool TryPlanarDistanceToHero(out float planarDistance)
        {
            planarDistance = float.MaxValue;
            var go = GameObject.FindGameObjectWithTag(GameConstants.PlayerTag);
            if (go == null)
                return false;
            var heroCc = go.GetComponent<CharacterController>();
            var heroAnchor = heroCc != null ? go.transform.TransformPoint(heroCc.center) : go.transform.position;
            var delta = heroAnchor - WorldAnchorPosition();
            delta.y = 0f;
            planarDistance = delta.magnitude;
            return true;
        }

        void StopSkyFadeRoutine()
        {
            if (_skyFadeRoutine != null)
            {
                StopCoroutine(_skyFadeRoutine);
                _skyFadeRoutine = null;
            }
        }

        IEnumerator CoFadeSkyToMenaceThenTryOpenDialogue()
        {
            _skyPhase = SkyFadePhase.FadingToMenace;
            if (!EnsureFadeWorkingMaterialFromCurrentSky())
            {
                _skyPhase = SkyFadePhase.Idle;
                _skyFadeRoutine = null;
                yield break;
            }

            var menace = SkySnapshot.MenaceTargetsMatchingBaseline(in _capturedBaseline);
            var elapsed = 0f;
            while (elapsed < SkyFadeDurationSeconds)
            {
                if (!_menaceDialogueOpened && !IsHeroWithinMenaceDialogueRadius(out _))
                {
                    yield return StartCoroutine(CoFadeSkyFromCurrentToBaseline(clearRoutineHandleAtEnd: false));
                    _skyPhase = SkyFadePhase.Idle;
                    _skyFadeRoutine = null;
                    yield break;
                }

                elapsed += Time.deltaTime;
                var u = Mathf.Clamp01(elapsed / SkyFadeDurationSeconds);
                u = Mathf.SmoothStep(0f, 1f, u);
                SkySnapshot.LerpApply(_fadeWorkingMaterial, _capturedBaseline, menace, u);
                yield return null;
            }

            SkySnapshot.LerpApply(_fadeWorkingMaterial, _capturedBaseline, menace, 1f);

            var dm = DialogueManager.Instance;
            var opened = dm != null && !dm.IsDialogueOpen && _binding?.Definition != null && IsHeroWithinMenaceDialogueRadius(out _)
                && dm.TryStartDialogue(_binding.Definition, null, false);
            if (opened)
            {
                _menaceDialogueOpened = true;
                _completedPlayerNpcCycles = 0;
                _snapAfterExchanges = UnityEngine.Random.Range(3, 6);
            }
            else
            {
                yield return StartCoroutine(CoFadeSkyFromCurrentToBaseline(clearRoutineHandleAtEnd: false));
            }

            _skyPhase = SkyFadePhase.Idle;
            _skyFadeRoutine = null;
        }

        IEnumerator CoFadeSkyFromCurrentToBaseline(bool clearRoutineHandleAtEnd = true)
        {
            _skyPhase = SkyFadePhase.FadingRestore;
            if (_fadeWorkingMaterial == null)
            {
                if (_savedSkyboxReference != null)
                    RenderSettings.skybox = _savedSkyboxReference;
                _skyPhase = SkyFadePhase.Idle;
                if (clearRoutineHandleAtEnd)
                    _skyFadeRoutine = null;
                yield break;
            }

            SkySnapshot.CaptureFromMaterial(_fadeWorkingMaterial, out var fromCurrent);
            var elapsed = 0f;
            while (elapsed < SkyFadeDurationSeconds)
            {
                elapsed += Time.deltaTime;
                var u = Mathf.Clamp01(elapsed / SkyFadeDurationSeconds);
                u = Mathf.SmoothStep(0f, 1f, u);
                SkySnapshot.LerpApply(_fadeWorkingMaterial, fromCurrent, _capturedBaseline, u);
                yield return null;
            }

            SkySnapshot.LerpApply(_fadeWorkingMaterial, fromCurrent, _capturedBaseline, 1f);
            if (_savedSkyboxReference != null)
                RenderSettings.skybox = _savedSkyboxReference;
            if (_fadeWorkingMaterial != null)
            {
                Destroy(_fadeWorkingMaterial);
                _fadeWorkingMaterial = null;
            }

            _skyPhase = SkyFadePhase.Idle;
            if (clearRoutineHandleAtEnd)
                _skyFadeRoutine = null;
        }

        void BeginFadeSkyToBaseline()
        {
            StopSkyFadeRoutine();
            _skyFadeRoutine = StartCoroutine(CoFadeSkyFromCurrentToBaseline(clearRoutineHandleAtEnd: true));
        }

        bool EnsureFadeWorkingMaterialFromCurrentSky()
        {
            if (_fadeWorkingMaterial != null)
            {
                Destroy(_fadeWorkingMaterial);
                _fadeWorkingMaterial = null;
            }

            if (_savedSkyboxReference == null)
                _savedSkyboxReference = RenderSettings.skybox;

            var source = _savedSkyboxReference != null ? _savedSkyboxReference : RenderSettings.skybox;
            if (source != null && source.shader != null)
            {
                _fadeWorkingMaterial = new Material(source);
                _fadeWorkingMaterial.name = "Ghoul_SkyFadeWorking";
            }
            else
            {
                _fadeWorkingMaterial = CreateFallbackBlackSkyboxMaterial();
                if (_fadeWorkingMaterial == null)
                {
                    Debug.LogWarning(
                        $"[{nameof(GhoulMenaceController)}] No skybox material to fade; assign a RenderSettings.skybox in the scene.");
                    return false;
                }
            }

            SkySnapshot.CaptureFromMaterial(_fadeWorkingMaterial, out _capturedBaseline);
            RenderSettings.skybox = _fadeWorkingMaterial;
            return true;
        }

        static Material CreateFallbackBlackSkyboxMaterial()
        {
            var cube = new Cubemap(1, TextureFormat.RGB24, false);
            var faceBlack = new[] { Color.black };
            for (var face = 0; face < 6; face++)
                cube.SetPixels(faceBlack, (CubemapFace)face);
            cube.Apply(false, true);
            var cubemapShader = Shader.Find("Skybox/Cubemap");
            if (cubemapShader != null)
            {
                var m = new Material(cubemapShader);
                m.name = "Ghoul_RuntimeBlackCubemapSky";
                m.SetTexture("_Tex", cube);
                if (m.HasProperty("_Tint"))
                    m.SetColor("_Tint", Color.white);
                return m;
            }

            var proc = Shader.Find("Skybox/Procedural");
            if (proc == null)
                return null;
            var pm = new Material(proc);
            pm.name = "Ghoul_RuntimeBlackSkyProcedural";
            pm.SetColor("_SkyTint", Color.black);
            pm.SetColor("_GroundColor", Color.black);
            if (pm.HasProperty("_SunDisk"))
                pm.SetFloat("_SunDisk", 0f);
            if (pm.HasProperty("_SunSize"))
                pm.SetFloat("_SunSize", 0f);
            if (pm.HasProperty("_SunSizeConvergence"))
                pm.SetFloat("_SunSizeConvergence", 1f);
            if (pm.HasProperty("_AtmosphereThickness"))
                pm.SetFloat("_AtmosphereThickness", 0f);
            if (pm.HasProperty("_Exposure"))
                pm.SetFloat("_Exposure", 0f);
            if (pm.HasProperty("_SkyIntensity"))
                pm.SetFloat("_SkyIntensity", 0f);
            if (pm.HasProperty("_Tint"))
                pm.SetColor("_Tint", Color.black);
            return pm;
        }

        sealed class SkySnapshot
        {
            public bool HasTint;
            public Color Tint;
            public bool HasSkyTint;
            public Color SkyTint;
            public bool HasGroundColor;
            public Color GroundColor;
            public bool HasFogCol;
            public Color FogCol;
            public bool HasExposure;
            public float Exposure;
            public bool HasAtmosphereThickness;
            public float AtmosphereThickness;
            public bool HasSunSize;
            public float SunSize;
            public bool HasSunDisk;
            public float SunDisk;
            public bool HasFogIntens;
            public float FogIntens;
            public bool HasSkyIntensity;
            public float SkyIntensity;

            public static SkySnapshot MenaceTargetsMatchingBaseline(in SkySnapshot baseline)
            {
                var m = new SkySnapshot();
                if (baseline.HasTint)
                {
                    m.HasTint = true;
                    m.Tint = Color.black;
                }

                if (baseline.HasSkyTint)
                {
                    m.HasSkyTint = true;
                    m.SkyTint = Color.black;
                }

                if (baseline.HasGroundColor)
                {
                    m.HasGroundColor = true;
                    m.GroundColor = Color.black;
                }

                if (baseline.HasFogCol)
                {
                    m.HasFogCol = true;
                    m.FogCol = Color.black;
                }

                if (baseline.HasExposure)
                {
                    m.HasExposure = true;
                    m.Exposure = 0f;
                }

                if (baseline.HasAtmosphereThickness)
                {
                    m.HasAtmosphereThickness = true;
                    m.AtmosphereThickness = 0f;
                }

                if (baseline.HasSunSize)
                {
                    m.HasSunSize = true;
                    m.SunSize = 0f;
                }

                if (baseline.HasSunDisk)
                {
                    m.HasSunDisk = true;
                    m.SunDisk = 0f;
                }

                if (baseline.HasFogIntens)
                {
                    m.HasFogIntens = true;
                    m.FogIntens = Mathf.Max(baseline.FogIntens, 1f);
                }

                if (baseline.HasSkyIntensity)
                {
                    m.HasSkyIntensity = true;
                    m.SkyIntensity = 0f;
                }

                return m;
            }

            public static void CaptureFromMaterial(Material m, out SkySnapshot s)
            {
                s = new SkySnapshot();
                if (m == null)
                    return;
                if (m.HasProperty("_Tint"))
                {
                    s.HasTint = true;
                    s.Tint = m.GetColor("_Tint");
                }

                if (m.HasProperty("_SkyTint"))
                {
                    s.HasSkyTint = true;
                    s.SkyTint = m.GetColor("_SkyTint");
                }

                if (m.HasProperty("_GroundColor"))
                {
                    s.HasGroundColor = true;
                    s.GroundColor = m.GetColor("_GroundColor");
                }

                if (m.HasProperty("_FogCol"))
                {
                    s.HasFogCol = true;
                    s.FogCol = m.GetColor("_FogCol");
                }

                if (m.HasProperty("_Exposure"))
                {
                    s.HasExposure = true;
                    s.Exposure = m.GetFloat("_Exposure");
                }

                if (m.HasProperty("_AtmosphereThickness"))
                {
                    s.HasAtmosphereThickness = true;
                    s.AtmosphereThickness = m.GetFloat("_AtmosphereThickness");
                }

                if (m.HasProperty("_SunSize"))
                {
                    s.HasSunSize = true;
                    s.SunSize = m.GetFloat("_SunSize");
                }

                if (m.HasProperty("_SunDisk"))
                {
                    s.HasSunDisk = true;
                    s.SunDisk = m.GetFloat("_SunDisk");
                }

                if (m.HasProperty("_FogIntens"))
                {
                    s.HasFogIntens = true;
                    s.FogIntens = m.GetFloat("_FogIntens");
                }

                if (m.HasProperty("_SkyIntensity"))
                {
                    s.HasSkyIntensity = true;
                    s.SkyIntensity = m.GetFloat("_SkyIntensity");
                }
            }

            public static void LerpApply(Material dst, in SkySnapshot a, in SkySnapshot b, float u)
            {
                if (dst == null)
                    return;
                if (a.HasTint && b.HasTint && dst.HasProperty("_Tint"))
                    dst.SetColor("_Tint", Color.Lerp(a.Tint, b.Tint, u));
                if (a.HasSkyTint && b.HasSkyTint && dst.HasProperty("_SkyTint"))
                    dst.SetColor("_SkyTint", Color.Lerp(a.SkyTint, b.SkyTint, u));
                if (a.HasGroundColor && b.HasGroundColor && dst.HasProperty("_GroundColor"))
                    dst.SetColor("_GroundColor", Color.Lerp(a.GroundColor, b.GroundColor, u));
                if (a.HasFogCol && b.HasFogCol && dst.HasProperty("_FogCol"))
                    dst.SetColor("_FogCol", Color.Lerp(a.FogCol, b.FogCol, u));
                if (a.HasExposure && b.HasExposure && dst.HasProperty("_Exposure"))
                    dst.SetFloat("_Exposure", Mathf.Lerp(a.Exposure, b.Exposure, u));
                if (a.HasAtmosphereThickness && b.HasAtmosphereThickness && dst.HasProperty("_AtmosphereThickness"))
                    dst.SetFloat("_AtmosphereThickness", Mathf.Lerp(a.AtmosphereThickness, b.AtmosphereThickness, u));
                if (a.HasSunSize && b.HasSunSize && dst.HasProperty("_SunSize"))
                    dst.SetFloat("_SunSize", Mathf.Lerp(a.SunSize, b.SunSize, u));
                if (a.HasSunDisk && b.HasSunDisk && dst.HasProperty("_SunDisk"))
                    dst.SetFloat("_SunDisk", Mathf.Lerp(a.SunDisk, b.SunDisk, u));
                if (a.HasFogIntens && b.HasFogIntens && dst.HasProperty("_FogIntens"))
                    dst.SetFloat("_FogIntens", Mathf.Lerp(a.FogIntens, b.FogIntens, u));
                if (a.HasSkyIntensity && b.HasSkyIntensity && dst.HasProperty("_SkyIntensity"))
                    dst.SetFloat("_SkyIntensity", Mathf.Lerp(a.SkyIntensity, b.SkyIntensity, u));
            }
        }
    }
}
