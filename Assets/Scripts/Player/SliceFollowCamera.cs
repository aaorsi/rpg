using UnityEngine;
using Rpg.Dialogue;

namespace Rpg.Player
{
    /// <summary>
    /// Third-person orbit follows <b>movement direction</b> (velocity), not character facing, so forward/back walk
    /// does not flip the camera. Scroll adjusts distance (1–10 m). First-person uses mouse yaw/pitch; movement
    /// uses first-person yaw via <see cref="FirstPersonYawDegrees"/>.
    /// </summary>
    [DefaultExecutionOrder(80)]
    public sealed class SliceFollowCamera : MonoBehaviour
    {
        [SerializeField] float pivotHeight = 1.2f;
        [SerializeField] float cameraDistance = 6f;
        [SerializeField] float minCameraDistance = 1f;
        [SerializeField] float maxCameraDistance = 10f;
        [SerializeField] float zoomScrollSensitivity = 2f;
        [SerializeField] float pitchOffsetDegrees = 22f;
        [SerializeField] float lookAtHeight = 1.05f;
        [SerializeField] float yawSmoothTime = 0.6f;
        [SerializeField] float positionSmoothTime = 0.22f;
        [SerializeField] float lookRotationSmooth = 5.5f;
        [SerializeField] float rotationSpeedMultiplier = 0.5f;
        [Tooltip("Camera anchor along standing height: 0 = feet / capsule bottom, 1 = head / capsule top. 0.95 = 95% up the body (typical eye band).")]
        [SerializeField] float firstPersonHeightFactor = 0.95f;
        [SerializeField] float firstPersonForwardOffset = 1.0f;
        [SerializeField] float firstPersonMouseSensitivity = 2f;
        [Tooltip("Degrees of yaw/pitch per screen pixel, before firstPersonMouseSensitivity.")]
        [SerializeField] float firstPersonMouseDegreesPerPixel = 0.12f;
        [SerializeField] float firstPersonPitchMin = -80f;
        [SerializeField] float firstPersonPitchMax = 80f;
        [SerializeField] float dialogueFocusDistance = 1f;
        [SerializeField] float dialogueFocusHeightFactor = 0.95f;
        [SerializeField] float dialogueFocusMoveSmoothTime = 0.45f;
        [SerializeField] float dialogueFocusStepBackDistance = 1f;
        [SerializeField] float dialogueFocusStepBackDuration = 5f;
        [SerializeField] float velocityYawDeadZone = 0.08f;
        [SerializeField] float thirdPersonMouseSensitivity = 2f;
        [SerializeField] float thirdPersonPitchMin = -10f;
        [SerializeField] float thirdPersonPitchMax = 70f;
        [SerializeField] float thirdPersonVelocityFollowDelay = 0.8f;

        Transform _target;
        Transform _dialogueNpcTarget;
        /// <summary>Third-person orbit yaw (degrees), driven by planar velocity, not character transform.forward.</summary>
        float _orbitYaw;
        float _yawVelocity;
        Vector3 _posVelocity;
        bool _initialized;
        bool _firstPerson;
        bool _dialogueFocusActive;
        float _dialogueFocusElapsed;
        float _fpYaw;
        float _fpPitch;
        Vector3 _fpLastMousePixels;
        bool _fpSkipMouseDeltaOnce;
        float _orbitPitch;
        float _manualOrbitUntilTime;

        public bool IsFirstPersonView => _firstPerson;
        public bool IsMouseLookActive => Input.GetMouseButton(0) && !PlayerItemPickupInteractor.IsLeftClickConsumedThisFrame;
        /// <summary>Horizontal angle (degrees) used for first-person movement (mouse pan).</summary>
        public float FirstPersonYawDegrees => _fpYaw;

        public void SetTarget(Transform target)
        {
            _target = target;
            _initialized = false;
        }

        public void StartDialogueFocus(Transform npcTarget)
        {
            if (npcTarget == null)
                return;
            _dialogueNpcTarget = npcTarget;
            _dialogueFocusActive = true;
            _dialogueFocusElapsed = 0f;
        }

        public void StopDialogueFocus()
        {
            _dialogueFocusActive = false;
            _dialogueNpcTarget = null;
        }

