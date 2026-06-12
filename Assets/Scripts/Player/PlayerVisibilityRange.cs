using Rpg.Npc;
using UnityEngine;

namespace Rpg.Player
{
    /// <summary>
    /// Limits clear view distance around the player by combining camera far clip with linear fog.
    /// Attach to the main camera. Values are tweakable in the inspector.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    [DefaultExecutionOrder(90)]
    public sealed class PlayerVisibilityRange : MonoBehaviour
    {
        [SerializeField] bool enableDistanceFog = true;
        [SerializeField] float clearRadiusMeters = 300f;
        [SerializeField] float diffuseBandMeters = 180f;
        [SerializeField] float farClipPaddingMeters = 40f;
        [SerializeField] bool useExponentialFog = true;
        [SerializeField] bool useExp2Fog = true;
        [SerializeField] float targetVisibilityAtBandEnd = 0.02f;
        [SerializeField] float linearFogSoftStartMeters = 50f;
        [SerializeField] bool autoFogColorFromSkybox = true;
        [SerializeField] Color fogColor = new(0.67f, 0.73f, 0.78f, 1f);
        [SerializeField] bool applySkyHaze = true;
        [SerializeField] float skyHazeStrength = 0.25f;
        [SerializeField] float skyExposureMultiplierAtMaxHaze = 0.7f;
        [SerializeField] bool driveUltraSkyboxFogProperties = true;
        [SerializeField] float ultraSkyFogIntensityAtMaxHaze = 0.9f;
        [SerializeField] Vector2 ultraSkyFogBandAtMaxHaze = new(0.02f, 0.45f);

        Camera _cam;
        Material _sourceSkyboxMaterial;
        Material _runtimeSkyboxMaterial;
        Color _baseTint;
        Color _baseSkyTint;
        float _baseExposure = 1f;
        Color _baseUltraFogColor;
        float _baseUltraFogIntensity;
        float _baseUltraFogStart;
        float _baseUltraFogEnd;
        bool _hasTint;
        bool _hasSkyTint;
        bool _hasExposure;
        bool _hasUltraFogColor;
        bool _hasUltraFogIntensity;
        bool _hasUltraFogStart;
        bool _hasUltraFogEnd;

        void Awake()
        {
            _cam = GetComponent<Camera>();
            Apply();
        }

        void OnValidate()
        {
            Apply();
        }

        void LateUpdate()
        {
            Apply();
        }

        void OnDisable()
        {
            RestoreSkyboxMaterial();
        }

        void Apply()
        {
            if (_cam == null)
                _cam = GetComponent<Camera>();
            if (_cam == null)
                return;

            var clearRadius = Mathf.Max(5f, clearRadiusMeters);
            var diffuseBand = Mathf.Max(5f, diffuseBandMeters);
            var farPadding = Mathf.Max(0f, farClipPaddingMeters);

            _cam.farClipPlane = clearRadius + diffuseBand + farPadding;

            if (!enableDistanceFog)
                return;

            RenderSettings.fog = true;
            var resolvedFogColor = autoFogColorFromSkybox
                ? ResolveSkyboxFogColor(fogColor)
                : fogColor;
            RenderSettings.fogColor = resolvedFogColor;

            if (useExponentialFog)
            {
                var endDistance = clearRadius + diffuseBand;
                var targetVisibility = Mathf.Clamp(targetVisibilityAtBandEnd, 0.001f, 0.99f);
                var dist = Mathf.Max(1f, endDistance);
                if (useExp2Fog)
                {
                    RenderSettings.fogMode = FogMode.ExponentialSquared;
                    RenderSettings.fogDensity = Mathf.Sqrt(-Mathf.Log(targetVisibility)) / dist;
                }
                else
                {
                    RenderSettings.fogMode = FogMode.Exponential;
                    RenderSettings.fogDensity = -Mathf.Log(targetVisibility) / dist;
                }
            }
            else
            {
                RenderSettings.fogMode = FogMode.Linear;
                var softStart = Mathf.Max(0f, linearFogSoftStartMeters);
                RenderSettings.fogStartDistance = Mathf.Max(0f, clearRadius - softStart);
                RenderSettings.fogEndDistance = clearRadius + diffuseBand;
            }

            if (applySkyHaze)
            {
                if (GhoulMenaceController.AnyInstanceBlocksPlayerVisibilitySkyDriver())
                    return;
                ApplySkyHaze(resolvedFogColor);
            }
            else
                RestoreSkyboxMaterial();
        }

        static Color ResolveSkyboxFogColor(Color fallback)
        {
            var skybox = RenderSettings.skybox;
            if (skybox == null)
                return fallback;
            if (skybox.HasProperty("_Tint"))
                return skybox.GetColor("_Tint");
            if (skybox.HasProperty("_SkyTint"))
                return skybox.GetColor("_SkyTint");
            return fallback;
        }

