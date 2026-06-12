using Rpg.Dialogue;
using Rpg.Audio;
using Rpg.UI;
using UnityEngine;
using UnityEngine.AI;

namespace Rpg.Player
{
    /// <summary>
    /// Camera-relative WASD / arrows plus click-to-move on the Ground layer.
    /// Uses <see cref="CharacterController"/> for motion (reliable on a flat slice); optional NavMesh sampling keeps clicks on walkable areas.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [DefaultExecutionOrder(10)]
    public sealed class PlayerClickMove : MonoBehaviour
    {
        [SerializeField] float moveSpeed = 4.5f;
        [SerializeField] float sprintMultiplier = 2f;
        [SerializeField] LayerMask groundMask = ~0;
        [SerializeField] float navSampleRadius = 2f;
        [SerializeField] float clickStopDistance = 0.2f;
        [SerializeField] float gravity = -24f;
        [SerializeField] float jumpHeight = 1.2f;
        [SerializeField] float coyoteTime = 0.12f;
        [SerializeField] float initialSpawnGravityScale = 0.5f;
        [SerializeField] float obstacleTraverseTriggerSeconds = 1f;
        [SerializeField] float obstacleTraverseDurationSeconds = 3f;
        [SerializeField] float traverseFeetClearanceAboveTerrain = 0.35f;
        [SerializeField] float traverseLookaheadMeters = 2f;
        [SerializeField] float traverseUphillLiftBlend = 0.45f;
        [SerializeField] float traverseMaxVerticalSpeed = 5f;

        Camera _cam;
        SliceFollowCamera _followCam;
        CharacterController _cc;
        PlayerItemPickupInteractor _pickupInteractor;
        Vector3? _clickGoalXZ;
        float _verticalVelocity;
        bool _isSprinting;
        bool _isGroundedForJump;
        Vector3 _lastStableGroundPos;
        bool _hasStableGroundPos;
        float _lastGroundedAt = -999f;
        bool _hasCompletedInitialLanding;
        bool _movementBasisLockedByLook;
        float _lockedFirstPersonYaw;
        Vector3 _lockedForward;
        Vector3 _lockedRight;
        float _obstacleBlockedTimer;
        bool _isObstacleTraverseActive;
        float _obstacleTraverseDeadline;
        Vector3 _lastObstacleTraversePlanarDir;
        AudioSource _movementSfxSource;
        AudioClip _walkSfx;
        AudioClip _runSfx;
        AudioClip _idleSfx;
        string _movementSfxMode;

        public bool IsSprinting => _isSprinting;
        public bool IsGrounded => _isGroundedForJump;
        public float VerticalVelocity => _verticalVelocity;
        /// <summary>Configured walk speed (no sprint multiplier).</summary>
        public float WalkSpeed => moveSpeed;
        /// <summary>Walk speed multiplied by sprint multiplier (for AI / tuning).</summary>
        public float SprintSpeed => moveSpeed * sprintMultiplier;
        /// <summary>Horizontal velocity from the character controller (world space).</summary>
        public Vector3 PlanarWorldVelocity =>
            _cc != null
                ? new Vector3(_cc.velocity.x, 0f, _cc.velocity.z)
                : Vector3.zero;
        /// <summary>Magnitude of <see cref="PlanarWorldVelocity"/>.</summary>
        public float PlanarSpeed => PlanarWorldVelocity.magnitude;
        /// <summary>True after the first frame the player is considered near ground (intro drop / landing).</summary>
        public bool HasCompletedIntroLanding => _hasCompletedInitialLanding;

        void Awake()
        {
            _cc = GetComponent<CharacterController>();
            _cam = Camera.main;
            _lastStableGroundPos = transform.position;
            _hasStableGroundPos = true;
            _movementSfxSource = gameObject.AddComponent<AudioSource>();
            _movementSfxSource.playOnAwake = false;
            _movementSfxSource.loop = true;
            _movementSfxSource.spatialBlend = 0f;
            _movementSfxSource.volume = 0.05f;
            _walkSfx = RuntimeAudioClipLoader.LoadFromAssetPath("Assets/FREE SOUND PACK_TM(355)/Footsteps(15)/Grass_06.wav");
            _runSfx = RuntimeAudioClipLoader.LoadFromAssetPath("Assets/FREE SOUND PACK_TM(355)/Footsteps(15)/Snow 04.wav");
            _idleSfx = RuntimeAudioClipLoader.LoadFromAssetPath("Assets/FREE SOUND PACK_TM(355)/Ambiences(43)/Birds_Singing-005.wav");
        }

