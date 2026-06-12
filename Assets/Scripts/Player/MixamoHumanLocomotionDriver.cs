using Rpg.Core;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Rpg.Player
{
    /// <summary>
    /// Drives humanoid locomotion from shared clips under <c>Resources/{GameConstants.MixamoAnimationsResourcesFolder}</c>.
    /// Uses a <see cref="PlayableGraph"/> (idle/walk blend when two distinct clips exist; otherwise one clip scaled by speed).
    /// Skips setup if the <see cref="Animator"/> already has a <see cref="RuntimeAnimatorController"/> (prefab fully authored in-editor).
    /// </summary>
    [DefaultExecutionOrder(12)]
    public sealed class MixamoHumanLocomotionDriver : MonoBehaviour
    {
        static (AnimationClip idleA, AnimationClip idleB, AnimationClip walk, Animator animator)? _addPending;

        [SerializeField] float referencePlanarSpeed = 4.5f;
        [SerializeField] float speedDeadZone = 0.04f;
        [SerializeField] float maxPlaybackSpeed = 1.15f;

        CharacterController _cc;
        Animator _animator;
        AnimationClip _idleA;
        AnimationClip _idleB;
        AnimationClip _walk;
        int _idleChoice;
        PlayableGraph _graph;
        AnimationMixerPlayable _mixer;
        AnimationClipPlayable _idleAPlayable;
        AnimationClipPlayable _idleBPlayable;
        AnimationClipPlayable _walkPlayable;

        /// <summary>
        /// When <paramref name="onlyIfAnimatorHasNoController"/> is true (default), does nothing if the prefab already has an animator controller assigned.
        /// </summary>
        public static bool TryAdd(GameObject playerRoot, bool onlyIfAnimatorHasNoController = true)
        {
            if (!Application.isPlaying || playerRoot == null)
                return false;

            var anim = playerRoot.GetComponentInChildren<Animator>(true);
            if (anim == null)
                return false;
            if (onlyIfAnimatorHasNoController && anim.runtimeAnimatorController != null)
                return false;

            var clips = MixamoAnimationCatalog.GetSelection();
            if (!clips.IsValid)
            {
                Debug.LogWarning(
                    $"Mixamo locomotion: no valid clips under Resources/{GameConstants.MixamoAnimationsResourcesFolder}.");
                return false;
            }

            var idleA = clips.IdleA ?? clips.Walk;
            var idleB = clips.IdleB ?? idleA;
            var walk = clips.Walk ?? idleA;
            if (idleA == null || walk == null)
                return false;

            anim.runtimeAnimatorController = null;
            anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            _addPending = (idleA, idleB, walk, anim);
            playerRoot.AddComponent<MixamoHumanLocomotionDriver>();
            _addPending = null;
            return true;
        }

        void Awake()
        {
            if (!_addPending.HasValue)
            {
                enabled = false;
                return;
            }

            _idleA = _addPending.Value.idleA;
            _idleB = _addPending.Value.idleB;
            _walk = _addPending.Value.walk;
            _animator = _addPending.Value.animator;
            _addPending = null;
            _cc = GetComponent<CharacterController>();
            _idleChoice = Mathf.Abs(gameObject.name.GetHashCode()) % 2;
        }

        void OnEnable()
        {
            if (!Application.isPlaying || _animator == null || _idleA == null || _walk == null)
                return;
            if (_graph.IsValid())
                return;

            _graph = PlayableGraph.Create($"{nameof(MixamoHumanLocomotionDriver)}_{GetInstanceID()}");
            _graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            var output = AnimationPlayableOutput.Create(_graph, "MixamoLocomotion", _animator);

            _mixer = AnimationMixerPlayable.Create(_graph, 3, true);
            _idleAPlayable = AnimationClipPlayable.Create(_graph, _idleA);
            _idleBPlayable = AnimationClipPlayable.Create(_graph, _idleB ?? _idleA);
            _walkPlayable = AnimationClipPlayable.Create(_graph, _walk);
            _idleAPlayable.SetApplyFootIK(true);
            _idleBPlayable.SetApplyFootIK(true);
            _walkPlayable.SetApplyFootIK(true);
            _graph.Connect(_idleAPlayable, 0, _mixer, 0);
            _graph.Connect(_idleBPlayable, 0, _mixer, 1);
            _graph.Connect(_walkPlayable, 0, _mixer, 2);
            _mixer.SetInputWeight(0, _idleChoice == 0 ? 1f : 0f);
            _mixer.SetInputWeight(1, _idleChoice == 1 ? 1f : 0f);
            _mixer.SetInputWeight(2, 0f);
            output.SetSourcePlayable(_mixer);

            _graph.Play();
        }

        void OnDisable()
        {
            DestroyGraphIfNeeded();
        }

        void OnDestroy()
        {
            DestroyGraphIfNeeded();
        }

        void DestroyGraphIfNeeded()
        {
            if (_graph.IsValid())
                _graph.Destroy();
        }

        void Update()
        {
            if (!_graph.IsValid() || _animator == null)
                return;
            if (_cc == null)
                _cc = GetComponent<CharacterController>();
            if (_cc == null)
                return;

            var planar = new Vector3(_cc.velocity.x, 0f, _cc.velocity.z).magnitude;
            var moveBlend = planar > speedDeadZone
                ? Mathf.Clamp01(planar / Mathf.Max(referencePlanarSpeed, 0.01f))
                : 0f;
            var walkSpeed = moveBlend * maxPlaybackSpeed;

            if (!_mixer.IsValid())
                return;
            _mixer.SetInputWeight(0, _idleChoice == 0 ? 1f - moveBlend : 0f);
            _mixer.SetInputWeight(1, _idleChoice == 1 ? 1f - moveBlend : 0f);
            _mixer.SetInputWeight(2, moveBlend);
            _idleAPlayable.SetSpeed(1f);
            _idleBPlayable.SetSpeed(1f);
            _walkPlayable.SetSpeed(Mathf.Max(0.01f, walkSpeed));
        }
    }
}
