using Rpg.Dialogue;
using Rpg.Npc;
using UnityEngine;

namespace Rpg.Player
{
    /// <summary>Drives parameterized StylizedCharacterPack controllers so NPCs stay in visible idle state.</summary>
    [DefaultExecutionOrder(13)]
    public sealed class StylizedNpcAnimatorDriver : MonoBehaviour
    {
        [SerializeField] bool forceIdle;
        [SerializeField] float locomotionAnimationSpeedScale = 0.5f;
        [SerializeField] float sprintExtraSlowdown = 0.5f;

        Animator _animator;
        CharacterController _cc;
        PlayerClickMove _playerMove;
        SidekickFollowHeroController _sidekickFollow;
        int _speedHash;
        int _sprintHash;
        int _groundedHash;
        int _verticalSpeedHash;

        bool _hasSpeed;
        bool _hasSprint;
        bool _hasGrounded;
        bool _hasVerticalSpeed;

        public static bool TryAdd(GameObject root, bool forceIdle = true)
        {
            if (!Application.isPlaying || root == null)
                return false;
            if (root.TryGetComponent<StylizedNpcAnimatorDriver>(out var existing))
            {
                existing.forceIdle = forceIdle;
                return true;
            }
            var anim = root.GetComponentInChildren<Animator>(true);
            if (anim == null || anim.runtimeAnimatorController == null)
                return false;
            var added = root.AddComponent<StylizedNpcAnimatorDriver>();
            added.forceIdle = forceIdle;
            return true;
        }

        void Awake()
        {
            _animator = GetComponent<Animator>() ?? GetComponentInChildren<Animator>(true);
            _cc = GetComponent<CharacterController>();
            _playerMove = GetComponent<PlayerClickMove>();
            _sidekickFollow = GetComponent<SidekickFollowHeroController>();
            if (_animator == null || _animator.runtimeAnimatorController == null)
            {
                enabled = false;
                return;
            }

            _speedHash = Animator.StringToHash("Speed");
            _sprintHash = Animator.StringToHash("Sprint");
            _groundedHash = Animator.StringToHash("Grounded");
            _verticalSpeedHash = Animator.StringToHash("VerticalSpeed");

            foreach (var p in _animator.parameters)
            {
                if (p.nameHash == _speedHash)
                    _hasSpeed = true;
                else if (p.nameHash == _sprintHash)
                    _hasSprint = true;
                else if (p.nameHash == _groundedHash)
                    _hasGrounded = true;
                else if (p.nameHash == _verticalSpeedHash)
                    _hasVerticalSpeed = true;
            }
        }

        void Update()
        {
            if (_animator == null)
                return;
            var inDialogue = DialogueManager.Instance != null && DialogueManager.Instance.IsDialogueOpen;
            var followingHero = _sidekickFollow != null && _sidekickFollow.IsFollowing;
            var planar = _cc != null ? new Vector3(_cc.velocity.x, 0f, _cc.velocity.z).magnitude : 0f;
            var sprint = _playerMove != null && _playerMove.IsSprinting;
            if (!sprint && followingHero)
                sprint = _sidekickFollow != null && _sidekickFollow.HeroIsSprinting;
            var grounded = _cc != null && _cc.isGrounded;
            if (!grounded && _playerMove != null)
                grounded = _playerMove.IsGrounded;
            var verticalSpeed = _playerMove != null ? _playerMove.VerticalVelocity : (_cc != null ? _cc.velocity.y : 0f);
            if (grounded)
                verticalSpeed = 0f;
            if (inDialogue || (forceIdle && !followingHero))
            {
                planar = 0f;
                sprint = false;
                grounded = true;
                verticalSpeed = 0f;
            }

            if (_hasSpeed)
                _animator.SetFloat(_speedHash, planar);
            if (_hasSprint)
                _animator.SetBool(_sprintHash, sprint);
            if (_hasGrounded)
                _animator.SetBool(_groundedHash, grounded);
            if (_hasVerticalSpeed)
                _animator.SetFloat(_verticalSpeedHash, verticalSpeed);

            if ((!forceIdle || followingHero) && grounded && planar > 0.05f)
                _animator.speed = sprint
                    ? locomotionAnimationSpeedScale * sprintExtraSlowdown
                    : locomotionAnimationSpeedScale;
            else
                _animator.speed = 1f;
        }
    }
}