        void LateUpdate()
        {
            if (!Application.isPlaying || _target == null)
                return;
            if (_dialogueFocusActive && (DialogueManager.Instance == null || !DialogueManager.Instance.IsDialogueOpen))
                StopDialogueFocus();
            if (Input.GetKeyDown(KeyCode.Alpha1))
            {
                _firstPerson = !_firstPerson;
                if (_firstPerson)
                {
                    _fpYaw = _target.eulerAngles.y;
                    _fpPitch = 0f;
                    _fpLastMousePixels = Input.mousePosition;
                    _fpSkipMouseDeltaOnce = true;
                }
                else
                {
                    _initialized = false;
                    _orbitPitch = Mathf.Clamp(pitchOffsetDegrees, thirdPersonPitchMin, thirdPersonPitchMax);
                    _manualOrbitUntilTime = Time.time + thirdPersonVelocityFollowDelay;
                }
            }

            if (_dialogueFocusActive && _dialogueNpcTarget != null)
            {
                var rotSharpness = Mathf.Max(0.01f, lookRotationSmooth * rotationSpeedMultiplier);
                _dialogueFocusElapsed += Time.deltaTime;
                var retreatT = dialogueFocusStepBackDuration > 0.01f
                    ? Mathf.Clamp01(_dialogueFocusElapsed / dialogueFocusStepBackDuration)
                    : 1f;
                var focusDistance = dialogueFocusDistance + dialogueFocusStepBackDistance * retreatT;
                var npcHeight = ComputeTargetHeight(_dialogueNpcTarget);
                var npcLookPoint = _dialogueNpcTarget.position + Vector3.up * (npcHeight * dialogueFocusHeightFactor);
                var focusDesiredPos = _dialogueNpcTarget.position + _dialogueNpcTarget.forward * focusDistance + Vector3.up * (npcHeight * dialogueFocusHeightFactor);
                transform.position = Vector3.SmoothDamp(transform.position, focusDesiredPos, ref _posVelocity, dialogueFocusMoveSmoothTime);
                var focusDir = npcLookPoint - transform.position;
                if (focusDir.sqrMagnitude > 0.0001f)
                {
                    var focusTargetRot = Quaternion.LookRotation(focusDir.normalized, Vector3.up);
                    transform.rotation = Quaternion.Slerp(transform.rotation, focusTargetRot, 1f - Mathf.Exp(-rotSharpness * Time.deltaTime));
                }
                return;
            }

            if (_firstPerson)
            {
                var rotSharpness = Mathf.Max(0.01f, lookRotationSmooth * rotationSpeedMultiplier);
                var mousePixels = Input.mousePosition;
                var lookActive = IsMouseLookActive;
                if (!lookActive)
                {
                    _fpSkipMouseDeltaOnce = true;
                }
                else if (_fpSkipMouseDeltaOnce)
                {
                    _fpSkipMouseDeltaOnce = false;
                    _fpLastMousePixels = mousePixels;
                }
                else
                {
                    var dx = mousePixels.x - _fpLastMousePixels.x;
                    var dy = mousePixels.y - _fpLastMousePixels.y;
                    _fpLastMousePixels = mousePixels;
                    var scale = firstPersonMouseDegreesPerPixel * firstPersonMouseSensitivity;
                    _fpYaw += dx * scale;
                    _fpPitch -= dy * scale;
                }
                _fpPitch = Mathf.Clamp(_fpPitch, firstPersonPitchMin, firstPersonPitchMax);

                var fpRot = Quaternion.AngleAxis(_fpYaw, Vector3.up) * Quaternion.AngleAxis(_fpPitch, Vector3.right);
                var fpAnchor = _target.position;
                fpAnchor.y = ComputeFirstPersonEyeWorldY(_target);
                var fpPos = fpAnchor + fpRot * Vector3.forward * firstPersonForwardOffset;
                transform.position = Vector3.SmoothDamp(transform.position, fpPos, ref _posVelocity, 0.06f);
                transform.rotation = Quaternion.Slerp(
                    transform.rotation,
                    fpRot,
                    1f - Mathf.Exp(-rotSharpness * Time.deltaTime));
                return;
            }

            cameraDistance = Mathf.Clamp(cameraDistance - Input.mouseScrollDelta.y * zoomScrollSensitivity, minCameraDistance, maxCameraDistance);

            var cc = _target.GetComponent<CharacterController>();
            var planarVel = cc != null ? new Vector3(cc.velocity.x, 0f, cc.velocity.z) : Vector3.zero;
            if (!_initialized)
            {
                _yawVelocity = 0f;
                _posVelocity = Vector3.zero;
                var flatFromCam = transform.position - _target.position;
                flatFromCam.y = 0f;
                if (flatFromCam.sqrMagnitude > 0.0001f)
                    _orbitYaw = Quaternion.LookRotation(flatFromCam.normalized).eulerAngles.y;
                else
                    _orbitYaw = _target.eulerAngles.y;
                _orbitPitch = Mathf.Clamp(pitchOffsetDegrees, thirdPersonPitchMin, thirdPersonPitchMax);
                var pivot0 = _target.position + Vector3.up * pivotHeight;
                var orbit0 = Quaternion.Euler(_orbitPitch, _orbitYaw, 0f);
                transform.position = pivot0 + orbit0 * new Vector3(0f, 0f, -cameraDistance);
                _initialized = true;
            }

            var tpMousePixels = Input.mousePosition;
            var tpLookActive = IsMouseLookActive;
            if (tpLookActive)
            {
                if (_fpSkipMouseDeltaOnce)
                {
                    _fpSkipMouseDeltaOnce = false;
                    _fpLastMousePixels = tpMousePixels;
                }
                else
                {
                    var dx = tpMousePixels.x - _fpLastMousePixels.x;
                    var dy = tpMousePixels.y - _fpLastMousePixels.y;
                    _fpLastMousePixels = tpMousePixels;
                    var scale = firstPersonMouseDegreesPerPixel * thirdPersonMouseSensitivity;
                    _orbitYaw += dx * scale;
                    _orbitPitch = Mathf.Clamp(_orbitPitch - dy * scale, thirdPersonPitchMin, thirdPersonPitchMax);
                }
                _manualOrbitUntilTime = Time.time + thirdPersonVelocityFollowDelay;
            }
            else
            {
                _fpSkipMouseDeltaOnce = true;
            }

            if (!tpLookActive && Time.time >= _manualOrbitUntilTime
                && planarVel.sqrMagnitude > velocityYawDeadZone * velocityYawDeadZone)
            {
                var moveYaw = Quaternion.LookRotation(planarVel.normalized).eulerAngles.y;
                var effectiveYawSmoothTime = yawSmoothTime / Mathf.Max(0.05f, rotationSpeedMultiplier);
                _orbitYaw = Mathf.SmoothDampAngle(_orbitYaw, moveYaw, ref _yawVelocity, effectiveYawSmoothTime);
            }

            var pivot = _target.position + Vector3.up * pivotHeight;
            var orbit = Quaternion.Euler(_orbitPitch, _orbitYaw, 0f);
            var desiredPos = pivot + orbit * new Vector3(0f, 0f, -cameraDistance);

            transform.position = Vector3.SmoothDamp(transform.position, desiredPos, ref _posVelocity, positionSmoothTime);

            var lookPoint = _target.position + Vector3.up * lookAtHeight;
            var dir = lookPoint - transform.position;
            if (dir.sqrMagnitude < 0.0001f)
                return;
            var targetRot = Quaternion.LookRotation(dir.normalized, Vector3.up);
            var rotSharpnessThirdPerson = Mathf.Max(0.01f, lookRotationSmooth * rotationSpeedMultiplier);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot,
                1f - Mathf.Exp(-rotSharpnessThirdPerson * Time.deltaTime));
        }

