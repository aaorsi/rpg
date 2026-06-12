using Rpg.Core;
using Rpg.Dialogue;
using Rpg.Player;
using Rpg.UI;
using UnityEngine;
using UnityEngine.Rendering;

namespace Rpg.Npc
{
    /// <summary>
    /// Keeps a sidekick near the hero after the NPC agrees to follow.
    /// Recomputes a follow slot every few seconds and moves toward it while preserving gravity.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [DefaultExecutionOrder(18)]
    public sealed class SidekickFollowHeroController : MonoBehaviour
    {
        [SerializeField]
        [Min(1f)]
        float retargetSeconds = 5f;

        [SerializeField]
        [Min(0.5f)]
        float keepWithinMeters = 2f;

        [SerializeField]
        [Min(0.5f)]
        float sideOffsetMeters = 1.6f;

        [SerializeField]
        [Min(0.1f)]
        float fallbackWalkSpeed = 4.5f;

        [SerializeField]
        [Min(0.1f)]
        float fallbackRunSpeed = 8.5f;

        [SerializeField]
        float gravity = -18f;

        const float FollowIndicatorHeightMeters = 2.6f;

        CharacterController _cc;
        Transform _hero;
        PlayerClickMove _heroMove;
        PackAnimalAnimatorDriver _packDriver;
        float _nextRetargetAt;
        float _verticalVelocity;
        float _sideSign = 1f;
        Vector3 _slotWorld;
        bool _following;
        Transform _followIndicator;
        Renderer _followIndicatorRenderer;

        public bool IsFollowing => _following;

        /// <summary>Hero sprint intent for humanoid anim drivers (sidekicks have no PlayerClickMove).</summary>
        public bool HeroIsSprinting
        {
            get
            {
                if (_heroMove == null || _hero == null || !_hero.gameObject.activeInHierarchy)
                    RefreshHeroReferences();
                return _heroMove != null && _heroMove.IsSprinting;
            }
        }

        void Awake()
        {
            _cc = GetComponent<CharacterController>();
            _packDriver = GetComponent<PackAnimalAnimatorDriver>();
            enabled = false;
        }

        public void StartFollowingHero()
        {
            _following = true;
            enabled = true;

            if (TryGetComponent<NpcAmbientDrift>(out var drift))
                drift.enabled = false;
            if (TryGetComponent<NpcGuideToLocation>(out var guide))
                guide.enabled = false;

            _sideSign = Random.value < 0.5f ? -1f : 1f;
            _nextRetargetAt = -999f;
            RefreshHeroReferences();
            RefreshSlot(force: true);
            EnsureFollowIndicator();
            SetFollowIndicatorActive(true, new Color(0.95f, 0.45f, 1f, 0.95f));
            DialogueTelemetry.Log("NpcSidekickFollow", $"{name} started following hero.");
        }

        void OnDisable()
        {
            SetFollowIndicatorActive(false, Color.clear);
        }

        void OnDestroy()
        {
            if (_followIndicator != null)
            {
                if (Application.isPlaying)
                    Destroy(_followIndicator.gameObject);
                else
                    DestroyImmediate(_followIndicator.gameObject);
                _followIndicator = null;
                _followIndicatorRenderer = null;
            }
        }

        void LateUpdate()
        {
            if (!_following || _followIndicator == null || !_followIndicator.gameObject.activeSelf)
                return;
            _followIndicator.localPosition =
                new Vector3(0f, FollowIndicatorHeightMeters + Mathf.Sin(Time.time * 4f) * 0.06f, 0f);
            var cam = Camera.main;
            if (cam != null)
            {
                var f = cam.transform.forward;
                f.y = 0f;
                if (f.sqrMagnitude > 0.001f)
                    _followIndicator.rotation = Quaternion.LookRotation(f.normalized, Vector3.up);
            }
        }

        void EnsureFollowIndicator()
        {
            if (_followIndicator != null)
                return;
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "SidekickFollowIndicator";
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, FollowIndicatorHeightMeters, 0f);
            go.transform.localScale = new Vector3(0.22f, 0.22f, 0.22f);
            var c = go.GetComponent<Collider>();
            if (c != null)
                Destroy(c);
            _followIndicator = go.transform;
            _followIndicatorRenderer = go.GetComponent<Renderer>();
            if (_followIndicatorRenderer != null)
                _followIndicatorRenderer.shadowCastingMode = ShadowCastingMode.Off;
            go.SetActive(false);
        }

        void SetFollowIndicatorActive(bool on, Color color)
        {
            if (_followIndicator == null)
                return;
            _followIndicator.gameObject.SetActive(on);
            if (!on || _followIndicatorRenderer == null)
                return;
            if (_followIndicatorRenderer.material != null)
            {
                _followIndicatorRenderer.material.color = color;
                _followIndicatorRenderer.material.EnableKeyword("_EMISSION");
                _followIndicatorRenderer.material.SetColor("_EmissionColor", color * 1.4f);
            }
        }

