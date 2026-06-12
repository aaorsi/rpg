using System;
using System.Collections.Generic;
using System.Threading;
using Rpg.Core;
using Rpg.Dialogue;
using Rpg.Player;
using Rpg.UI;
using UnityEngine;

namespace Rpg.Npc
{
    /// <summary>
    /// When the hero carries a stolen live chicken, the nearest eligible NPC within range shouts (LLM),
    /// approaches for a limited time, opens a confrontation dialogue at most once per incident (no re-open after the player closes it),
    /// accepts any hero→NPC item as amends,
    /// or returns home if the hero escapes or closes dialogue before paying.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [DefaultExecutionOrder(17)]
    public sealed class NpcChickenTheftConfrontation : MonoBehaviour
    {
        enum Phase
        {
            Idle,
            ShoutPending,
            Chasing,
            ReturningHome,
            Cooldown
        }

        const string FallbackShout = "Hey—stop there! You're stealing my chicken!";

        static readonly List<NpcChickenTheftConfrontation> Registry = new List<NpcChickenTheftConfrontation>(16);
        static int s_pickFrame = -1;
        static NpcChickenTheftConfrontation s_frameWinner;
        static NpcChickenTheftConfrontation s_exclusiveRunner;

        [SerializeField, Min(2f)] float triggerRadiusMeters = 20f;
        [SerializeField, Min(1f)] float confrontDistanceMeters = 5f;
        [SerializeField, Min(1f)] float approachWindowSeconds = 20f;
        [SerializeField, Min(0.5f)] float walkSpeed = 4.5f;
        [SerializeField] float gravity = -18f;
        [SerializeField, Min(0.5f)] float returnStopDistanceMeters = 1.35f;
        [SerializeField, Min(0f)] float cooldownAfterSessionSeconds = 25f;

        NpcDialogueBinding _binding;
        CharacterController _cc;
        NpcAmbientDrift _drift;
        PackAnimalAnimatorDriver _packDriver;
        Transform _hero;

        Phase _phase = Phase.Idle;
        Vector3 _homePosition;
        bool _hasHomePosition;
        float _approachDeadline;
        float _cooldownUntil;
        float _verticalVelocity;
        bool _driftWasEnabled;
        float _lastDialogueOpenAttemptAt = -999f;
        /// <summary>Auto-opened theft dialogue at most once per chase session; after the player closes it, no more auto-opens.</summary>
        bool _theftAutoDialogueOpenedOnce;

