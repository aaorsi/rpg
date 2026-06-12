using System;
using Rpg.UI;
using UnityEngine;

namespace Rpg.Player
{
    /// <summary>
    /// Food / hunger meter: drains slowly; eating restores a chunk; death at zero.
    /// </summary>
    public sealed class HeroHunger : MonoBehaviour
    {
        const float DrainIntervalSeconds = 5f;
        const float DrainFractionOfMaxPerTick = 0.01f;

        [SerializeField]
        [Min(1f)]
        float maxFood = 100f;

        [SerializeField]
        float currentFood = 50f;

        float _drainCarrySeconds;
        bool _deathDispatched;

        public float MaxFood => maxFood;
        public float CurrentFood => currentFood;
        public float Normalized => maxFood > 0f ? Mathf.Clamp01(currentFood / maxFood) : 0f;

        public event Action HungerChanged;

        void Awake()
        {
            maxFood = Mathf.Max(1f, maxFood);
            currentFood = Mathf.Clamp(currentFood, 0f, maxFood);
        }

        void Start() => RaiseHungerChanged();

        void Update()
        {
            if (!enabled || _deathDispatched)
                return;
            if (GameOverController.Instance != null && GameOverController.Instance.IsGameOver)
                return;
            if (currentFood <= 0f)
                return;

            _drainCarrySeconds += Time.deltaTime;
            while (_drainCarrySeconds >= DrainIntervalSeconds)
            {
                _drainCarrySeconds -= DrainIntervalSeconds;
                currentFood = Mathf.Max(0f, currentFood - maxFood * DrainFractionOfMaxPerTick);
                RaiseHungerChanged();
            }
        }

        /// <summary>Adds <paramref name="fractionOfMax"/> × max food (e.g. 0.5 = +50%), capped at max.</summary>
        public void AddFoodFractionOfMax(float fractionOfMax)
        {
            if (fractionOfMax <= 0f || _deathDispatched)
                return;
            currentFood = Mathf.Min(maxFood, currentFood + maxFood * Mathf.Clamp01(fractionOfMax));
            RaiseHungerChanged();
        }

        void RaiseHungerChanged()
        {
            HungerChanged?.Invoke();
            if (currentFood > 0f || _deathDispatched)
                return;
            if (!Application.isPlaying)
                return;
            var ui = GameOverController.Instance;
            if (ui == null || ui.IsGameOver)
                return;
            _deathDispatched = true;
            ui.TriggerPlayerDeath(gameObject);
        }
    }
}
