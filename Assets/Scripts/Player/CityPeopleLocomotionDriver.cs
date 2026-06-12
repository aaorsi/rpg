using System;
using Rpg.Dialogue;
using Rpg.Npc;
using UnityEngine;

namespace Rpg.Player
{
    /// <summary>Simple idle/walk clip switcher for CityPeople controllers.</summary>
    [DefaultExecutionOrder(12)]
    public sealed class CityPeopleLocomotionDriver : MonoBehaviour
    {
        [SerializeField] float speedDeadZone = 0.04f;
        [SerializeField] float runSpeedDeadZone = 6f;
        [SerializeField] float fadeDuration = 0.2f;

        CharacterController _cc;
        Animator _animator;
        string _idleStateName;
        string _walkStateName;
        string _runStateName;
        bool _hasRunClip;
        int _locomotionState;
        PlayerClickMove _playerMove;
        SidekickFollowHeroController _sidekickFollow;

        public static bool TryAdd(GameObject root)
        {
            if (!Application.isPlaying || root == null)
                return false;
            if (root.GetComponent<CityPeopleLocomotionDriver>() != null)
                return true;
            var anim = root.GetComponentInChildren<Animator>(true);
            if (anim == null || anim.runtimeAnimatorController == null)
                return false;
            root.AddComponent<CityPeopleLocomotionDriver>();
            return true;
        }

        void Awake()
        {
            _cc = GetComponent<CharacterController>();
            _animator = GetComponent<Animator>() ?? GetComponentInChildren<Animator>(true);
            if (_animator == null || _animator.runtimeAnimatorController == null)
            {
                enabled = false;
                return;
            }

            PickStateNames(_animator.runtimeAnimatorController.animationClips, out _idleStateName, out _walkStateName);
            _playerMove = GetComponent<PlayerClickMove>();
            _sidekickFollow = GetComponent<SidekickFollowHeroController>();
            if (string.IsNullOrEmpty(_idleStateName) || string.IsNullOrEmpty(_walkStateName))
            {
                enabled = false;
                return;
            }
            _runStateName = PickRunStateName(_animator.runtimeAnimatorController.animationClips);
            _hasRunClip = !string.IsNullOrEmpty(_runStateName);

            _animator.CrossFadeInFixedTime(_idleStateName, 0f);
        }

        static void PickStateNames(AnimationClip[] clips, out string idleName, out string walkName)
        {
            idleName = null;
            walkName = null;
            if (clips == null)
                return;
            foreach (var c in clips)
            {
                if (c == null)
                    continue;
                var n = c.name.ToLowerInvariant();
                if (idleName == null && n.Contains("idle"))
                    idleName = c.name;
                if (walkName == null && (n.Contains("walk") || n.Contains("locom")))
                    walkName = c.name;
            }

            // Conservative fallback: first clip as idle if no explicit idle was found.
            if (idleName == null && clips.Length > 0 && clips[0] != null)
                idleName = clips[0].name;
            if (walkName == null)
                walkName = idleName;
        }

        static string PickRunStateName(AnimationClip[] clips)
        {
            if (clips == null)
                return null;
            foreach (var c in clips)
            {
                if (c == null)
                    continue;
                var n = c.name.ToLowerInvariant();
                if (n.Contains("run") || n.Contains("sprint") || n.Contains("jog"))
                    return c.name;
            }

            return null;
        }

        void Update()
        {
            if (_cc == null || _animator == null)
                return;
            var followingHero = _sidekickFollow != null && _sidekickFollow.IsFollowing;
            if (DialogueManager.Instance != null && DialogueManager.Instance.IsDialogueOpen && !followingHero)
            {
                if (_locomotionState != 0)
                {
                    _locomotionState = 0;
                    _animator.speed = 1f;
                    _animator.CrossFadeInFixedTime(_idleStateName, fadeDuration);
                }
                return;
            }
            var planar = new Vector3(_cc.velocity.x, 0f, _cc.velocity.z).magnitude;
            var sprintIntent = _playerMove != null && _playerMove.IsSprinting;
            if (!sprintIntent && followingHero)
                sprintIntent = _sidekickFollow.HeroIsSprinting;
            var targetState = 0;
            if (planar > speedDeadZone)
                targetState = 1;
            if (sprintIntent || planar > runSpeedDeadZone)
                targetState = 2;

            if (targetState == _locomotionState)
                return;
            _locomotionState = targetState;
            if (targetState == 0)
            {
                _animator.speed = 1f;
                _animator.CrossFadeInFixedTime(_idleStateName, fadeDuration);
                return;
            }

            if (targetState == 2)
            {
                if (_hasRunClip)
                {
                    _animator.speed = 1f;
                    _animator.CrossFadeInFixedTime(_runStateName, fadeDuration);
                }
                else
                {
                    _animator.speed = 2f;
                    _animator.CrossFadeInFixedTime(_walkStateName, fadeDuration);
                }
                return;
            }

            _animator.speed = 1f;
            _animator.CrossFadeInFixedTime(_walkStateName, fadeDuration);
        }
    }
}