        public static void EnsureOnAllNpcBindings()
        {
            foreach (var b in UnityEngine.Object.FindObjectsByType<NpcDialogueBinding>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                EnsureOnBinding(b);
        }

        public static void EnsureOnBinding(NpcDialogueBinding binding)
        {
            if (binding == null || binding.gameObject == null)
                return;
            var go = binding.gameObject;
            if (go.GetComponent<SidekickCompanion>() != null)
                return;
            if (go.GetComponent<GhoulMenaceController>() != null)
                return;
            if (go.GetComponent<BossAi>() != null)
                return;
            if (go.GetComponent<CharacterController>() == null)
                return;
            if (go.GetComponent<NpcChickenTheftConfrontation>() == null)
                go.AddComponent<NpcChickenTheftConfrontation>();
        }

        /// <summary>Dialogue closed before any hero→NPC reparation during chicken confrontation.</summary>
        public static void NotifyDialogueClosedWithoutTrade(string npcId)
        {
            var c = FindByNpcId(npcId);
            c?.OnDialogueClosedWithoutTrade();
        }

        /// <summary>Hero gave any item (or narrative receive_object) during confrontation.</summary>
        public static void NotifyReparationComplete(string npcId)
        {
            var c = FindByNpcId(npcId);
            c?.OnReparationComplete();
        }

        static NpcChickenTheftConfrontation FindByNpcId(string npcId)
        {
            if (string.IsNullOrWhiteSpace(npcId))
                return null;
            for (var i = 0; i < Registry.Count; i++)
            {
                var c = Registry[i];
                if (c == null || c._binding == null || c._binding.Definition == null)
                    continue;
                if (string.Equals(c._binding.Definition.npcId, npcId, StringComparison.OrdinalIgnoreCase))
                    return c;
            }

            return null;
        }

        void Awake()
        {
            if (GetComponent<GhoulMenaceController>() != null || GetComponent<BossAi>() != null)
            {
                Destroy(this);
                return;
            }

            _binding = GetComponent<NpcDialogueBinding>();
            _cc = GetComponent<CharacterController>();
            _drift = GetComponent<NpcAmbientDrift>();
            _packDriver = GetComponent<PackAnimalAnimatorDriver>();
        }

        void OnEnable() => Registry.Add(this);

        void OnDisable()
        {
            Registry.Remove(this);
            if (s_exclusiveRunner == this)
                s_exclusiveRunner = null;
            RestoreDriftIfNeeded();
        }

        void Update()
        {
            if (_binding == null || _binding.Definition == null || _cc == null)
                return;
            if (GameOverController.Instance != null && GameOverController.Instance.IsGameOver)
                return;

            RefreshHero();
            var dm = DialogueManager.Instance;
            var inv = dm != null ? dm.Inventory : null;

            switch (_phase)
            {
                case Phase.Idle:
                    if (Time.time < _cooldownUntil)
                        return;
                    if (!HeroHasLiveChicken(inv))
                        return;
                    if (s_exclusiveRunner != null && s_exclusiveRunner != this && s_exclusiveRunner._phase != Phase.Idle)
                        return;
                    if (DesignatedThisFrame() != this)
                        return;
                    if (PlanarDistanceToHero() > triggerRadiusMeters)
                        return;
                    BeginConfrontationFromIdle();
                    break;
                case Phase.ShoutPending:
                    break;
                case Phase.Chasing:
                    if (!HeroHasLiveChicken(inv))
                    {
                        AbortToIdleResumeWorld();
                        return;
                    }

                    if (dm != null && dm.IsDialogueOpen)
                        return;

                    if (Time.time >= _approachDeadline)
                    {
                        StartReturnHome();
                        return;
                    }

                    if (_hero == null)
                    {
                        StartReturnHome();
                        return;
                    }

                    if (PlanarDistanceToHero() <= confrontDistanceMeters)
                    {
                        TryOpenConfrontationDialogue(dm);
                        return;
                    }

                    StepChaseToward(_hero.position);
                    break;
                case Phase.ReturningHome:
                    TickReturnHome();
                    break;
                case Phase.Cooldown:
                    if (Time.time >= _cooldownUntil)
                        _phase = Phase.Idle;
                    break;
            }
        }

        NpcChickenTheftConfrontation DesignatedThisFrame()
        {
            if (Time.frameCount == s_pickFrame)
                return s_frameWinner;
            s_pickFrame = Time.frameCount;
            s_frameWinner = null;
            if (s_exclusiveRunner != null && s_exclusiveRunner._phase != Phase.Idle)
            {
                s_frameWinner = s_exclusiveRunner;
                return s_frameWinner;
            }

            RefreshHero();
            var dm = DialogueManager.Instance;
            var inv = dm != null ? dm.Inventory : null;
            if (!HeroHasLiveChicken(inv) || _hero == null)
            {
                dm?.ClearChickenTheftIncidentVictim();
                return null;
            }

            var best = float.MaxValue;
            NpcChickenTheftConfrontation winner = null;
            for (var i = 0; i < Registry.Count; i++)
            {
                var c = Registry[i];
                if (c == null || !c.isActiveAndEnabled || c._binding == null || c._binding.Definition == null)
                    continue;
                if (c.GetComponent<SidekickCompanion>() != null)
                    continue;
                if (c._phase != Phase.Idle || Time.time < c._cooldownUntil)
                    continue;
                var d = c.PlanarDistanceToHero(c._hero);
                if (d > c.triggerRadiusMeters)
                    continue;
                if (d < best)
                {
                    best = d;
                    winner = c;
                }
            }

            s_frameWinner = winner;
            return s_frameWinner;
        }

        void BeginConfrontationFromIdle()
        {
            if (_binding?.Definition != null)
                DialogueManager.Instance?.RegisterChickenTheftIncidentVictim(_binding.Definition.npcId);
            if (!_hasHomePosition)
            {
                _homePosition = transform.position;
                _hasHomePosition = true;
            }

            s_exclusiveRunner = this;
            _theftAutoDialogueOpenedOnce = false;
            _phase = Phase.ShoutPending;
            DisableDriftForChase();
            RunShoutThenChaseAsync();
        }

        async void RunShoutThenChaseAsync()
        {
            string shout = FallbackShout;
            try
            {
                if (DialogueManager.Instance != null)
                    shout = await DialogueManager.Instance.RequestChickenTheftShoutLineAsync(CancellationToken.None);
            }
            catch
            {
                shout = FallbackShout;
            }

            if (!isActiveAndEnabled || _phase != Phase.ShoutPending)
                return;

            shout = SanitizeOneLine(shout);
            var overlay = UnityEngine.Object.FindFirstObjectByType<GameplayIntroOverlay>();
            if (overlay != null)
                overlay.ShowNpcShoutLine(shout, holdSeconds: 3.2f);

            if (_phase != Phase.ShoutPending)
                return;
            _phase = Phase.Chasing;
            _approachDeadline = Time.time + approachWindowSeconds;
        }

        static string SanitizeOneLine(string raw)
        {
            var t = (raw ?? string.Empty).Trim();
            if (t.Length == 0)
                return FallbackShout;
            var nl = t.IndexOfAny(new[] { '\r', '\n' });
            if (nl >= 0)
                t = t.Substring(0, nl).Trim();
            if (t.Length > 200)
                t = t.Substring(0, 200).TrimEnd();
            return string.IsNullOrEmpty(t) ? FallbackShout : t;
        }

        void TryOpenConfrontationDialogue(DialogueManager dm)
        {
            if (dm == null || dm.IsDialogueOpen || _binding.Definition == null)
                return;
            if (_theftAutoDialogueOpenedOnce)
                return;
            if (Time.time - _lastDialogueOpenAttemptAt < 1.25f)
                return;
            _lastDialogueOpenAttemptAt = Time.time;
            var opening =
                $"{_binding.Definition.displayName}: I saw you take one of my chickens. I need something in return — " +
                "anything you can honestly spare will settle it. Use Give in the quick panel below, or tell me what you offer.";
            if (!dm.TryStartChickenTheftConfrontationDialogue(_binding.Definition, opening))
            {
                _approachDeadline = Time.time + approachWindowSeconds;
                return;
            }

            _theftAutoDialogueOpenedOnce = true;
            var cam = Camera.main != null ? Camera.main.GetComponent<SliceFollowCamera>() : null;
            cam?.StartDialogueFocus(transform);
        }

        void OnDialogueClosedWithoutTrade()
        {
            if (!HeroHasLiveChicken(DialogueManager.Instance != null ? DialogueManager.Instance.Inventory : null))
            {
                AbortToIdleResumeWorld();
                return;
            }

            DisableDriftForChase();
            _phase = Phase.Chasing;
            _approachDeadline = Time.time + approachWindowSeconds;
        }

        void OnReparationComplete()
        {
            RestoreDriftIfNeeded();
            _phase = Phase.Cooldown;
            _cooldownUntil = Time.time + cooldownAfterSessionSeconds;
            if (s_exclusiveRunner == this)
                s_exclusiveRunner = null;
            DialogueManager.Instance?.ClearChickenTheftIncidentVictim();
        }

        void AbortToIdleResumeWorld()
        {
            RestoreDriftIfNeeded();
            _phase = Phase.Cooldown;
            _cooldownUntil = Time.time + cooldownAfterSessionSeconds * 0.6f;
            if (s_exclusiveRunner == this)
                s_exclusiveRunner = null;
            DialogueManager.Instance?.ClearChickenTheftIncidentVictim();
        }

        void StartReturnHome()
        {
            if (s_exclusiveRunner == this)
                s_exclusiveRunner = null;
            _phase = Phase.ReturningHome;
            _verticalVelocity = 0f;
        }

        void TickReturnHome()
        {
            var to = _homePosition - transform.position;
            to.y = 0f;
            var dist = to.magnitude;
            if (dist <= returnStopDistanceMeters)
            {
                RestoreDriftIfNeeded();
                _phase = Phase.Cooldown;
                _cooldownUntil = Time.time + cooldownAfterSessionSeconds;
                if (s_exclusiveRunner == this)
                    s_exclusiveRunner = null;
                return;
            }

            StepChaseToward(_homePosition);
        }

        void StepChaseToward(Vector3 targetXZ)
        {
            if (_cc == null || !_cc.enabled || !_cc.gameObject.activeInHierarchy)
                return;

            var here = transform.position;
            var to = targetXZ - here;
            to.y = 0f;
            var dist = to.magnitude;
            if (dist < 0.001f)
                return;
            var dir = to / dist;
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                Quaternion.LookRotation(dir, Vector3.up),
                1f - Mathf.Exp(-8f * Time.deltaTime));

            if (_cc.isGrounded && _verticalVelocity < 0f)
                _verticalVelocity = -1f;
            _verticalVelocity += gravity * Time.deltaTime;
            var planar = dir * walkSpeed;
            _cc.Move((planar + Vector3.up * _verticalVelocity) * Time.deltaTime);
            if (_packDriver != null)
                _packDriver.SetLocomotionReferenceSpeed(Mathf.Max(0.01f, walkSpeed), Mathf.Max(0.01f, walkSpeed * 0.92f));
        }

