using System.Collections.Generic;
using Rpg.Audio;
using Rpg.Core;
using Rpg.Dialogue;
using Rpg.Player;
using Rpg.UI;
using UnityEngine;

namespace Rpg.Npc
{
    /// <summary>
    /// Boss encounter: re-checks planar distance to the hero every <see cref="distanceCheckIntervalSeconds"/> to decide chase vs idle
    /// (within <see cref="aggroRadiusMeters"/> starts chase). Moves at <see cref="chaseSpeedMultiplierVsHeroWalk"/> × hero walk speed while chasing.
    /// Kill range <see cref="killRadiusMeters"/> is evaluated every frame; while inside, applies
    /// <see cref="meleeDamageFractionOfMaxHealth"/> of hero max health every <see cref="meleeDamageIntervalSeconds"/>.
    /// At play mode, a <see cref="BossAi"/> is added automatically to scene objects named <c>Ghoul</c> (including <c>Ghoul (Clone)</c>).
    /// </summary>
    [AddComponentMenu("Rpg/NPC/Boss AI")]
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(15)]
    public sealed class BossAi : MonoBehaviour
    {
        const string WalkTriggerName = "Walk";
        const string IdleTriggerName = "Idle";
        const float FallbackHeroWalkSpeed = 4.5f;
        const float Gravity = -18f;

        [Header("Boss AI (Inspector tuning)")]
        [Tooltip("Horizontal distance at which the boss will chase (re-evaluated on the interval below). Minimum 50 m.")]
        [SerializeField]
        [Min(50f)]
        float aggroRadiusMeters = 50f;

        [Tooltip("How often to re-sample distance to the hero for chase vs idle (kill range is still checked every frame).")]
        [SerializeField]
        [Min(0.1f)]
        float distanceCheckIntervalSeconds = 3f;

        [Tooltip("Horizontal distance at which melee damage ticks apply.")]
        [SerializeField]
        [Min(0.1f)]
        float killRadiusMeters = 5f;

        [Tooltip("Seconds between Ghoul melee hits while the hero stays inside kill radius.")]
        [SerializeField]
        [Min(0.1f)]
        float meleeDamageIntervalSeconds = 3f;

        [Tooltip("Each hit removes this fraction of the hero's max health (0.8 = 80%).")]
        [SerializeField]
        [Range(0.01f, 1f)]
        float meleeDamageFractionOfMaxHealth = 0.8f;

        [Tooltip("Planar move speed while chasing = hero walk speed × this value (default: twice hero walk).")]
        [SerializeField]
        [Min(0.1f)]
        float chaseSpeedMultiplierVsHeroWalk = 2f;

        CharacterController _cc;
        Animator _animator;
        float _verticalVelocity;
        bool _isChasingAnim;
        bool _hasWalkSpeedFloat;
        int _walkSpeedHash;
        bool _heroInMeleeDamageRange;
        float _nextMeleeDamageTime;
        bool _chaseWanted;
        float _nextRangeCheckTime = -999f;
        AudioSource _chaseSfxSource;
        AudioClip _chaseStartSfx;

        /// <summary>Horizontal distance at which melee damage applies (Ghoul menace dialogue uses this + offset).</summary>
        public float MeleeKillRadiusMeters => killRadiusMeters;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoWireGhoulBossInPlayMode()
        {
            if (!Application.isPlaying)
                return;
            WireAllInActiveScene();
        }

        /// <summary>
        /// Adds <see cref="BossAi"/> to scene objects whose name is a Ghoul boss instance (e.g. <c>Ghoul</c>, <c>Ghoul (Clone)</c>).
        /// A <see cref="CharacterController"/> is created on that object at runtime if none exists on the root.
        /// </summary>
        public static void WireAllInActiveScene()
        {
            if (!Application.isPlaying)
                return;

            var seenHosts = new HashSet<int>();
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (t == null || t.gameObject == null)
                    continue;
                if (!NameLooksLikeSceneGhoulBoss(t.gameObject.name))
                    continue;
                TryWireGhoulRoot(t.gameObject, seenHosts);
            }
        }

        static bool NameLooksLikeSceneGhoulBoss(string rawName)
        {
            if (string.IsNullOrWhiteSpace(rawName))
                return false;
            var nm = rawName.Trim();
            if (!nm.StartsWith("Ghoul", System.StringComparison.OrdinalIgnoreCase))
                return false;
            if (nm.Length == 5)
                return true;
            var c = nm[5];
            if (char.IsLetterOrDigit(c))
                return false;
            return true;
        }

        static void TryWireGhoulRoot(GameObject host, HashSet<int> seenHosts)
        {
            if (host == null)
                return;
            if (!seenHosts.Add(host.GetInstanceID()))
                return;

            foreach (var w in host.GetComponentsInChildren<AnimalNpcWanderAi>(true))
                w.enabled = false;
            foreach (var tw in host.GetComponentsInChildren<TigerNpcWanderAi>(true))
                tw.enabled = false;

            GhoulMenaceController.EnsureOnGhoulHost(host);

            if (host.GetComponent<BossAi>() == null)
                host.AddComponent<BossAi>();
        }

        void Awake()
        {
            aggroRadiusMeters = Mathf.Max(50f, aggroRadiusMeters);
            distanceCheckIntervalSeconds = Mathf.Max(0.1f, distanceCheckIntervalSeconds);
            EnsureRuntimeCharacterController();
            _animator = GetComponent<Animator>() ?? GetComponentInChildren<Animator>(true);
            if (_animator != null)
                _animator.applyRootMotion = false;

            if (_animator != null && _animator.runtimeAnimatorController != null)
            {
                _walkSpeedHash = Animator.StringToHash("WalkSpeed");
                foreach (var p in _animator.parameters)
                {
                    if (p.type == AnimatorControllerParameterType.Float && p.nameHash == _walkSpeedHash)
                    {
                        _hasWalkSpeedFloat = true;
                        break;
                    }
                }
            }

            if (TryGetComponent<NpcAmbientDrift>(out var drift))
                drift.enabled = false;
            foreach (var w in GetComponentsInChildren<AnimalNpcWanderAi>(true))
                w.enabled = false;
            foreach (var tw in GetComponentsInChildren<TigerNpcWanderAi>(true))
                tw.enabled = false;
            _chaseSfxSource = gameObject.AddComponent<AudioSource>();
            _chaseSfxSource.playOnAwake = false;
            _chaseSfxSource.spatialBlend = 0f;
            _chaseStartSfx = RuntimeAudioClipLoader.LoadFromAssetPath("Assets/FREE SOUND PACK_TM(355)/Horror(13)/Spirits_In_Dispair-006.wav");
        }

        void EnsureRuntimeCharacterController()
        {
            _cc = GetComponent<CharacterController>();
            if (_cc == null)
            {
                _cc = gameObject.AddComponent<CharacterController>();
                _cc.height = 2f;
                _cc.radius = 0.45f;
                _cc.center = new Vector3(0f, 1f, 0f);
                _cc.stepOffset = 0.25f;
                _cc.skinWidth = 0.05f;
            }

            foreach (var other in GetComponentsInChildren<CharacterController>(true))
            {
                if (other != null && other != _cc)
                    other.enabled = false;
            }
        }

        void Update()
        {
            if (_cc == null || !_cc.enabled)
                return;

            if (GameOverController.Instance != null && GameOverController.Instance.IsGameOver)
            {
                StepGravityOnly();
                SetLocomotionVisual(false, FallbackHeroWalkSpeed);
                return;
            }

            if (DialogueManager.Instance != null && DialogueManager.Instance.IsDialogueOpen)
            {
                StepGravityOnly();
                SetLocomotionVisual(false, FallbackHeroWalkSpeed);
                return;
            }

            if (TryGetComponent<GhoulMenaceController>(out var ghoulMenace) && !ghoulMenace.IsAggressionUnleashed)
            {
                if (!TryResolveHero(out _, out var walkSpeedPrey))
                {
                    StepGravityOnly();
                    SetLocomotionVisual(false, FallbackHeroWalkSpeed);
                    return;
                }

                StepGravityOnly();
                SetLocomotionVisual(false, walkSpeedPrey);
                return;
            }

            if (!TryResolveHero(out var hero, out var walkSpeed))
            {
                StepGravityOnly();
                SetLocomotionVisual(false, FallbackHeroWalkSpeed);
                return;
            }

            var toHero = hero.position - transform.position;
            toHero.y = 0f;
            var dist = toHero.magnitude;

            if (dist <= killRadiusMeters)
            {
                if (hero.TryGetComponent<HeroHealth>(out var heroHealth) && heroHealth != null)
                {
                    if (!_heroInMeleeDamageRange)
                    {
                        _heroInMeleeDamageRange = true;
                        _nextMeleeDamageTime = Time.time;
                    }

                    if (Time.time >= _nextMeleeDamageTime)
                    {
                        heroHealth.ApplyDamageFractionOfMax(meleeDamageFractionOfMaxHealth);
                        _nextMeleeDamageTime = Time.time + meleeDamageIntervalSeconds;
                    }
                }

                StepGravityOnly();
                SetLocomotionVisual(false, walkSpeed);
                return;
            }

            _heroInMeleeDamageRange = false;

            if (Time.time >= _nextRangeCheckTime)
            {
                _nextRangeCheckTime = Time.time + distanceCheckIntervalSeconds;
                var wasChasing = _chaseWanted;
                _chaseWanted = dist <= aggroRadiusMeters;
                if (!wasChasing && _chaseWanted && _chaseSfxSource != null && _chaseStartSfx != null)
                    _chaseSfxSource.PlayOneShot(_chaseStartSfx);
            }

            if (_chaseWanted)
            {
                var chaseSpeed = walkSpeed * chaseSpeedMultiplierVsHeroWalk;
                var dir = dist > 0.05f ? toHero / dist : transform.forward;
                dir.y = 0f;
                StepGravityAndPlanar(dir * chaseSpeed);
                SetLocomotionVisual(true, chaseSpeed);
                if (dir.sqrMagnitude > 1e-6f)
                {
                    var look = Quaternion.LookRotation(dir, Vector3.up);
                    transform.rotation = Quaternion.Slerp(transform.rotation, look, 1f - Mathf.Exp(-12f * Time.deltaTime));
                }
            }
            else
            {
                StepGravityOnly();
                SetLocomotionVisual(false, walkSpeed);
            }
        }

        void SetLocomotionVisual(bool chasing, float heroWalkSpeed)
        {
            if (_animator != null && _animator.runtimeAnimatorController != null)
            {
                if (chasing)
                {
                    if (!_isChasingAnim)
                    {
                        _animator.ResetTrigger(IdleTriggerName);
                        _animator.SetTrigger(WalkTriggerName);
                        _isChasingAnim = true;
                    }

                    if (_hasWalkSpeedFloat)
                    {
                        var norm = Mathf.Clamp01(heroWalkSpeed / Mathf.Max(0.01f, FallbackHeroWalkSpeed));
                        _animator.SetFloat(_walkSpeedHash, Mathf.Max(0.35f, norm));
                    }
                }
                else
                {
                    if (_isChasingAnim)
                    {
                        _animator.ResetTrigger(WalkTriggerName);
                        _animator.SetTrigger(IdleTriggerName);
                        _isChasingAnim = false;
                    }

                    if (_hasWalkSpeedFloat)
                        _animator.SetFloat(_walkSpeedHash, 1f);
                }

                return;
            }

            DrivePackAnimalDriver(chasing ? heroWalkSpeed : 0f);
        }

        void DrivePackAnimalDriver(float planarSpeed)
        {
            var driver = GetComponent<PackAnimalAnimatorDriver>()
                ?? GetComponentInChildren<PackAnimalAnimatorDriver>(true);
            if (driver != null)
                driver.SetLocomotionReferenceSpeed(Mathf.Max(0.01f, planarSpeed), Mathf.Max(0.01f, planarSpeed * 0.92f));
        }

        static bool TryResolveHero(out Transform hero, out float walkSpeed)
        {
            hero = null;
            walkSpeed = FallbackHeroWalkSpeed;
            var go = GameObject.FindGameObjectWithTag(GameConstants.PlayerTag);
            if (go == null)
                return false;
            hero = go.transform;
            if (go.TryGetComponent<PlayerClickMove>(out var move))
                walkSpeed = move.WalkSpeed;
            return true;
        }

        void StepGravityOnly()
        {
            StepGravityAndPlanar(Vector3.zero);
        }

        void StepGravityAndPlanar(Vector3 planarWish)
        {
            if (_cc == null)
                return;
            if (_cc.isGrounded && _verticalVelocity < 0f)
                _verticalVelocity = -1f;
            _verticalVelocity += Gravity * Time.deltaTime;
            var motion = planarWish + Vector3.up * _verticalVelocity;
            _cc.Move(motion * Time.deltaTime);
        }
    }
}