        void Start()
        {
            var gl = LayerMask.NameToLayer("Ground");
            if (gl >= 0)
                groundMask = 1 << gl;
        }

        void Update()
        {
            if (GameOverController.Instance != null && GameOverController.Instance.IsGameOver)
            {
                StopMovementSfx();
                return;
            }
            if (Object.FindFirstObjectByType<StartupTitleScreenStage>() != null
                || Object.FindFirstObjectByType<PlayerCharacterSelectionStage>() != null)
            {
                StopMovementSfx();
                return;
            }
            if (DialogueManager.Instance != null && DialogueManager.Instance.IsDialogueOpen)
            {
                if (_isObstacleTraverseActive)
                    EndObstacleTraverse();
                StopMovementSfx();
                return;
            }

            if (_cam == null)
                _cam = Camera.main;
            if (_followCam == null && _cam != null)
                _followCam = _cam.GetComponent<SliceFollowCamera>();
            if (_pickupInteractor == null)
                _pickupInteractor = GetComponent<PlayerItemPickupInteractor>();

            if (TryBeginClickMove())
                return;

            var wish = Vector3.zero;
            var h = Input.GetAxisRaw("Horizontal");
            var v = Input.GetAxisRaw("Vertical");
            var keyboard = !Mathf.Approximately(h, 0f) || !Mathf.Approximately(v, 0f);
            if (keyboard)
            {
                _clickGoalXZ = null;
                wish = CameraRelativeWish(h, v);
            }
            else if (_clickGoalXZ.HasValue)
            {
                var p = transform.position;
                var to = _clickGoalXZ.Value - p;
                to.y = 0f;
                if (to.sqrMagnitude <= clickStopDistance * clickStopDistance)
                    _clickGoalXZ = null;
                else
                    wish = to.normalized;
            }

            if (wish.sqrMagnitude > 1f)
                wish.Normalize();

            if (wish.sqrMagnitude > 0.0001f)
            {
                var lookDir = new Vector3(wish.x, 0f, wish.z);
                if (lookDir.sqrMagnitude > 0.0001f)
                {
                    var look = Quaternion.LookRotation(lookDir);
                    transform.rotation = Quaternion.Slerp(transform.rotation, look,
                        1f - Mathf.Exp(-12f * Time.deltaTime));
                }
            }

            if (_isObstacleTraverseActive)
            {
                ProcessObstacleTraverse(wish);
                MaintainGroundSafety();
                return;
            }

            _isGroundedForJump = IsNearGround();
            if (_isGroundedForJump)
            {
                _lastGroundedAt = Time.time;
                _hasCompletedInitialLanding = true;
            }
            if ((_cc.isGrounded || _isGroundedForJump) && _verticalVelocity < 0f)
                _verticalVelocity = -2f;
            var canJump = Time.time - _lastGroundedAt <= coyoteTime;
            if (canJump && Input.GetKeyDown(KeyCode.Space))
            {
                _verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
                _lastGroundedAt = -999f;
            }
            var gravityScale = _hasCompletedInitialLanding ? 1f : Mathf.Clamp(initialSpawnGravityScale, 0.05f, 1f);
            _verticalVelocity += gravity * gravityScale * Time.deltaTime;

            var sprintPressed = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            _isSprinting = sprintPressed && Mathf.Abs(v) > 0.05f && wish.sqrMagnitude > 0.0001f;
            var activeSpeed = _isSprinting ? moveSpeed * sprintMultiplier : moveSpeed;

            var before = transform.position;
            var motion = wish * activeSpeed + Vector3.up * _verticalVelocity;
            var flags = _cc.Move(motion * Time.deltaTime);
            var after = transform.position;
            TrackObstacleBlocking(wish, before, after, flags);
            MaintainGroundSafety();
            UpdateMovementSfx(wish.sqrMagnitude > 0.0001f, _isSprinting);
        }

