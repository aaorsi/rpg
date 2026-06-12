using UnityEngine;

namespace Rpg.Player
{
    /// <summary>
    /// Drives Animals FREE <c>Vert</c> / <c>State</c> the same way the pack's <c>CreatureMover.AnimationHandler</c> does:
    /// <c>Vert</c> is the magnitude of a smoothed 2D axis (strafe, forward in character space), not raw world speed.
    /// Runs after <see cref="CharacterController"/> movement via execution order.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [DefaultExecutionOrder(25)]
    public sealed class PackAnimalAnimatorDriver : MonoBehaviour
    {
        const string VertId = "Vert";
        const string StateId = "State";
        const float InputFlow = 4.5f;

        /// <summary>True when the controller exposes Animals FREE style <c>Vert</c> and <c>State</c> floats.</summary>
        public static bool AnimatorHasAnimalLocomotionParameters(Animator animator)
        {
            if (animator == null || animator.runtimeAnimatorController == null)
                return false;
            var hasVert = false;
            var hasState = false;
            foreach (var p in animator.parameters)
            {
                if (p.type != AnimatorControllerParameterType.Float)
                    continue;
                if (p.name == VertId)
                    hasVert = true;
                if (p.name == StateId)
                    hasState = true;
            }

            return hasVert && hasState;
        }

        /// <summary>Adds this driver only when the rig uses pack locomotion parameters.</summary>
        public static bool TryAddForAnimalLocomotion(GameObject go)
        {
            if (go == null || go.GetComponent<CharacterController>() == null)
                return false;
            var anim = go.GetComponent<Animator>() ?? go.GetComponentInChildren<Animator>(true);
            if (!AnimatorHasAnimalLocomotionParameters(anim))
                return false;
            if (go.GetComponent<PackAnimalAnimatorDriver>() == null)
                go.AddComponent<PackAnimalAnimatorDriver>();
            return true;
        }

        [SerializeField] float referencePlanarSpeed = 4.5f;
        [SerializeField] float runStateSpeedThreshold = 2.4f;
        [SerializeField] float moveDeadZone = 0.04f;

        /// <summary>Runtime tuning (e.g. slow NPC wander vs player sprint).</summary>
        public void SetLocomotionReferenceSpeed(float reference, float? runSpeedThresholdOverride = null)
        {
            referencePlanarSpeed = Mathf.Max(0.01f, reference);
            if (runSpeedThresholdOverride.HasValue)
                runStateSpeedThreshold = Mathf.Max(0.01f, runSpeedThresholdOverride.Value);
            else
                runStateSpeedThreshold = referencePlanarSpeed * 0.55f;
        }

        CharacterController _cc;
        Animator _animator;
        int _vertHash;
        int _stateHash;
        bool _drivesVert;
        bool _drivesState;
        Vector3 _lastPosition;
        Vector2 _flowAxis;
        float _flowState;

        void Awake()
        {
            _cc = GetComponent<CharacterController>();
            _animator = GetComponent<Animator>();
            if (_animator == null)
                _animator = GetComponentInChildren<Animator>(true);
            if (_animator == null || _animator.runtimeAnimatorController == null)
                return;

            _vertHash = Animator.StringToHash(VertId);
            _stateHash = Animator.StringToHash(StateId);
            foreach (var p in _animator.parameters)
            {
                if (p.type == AnimatorControllerParameterType.Float && p.name == VertId)
                    _drivesVert = true;
                if (p.type == AnimatorControllerParameterType.Float && p.name == StateId)
                    _drivesState = true;
            }

            _lastPosition = transform.position;
        }

        void Update()
        {
            if (_animator == null || (!_drivesVert && !_drivesState))
                return;

            var planarFromCc = new Vector3(_cc.velocity.x, 0f, _cc.velocity.z).magnitude;
            var planarDisp = transform.position - _lastPosition;
            planarDisp.y = 0f;
            var dt = Time.deltaTime;
            var planarFromDelta = dt > 1e-6f ? planarDisp.magnitude / dt : 0f;
            _lastPosition = transform.position;

            var planar = Mathf.Max(planarFromCc, planarFromDelta);
            var runCut = Mathf.Max(runStateSpeedThreshold, referencePlanarSpeed * 0.48f);

            Vector2 targetAxis;
            if (planar < moveDeadZone)
                targetAxis = Vector2.zero;
            else
            {
                var vel = planarFromCc > moveDeadZone
                    ? new Vector3(_cc.velocity.x, 0f, _cc.velocity.z)
                    : planarDisp / Mathf.Max(dt, 1e-6f);
                vel.y = 0f;
                if (vel.sqrMagnitude < 1e-8f)
                    targetAxis = Vector2.zero;
                else
                {
                    var d = vel.normalized;
                    var r = transform.right;
                    r.y = 0f;
                    r.Normalize();
                    var f = transform.forward;
                    f.y = 0f;
                    f.Normalize();
                    var axis = new Vector2(Vector3.Dot(d, r), Vector3.Dot(d, f));
                    var mag = Mathf.Clamp01(planar / Mathf.Max(referencePlanarSpeed, 0.01f));
                    targetAxis = axis * mag;
                }
            }

            var runTarget = planar >= runCut ? 1f : 0f;

            if (_drivesVert)
                _animator.SetFloat(_vertHash, _flowAxis.magnitude);
            if (_drivesState)
                _animator.SetFloat(_stateHash, Mathf.Clamp01(_flowState));

            var axisErr = targetAxis - _flowAxis;
            if (axisErr.sqrMagnitude > 1e-10f)
                _flowAxis = Vector2.ClampMagnitude(_flowAxis + InputFlow * dt * axisErr.normalized, 1f);
            else
                _flowAxis = targetAxis;

            _flowState = Mathf.Clamp01(_flowState + InputFlow * dt * Mathf.Sign(runTarget - _flowState));
        }
    }
}
