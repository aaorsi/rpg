using Rpg.Core;
using Rpg.Dialogue;
using Rpg.Npc;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Rpg.Player
{
    /// <summary>
    /// Runtime locomotion for casual humanoid NPC prefabs that ship without AnimatorController.
    /// Uses shared Mixamo clips via Playables and blends idle/walk by planar CharacterController speed.
    /// </summary>
    /// <summary>Runs after typical movement (e.g. <see cref="VillagerAmbientRoutine"/>) so planar speed matches the same frame.</summary>
    [DefaultExecutionOrder(40)]
    public sealed class NpcCasualLocomotionPlayableDriver : MonoBehaviour
    {
        struct PendingSetup
        {
            public AnimationClip idle;
            public AnimationClip walk;
            public Animator animator;
        }

        static PendingSetup? _pending;

        [SerializeField] float referencePlanarSpeed = 4.5f;
        [SerializeField] float speedDeadZone = 0.04f;
        [SerializeField] float maxWalkPlaybackSpeed = 1.25f;

        CharacterController _cc;
        SidekickFollowHeroController _sidekickFollow;
        VillagerAmbientRoutine _villagerRoutine;
        Animator _animator;
        AnimationClip _idleClip;
        AnimationClip _walkClip;

        PlayableGraph _graph;
        AnimationMixerPlayable _mixer;
        AnimationClipPlayable _idlePlayable;
        AnimationClipPlayable _walkPlayable;

        public static bool TryAddForCasualPrefab(
            GameObject npcRoot,
            string sourceCasualPrefabAssetName,
            string idleClipNameOverride = null,
            string walkClipNameOverride = null)
        {
            if (!Application.isPlaying || npcRoot == null || string.IsNullOrWhiteSpace(sourceCasualPrefabAssetName))
                return false;

            if (npcRoot.GetComponent<NpcCasualLocomotionPlayableDriver>() != null)
                return true;

            var anim = npcRoot.GetComponentInChildren<Animator>(true);
            if (anim == null)
                return false;

            var sel = MixamoAnimationCatalog.GetSelection();
            if (!sel.IsValid || sel.Walk == null)
                return false;

            var idle = sel.IdleA != null ? sel.IdleA : sel.Walk;
            var walk = sel.Walk;

            if (!string.IsNullOrWhiteSpace(idleClipNameOverride)
                && MixamoAnimationCatalog.TryFindAnimationClipByName(idleClipNameOverride.Trim(), out var idleResolved))
                idle = idleResolved;

            if (!string.IsNullOrWhiteSpace(walkClipNameOverride)
                && MixamoAnimationCatalog.TryFindAnimationClipByName(walkClipNameOverride.Trim(), out var walkResolved))
                walk = walkResolved;

            anim.runtimeAnimatorController = null;
            anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            _pending = new PendingSetup
            {
                idle = idle,
                walk = walk,
                animator = anim
            };

            npcRoot.AddComponent<NpcCasualLocomotionPlayableDriver>();
            _pending = null;
            return true;
        }

        void Awake()
        {
            _cc = GetComponent<CharacterController>();
            _sidekickFollow = GetComponent<SidekickFollowHeroController>();
            _villagerRoutine = GetComponent<VillagerAmbientRoutine>();

            if (!_pending.HasValue)
            {
                enabled = false;
                return;
            }

            _idleClip = _pending.Value.idle;
            _walkClip = _pending.Value.walk;
            _animator = _pending.Value.animator;
            _pending = null;

            if (_idleClip == null || _walkClip == null || _animator == null)
                enabled = false;
        }

        void OnEnable()
        {
            if (!Application.isPlaying || _idleClip == null || _walkClip == null || _animator == null)
                return;
            if (_graph.IsValid())
                return;

            _graph = PlayableGraph.Create($"{nameof(NpcCasualLocomotionPlayableDriver)}_{GetInstanceID()}");
            _graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);

            _mixer = AnimationMixerPlayable.Create(_graph, 2, true);
            _idlePlayable = AnimationClipPlayable.Create(_graph, _idleClip);
            _walkPlayable = AnimationClipPlayable.Create(_graph, _walkClip);
            // Foot IK often fights retargeted Mixamo clips on third-party meshes (odd upper-body / frozen poses).
            _idlePlayable.SetApplyFootIK(false);
            _walkPlayable.SetApplyFootIK(false);

            _graph.Connect(_idlePlayable, 0, _mixer, 0);
            _graph.Connect(_walkPlayable, 0, _mixer, 1);
            _mixer.SetInputWeight(0, 1f);
            _mixer.SetInputWeight(1, 0f);
            _walkPlayable.SetSpeed(0f);

            var output = AnimationPlayableOutput.Create(_graph, "NpcCasualLocomotion", _animator);
            output.SetSourcePlayable(_mixer);
            _graph.Play();
        }

        void OnDisable() => DestroyGraphIfNeeded();
        void OnDestroy() => DestroyGraphIfNeeded();

        void DestroyGraphIfNeeded()
        {
            if (_graph.IsValid())
                _graph.Destroy();
        }

        void LateUpdate()
        {
            if (!_graph.IsValid() || !_mixer.IsValid())
                return;
            if (_cc == null)
                _cc = GetComponent<CharacterController>();
            if (_cc == null)
                return;

            var inDialogue = DialogueManager.Instance != null && DialogueManager.Instance.IsDialogueOpen;
            var followingHero = _sidekickFollow != null && _sidekickFollow.IsFollowing;

            if (_villagerRoutine == null)
                _villagerRoutine = GetComponent<VillagerAmbientRoutine>();

            // Villagers are CC-driven with gravity each frame; their routine exposes a cleaner planar speed signal for animation.
            var planar = _villagerRoutine != null
                ? Mathf.Max(0f, _villagerRoutine.CurrentPlanarSpeed)
                : new Vector3(_cc.velocity.x, 0f, _cc.velocity.z).magnitude;
            if (inDialogue && !followingHero)
                planar = 0f;

            var move01 = planar > speedDeadZone
                ? Mathf.Clamp01(planar / Mathf.Max(referencePlanarSpeed, 0.01f))
                : 0f;

            _mixer.SetInputWeight(0, 1f - move01);
            _mixer.SetInputWeight(1, move01);

            // Keep walk cycle visibly moving whenever we blend toward walk (avoids "frozen arms" from near-zero playback).
            var walkSpeed = move01 <= 0.001f
                ? 0f
                : Mathf.Max(0.85f, Mathf.Lerp(0.85f, maxWalkPlaybackSpeed, move01));
            _walkPlayable.SetSpeed(walkSpeed);
            _idlePlayable.SetSpeed(1f);
        }

        public void SetLocomotionReferenceSpeed(float planarReferenceSpeed, float maxWalkSpeed = 1.25f, float deadZone = 0.04f)
        {
            referencePlanarSpeed = Mathf.Max(0.05f, planarReferenceSpeed);
            maxWalkPlaybackSpeed = Mathf.Clamp(maxWalkSpeed, 0.5f, 3f);
            speedDeadZone = Mathf.Clamp(deadZone, 0f, 0.3f);
        }
    }
}