        void ApplySkyHaze(Color targetFogColor)
        {
            if (!Application.isPlaying)
                return;
            if (!EnsureRuntimeSkyboxInstance())
                return;

            var targetVisibility = Mathf.Clamp(targetVisibilityAtBandEnd, 0.001f, 0.99f);
            var hazeByFog = 1f - Mathf.Sqrt(targetVisibility);
            var haze = Mathf.Clamp01(hazeByFog * Mathf.Max(0f, skyHazeStrength));
            if (_hasTint)
                _runtimeSkyboxMaterial.SetColor("_Tint", Color.Lerp(_baseTint, targetFogColor, haze));
            if (_hasSkyTint)
                _runtimeSkyboxMaterial.SetColor("_SkyTint", Color.Lerp(_baseSkyTint, targetFogColor, haze));
            if (_hasExposure)
            {
                var exposureMul = Mathf.Clamp(skyExposureMultiplierAtMaxHaze, 0.05f, 1.5f);
                var targetExposure = _baseExposure * exposureMul;
                _runtimeSkyboxMaterial.SetFloat("_Exposure", Mathf.Lerp(_baseExposure, targetExposure, haze));
            }

            if (driveUltraSkyboxFogProperties)
                ApplyUltraSkyboxFogProperties(targetFogColor, haze);
        }

        bool EnsureRuntimeSkyboxInstance()
        {
            var activeSkybox = RenderSettings.skybox;
            if (activeSkybox == null)
                return false;
            if (GhoulMenaceController.AnyInstanceBlocksPlayerVisibilitySkyDriver())
                return false;

            if (_runtimeSkyboxMaterial != null && activeSkybox == _runtimeSkyboxMaterial)
                return true;

            if (_runtimeSkyboxMaterial != null && activeSkybox == _sourceSkyboxMaterial)
            {
                RenderSettings.skybox = _runtimeSkyboxMaterial;
                return true;
            }

            DisposeRuntimeSkyboxOnly();
            _sourceSkyboxMaterial = activeSkybox;
            _runtimeSkyboxMaterial = new Material(_sourceSkyboxMaterial);
            _runtimeSkyboxMaterial.name = $"{activeSkybox.name}_RuntimeFogBlend";
            _hasTint = _runtimeSkyboxMaterial.HasProperty("_Tint");
            _hasSkyTint = _runtimeSkyboxMaterial.HasProperty("_SkyTint");
            _hasExposure = _runtimeSkyboxMaterial.HasProperty("_Exposure");
            _hasUltraFogColor = _runtimeSkyboxMaterial.HasProperty("_FogCol");
            _hasUltraFogIntensity = _runtimeSkyboxMaterial.HasProperty("_FogIntens");
            _hasUltraFogStart = _runtimeSkyboxMaterial.HasProperty("_FogStart");
            _hasUltraFogEnd = _runtimeSkyboxMaterial.HasProperty("_FogEnd");
            _baseTint = _hasTint ? _runtimeSkyboxMaterial.GetColor("_Tint") : Color.white;
            _baseSkyTint = _hasSkyTint ? _runtimeSkyboxMaterial.GetColor("_SkyTint") : Color.white;
            _baseExposure = _hasExposure ? _runtimeSkyboxMaterial.GetFloat("_Exposure") : 1f;
            _baseUltraFogColor = _hasUltraFogColor ? _runtimeSkyboxMaterial.GetColor("_FogCol") : Color.white;
            _baseUltraFogIntensity = _hasUltraFogIntensity ? _runtimeSkyboxMaterial.GetFloat("_FogIntens") : 0f;
            _baseUltraFogStart = _hasUltraFogStart ? _runtimeSkyboxMaterial.GetFloat("_FogStart") : 0f;
            _baseUltraFogEnd = _hasUltraFogEnd ? _runtimeSkyboxMaterial.GetFloat("_FogEnd") : 0.4f;
            RenderSettings.skybox = _runtimeSkyboxMaterial;
            return true;
        }

        void ApplyUltraSkyboxFogProperties(Color targetFogColor, float haze)
        {
            if (_runtimeSkyboxMaterial == null)
                return;

            if (_hasUltraFogColor)
                _runtimeSkyboxMaterial.SetColor("_FogCol", Color.Lerp(_baseUltraFogColor, targetFogColor, haze));
            if (_hasUltraFogIntensity)
            {
                var maxIntensity = Mathf.Clamp01(ultraSkyFogIntensityAtMaxHaze);
                var targetIntensity = Mathf.Max(_baseUltraFogIntensity, maxIntensity);
                _runtimeSkyboxMaterial.SetFloat("_FogIntens", Mathf.Lerp(_baseUltraFogIntensity, targetIntensity, haze));
            }
            if (_hasUltraFogStart)
                _runtimeSkyboxMaterial.SetFloat("_FogStart", Mathf.Lerp(_baseUltraFogStart, ultraSkyFogBandAtMaxHaze.x, haze));
            if (_hasUltraFogEnd)
                _runtimeSkyboxMaterial.SetFloat("_FogEnd", Mathf.Lerp(_baseUltraFogEnd, ultraSkyFogBandAtMaxHaze.y, haze));
        }

        void RestoreSkyboxMaterial()
        {
            if (_sourceSkyboxMaterial != null && RenderSettings.skybox == _runtimeSkyboxMaterial)
                RenderSettings.skybox = _sourceSkyboxMaterial;
            if (_runtimeSkyboxMaterial != null)
            {
                if (Application.isPlaying)
                    Destroy(_runtimeSkyboxMaterial);
                else
                    DestroyImmediate(_runtimeSkyboxMaterial);
            }
            _runtimeSkyboxMaterial = null;
            _sourceSkyboxMaterial = null;
        }

        void DisposeRuntimeSkyboxOnly()
        {
            if (_runtimeSkyboxMaterial == null)
                return;
            if (Application.isPlaying)
                Destroy(_runtimeSkyboxMaterial);
            else
                DestroyImmediate(_runtimeSkyboxMaterial);
            _runtimeSkyboxMaterial = null;
        }
    }
}
