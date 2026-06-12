using Rpg.Core;
using Rpg.Dialogue;
using Rpg.Player;
using UnityEngine;

namespace Rpg.Npc
{
    /// <summary>
    /// Wander / idle for spider <see cref="AnimalNpc"/>; within hero range uses approach instead of evade.
    /// Mirrors tiger behavior so scene-authored spider_* predators play the same.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [DefaultExecutionOrder(12)]
    public sealed class SpiderNpcWanderAi : MonoBehaviour
    {
        const float HeroDistanceScanIntervalSeconds = 3f;
        const float WalkRunDistanceThresholdM = 15f;
        const float CloseIdleProximityM = 1f;
        const float CloseIdleDurationSeconds = 10f;
        const float OuterApproachWalkChunkM = 5f;
        const float CloseApproachRunLegM = 5f;
        const float CloseApproachWalkLegM = 2f;
        const float MotionDistanceMinM = 2f;
        const float MotionDistanceMaxM = 30f;
        const float IdleSecondsMin = 5f;
        const float IdleSecondsMax = 60f;
        const float IdleVersusMotionIdleChance = 0.4f;
        const float Gravity = -18f;
        const float FallbackHeroWalkSpeed = 4.5f;
        const float MotionCompleteEpsilon = 0.08f;
        const float AttackRangeMeters = 2f;
        const string RunClipToken = "run";
        const string AttackClipToken = "attack1";
        const string IdleClipToken = "idle";

        enum Phase
        {
            Idle,
            Motion,
            Approach
        }

        [SerializeField, Min(1f)] float heroApproachAggroRadiusMeters = 32f;
        CharacterController _cc;
        Phase _phase;
        float _nextHeroAggroScanTime;
        float _verticalVelocity;
        float _idleUntil;
        float _segmentDistanceRemaining;
        Vector3 _segmentPlanarDir;
        float _segmentPlanarSpeed;
        float _animalWalkSpeed;
        float _approachChunkRemaining;
        bool _closeApproachBand;
        bool _closeLegIsSprint;
        Animator _animator;
        Animation _legacyAnimation;
        string _runStateName;
        string _attackStateName;
        string _idleStateName;
        string _activeAnimState;

        void Awake()
        {
            if (!IsSpiderInstance())
            {
                enabled = false;
                return;
            }

            _cc = GetComponent<CharacterController>();
            _animator = GetComponent<Animator>() ?? GetComponentInChildren<Animator>(true);
            _legacyAnimation = GetComponent<Animation>() ?? GetComponentInChildren<Animation>(true);
            ResolveAnimationStateNames();
            PlayAnimationState(_idleStateName);
            if (TryGetComponent<NpcAmbientDrift>(out var drift))
                drift.enabled = false;
            _nextHeroAggroScanTime = Time.time + Random.Range(0f, 2f);
        }

        void Start()
        {
            if (!enabled)
                return;
            BeginActionCycle();
        }

        void Update()
        {
            if (!enabled)
                return;

            if (DialogueManager.Instance != null && DialogueManager.Instance.IsDialogueOpen)
            {
                StepGravityOnly();
                return;
            }

            switch (_phase)
            {
                case Phase.Idle:
                    StepGravityOnly();
                    if (TryAggroHeroFromPeriodicScan())
                        break;
                    if (Time.time >= _idleUntil)
                        BeginActionCycle();
                    break;
                case Phase.Motion:
                    if (TryAggroHeroFromPeriodicScan())
                        break;
                    StepLocomotionSegment();
                    break;
                case Phase.Approach:
                    StepApproach();
                    break;
            }
        }

        void BeginActionCycle()
        {
            if (!TryResolveHero(out var hero, out _, out var heroWalk))
            {
                ChooseIdleOrMotion(heroWalk);
                return;
            }

            var planar = hero.position - transform.position;
            planar.y = 0f;
            var dist = planar.magnitude;

            if (dist <= CloseIdleProximityM)
                StartCloseIdleSegment();
            else if (dist <= heroApproachAggroRadiusMeters)
                EnterApproach(hero, heroWalk, dist);
            else
                ChooseIdleOrMotion(heroWalk);
        }

        void ChooseIdleOrMotion(float heroWalk)
        {
            if (Random.value < IdleVersusMotionIdleChance)
                StartIdleSegment();
            else
                StartMotionSegment(heroWalk);
        }

        void StartIdleSegment()
        {
            _phase = Phase.Idle;
            _idleUntil = Time.time + Random.Range(IdleSecondsMin, IdleSecondsMax);
            ApplyAnimatorReferenceSpeed(0f);
        }

        void StartCloseIdleSegment()
        {
            _phase = Phase.Idle;
            _idleUntil = Time.time + CloseIdleDurationSeconds;
            ApplyAnimatorReferenceSpeed(0f);
        }