        /// <summary>
        /// World Y for the first-person camera anchor: feet (capsule / mesh bottom) plus a fraction of standing height.
        /// Uses <see cref="CharacterController"/> geometry when present so a non-zero <c>center</c> does not lift the view.
        /// </summary>
        float ComputeFirstPersonEyeWorldY(Transform t)
        {
            if (t == null)
                return 1.65f;
            var cc = t.GetComponent<CharacterController>();
            if (cc != null)
            {
                var half = cc.height * 0.5f;
                var bottomLocal = cc.center - new Vector3(0f, half, 0f);
                var topLocal = cc.center + new Vector3(0f, half, 0f);
                var bottomY = t.TransformPoint(bottomLocal).y;
                var topY = t.TransformPoint(topLocal).y;
                if (bottomY > topY)
                    (bottomY, topY) = (topY, bottomY);
                var extent = Mathf.Max(0.25f, topY - bottomY);
                return bottomY + extent * Mathf.Clamp01(firstPersonHeightFactor);
            }

            if (TryGetRenderersWorldVerticalExtents(t, out var minY, out var maxY))
            {
                if (minY > maxY)
                    (minY, maxY) = (maxY, minY);
                var extent = Mathf.Max(0.25f, maxY - minY);
                return minY + extent * Mathf.Clamp01(firstPersonHeightFactor);
            }

            var fallbackH = Mathf.Max(0.25f, ComputeTargetHeight(t));
            return t.position.y + fallbackH * Mathf.Clamp01(firstPersonHeightFactor);
        }

        static bool TryGetRenderersWorldVerticalExtents(Transform t, out float minY, out float maxY)
        {
            minY = 0f;
            maxY = 0f;
            var renderers = t.GetComponentsInChildren<Renderer>(true);
            var has = false;
            foreach (var r in renderers)
            {
                if (r == null || !r.enabled)
                    continue;
                var b = r.bounds;
                if (!has)
                {
                    minY = b.min.y;
                    maxY = b.max.y;
                    has = true;
                }
                else
                {
                    minY = Mathf.Min(minY, b.min.y);
                    maxY = Mathf.Max(maxY, b.max.y);
                }
            }

            return has;
        }

        static float ComputeTargetHeight(Transform t)
        {
            if (t == null)
                return 1f;
            var cc = t.GetComponent<CharacterController>();
            if (cc != null)
                return Mathf.Max(0.25f, cc.height * t.lossyScale.y);

            var renderers = t.GetComponentsInChildren<Renderer>(true);
            var has = false;
            var bounds = default(Bounds);
            foreach (var r in renderers)
            {
                if (r == null || !r.enabled)
                    continue;
                if (!has)
                {
                    bounds = r.bounds;
                    has = true;
                }
                else
                {
                    bounds.Encapsulate(r.bounds);
                }
            }

            if (!has)
                return 1f;
            return Mathf.Max(0.25f, bounds.size.y);
        }
    }
}
