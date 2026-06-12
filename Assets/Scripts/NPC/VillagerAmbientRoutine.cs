using Rpg.Dialogue;
using UnityEngine;

namespace Rpg.Npc
{
    /// <summary>
    /// Basic villager loop:
    /// idle 5-10s, then 50% chance to move 2-5m or remain idle.
    /// After a random move, the next move target is the original spawn location.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [DefaultExecutionOrder(16)]
    public sealed class VillagerAmbientRoutine : MonoBehaviour
    {
        [SerializeField] float minIdleSeconds = 5f;
        [SerializeField] float maxIdleSeconds = 10f;
        [SerializeField] float minMoveDistanceMeters = 2f;
        [SerializeField] float maxMoveDistanceMeters = 5f;
        [SerializeField] float moveSpeedMetersPerSecond = 2.2f;
        [SerializeField] float stopDistanceMeters = 0.35f;
        [SerializeField] float gravity = -18f;

        CharacterController _cc;
        NpcAmbientDrift _ambientDrift;
        Vector3 _homePosition;
        Vector3 _moveTarget;
        float _idleUntil;
        float _verticalVelocity;
        bool _isMoving;
        bool _mustReturnHomeNextMove;
        public float CurrentPlanarSpeed { get; private set; }

        void Awake()
        {
            _cc = GetComponent<CharacterController>();
            _ambientDrift = GetComponent<NpcAmbientDrift>();
            if (_ambientDrift != null)
                _ambientDrift.enabled = false;
            _homePosition = transform.position;
            BeginIdle();
        }

        void Update()
        {
            if (_cc == null || !_cc.enabled || !_cc.gameObject.activeInHierarchy)
                return;
            if (DialogueManager.Instance != null && DialogueManager.Instance.IsDialogueOpen)
            {
                StepGravityOnly();
                return;
            }

            if (_isMoving)
            {
                TickMove();
                return;
            }

            if (Time.time < _idleUntil)
            {
                StepGravityOnly();
                return;
            }

            // 50% chance to remain idle this cycle.
            if (Random.value < 0.5f)
            {
                BeginIdle();
                return;
            }

            if (_mustReturnHomeNextMove)
            {
                _moveTarget = _homePosition;
                _mustReturnHomeNextMove = false;
            }
            else
            {
                var angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
                var dist = Random.Range(minMoveDistanceMeters, maxMoveDistanceMeters);
                var dir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                _moveTarget = transform.position + dir * dist;
                _mustReturnHomeNextMove = true;
            }

            _moveTarget = SnapToGround(_moveTarget, transform.position.y);
            _isMoving = true;
        }

        void TickMove()
        {
            var to = _moveTarget - transform.position;
            to.y = 0f;
            var dist = to.magnitude;
            if (dist <= Mathf.Max(0.1f, stopDistanceMeters))
            {
                _isMoving = false;
                BeginIdle();
                return;
            }

            var dir = to / dist;
            var planar = dir * Mathf.Max(0.2f, moveSpeedMetersPerSecond);
            if (planar.sqrMagnitude > 1e-6f)
            {
                var look = Quaternion.LookRotation(planar.normalized, Vector3.up);
                transform.rotation = Quaternion.Slerp(transform.rotation, look, 1f - Mathf.Exp(-10f * Time.deltaTime));
            }

            Step(planar);
        }

        void BeginIdle()
        {
            _isMoving = false;
            CurrentPlanarSpeed = 0f;
            var minIdle = Mathf.Max(0.1f, minIdleSeconds);
            var maxIdle = Mathf.Max(minIdle, maxIdleSeconds);
            _idleUntil = Time.time + Random.Range(minIdle, maxIdle);
        }

        void StepGravityOnly() => Step(Vector3.zero);

        void Step(Vector3 planarVelocity)
        {
            CurrentPlanarSpeed = new Vector3(planarVelocity.x, 0f, planarVelocity.z).magnitude;
            if (_cc.isGrounded && _verticalVelocity < 0f)
                _verticalVelocity = -1f;
            _verticalVelocity += gravity * Time.deltaTime;
            var motion = planarVelocity + Vector3.up * _verticalVelocity;
            _cc.Move(motion * Time.deltaTime);
        }

        static Vector3 SnapToGround(Vector3 near, float fallbackY)
        {
            var t = Terrain.activeTerrain;
            if (t != null && t.terrainData != null)
            {
                var o = t.transform.position;
                var td = t.terrainData;
                if (near.x >= o.x && near.x <= o.x + td.size.x &&
                    near.z >= o.z && near.z <= o.z + td.size.z)
                {
                    var y = t.SampleHeight(new Vector3(near.x, o.y, near.z)) + o.y;
                    return new Vector3(near.x, y, near.z);
                }
            }

            var origin = new Vector3(near.x, near.y + 50f, near.z);
            if (Physics.Raycast(origin, Vector3.down, out var hit, 150f, ~0, QueryTriggerInteraction.Ignore))
                return hit.point;
            return new Vector3(near.x, fallbackY, near.z);
        }
    }
}