        void OnDisable()
        {
            if (_isObstacleTraverseActive)
                EndObstacleTraverse();
            StopMovementSfx();
        }

        void UpdateMovementSfx(bool isMoving, bool isSprintingNow)
        {
            if (_movementSfxSource == null)
                return;
            if (!isMoving)
            {
                PlayMovementLoop("idle", _idleSfx, 1f);
                return;
            }

            if (isSprintingNow)
            {
                PlayMovementLoop("run", _runSfx, 2f);
                return;
            }

            PlayMovementLoop("walk", _walkSfx, 1f);
        }

        void PlayMovementLoop(string mode, AudioClip clip, float pitch)
        {
            if (_movementSfxSource == null || clip == null)
                return;
            if (_movementSfxSource.isPlaying && _movementSfxMode == mode && _movementSfxSource.clip == clip)
                return;
            _movementSfxMode = mode;
            _movementSfxSource.pitch = Mathf.Max(0.1f, pitch);
            _movementSfxSource.clip = clip;
            _movementSfxSource.Play();
        }

        void StopMovementSfx()
        {
            if (_movementSfxSource == null)
                return;
            _movementSfxSource.Stop();
            _movementSfxMode = string.Empty;
        }

        void OnGUI()
        {
            if (!_isObstacleTraverseActive)
                return;
            var cam = _cam != null ? _cam : Camera.main;
            if (cam == null)
                return;
            var world = transform.position + Vector3.up * 2.6f;
            var sp = cam.WorldToScreenPoint(world);
            if (sp.z <= 0f)
                return;
            var prev = GUI.color;
            GUI.color = new Color(1f, 0.95f, 0.3f, 0.95f);
            GUI.Label(new Rect(sp.x - 42f, Screen.height - sp.y - 10f, 84f, 18f), "TRAVERSE");
            GUI.color = prev;
        }

        void TrackObstacleBlocking(Vector3 wish, Vector3 before, Vector3 after, CollisionFlags flags)
        {
            var hasMoveIntent = wish.sqrMagnitude > 0.0001f;
            if (!hasMoveIntent || _isObstacleTraverseActive)
            {
                _obstacleBlockedTimer = 0f;
                return;
            }
            var moved = new Vector3(after.x - before.x, 0f, after.z - before.z).magnitude;
            var expected = moveSpeed * Time.deltaTime;
            var movedFraction = expected > 1e-4f ? moved / expected : 1f;
            var collidedLaterally = (flags & CollisionFlags.Sides) != 0;
            var blocked = collidedLaterally && movedFraction < 0.15f;
            if (!blocked)
            {
                _obstacleBlockedTimer = 0f;
                return;
            }
            _obstacleBlockedTimer += Time.deltaTime;
            if (_obstacleBlockedTimer < obstacleTraverseTriggerSeconds)
                return;
            BeginObstacleTraverse(wish);
        }

        void BeginObstacleTraverse(Vector3 wish)
        {
            _isObstacleTraverseActive = true;
            _obstacleTraverseDeadline = Time.time + Mathf.Max(0.2f, obstacleTraverseDurationSeconds);
            _obstacleBlockedTimer = 0f;
            _lastObstacleTraversePlanarDir = wish.sqrMagnitude > 0.0001f
                ? new Vector3(wish.x, 0f, wish.z).normalized
                : new Vector3(transform.forward.x, 0f, transform.forward.z).normalized;
            if (_lastObstacleTraversePlanarDir.sqrMagnitude < 0.0001f)
                _lastObstacleTraversePlanarDir = Vector3.forward;
            if (_cc != null && _cc.enabled)
                _cc.enabled = false;
            _verticalVelocity = -2f;
        }

