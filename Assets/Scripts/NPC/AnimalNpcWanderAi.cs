using Rpg.Core;
using Rpg.Dialogue;
using Rpg.Player;
using UnityEngine;

namespace Rpg.Npc
{
    /// <summary>
    /// Simple wander / idle behaviour for <see cref="AnimalNpc"/> with hero proximity evade.
    /// Disabled automatically on instances whose name suggests a tiger asset.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [DefaultExecutionOrder(12)]
    public sealed class AnimalNpcWanderAi : MonoBehaviour
    {
        const float HeroIdleProximityM = 5f;
        const float HeroEvadeProximityM = 30f;
        const float MotionDistanceMinM = 2f;
        const float MotionDistanceMaxM = 30f;
        const float EvadeDistanceMinM = 10f;
        const float EvadeDistanceMaxM = 50f;
        const float IdleSecondsMin = 5f;
        const float IdleSecondsMax = 60f;
        const float IdleVersusMotionIdleChance = 0.4f;
        const float Gravity = -18f;
        const float HeroPlanarSpeedThreshold = 0.12f;
        const float FallbackHeroWalkSpeed = 4.5f;
        const float MotionCompleteEpsilon = 0.08f;

        enum Phase
        {
            Idle,
            Motion,
            Evade
        }

        CharacterController _cc;
        Phase _phase;
        float _verticalVelocity;
        float _idleUntil;
        float _segmentDistanceRemaining;
        Vector3 _segmentPlanarDir;
        float _segmentPlanarSpeed;
        float _animalWalkSpeed;

        void Awake()
        {
            if (IsTigerInstance())
            {
                enabled = false;
                return;
            }

            _cc = GetComponent<CharacterController>();
            if (TryGetComponent<NpcAmbientDrift>(out var drift))
                drift.enabled = false;
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
                    if (Time.time >= _idleUntil)
                        BeginActionCycle();
                    break;
                case Phase.Motion:
                case Phase.Evade:
                    StepLocomotionSegment();
                    break;
            }
        }

        void BeginActionCycle()
        {
            if (!TryResolveHero(out var hero, out var heroMove, out var heroWalk))
            {
                ChooseIdleOrMotion(heroWalk);
                return;
            }

            var planar = hero.position - transform.position;
            planar.y = 0f;
            var dist = planar.magnitude;

            if (dist < HeroIdleProximityM)
                StartIdleSegment();
            else if (dist < HeroEvadeProximityM)
                StartEvadeSegment(hero, heroMove, heroWalk);
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
            ApplyAnimatorReferenceSpeed(0.05f);
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

        void StartEvadeSegment(Transform hero, PlayerClickMove heroMove, float heroWalk)
        {
            EnsureAnimalWalkSample(heroWalk);

            _phase = Phase.Evade;
            _segmentDistanceRemaining = Random.Range(EvadeDistanceMinM, EvadeDistanceMaxM);
            _segmentPlanarSpeed = Mathf.Max(0.05f, 2f * _animalWalkSpeed);

            var away = transform.position - hero.position;
            away.y = 0f;
            if (away.sqrMagnitude < 1e-6f)
                away = RandomPlanarUnit();

            if (heroMove != null && heroMove.PlanarSpeed >= HeroPlanarSpeedThreshold)
            {
                var hv = heroMove.PlanarWorldVelocity;
                if (hv.sqrMagnitude < 1e-6f)
                    _segmentPlanarDir = RandomPlanarUnit();
                else
                {
                    var a = hv.normalized;
                    _segmentPlanarDir = Vector3.Dot(away, a) >= 0f ? a : -a;
                }
            }
            else
                _segmentPlanarDir = RandomPlanarUnit();

            ApplyAnimatorReferenceSpeed(_segmentPlanarSpeed);
        }

        void StepLocomotionSegment()
        {
            var planarWish = _segmentPlanarDir * _segmentPlanarSpeed;
            StepGravityAndPlanar(planarWish);

            var step = new Vector3(_cc.velocity.x, 0f, _cc.velocity.z) * Time.deltaTime;
            var traveled = step.magnitude;
            if (traveled < 1e-5f)
            {
                var disp = planarWish * Time.deltaTime;
                traveled = new Vector3(disp.x, 0f, disp.z).magnitude;
            }

            _segmentDistanceRemaining -= traveled;
            if (_segmentDistanceRemaining > MotionCompleteEpsilon)
                return;

            if (_phase == Phase.Evade)
                ChooseIdleOrMotionAfterEvade();
            else
                BeginActionCycle();
        }

        void ChooseIdleOrMotionAfterEvade()
        {
            TryResolveHero(out _, out _, out var heroWalk);
            ChooseIdleOrMotion(heroWalk);
        }

        void StepGravityOnly()
        {
            StepGravityAndPlanar(Vector3.zero);
            if (_phase != Phase.Idle)
                ApplyAnimatorReferenceSpeed(_segmentPlanarSpeed);
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

        void EnsureAnimalWalkSample(float heroWalk)
        {
            if (_animalWalkSpeed > 0.01f)
                return;
            _animalWalkSpeed = Mathf.Max(0.05f, Random.Range(0.5f, 1f) * heroWalk);
        }

        static Vector3 RandomPlanarUnit()
        {
            for (var i = 0; i < 8; i++)
            {
                var v = Random.onUnitSphere;
                v.y = 0f;
                if (v.sqrMagnitude > 1e-4f)
                    return v.normalized;
            }

            return Vector3.forward;
        }

        void ApplyAnimatorReferenceSpeed(float planarSpeed)
        {
            var driver = GetComponent<PackAnimalAnimatorDriver>()
                         ?? GetComponentInParent<PackAnimalAnimatorDriver>();
            if (driver != null)
                driver.SetLocomotionReferenceSpeed(planarSpeed);
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

        bool IsTigerInstance()
        {
            var n = gameObject.name;
            return n.IndexOf("tiger", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