        void StartMotionSegment(float heroWalk)
        {
            _phase = Phase.Motion;
            var yaw = Random.Range(0f, 360f);
            _segmentPlanarDir = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            _segmentDistanceRemaining = Random.Range(MotionDistanceMinM, MotionDistanceMaxM);
            _animalWalkSpeed = Mathf.Max(0.05f, Random.Range(0.5f, 1f) * heroWalk);
            _segmentPlanarSpeed = _animalWalkSpeed;
            ApplyAnimatorReferenceSpeed(_segmentPlanarSpeed);
        }

        void EnterApproach(Transform hero, float heroWalk, float dist)
        {
            _phase = Phase.Approach;
            _animalWalkSpeed = Mathf.Max(0.05f, Random.Range(0.5f, 1f) * heroWalk);
            _closeApproachBand = dist < WalkRunDistanceThresholdM;
            if (_closeApproachBand)
            {
                _closeLegIsSprint = true;
                _approachChunkRemaining = CloseApproachRunLegM;
            }
            else
                _approachChunkRemaining = OuterApproachWalkChunkM;

            hero.gameObject.TryGetComponent<PlayerClickMove>(out var move);
            RefreshApproachDirection(hero);
            ApplyApproachPlanarSpeed(move, heroWalk);
        }

        void ApplyApproachPlanarSpeed(PlayerClickMove move, float heroWalk)
        {
            if (_closeApproachBand && _closeLegIsSprint)
            {
                var run = move != null ? move.SprintSpeed : heroWalk * 2f;
                _segmentPlanarSpeed = Mathf.Max(0.05f, run);
            }
            else
                _segmentPlanarSpeed = _animalWalkSpeed;

            ApplyAnimatorReferenceSpeed(_segmentPlanarSpeed);
        }

        void StepApproach()
        {
            if (!TryResolveHero(out var hero, out var move, out var heroWalk))
            {
                ChooseIdleOrMotion(FallbackHeroWalkSpeed);
                return;
            }

            var planar = hero.position - transform.position;
            planar.y = 0f;
            var dist = planar.magnitude;

            if (dist > heroApproachAggroRadiusMeters)
            {
                BeginActionCycle();
                return;
            }

            if (dist <= CloseIdleProximityM)
            {
                StartCloseIdleSegment();
                return;
            }
            if (dist <= AttackRangeMeters)
            {
                StepGravityOnly();
                ApplyAnimatorReferenceSpeed(0f, forceAttack: true);
                return;
            }

            var inClose = dist < WalkRunDistanceThresholdM;
            if (inClose != _closeApproachBand)
            {
                _closeApproachBand = inClose;
                if (inClose)
                {
                    _closeLegIsSprint = true;
                    _approachChunkRemaining = CloseApproachRunLegM;
                }
                else
                    _approachChunkRemaining = OuterApproachWalkChunkM;

                RefreshApproachDirection(hero);
                ApplyApproachPlanarSpeed(move, heroWalk);
            }

            var planarWish = _segmentPlanarDir * _segmentPlanarSpeed;
            StepGravityAndPlanar(planarWish);

            var traveled = PlanarTraveledThisFrame(planarWish);
            _approachChunkRemaining -= traveled;
            if (_approachChunkRemaining > MotionCompleteEpsilon)
                return;

            if (_closeApproachBand)
            {
                _closeLegIsSprint = !_closeLegIsSprint;
                _approachChunkRemaining = _closeLegIsSprint ? CloseApproachRunLegM : CloseApproachWalkLegM;
            }
            else
                _approachChunkRemaining = OuterApproachWalkChunkM;

            RefreshApproachDirection(hero);
            ApplyApproachPlanarSpeed(move, heroWalk);
        }

        void RefreshApproachDirection(Transform hero)
        {
            var toward = hero.position - transform.position;
            toward.y = 0f;
            if (toward.sqrMagnitude < 1e-6f)
                return;
            _segmentPlanarDir = toward.normalized;
        }

        bool TryAggroHeroFromPeriodicScan()
        {
            if (_phase == Phase.Approach)
                return false;
            if (Time.time < _nextHeroAggroScanTime)
                return false;
            _nextHeroAggroScanTime = Time.time + HeroDistanceScanIntervalSeconds;
            if (!TryResolveHero(out var hero, out _, out var heroWalk))
                return false;
            var planar = hero.position - transform.position;
            planar.y = 0f;
            var dist = planar.magnitude;
            if (dist <= CloseIdleProximityM)
            {
                StartCloseIdleSegment();
                return true;
            }

            if (dist <= heroApproachAggroRadiusMeters)
            {
                EnterApproach(hero, heroWalk, dist);
                return true;
            }

            return false;
        }

        void StepLocomotionSegment()
        {
            var planarWish = _segmentPlanarDir * _segmentPlanarSpeed;
            StepGravityAndPlanar(planarWish);

            var traveled = PlanarTraveledThisFrame(planarWish);
            _segmentDistanceRemaining -= traveled;
            if (_segmentDistanceRemaining > MotionCompleteEpsilon)
                return;

            BeginActionCycle();
        }

