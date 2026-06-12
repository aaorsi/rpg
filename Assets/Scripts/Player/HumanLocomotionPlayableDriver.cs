using System;
using Rpg.Core;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Rpg.Player
{
    /// <summary>
    /// Plays embedded humanoid clips from <c>Resources/{GameConstants.CasualHumanMeshBasesResourcesFolder}</c> via a
    /// <see cref="PlayableGraph"/> so we do not need a runtime animator controller. Playback speed follows planar
    /// <see cref="CharacterController"/> velocity (zero when idle).
    /// </summary>
    [DefaultExecutionOrder(12)]
    public sealed class HumanLocomotionPlayableDriver : MonoBehaviour
    {
        static (AnimationClip clip, Animator animator)? _addPending;

        [SerializeField] float referencePlanarSpeed = 4.5f;
        [SerializeField] float speedDeadZone = 0.04f;
        [SerializeField] float maxPlaybackSpeed = 1.2f;

        CharacterController _cc;
        AnimationClip _clip;
        Animator _animator;
        PlayableGraph _graph;
        AnimationClipPlayable _clipPlayable;

        /// <summary>
        /// Maps <c>npc_csl_00_character_01f_01</c> → <c>npc_csl_00_character_01f</c> (Resources path to mesh FBX clips).
        /// </summary>
        public static string GetMeshResourceBaseName(string npcCasualPrefabAssetName)
        {
            const string prefix = "npc_csl_00_character_";
            if (string.IsNullOrEmpty(npcCasualPrefabAssetName)
                || !npcCasualPrefabAssetName.StartsWith(prefix, StringComparison.Ordinal))
                return null;
            var rest = npcCasualPrefabAssetName.Substring(prefix.Length);
            var idx = rest.LastIndexOf('_');
            if (idx <= 0)
                return null;
            return prefix + rest.Substring(0, idx);
        }

        public static bool TryAddForCasualPrefab(GameObject playerRoot, string sourceCasualPrefabAssetName)
        {
            if (!Application.isPlaying || playerRoot == null || string.IsNullOrEmpty(sourceCasualPrefabAssetName))
                return false;

            var meshBase = GetMeshResourceBaseName(sourceCasualPrefabAssetName);
            if (meshBase == null)
                return false;

            var path = $"{GameConstants.CasualHumanMeshBasesResourcesFolder}/{meshBase}";
            var clips = Resources.LoadAll<AnimationClip>(path);
            if (clips == null || clips.Length == 0)
            {
                Debug.LogWarning(
                    $"Human locomotion: no clips at Resources/{path}. Expected mesh FBX copies under CasualHumanMeshBases.");
                return false;
            }

            var anim = playerRoot.GetComponentInChildren<Animator>(true);
            if (anim == null)
                return false;

            var clip = PickLocomotionClip(clips);
            if (clip == null)
                return false;

            anim.runtimeAnimatorController = null;
            anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            _addPending = (clip, anim);
            playerRoot.AddComponent<HumanLocomotionPlayableDriver>();
            _addPending = null;
            return true;
        }

        static AnimationClip PickLocomotionClip(AnimationClip[] clips)
        {
            AnimationClip idle = null;
            AnimationClip longest = null;
            var longestLen = 0f;
            foreach (var c in clips)
            {
                if (c == null || c.name.IndexOf("mask", StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;
                var n = c.name.ToLowerInvariant();
                if (n.Contains("walk") || n.Contains("jog") || n.Contains("run") || n.Contains("move"))
                    return c;
                if (idle == null && n.Contains("idle"))
                    idle = c;
                if (c.length > longestLen)
                {
                    longestLen = c.length;
                    longest = c;
                }
            }

            if (idle != null)
                return idle;
            if (longest != null && longestLen > 0.2f)
                return longest;
            return clips[0];
        }

        void Awake()
        {
            if (!_addPending.HasValue)
            {
                enabled = false;
                return;
            }

            _clip = _addPending.Value.clip;
            _animator = _addPending.Value.animator;
            _addPending = null;
            _cc = GetComponent<CharacterController>();
        }

        void OnEnable()
        {
            if (!Application.isPlaying || _clip == null || _animator == null)
                return;
            if (_graph.IsValid())
                return;

            _graph = PlayableGraph.Create($"{nameof(HumanLocomotionPlayableDriver)}_{GetInstanceID()}");
            _graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            _clipPlayable = AnimationClipPlayable.Create(_graph, _clip);
            _clipPlayable.SetApplyFootIK(true);
            _clipPlayable.SetSpeed(0f);
            var output = AnimationPlayableOutput.Create(_graph, "Locomotion", _animator);
            output.SetSourcePlayable(_clipPlayable);
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
            if (!_graph.IsValid() || !_clipPlayable.IsValid())
                return;
            if (_cc == null)
                _cc = GetComponent<CharacterController>();
            if (_cc == null)
                return;

            var planar = new Vector3(_cc.velocity.x, 0f, _cc.velocity.z).magnitude;
            var sp = planar > speedDeadZone
                ? Mathf.Clamp01(planar / Mathf.Max(referencePlanarSpeed, 0.01f)) * maxPlaybackSpeed
                : 0f;
            _clipPlayable.SetSpeed(sp);
        }
    }
}