        void ProcessObstacleTraverse(Vector3 wish)
        {
            if (!_isObstacleTraverseActive)
                return;
            if (Time.time >= _obstacleTraverseDeadline)
            {
                EndObstacleTraverse();
                return;
            }

            var desiredPlanarDir = _lastObstacleTraversePlanarDir;
            if (wish.sqrMagnitude > 0.0001f)
            {
                desiredPlanarDir = new Vector3(wish.x, 0f, wish.z).normalized;
                _lastObstacleTraversePlanarDir = desiredPlanarDir;
            }

            var here = transform.position;
            var step = moveSpeed * Time.deltaTime;
            var nx = here.x + desiredPlanarDir.x * step;
            var nz = here.z + desiredPlanarDir.z * step;

            var yHere = SampleTerrainY(nx, nz);
            var lx = nx + desiredPlanarDir.x * traverseLookaheadMeters;
            var lz = nz + desiredPlanarDir.z * traverseLookaheadMeters;
            var yLook = SampleTerrainY(lx, lz);
            if (float.IsNaN(yHere))
                yHere = here.y;
            if (float.IsNaN(yLook))
                yLook = yHere;

            var ground = Mathf.Max(yHere, yLook);
            var minFeet = ground + traverseFeetClearanceAboveTerrain;
            var uphill = Mathf.Max(0f, yLook - yHere);
            var wantY = Mathf.Max(minFeet, here.y + uphill * traverseUphillLiftBlend);
            wantY = Mathf.Max(wantY, here.y);
            var maxDy = traverseMaxVerticalSpeed * Time.deltaTime;
            var targetY = Mathf.MoveTowards(here.y, wantY, maxDy);
            targetY = Mathf.Max(targetY, Mathf.Min(yHere, yLook) + traverseFeetClearanceAboveTerrain * 0.35f);

            transform.position = new Vector3(nx, targetY, nz);
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(desiredPlanarDir, Vector3.up),
                1f - Mathf.Exp(-12f * Time.deltaTime));
        }

        void EndObstacleTraverse()
        {
            _isObstacleTraverseActive = false;
            _obstacleBlockedTimer = 0f;
            if (_cc != null && !_cc.enabled)
                _cc.enabled = true;
            var p = transform.position;
            var y = SampleTerrainY(p.x, p.z);
            if (!float.IsNaN(y))
                transform.position = new Vector3(p.x, Mathf.Max(y + Mathf.Max(0.08f, _cc.skinWidth), p.y), p.z);
            _verticalVelocity = -2f;
        }

        float SampleTerrainY(float x, float z)
        {
            var origin = new Vector3(x, 1000f, z);
            var hits = Physics.RaycastAll(origin, Vector3.down, 3000f, ~0, QueryTriggerInteraction.Ignore);
            var found = false;
            var bestDistance = float.MaxValue;
            var bestY = float.NaN;
            foreach (var hit in hits)
            {
                if (hit.collider == null)
                    continue;
                if (IsFoliageLike(hit.collider.gameObject))
                    continue;
                if (hit.distance >= bestDistance)
                    continue;
                bestDistance = hit.distance;
                bestY = hit.point.y;
                found = true;
            }
            return found ? bestY : float.NaN;
        }

        void MaintainGroundSafety()
        {
            var origin = transform.position + Vector3.up * 0.6f;
            if (_cc.isGrounded)
            {
                _hasStableGroundPos = true;
                _lastStableGroundPos = transform.position;
                return;
            }

            if (TryGetGroundHit(origin, 1.6f, out var nearHit))
            {
                _hasStableGroundPos = true;
                _lastStableGroundPos = nearHit.point + Vector3.up * Mathf.Max(0.08f, _cc.skinWidth);
                return;
            }

            if (!_hasStableGroundPos)
                return;
            if (TryGetGroundHit(origin, 7f, out _))
                return;
            // During intro sky-drop, do not snap back to an old cached ground point
            // before the first proper landing has happened.
            if (!_hasCompletedInitialLanding)
                return;

            var safePos = _lastStableGroundPos;
            safePos.y += Mathf.Max(0.08f, _cc.skinWidth);
            transform.position = safePos;
            _verticalVelocity = -2f;
            _clickGoalXZ = null;
        }

        bool IsNearGround()
        {
            if (_cc == null)
                return false;
            if (_cc.isGrounded)
                return true;
            var origin = transform.position + Vector3.up * 0.2f;
            var probeDistance = Mathf.Max(0.5f, _cc.skinWidth + 0.3f);
            return TryGetGroundHit(origin, probeDistance, out _);
        }