        float PlanarTraveledThisFrame(Vector3 planarWish)
        {
            var step = new Vector3(_cc.velocity.x, 0f, _cc.velocity.z) * Time.deltaTime;
            var traveled = step.magnitude;
            if (traveled < 1e-5f)
            {
                var disp = planarWish * Time.deltaTime;
                traveled = new Vector3(disp.x, 0f, disp.z).magnitude;
            }

            return traveled;
        }

        void StepGravityOnly()
        {
            StepGravityAndPlanar(Vector3.zero);
            ApplyAnimatorReferenceSpeed(0f, forceIdle: true);
        }

        void StepGravityAndPlanar(Vector3 planarWish)
        {
            if (_cc.isGrounded && _verticalVelocity < 0f)
                _verticalVelocity = -1f;
            _verticalVelocity += Gravity * Time.deltaTime;

            var motion = planarWish + Vector3.up * _verticalVelocity;
            _cc.Move(motion * Time.deltaTime);

            if (_segmentPlanarDir.sqrMagnitude > 1e-6f && planarWish.sqrMagnitude > 1e-8f)
            {
                var look = Quaternion.LookRotation(_segmentPlanarDir);
                transform.rotation = Quaternion.Slerp(transform.rotation, look,
                    1f - Mathf.Exp(-10f * Time.deltaTime));
            }
        }

        void ApplyAnimatorReferenceSpeed(float planarSpeed, bool forceIdle = false, bool forceAttack = false)
        {
            var driver = GetComponent<PackAnimalAnimatorDriver>()
                         ?? GetComponentInParent<PackAnimalAnimatorDriver>();
            if (driver != null)
                driver.SetLocomotionReferenceSpeed(Mathf.Max(0.01f, planarSpeed), Mathf.Max(0.01f, planarSpeed * 0.92f));

            if (forceAttack)
            {
                PlayAnimationState(_attackStateName);
                return;
            }
            if (forceIdle || planarSpeed <= 0.05f)
            {
                PlayAnimationState(_idleStateName);
                return;
            }

            PlayAnimationState(_runStateName);
        }

        void PlayAnimationState(string stateName)
        {
            if (string.IsNullOrWhiteSpace(stateName))
                return;
            if (string.Equals(_activeAnimState, stateName, System.StringComparison.OrdinalIgnoreCase))
                return;
            _activeAnimState = stateName;

            if (_legacyAnimation != null)
            {
                if (_legacyAnimation[stateName] != null)
                    _legacyAnimation.CrossFade(stateName, 0.1f);
            }
            if (_animator != null && _animator.runtimeAnimatorController != null)
            {
                _animator.speed = 1f;
                _animator.CrossFadeInFixedTime(stateName, 0.1f);
            }
        }

        void ResolveAnimationStateNames()
        {
            _runStateName = RunClipToken;
            _attackStateName = AttackClipToken;
            _idleStateName = IdleClipToken;

            if (_legacyAnimation != null)
            {
                ResolveLegacyStateName(_legacyAnimation, RunClipToken, ref _runStateName);
                ResolveLegacyStateName(_legacyAnimation, AttackClipToken, ref _attackStateName);
                ResolveLegacyStateName(_legacyAnimation, IdleClipToken, ref _idleStateName);
                return;
            }

            if (_animator != null && _animator.runtimeAnimatorController != null)
            {
                ResolveAnimatorClipName(_animator.runtimeAnimatorController.animationClips, RunClipToken, ref _runStateName);
                ResolveAnimatorClipName(_animator.runtimeAnimatorController.animationClips, AttackClipToken, ref _attackStateName);
                ResolveAnimatorClipName(_animator.runtimeAnimatorController.animationClips, IdleClipToken, ref _idleStateName);
            }
        }

        static void ResolveLegacyStateName(Animation anim, string token, ref string target)
        {
            foreach (AnimationState state in anim)
            {
                if (state == null || string.IsNullOrWhiteSpace(state.name))
                    continue;
                if (state.name.IndexOf(token, System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    target = state.name;
                    return;
                }
            }
        }

        static void ResolveAnimatorClipName(AnimationClip[] clips, string token, ref string target)
        {
            if (clips == null)
                return;
            for (var i = 0; i < clips.Length; i++)
            {
                var c = clips[i];
                if (c == null || string.IsNullOrWhiteSpace(c.name))
                    continue;
                if (c.name.IndexOf(token, System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    target = c.name;
                    return;
                }
            }
        }

        static bool TryResolveHero(out Transform hero, out PlayerClickMove move, out float walkSpeed)
        {
            hero = null;
            move = null;
            walkSpeed = FallbackHeroWalkSpeed;
            var go = GameObject.FindGameObjectWithTag(GameConstants.PlayerTag);
            if (go == null)
                return false;
            hero = go.transform;
            if (go.TryGetComponent(out move))
                walkSpeed = move.WalkSpeed;
            return true;
        }

        bool IsSpiderInstance()
        {
            var n = gameObject.name;
            return n.IndexOf("spider", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