        void DisableDriftForChase()
        {
            if (_drift == null)
                return;
            _driftWasEnabled = _drift.enabled;
            _drift.enabled = false;
        }

        void RestoreDriftIfNeeded()
        {
            if (_drift == null)
                return;
            _drift.enabled = _driftWasEnabled;
        }

        void RefreshHero()
        {
            if (_hero == null || !_hero.gameObject.activeInHierarchy)
            {
                var go = GameObject.FindGameObjectWithTag(GameConstants.PlayerTag);
                _hero = go != null ? go.transform : null;
            }
        }

        float PlanarDistanceToHero(Transform heroOverride = null)
        {
            var h = heroOverride != null ? heroOverride : _hero;
            if (h == null)
                return float.MaxValue;
            var dx = transform.position.x - h.position.x;
            var dz = transform.position.z - h.position.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        static bool HeroHasLiveChicken(InventoryService inv)
        {
            if (inv == null)
                return false;
            var rows = inv.GetInventoryView(InventoryService.HeroActorId);
            if (rows == null)
                return false;
            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                if (row == null || row.quantity <= 0 || string.IsNullOrWhiteSpace(row.itemId))
                    continue;
                if (string.Equals(row.itemId.Trim(), GameConstants.LiveChickenItemId, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }
    }
}
