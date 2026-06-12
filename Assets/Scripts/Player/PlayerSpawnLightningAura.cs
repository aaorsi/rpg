using System.Collections.Generic;
using UnityEngine;

namespace Rpg.Player
{
    /// <summary>
    /// Spawns a lightning aura parented to the player during the intro sky drop and fades it out after landing.
    /// Uses <see cref="LateUpdate"/> so <see cref="CharacterController.isGrounded"/> is read after <see cref="PlayerClickMove"/> calls <c>Move</c>.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerSpawnLightningAura : MonoBehaviour
    {
        const string DefaultAuraResourcesPath = "Vfx/LightningAura";

        [SerializeField]
        [Min(0.05f)]
        float fadeOutSeconds = 2f;

        [Tooltip("When set, used instead of Resources load (Vfx/LightningAura).")]
        GameObject auraPrefabOverride;

        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int TintColorId = Shader.PropertyToID("_TintColor");
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        CharacterController _cc;
        GameObject _auraRoot;
        ParticleSystem[] _particleSystems;
        float[] _initialEmissionRateMultipliers;
        BurstScale[] _burstScales;
        RendererFade[] _rendererFades;
        MaterialPropertyBlock _fadePropertyBlock;
        float _fadeStartTime = -1f;
        PlayerClickMove _playerMove;

        struct BurstScale
        {
            public ParticleSystem Ps;
            public int BurstIndex;
            public float CountMin;
            public float CountMax;
        }

        struct RendererFade
        {
            public Renderer Renderer;
            public bool HasColor;
            public bool HasBaseColor;
            public bool HasTintColor;
            public Color InitialColor;
            public Color InitialBaseColor;
            public Color InitialTintColor;
        }

        /// <summary>Optional runtime override (e.g. from bootstrap). Null keeps <see cref="auraPrefabOverride"/> or Resources.</summary>
        public void Configure(GameObject prefabOverride) => auraPrefabOverride = prefabOverride;

        void Awake()
        {
            _cc = GetComponent<CharacterController>();
            _playerMove = GetComponent<PlayerClickMove>();
        }

        void Start()
        {
            var prefab = auraPrefabOverride != null
                ? auraPrefabOverride
                : Resources.Load<GameObject>(DefaultAuraResourcesPath);
            if (prefab == null)
            {
                enabled = false;
                return;
            }

            _auraRoot = Instantiate(prefab, transform, false);
            _auraRoot.name = prefab.name + "_Intro";
            _auraRoot.transform.localPosition = Vector3.zero;
            _auraRoot.transform.localRotation = Quaternion.identity;

            _particleSystems = _auraRoot.GetComponentsInChildren<ParticleSystem>(true);
            _initialEmissionRateMultipliers = new float[_particleSystems.Length];
            var burstList = new List<BurstScale>(8);
            for (var i = 0; i < _particleSystems.Length; i++)
            {
                var ps = _particleSystems[i];
                if (ps == null)
                    continue;
                _initialEmissionRateMultipliers[i] = ps.emission.rateOverTimeMultiplier;
                AppendBurstSnapshots(ps, burstList);
                if (!ps.isPlaying)
                    ps.Play(true);
            }

            _burstScales = burstList.ToArray();
            CacheRendererColors();
        }

        static void AppendBurstSnapshots(ParticleSystem ps, List<BurstScale> burstList)
        {
            var emission = ps.emission;
            for (var b = 0; b < emission.burstCount; b++)
            {
                var burst = emission.GetBurst(b);
                var curve = burst.count;
                float minC;
                float maxC;
                switch (curve.mode)
                {
                    case ParticleSystemCurveMode.Constant:
                        minC = maxC = curve.constant;
                        break;
                    case ParticleSystemCurveMode.TwoConstants:
                        minC = curve.constantMin;
                        maxC = curve.constantMax;
                        break;
                    default:
                        minC = maxC = Mathf.Max(0f, curve.constant);
                        break;
                }

                burstList.Add(new BurstScale
                {
                    Ps = ps,
                    BurstIndex = b,
                    CountMin = minC,
                    CountMax = maxC
                });
            }
        }

        void CacheRendererColors()
        {
            var renderers = _auraRoot.GetComponentsInChildren<Renderer>(true);
            var list = new List<RendererFade>(renderers.Length);
            foreach (var r in renderers)
            {
                if (r == null)
                    continue;
                var mat = r.sharedMaterial;
                if (mat == null)
                    continue;
                var rf = new RendererFade { Renderer = r };
                if (mat.HasProperty(ColorId))
                {
                    rf.HasColor = true;
                    rf.InitialColor = mat.GetColor(ColorId);
                }

                if (mat.HasProperty(BaseColorId))
                {
                    rf.HasBaseColor = true;
                    rf.InitialBaseColor = mat.GetColor(BaseColorId);
                }

                if (mat.HasProperty(TintColorId))
                {
                    rf.HasTintColor = true;
                    rf.InitialTintColor = mat.GetColor(TintColorId);
                }

                if (rf.HasColor || rf.HasBaseColor || rf.HasTintColor)
                    list.Add(rf);
            }

            _rendererFades = list.ToArray();
        }

        void LateUpdate()
        {
            if (_auraRoot == null)
            {
                enabled = false;
                return;
            }

            if (_cc == null)
                _cc = GetComponent<CharacterController>();

            if (_fadeStartTime < 0f && ShouldStartLandingFade())
                _fadeStartTime = Time.time;

            if (_fadeStartTime < 0f)
                return;

            var t = (Time.time - _fadeStartTime) / Mathf.Max(0.05f, fadeOutSeconds);
            var strength = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
            ApplyFadeStrength(strength);

            if (t < 1f)
                return;

            Destroy(_auraRoot);
            _auraRoot = null;
            Destroy(this);
        }

        bool ShouldStartLandingFade()
        {
            if (_cc == null)
                return false;
            if (_cc.isGrounded)
                return true;
            if (_playerMove == null || !_playerMove.HasCompletedIntroLanding)
                return false;
            var vy = _cc.velocity.y;
            return vy > -4f && vy < 4f;
        }

        void ApplyFadeStrength(float zeroToOne)
        {
            ApplyEmissionStrength(zeroToOne);
            ApplyBurstStrength(zeroToOne);
            ApplyRendererAlpha(zeroToOne);
        }

        void ApplyEmissionStrength(float zeroToOne)
        {
            if (_particleSystems == null)
                return;
            for (var i = 0; i < _particleSystems.Length; i++)
            {
                var ps = _particleSystems[i];
                if (ps == null)
                    continue;
                var emission = ps.emission;
                var mul = i < _initialEmissionRateMultipliers.Length
                    ? _initialEmissionRateMultipliers[i]
                    : emission.rateOverTimeMultiplier;
                emission.rateOverTimeMultiplier = mul * zeroToOne;
            }
        }

        void ApplyBurstStrength(float zeroToOne)
        {
            if (_burstScales == null)
                return;
            foreach (var bs in _burstScales)
            {
                if (bs.Ps == null)
                    continue;
                var emission = bs.Ps.emission;
                if (bs.BurstIndex < 0 || bs.BurstIndex >= emission.burstCount)
                    continue;
                var burst = emission.GetBurst(bs.BurstIndex);
                var mn = Mathf.Max(0f, bs.CountMin * zeroToOne);
                var mx = Mathf.Max(0f, bs.CountMax * zeroToOne);
                burst.count = Mathf.Abs(mx - mn) < 1e-4f
                    ? new ParticleSystem.MinMaxCurve(mn)
                    : new ParticleSystem.MinMaxCurve(mn, mx);
                emission.SetBurst(bs.BurstIndex, burst);
            }
        }

        void ApplyRendererAlpha(float zeroToOne)
        {
            if (_rendererFades == null)
                return;
            if (_fadePropertyBlock == null)
                _fadePropertyBlock = new MaterialPropertyBlock();
            foreach (var rf in _rendererFades)
            {
                if (rf.Renderer == null)
                    continue;
                _fadePropertyBlock.Clear();
                if (rf.HasColor)
                    _fadePropertyBlock.SetColor(ColorId, ScaleAlpha(rf.InitialColor, zeroToOne));
                if (rf.HasBaseColor)
                    _fadePropertyBlock.SetColor(BaseColorId, ScaleAlpha(rf.InitialBaseColor, zeroToOne));
                if (rf.HasTintColor)
                    _fadePropertyBlock.SetColor(TintColorId, ScaleAlpha(rf.InitialTintColor, zeroToOne));
                rf.Renderer.SetPropertyBlock(_fadePropertyBlock);
            }
        }

        static Color ScaleAlpha(Color c, float zeroToOne)
        {
            c.a *= zeroToOne;
            return c;
        }
    }
}
