using System;
using Rpg.UI;
using UnityEngine;

namespace Rpg.Player
{
    /// <summary>
    /// Hero HP pool: percent-based damage from hazards, passive regen when below max, game over at 0.
    /// </summary>
    public sealed class HeroHealth : MonoBehaviour
    {
        const float RegenIntervalSeconds = 3f;
        const float RegenFractionOfMaxPerTick = 0.01f;

        [SerializeField]
        [Min(1f)]
        float maxHealth = 100f;

        [SerializeField]
        float currentHealth = 100f;

        [SerializeField]
        [Tooltip("If true, Awake sets current HP to 75% of max (layout / tuning only).")]
        bool startAtThreeQuartersForTesting;

        float _regenCarrySeconds;
        bool _deathDispatched;

        public float MaxHealth => maxHealth;
        public float CurrentHealth => currentHealth;
        public float Normalized => maxHealth > 0f ? Mathf.Clamp01(currentHealth / maxHealth) : 0f;

        public event Action HealthChanged;

        void Awake()
        {
            maxHealth = Mathf.Max(1f, maxHealth);
            if (startAtThreeQuartersForTesting)
                currentHealth = maxHealth * 0.75f;
            else
                currentHealth = Mathf.Clamp(currentHealth, 0f, maxHealth);
        }

        void Start() => RaiseHealthChanged();

        void Update()
        {
            if (!enabled || _deathDispatched)
                return;
            if (GameOverController.Instance != null && GameOverController.Instance.IsGameOver)
                return;
            if (currentHealth <= 0f || currentHealth >= maxHealth - 1e-4f)
                return;

            _regenCarrySeconds += Time.deltaTime;
            while (_regenCarrySeconds >= RegenIntervalSeconds)
            {
                _regenCarrySeconds -= RegenIntervalSeconds;
                var before = currentHealth;
                currentHealth = Mathf.Min(maxHealth, currentHealth + maxHealth * RegenFractionOfMaxPerTick);
                if (currentHealth > before + 1e-5f)
                    RaiseHealthChanged();
            }
        }

        public void ApplyDamage(float amount)
        {
            if (amount <= 0f || _deathDispatched)
                return;
            currentHealth = Mathf.Max(0f, currentHealth - amount);
            RaiseHealthChanged();
        }

        /// <summary>Removes <paramref name="fractionOfMax"/> × <see cref="MaxHealth"/> (e.g. 0.33 = 33%).</summary>
        public void ApplyDamageFractionOfMax(float fractionOfMax)
        {
            if (fractionOfMax <= 0f || _deathDispatched)
                return;
            ApplyDamage(maxHealth * Mathf.Clamp01(fractionOfMax));
        }

        public void Heal(float amount)
        {
            if (amount <= 0f || _deathDispatched)
                return;
            currentHealth = Mathf.Min(maxHealth, currentHealth + amount);
            RaiseHealthChanged();
        }

        public void SetMaxHealth(float value, bool refill)
        {
            maxHealth = Mathf.Max(1f, value);
            if (refill)
                currentHealth = maxHealth;
            else
                currentHealth = Mathf.Clamp(currentHealth, 0f, maxHealth);
            RaiseHealthChanged();
        }

        void RaiseHealthChanged()
        {
            HealthChanged?.Invoke();
            if (currentHealth > 0f || _deathDispatched)
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