        void RefreshHeroReferences()
        {
            var go = GameObject.FindGameObjectWithTag(GameConstants.PlayerTag);
            _hero = go != null ? go.transform : null;
            _heroMove = go != null ? go.GetComponent<PlayerClickMove>() : null;
        }

        void Update()
        {
            if (!_following)
                return;
            if (GameOverController.Instance != null && GameOverController.Instance.IsGameOver)
                return;
            if (_cc == null || !_cc.enabled || !_cc.gameObject.activeInHierarchy)
                return;

            if (_hero == null || !_hero.gameObject.activeInHierarchy)
                RefreshHeroReferences();
            if (_hero == null)
            {
                StepGravityOnly();
                return;
            }

            RefreshSlot(force: false);

            var toHero = _hero.position - transform.position;
            toHero.y = 0f;
            var toSlot = _slotWorld - transform.position;
            toSlot.y = 0f;

            var heroDist = toHero.magnitude;
            if (heroDist <= keepWithinMeters && toSlot.magnitude <= 0.45f)
            {
                StepGravityOnly();
                ApplyPlanarAnimSpeed(0f);
                return;
            }

            var moveDir = toSlot.sqrMagnitude > 1e-5f ? toSlot.normalized : toHero.normalized;
            var speed = ResolveFollowSpeed();
            var planarWish = moveDir * speed;
            StepGravityAndPlanar(planarWish);
            ApplyPlanarAnimSpeed(speed);
        }

        void RefreshSlot(bool force)
        {
            if (_hero == null)
                return;

            var heroPos = _hero.position;
            var need = force || Time.time >= _nextRetargetAt;
            if (!need)
            {
                var far = new Vector2(transform.position.x - heroPos.x, transform.position.z - heroPos.z).magnitude;
                need = far > keepWithinMeters * 4f;
            }

            if (!need)
                return;

            _nextRetargetAt = Time.time + Mathf.Max(1f, retargetSeconds);
            var heroForward = _hero.forward;
            heroForward.y = 0f;
            if (heroForward.sqrMagnitude < 1e-5f)
                heroForward = Vector3.forward;
            heroForward.Normalize();
            var heroRight = Vector3.Cross(Vector3.up, heroForward).normalized;
            var trailing = Mathf.Clamp(keepWithinMeters * 0.6f, 0.6f, 1.6f);
            var raw = heroPos - heroForward * trailing + heroRight * (_sideSign * sideOffsetMeters);
            var y = SampleGroundY(raw, transform.position.y);
            _slotWorld = new Vector3(raw.x, y + Mathf.Max(0.08f, _cc.skinWidth), raw.z);
        }

        float SampleGroundY(Vector3 near, float fallbackY)
        {
            var t = Terrain.activeTerrain;
            if (t != null && t.terrainData != null)
            {
                var o = t.transform.position;
                var td = t.terrainData;
                if (near.x >= o.x && near.x <= o.x + td.size.x &&
                    near.z >= o.z && near.z <= o.z + td.size.z)
                    return t.SampleHeight(new Vector3(near.x, o.y, near.z)) + o.y;
            }

            var origin = new Vector3(near.x, near.y + 50f, near.z);
            if (Physics.Raycast(origin, Vector3.down, out var hit, 150f, ~0, QueryTriggerInteraction.Ignore))
                return hit.point.y;
            return fallbackY;
        }

        float ResolveFollowSpeed()
        {
            if (_heroMove == null)
                return fallbackWalkSpeed;
            return _heroMove.IsSprinting ? _heroMove.SprintSpeed : _heroMove.WalkSpeed;
        }

        void ApplyPlanarAnimSpeed(float planarSpeed)
        {
            if (_packDriver != null)
                _packDriver.SetLocomotionReferenceSpeed(Mathf.Max(0.01f, planarSpeed), Mathf.Max(0.01f, planarSpeed * 0.92f));
        }

        void StepGravityOnly()
        {
            StepGravityAndPlanar(Vector3.zero);
        }

        void StepGravityAndPlanar(Vector3 planarWish)
        {
            if (_cc.isGrounded && _verticalVelocity < 0f)
                _verticalVelocity = -1f;
            _verticalVelocity += gravity * Time.deltaTime;

            var motion = planarWish + Vector3.up * _verticalVelocity;
            _cc.Move(motion * Time.deltaTime);

            if (planarWish.sqrMagnitude > 1e-8f)
            {
                var look = Quaternion.LookRotation(planarWish.normalized, Vector3.up);
                transform.rotation = Quaternion.Slerp(transform.rotation, look, 1f - Mathf.Exp(-10f * Time.deltaTime));
            }
        }
    }
}