        static bool TryGetGroundHit(Vector3 origin, float maxDistance, out RaycastHit bestHit)
        {
            bestHit = default;
            var hits = Physics.RaycastAll(origin, Vector3.down, maxDistance, ~0, QueryTriggerInteraction.Ignore);
            var found = false;
            var bestDistance = float.MaxValue;
            foreach (var hit in hits)
            {
                if (hit.collider == null)
                    continue;
                if (IsFoliageLike(hit.collider.gameObject))
                    continue;
                if (hit.distance >= bestDistance)
                    continue;
                bestDistance = hit.distance;
                bestHit = hit;
                found = true;
            }
            return found;
        }

        static bool IsFoliageLike(GameObject go)
        {
            if (go == null)
                return false;
            var n = go.name.ToLowerInvariant();
            return n.Contains("grass") || n.Contains("foliage") || n.Contains("bush")
                   || n.Contains("leaf") || n.Contains("flower") || n.Contains("plant")
                   || n.Contains("water") || n.Contains("ocean") || n.Contains("river");
        }

        bool TryBeginClickMove()
        {
            if (_pickupInteractor != null && _pickupInteractor.TryHandleLeftClickPickup())
                return true;
            // Left mouse is reserved for free-look camera rotation.
            if (!Input.GetMouseButtonDown(1))
                return false;
            if (_cam == null)
                return false;

            var ray = _cam.ScreenPointToRay(Input.mousePosition);
            if (!Physics.Raycast(ray, out var hit, 200f, groundMask, QueryTriggerInteraction.Ignore))
                return false;

            var target = hit.point;
            if (NavMesh.SamplePosition(hit.point, out var navHit, navSampleRadius, NavMesh.AllAreas))
                target = navHit.position;

            target.y = transform.position.y;
            _clickGoalXZ = target;
            return true;
        }

        Vector3 CameraRelativeWish(float h, float v)
        {
            var mouseLookActive = _followCam != null && _followCam.IsMouseLookActive;
            if (mouseLookActive && !_movementBasisLockedByLook)
            {
                _movementBasisLockedByLook = true;
                _lockedFirstPersonYaw = _followCam != null ? _followCam.FirstPersonYawDegrees : (_cam != null ? _cam.transform.eulerAngles.y : 0f);
                var lockForwardCam = _cam != null ? _cam.transform.forward : Vector3.forward;
                var lockRightCam = _cam != null ? _cam.transform.right : Vector3.right;
                lockForwardCam.y = 0f;
                lockRightCam.y = 0f;
                _lockedForward = lockForwardCam.sqrMagnitude > 0.0001f ? lockForwardCam.normalized : Vector3.forward;
                _lockedRight = lockRightCam.sqrMagnitude > 0.0001f ? lockRightCam.normalized : Vector3.right;
            }
            else if (!mouseLookActive)
            {
                _movementBasisLockedByLook = false;
            }

            if (_followCam != null && _followCam.IsFirstPersonView)
            {
                var yaw = _movementBasisLockedByLook ? _lockedFirstPersonYaw : _followCam.FirstPersonYawDegrees;
                var basis = Quaternion.Euler(0f, yaw, 0f);
                var forward = basis * Vector3.forward;
                forward.y = 0f;
                forward.Normalize();
                var right = basis * Vector3.right;
                right.y = 0f;
                right.Normalize();
                var worldDir = right * h + forward * v;
                return worldDir.sqrMagnitude < 0.0001f ? Vector3.zero : worldDir.normalized;
            }

            var forwardCam = _movementBasisLockedByLook ? _lockedForward : (_cam != null ? _cam.transform.forward : Vector3.forward);
            var rightCam = _movementBasisLockedByLook ? _lockedRight : (_cam != null ? _cam.transform.right : Vector3.right);
            forwardCam.y = 0f;
            rightCam.y = 0f;
            forwardCam.Normalize();
            rightCam.Normalize();
            var worldDirCam = rightCam * h + forwardCam * v;
            return worldDirCam.sqrMagnitude < 0.0001f ? Vector3.zero : worldDirCam.normalized;
        }
    }
}
