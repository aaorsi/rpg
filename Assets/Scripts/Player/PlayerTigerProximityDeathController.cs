using Rpg.Npc;
using Rpg.UI;
using UnityEngine;

namespace Rpg.Player
{
    /// <summary>
    /// While a tiger or spider predator stays in melee range, applies periodic percent-max damage:
    /// spider every 2s (33% max), tiger every 3s (50% max). Uses capsule geometry plus a planar pivot check
    /// because <see cref="CharacterController"/> size does not scale with <c>transform.localScale</c>.
    /// </summary>
    [DefaultExecutionOrder(200)]
    public sealed class PlayerTigerProximityDeathController : MonoBehaviour
    {
        const float PredatorScanIntervalSeconds = 0.25f;
        const float SpiderHitIntervalSeconds = 2f;
        const float TigerHitIntervalSeconds = 3f;
        const float SpiderDamageFractionOfMax = 0.33f;
        const float TigerDamageFractionOfMax = 0.5f;

        [SerializeField]
        [Tooltip("Planar gap between capsule silhouettes (center distance minus sum of radii). Larger = trigger from farther.")]
        [Min(-0.25f)]
        float surfaceGapThresholdMeters = 2.75f;

        [SerializeField]
        [Tooltip("Also treat as in range when horizontal distance between player and predator roots is within this many meters.")]
        [Min(0.5f)]
        float maxPlanarPivotDistanceMeters = 3.5f;

        CharacterController _playerCc;
        float _nextPredatorCacheTime = -999f;
        TigerNpcWanderAi[] _cachedTigers = System.Array.Empty<TigerNpcWanderAi>();
        SpiderNpcWanderAi[] _cachedSpiders = System.Array.Empty<SpiderNpcWanderAi>();

        bool _spiderInRange;
        bool _tigerInRange;
        float _nextSpiderDamageTime;
        float _nextTigerDamageTime;

        void Awake()
        {
            TryGetComponent(out _playerCc);
        }

        void LateUpdate()
        {
            if (GameOverController.Instance == null || GameOverController.Instance.IsGameOver)
                return;
            if (!TryGetComponent<HeroHealth>(out var health) || health == null)
                return;

            if (Time.time >= _nextPredatorCacheTime)
            {
                _nextPredatorCacheTime = Time.time + PredatorScanIntervalSeconds;
                _cachedTigers = Object.FindObjectsByType<TigerNpcWanderAi>(
                    FindObjectsInactive.Exclude,
                    FindObjectsSortMode.None);
                _cachedSpiders = Object.FindObjectsByType<SpiderNpcWanderAi>(
                    FindObjectsInactive.Exclude,
                    FindObjectsSortMode.None);
            }

            var spiderClose = AnySpiderInMeleeRange();
            var tigerClose = AnyTigerInMeleeRange();

            TickSpiderDamage(spiderClose, health);
            TickTigerDamage(tigerClose, health);
        }

        void TickSpiderDamage(bool spiderClose, HeroHealth health)
        {
            if (spiderClose)
            {
                if (!_spiderInRange)
                {
                    _spiderInRange = true;
                    _nextSpiderDamageTime = Time.time;
                }

                if (Time.time >= _nextSpiderDamageTime)
                {
                    health.ApplyDamageFractionOfMax(SpiderDamageFractionOfMax);
                    _nextSpiderDamageTime = Time.time + SpiderHitIntervalSeconds;
                }
            }
            else
                _spiderInRange = false;
        }

        void TickTigerDamage(bool tigerClose, HeroHealth health)
        {
            if (tigerClose)
            {
                if (!_tigerInRange)
                {
                    _tigerInRange = true;
                    _nextTigerDamageTime = Time.time;
                }

                if (Time.time >= _nextTigerDamageTime)
                {
                    health.ApplyDamageFractionOfMax(TigerDamageFractionOfMax);
                    _nextTigerDamageTime = Time.time + TigerHitIntervalSeconds;
                }
            }
            else
                _tigerInRange = false;
        }

        bool AnyTigerInMeleeRange()
        {
            for (var i = 0; i < _cachedTigers.Length; i++)
            {
                var tw = _cachedTigers[i];
                if (tw == null)
                    continue;
                if (IsPredatorWithinPlanarMeleeRange(tw.transform, tw.GetComponent<CharacterController>()))
                    return true;
            }

            return false;
        }

        bool AnySpiderInMeleeRange()
        {
            for (var i = 0; i < _cachedSpiders.Length; i++)
            {
                var sw = _cachedSpiders[i];
                if (sw == null)
                    continue;
                if (IsPredatorWithinPlanarMeleeRange(sw.transform, sw.GetComponent<CharacterController>()))
                    return true;
            }

            return false;
        }

        bool IsPredatorWithinPlanarMeleeRange(Transform predatorTransform, CharacterController predatorCc)
        {
            var playerCenter = CapsuleCenterWorld(transform, _playerCc);
            var predatorCenter = CapsuleCenterWorld(predatorTransform, predatorCc);
            var dx = playerCenter.x - predatorCenter.x;
            var dz = playerCenter.z - predatorCenter.z;
            var centerDistPlanar = Mathf.Sqrt(dx * dx + dz * dz);
            var sumRadii = EffectiveRadius(_playerCc) + EffectiveRadius(predatorCc);
            var surfaceGap = centerDistPlanar - sumRadii;
            if (surfaceGap <= surfaceGapThresholdMeters)
                return true;

            var pdx = transform.position.x - predatorTransform.position.x;
            var pdz = transform.position.z - predatorTransform.position.z;
            var pivotPlanar = Mathf.Sqrt(pdx * pdx + pdz * pdz);
            return pivotPlanar <= maxPlanarPivotDistanceMeters;
        }

        static Vector3 CapsuleCenterWorld(Transform t, CharacterController cc)
        {
            if (cc == null)
                return t.position;
            return t.TransformPoint(cc.center);
        }

        static float EffectiveRadius(CharacterController cc)
        {
            return cc != null ? Mathf.Max(0.05f, cc.radius) : 0.35f;
        }
    }
}
