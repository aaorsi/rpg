using Rpg.UI;
using UnityEngine;

namespace Rpg.Player
{
    /// <summary>
    /// When the player's render bounds are fully below the <c>Water Surface Mirror</c> Y plane, counts down;
    /// after the timer reaches zero, applies 20% of max health per second until the hero surfaces or dies.
    /// </summary>
    public sealed class PlayerUnderwaterDeathController : MonoBehaviour
    {
        const float PostCountdownDamagePerSecondFractionOfMax = 0.2f;

        [SerializeField]
        [Tooltip("Scene object whose world Y is the water surface (mirror plane).")]
        string waterSurfaceObjectName = "Water Surface Mirror";

        [SerializeField]
        [Min(0.5f)]
        float submergedDeathCountdownSeconds = 12f;

        [SerializeField]
        [Tooltip("Body top must be at least this far below the water plane to count as fully submerged.")]
        float fullySubmergedEpsilonMeters = 0.08f;

        Transform _waterSurface;
        float _waterPlaneY;
        bool _hasWaterPlane;
        float _remainingSeconds;

        void OnEnable()
        {
            _remainingSeconds = submergedDeathCountdownSeconds;
        }

        void Start()
        {
            CacheWaterSurface();
        }

        void CacheWaterSurface()
        {
            _hasWaterPlane = false;
            _waterSurface = null;
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (t == null || t.gameObject == null)
                    continue;
                if (!string.Equals(t.gameObject.name, waterSurfaceObjectName, System.StringComparison.Ordinal))
                    continue;
                _waterSurface = t;
                _waterPlaneY = t.position.y;
                _hasWaterPlane = true;
                return;
            }
        }

        void Update()
        {
            var ui = GameOverController.Instance;
            if (ui == null || ui.IsGameOver)
                return;
            if (!_hasWaterPlane)
            {
                if (Time.frameCount % 120 == 0)
                    CacheWaterSurface();
                return;
            }

            if (_waterSurface != null)
                _waterPlaneY = _waterSurface.position.y;

            if (!TryGetPlayerBodyWorldYRange(out _, out var maxBodyY))
                return;

            var fullyBelowSurface = maxBodyY < _waterPlaneY - fullySubmergedEpsilonMeters;
            if (fullyBelowSurface)
            {
                if (_remainingSeconds > 0f)
                    _remainingSeconds -= Time.deltaTime;

                ui.SetDrowningCountdownVisible(Mathf.Max(0f, _remainingSeconds), submergedDeathCountdownSeconds);

                if (_remainingSeconds <= 0f
                    && TryGetComponent<HeroHealth>(out var health)
                    && health != null)
                {
                    health.ApplyDamage(health.MaxHealth * PostCountdownDamagePerSecondFractionOfMax * Time.deltaTime);
                }
            }
            else
            {
                _remainingSeconds = submergedDeathCountdownSeconds;
                ui.HideDrowningCountdown();
            }
        }

        bool TryGetPlayerBodyWorldYRange(out float minY, out float maxY)
        {
            minY = 0f;
            maxY = 0f;
            var has = false;
            var b = default(Bounds);
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                if (r == null || !r.enabled)
                    continue;
                if (!has)
                {
                    b = r.bounds;
                    has = true;
                }
                else
                    b.Encapsulate(r.bounds);
            }
            if (!has)
            {
                if (TryGetComponent<CharacterController>(out var cc))
                {
                    var c = transform.position + cc.center;
                    var extY = cc.height * 0.5f * Mathf.Abs(transform.lossyScale.y);
                    minY = c.y - extY;
                    maxY = c.y + extY;
                    return true;
                }
                return false;
            }
            minY = b.min.y;
            maxY = b.max.y;
            return true;
        }
    }
}
