using System.Collections;
using System.Collections.Generic;
using Rpg.Audio;
using Rpg.Core;
using Rpg.Player;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using Object = UnityEngine.Object;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Rpg.UI
{
    /// <summary>
    /// World-space lineup of hero prefabs, camera pan with arrows, magic circle on selection,
    /// Enter or click to confirm with sparks + fade, then reports the chosen prefab.
    /// </summary>
    [DefaultExecutionOrder(-40)]
    public sealed class PlayerCharacterSelectionStage : MonoBehaviour
    {
        const float RowSpacingMeters = 2.85f;
        const float CameraDistance = 8.4f;
        const float CameraHeight = 1.35f;
        const float PanSmoothTime = 0.32f;
        const float CameraRotateSmooth = 10f;
        const float FadeSeconds = 0.55f;
        /// <summary>Local Y offset under the preview root (same glue pattern as <see cref="PlayerSpawnLightningAura"/>).</summary>
        const float MagicCircleLocalYOffset = 0.04f;

        const string DefaultMagicCircleAssetPath =
            "Assets/Hovl Studio/Magic effects pack/Prefabs/Magic circles/Magic circle 2.prefab";
        const string DefaultSparksAssetPath =
            "Assets/Hovl Studio/Magic effects pack/Prefabs/Sparks/Sparks flashing blue.prefab";

        GameObject _magicCirclePrefab;
        GameObject _sparksPrefab;
        RuntimeLevelBootstrap _bootstrap;

        readonly List<GameObject> _prefabs = new List<GameObject>();
        readonly List<GameObject> _instances = new List<GameObject>();

        Camera _selectionCam;
        readonly List<Camera> _disabledMainCameras = new List<Camera>();
        GameObject _hiddenPlayer;

        GameObject _floorGo;
        float _floorTopY;
        Light _selectionKeyLight;
        Light _selectionFillLight;

        int _index;
        Vector3 _camPosVelocity;
        GameObject _magicCircleInstance;
        bool _finished;
        GameObject _fadeCanvas;
        Image _fadeImage;
        GameObject _titleCanvas;
        AudioSource _uiSfxSource;
        AudioClip _scrollSfx;
        AudioClip _confirmSfx;

        public GameObject SelectedPrefab { get; private set; }

        public void ConfigureVfx(GameObject magicCirclePrefab, GameObject sparksPrefab, RuntimeLevelBootstrap bootstrap)
        {
            _magicCirclePrefab = magicCirclePrefab ?? LoadDefaultPrefab(DefaultMagicCircleAssetPath);
            _sparksPrefab = sparksPrefab ?? LoadDefaultPrefab(DefaultSparksAssetPath);
            _bootstrap = bootstrap;
        }

        static GameObject LoadDefaultPrefab(string assetPath)
        {
#if UNITY_EDITOR
            return AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
#else
            return null;
#endif
        }

        public IEnumerator RunSelection(IReadOnlyList<GameObject> prefabChoices)
        {
            _finished = false;
            SelectedPrefab = null;
            _prefabs.Clear();
            _instances.Clear();
            if (prefabChoices == null || prefabChoices.Count == 0)
            {
                _finished = true;
                yield break;
            }
            foreach (var p in prefabChoices)
            {
                if (p != null)
                    _prefabs.Add(p);
            }
            if (_prefabs.Count == 0)
            {
                _finished = true;
                yield break;
            }

            _hiddenPlayer = GameObject.FindGameObjectWithTag(GameConstants.PlayerTag);
            if (_hiddenPlayer != null)
                _hiddenPlayer.SetActive(false);

            CreateSelectionCamera();
            DisableOtherCamerasExceptSelection();
            BuildSelectionFloorAndRow();
            _index = 0;
            EnsureMagicCircleAttachedToSelection();
            EnsureTitleBanner();
            EnsureSelectionLights();
            EnsureSelectionUiSfx();
            SnapCameraToIndex(true);

            while (!_finished)
            {
                if (!_confirming)
                    PollSelectionInput();
                yield return null;
            }

            TeardownVisuals();
            RestoreDisabledCameras();
            if (_hiddenPlayer != null)
            {
                // Replaced during confirm; stale reference.
                _hiddenPlayer = null;
            }
        }

        void PollSelectionInput()
        {
            if (_selectionCam == null || _instances.Count == 0)
                return;

            // Screen-left / screen-right vs row order: with camera on +Z, row +X reads opposite to “move focus right” expectation — swap arrows.
            if (WasLeftArrowPressed())
            {
                _index = (_index + 1) % _instances.Count;
                _camPosVelocity = Vector3.zero;
                ReattachMagicCircleToSelection();
                PlayUiSfx(_scrollSfx);
            }
            else if (WasRightArrowPressed())
            {
                _index = (_index - 1 + _instances.Count) % _instances.Count;
                _camPosVelocity = Vector3.zero;
                ReattachMagicCircleToSelection();
                PlayUiSfx(_scrollSfx);
            }
            else if (WasSubmitPressedThisFrame())
            {
                PlayUiSfx(_confirmSfx);
                StartCoroutine(ConfirmSelectionFlow());
            }

            if (Input.GetMouseButtonDown(0))
                TryPickFromRaycast();
        }

        static bool WasSubmitPressedThisFrame()
        {
            if (Input.GetButtonDown("Submit")
                || Input.GetKeyDown(KeyCode.Return)
                || Input.GetKeyDown(KeyCode.KeypadEnter))
                return true;
            var kb = Keyboard.current;
            return kb != null
                && (kb.enterKey.wasPressedThisFrame
                    || kb.numpadEnterKey.wasPressedThisFrame);
        }

        static bool WasLeftArrowPressed()
        {
            if (Input.GetKeyDown(KeyCode.LeftArrow))
                return true;
            var kb = Keyboard.current;
            return kb != null && kb.leftArrowKey.wasPressedThisFrame;
        }

        static bool WasRightArrowPressed()
        {
            if (Input.GetKeyDown(KeyCode.RightArrow))
                return true;
            var kb = Keyboard.current;
            return kb != null && kb.rightArrowKey.wasPressedThisFrame;
        }

        bool _confirming;

        void TryPickFromRaycast()
        {
            if (_selectionCam == null || _confirming)
                return;
            var ray = _selectionCam.ScreenPointToRay(Input.mousePosition);
            if (!Physics.Raycast(ray, out var hit, 200f, ~0, QueryTriggerInteraction.Ignore))
                return;
            var root = hit.collider.transform.root.gameObject;
            for (var i = 0; i < _instances.Count; i++)
            {
                if (_instances[i] != null && (_instances[i] == root || hit.collider.transform.IsChildOf(_instances[i].transform)))
                {
                    _index = i;
                    _camPosVelocity = Vector3.zero;
                    ReattachMagicCircleToSelection();
                    PlayUiSfx(_scrollSfx);
                    StartCoroutine(ConfirmSelectionFlow());
                    return;
                }
            }
        }

        void EnsureSelectionUiSfx()
        {
            if (_uiSfxSource == null)
            {
                _uiSfxSource = gameObject.AddComponent<AudioSource>();
                _uiSfxSource.playOnAwake = false;
                _uiSfxSource.spatialBlend = 0f;
            }
            if (_scrollSfx == null)
                _scrollSfx = RuntimeAudioClipLoader.LoadFromAssetPath("Assets/FREE SOUND PACK_TM(355)/UI(27)/Interface Button 9.wav");
            if (_confirmSfx == null)
                _confirmSfx = RuntimeAudioClipLoader.LoadFromAssetPath("Assets/FREE SOUND PACK_TM(355)/UI(27)/Interface Button 1.wav");
        }

        void PlayUiSfx(AudioClip clip)
        {
            if (_uiSfxSource == null || clip == null)
                return;
            _uiSfxSource.PlayOneShot(clip);
        }

        IEnumerator ConfirmSelectionFlow()
        {
            if (_confirming || _finished || _prefabs.Count == 0 || _index < 0 || _index >= _prefabs.Count)
                yield break;
            _confirming = true;
            var chosen = _prefabs[_index];
            var focus = _instances[_index];
            if (_sparksPrefab != null && focus != null)
            {
                var sparks = Object.Instantiate(_sparksPrefab, focus.transform, false);
                sparks.transform.localPosition = new Vector3(0f, 0.05f, 0f);
                PlayAllParticleSystemsUnder(sparks);
                var ps = sparks.GetComponent<ParticleSystem>();
                if (ps != null)
                {
                    ps.Play(true);
                    Object.Destroy(sparks, ps.main.duration + ps.main.startLifetime.constantMax + 1f);
                }
                else
                    Object.Destroy(sparks, 3f);
            }

            yield return FadeToBlack();
            SelectedPrefab = chosen;

            if (_hiddenPlayer != null)
            {
                var parent = _hiddenPlayer.transform.parent;
                var pos = _hiddenPlayer.transform.position;
                var rot = _hiddenPlayer.transform.rotation;
                Object.Destroy(_hiddenPlayer);
                _hiddenPlayer = null;
                yield return null;
                var newPlayer = Object.Instantiate(chosen, parent);
                newPlayer.name = "Player";
                newPlayer.tag = GameConstants.PlayerTag;
                newPlayer.transform.SetPositionAndRotation(pos, rot);
                if (_bootstrap != null)
                    _bootstrap.WirePlayerAfterCharacterSelection(newPlayer, chosen);
            }

            _finished = true;
            _confirming = false;
        }

        static void PlayAllParticleSystemsUnder(GameObject root)
        {
            if (root == null)
                return;
            foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true))
                ps.Play(true);
        }

        IEnumerator FadeToBlack()
        {
            EnsureFadeCanvas();
            if (_fadeImage == null)
                yield break;
            var c = _fadeImage.color;
            c.a = 0f;
            _fadeImage.color = c;
            var t = 0f;
            while (t < FadeSeconds)
            {
                t += Time.unscaledDeltaTime;
                c.a = Mathf.Clamp01(t / FadeSeconds);
                _fadeImage.color = c;
                yield return null;
            }
            c.a = 1f;
            _fadeImage.color = c;
        }

        void EnsureTitleBanner()
        {
            if (_titleCanvas != null)
                return;
            _titleCanvas = new GameObject("PlayerSelectionTitle");
            _titleCanvas.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
            var canvas = _titleCanvas.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 31990;
            _titleCanvas.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            var textGo = new GameObject("TitleText");
            textGo.transform.SetParent(_titleCanvas.transform, false);
            var text = textGo.AddComponent<Text>();
            text.text = "Select your Avatar";
            text.font = RuntimeUiBuildMaterials.BuiltinUiFont();
            text.fontSize = 34;
            text.color = new Color(0.98f, 0.98f, 1f, 1f);
            text.alignment = TextAnchor.UpperCenter;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            var outline = textGo.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.55f);
            outline.effectDistance = new Vector2(0.75f, -0.75f);
            RuntimeUiBuildMaterials.TryApplyLegacyTextMaterial(text);
            var rt = textGo.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -28f);
            rt.sizeDelta = new Vector2(0f, 56f);
        }

        void EnsureFadeCanvas()
        {
            if (_fadeCanvas != null)
                return;
            _fadeCanvas = new GameObject("PlayerSelectionFade");
            _fadeCanvas.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
            var canvas = _fadeCanvas.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32000;
            _fadeCanvas.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            var imgGo = new GameObject("Black");
            imgGo.transform.SetParent(_fadeCanvas.transform, false);
            _fadeImage = imgGo.AddComponent<Image>();
            _fadeImage.color = new Color(0f, 0f, 0f, 0f);
            RuntimeUiBuildMaterials.TryApplySpriteImageMaterial(_fadeImage);
            var rt = imgGo.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        void LateUpdate()
        {
            if (_finished || _selectionCam == null || _instances.Count == 0)
                return;
            var target = GetCameraLookTargetForIndex(_index);
            // StylizedCharacterPack avatars face roughly -Z at identity; camera on +Z looks toward them = front view.
            var camPos = target + new Vector3(0f, CameraHeight, CameraDistance);
            _selectionCam.transform.position = Vector3.SmoothDamp(
                _selectionCam.transform.position,
                camPos,
                ref _camPosVelocity,
                PanSmoothTime,
                Mathf.Infinity,
                Time.unscaledDeltaTime);
            var lookPoint = target + Vector3.up * 0.9f;
            var desiredRot = Quaternion.LookRotation(lookPoint - _selectionCam.transform.position, Vector3.up);
            var dt = Time.unscaledDeltaTime;
            var t = 1f - Mathf.Exp(-CameraRotateSmooth * dt);
            _selectionCam.transform.rotation = Quaternion.Slerp(_selectionCam.transform.rotation, desiredRot, t);
        }

        Vector3 GetCameraLookTargetForIndex(int i)
        {
            if (i < 0 || i >= _instances.Count || _instances[i] == null)
                return transform.position;
            return _instances[i].transform.position + Vector3.up * 0.15f;
        }

        void SnapCameraToIndex(bool instant)
        {
            if (_selectionCam == null || _instances.Count == 0)
                return;
            var target = GetCameraLookTargetForIndex(_index);
            var camPos = target + new Vector3(0f, CameraHeight, CameraDistance);
            if (instant)
            {
                _selectionCam.transform.position = camPos;
                var lookPoint = target + Vector3.up * 0.9f;
                _selectionCam.transform.rotation = Quaternion.LookRotation(lookPoint - camPos, Vector3.up);
                _camPosVelocity = Vector3.zero;
            }
        }

        void BuildSelectionFloorAndRow()
        {
            var rowCenter = ComputeRowCenterOnGround();
            BuildFloorUnderRow(rowCenter);

            var n = _prefabs.Count;
            var offset = -0.5f * (n - 1) * RowSpacingMeters;
            for (var i = 0; i < n; i++)
            {
                var pf = _prefabs[i];
                var inst = Object.Instantiate(pf, transform);
                inst.name = $"SelectionPreview_{pf.name}";
                var x = rowCenter.x + offset + i * RowSpacingMeters;
                var pos = new Vector3(x, _floorTopY + 0.02f, rowCenter.z);
                inst.transform.SetPositionAndRotation(pos, Quaternion.identity);
                if (_bootstrap != null)
                    _bootstrap.ApplyStylizedPlayerScaleIfNeeded(inst, pf, _bootstrap.StylizedPlayerScaleMultiplier);
                foreach (var cc in inst.GetComponentsInChildren<CharacterController>(true))
                    cc.enabled = false;
                StylizedNpcAnimatorDriver.TryAdd(inst, forceIdle: true);
                foreach (var anim in inst.GetComponentsInChildren<Animator>(true))
                    anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                RuntimeLevelBootstrap.LiftCharacterFeetAboveGround(inst, 0.12f);
                _instances.Add(inst);
            }
        }

        void BuildFloorUnderRow(Vector3 rowCenterOnGround)
        {
            if (_floorGo != null)
                return;
            _floorGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _floorGo.name = "PlayerSelectionFloor";
            _floorGo.transform.SetParent(transform, false);
            var halfThickness = 0.06f;
            var width = Mathf.Max(42f, _prefabs.Count * RowSpacingMeters + 10f);
            var depth = 14f;
            _floorGo.transform.localScale = new Vector3(width, halfThickness * 2f, depth);
            // Top of cube flush with sampled ground.
            _floorTopY = rowCenterOnGround.y;
            _floorGo.transform.position = new Vector3(rowCenterOnGround.x, _floorTopY - halfThickness, rowCenterOnGround.z);
            var col = _floorGo.GetComponent<BoxCollider>();
            if (col != null)
                col.contactOffset = 0.01f;
            var renderer = _floorGo.GetComponent<Renderer>();
            if (renderer != null)
                renderer.enabled = false;
        }

        Vector3 ComputeRowCenterOnGround()
        {
            var basePos = transform.position;
            if (TryTerrainCenter(out var center))
                basePos = center + new Vector3(140f, 0f, 40f);
            var probe = new Vector3(basePos.x, 800f, basePos.z);
            if (Physics.Raycast(probe, Vector3.down, out var hit, 2000f, ~0, QueryTriggerInteraction.Ignore))
                return new Vector3(basePos.x, hit.point.y, basePos.z);
            return basePos;
        }

        static bool TryTerrainCenter(out Vector3 center)
        {
            center = default;
            foreach (var t in Object.FindObjectsByType<Terrain>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (t == null || t.terrainData == null)
                    continue;
                if (!string.Equals(t.gameObject.name, "Terrain", System.StringComparison.OrdinalIgnoreCase))
                    continue;
                var o = t.transform.position;
                var s = t.terrainData.size;
                center = o + new Vector3(s.x * 0.5f, 0f, s.z * 0.5f);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Same glue pattern as <see cref="PlayerSpawnLightningAura"/>: instantiate VFX as a child of the character root
        /// with local position/rotation identity (small Y offset), then ensure particle systems are playing.
        /// </summary>
        void EnsureMagicCircleAttachedToSelection()
        {
            if (_magicCirclePrefab == null || _instances.Count == 0 || _index < 0 || _index >= _instances.Count)
                return;
            var parent = _instances[_index];
            if (parent == null)
                return;
            if (_magicCircleInstance == null)
            {
                _magicCircleInstance = Object.Instantiate(_magicCirclePrefab, parent.transform, false);
                _magicCircleInstance.name = _magicCirclePrefab.name + "_Selection";
            }
            else
                _magicCircleInstance.transform.SetParent(parent.transform, false);

            _magicCircleInstance.transform.localPosition = new Vector3(0f, MagicCircleLocalYOffset, 0f);
            _magicCircleInstance.transform.localRotation = Quaternion.identity;
            _magicCircleInstance.transform.localScale = Vector3.one;
            StartParticleSystemsLikeIntroAura(_magicCircleInstance);
        }

        void ReattachMagicCircleToSelection()
        {
            if (_magicCirclePrefab == null || _instances.Count == 0 || _index < 0 || _index >= _instances.Count)
                return;
            var parent = _instances[_index];
            if (parent == null)
                return;
            if (_magicCircleInstance == null)
            {
                EnsureMagicCircleAttachedToSelection();
                return;
            }

            _magicCircleInstance.transform.SetParent(parent.transform, false);
            _magicCircleInstance.transform.localPosition = new Vector3(0f, MagicCircleLocalYOffset, 0f);
            _magicCircleInstance.transform.localRotation = Quaternion.identity;
            _magicCircleInstance.transform.localScale = Vector3.one;
            StartParticleSystemsLikeIntroAura(_magicCircleInstance);
        }

        static void StartParticleSystemsLikeIntroAura(GameObject auraRoot)
        {
            if (auraRoot == null)
                return;
            foreach (var ps in auraRoot.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (ps == null)
                    continue;
                var r = ps.GetComponent<ParticleSystemRenderer>();
                if (r != null)
                {
                    r.enabled = true;
                    r.sortingFudge = -60f;
                }
                if (!ps.isPlaying)
                    ps.Play(true);
            }
        }

        void EnsureSelectionLights()
        {
            if (_selectionKeyLight == null)
            {
                var keyGo = new GameObject("PlayerSelectionKeyLight");
                keyGo.transform.SetParent(transform, false);
                keyGo.transform.rotation = Quaternion.Euler(47f, -28f, 0f);
                _selectionKeyLight = keyGo.AddComponent<Light>();
                _selectionKeyLight.type = LightType.Directional;
                _selectionKeyLight.intensity = 2.2f;
                _selectionKeyLight.color = new Color(1f, 0.98f, 0.94f, 1f);
                _selectionKeyLight.shadows = LightShadows.Soft;
            }

            if (_selectionFillLight == null)
            {
                var fillGo = new GameObject("PlayerSelectionFillLight");
                fillGo.transform.SetParent(transform, false);
                fillGo.transform.rotation = Quaternion.Euler(32f, 146f, 0f);
                _selectionFillLight = fillGo.AddComponent<Light>();
                _selectionFillLight.type = LightType.Directional;
                _selectionFillLight.intensity = 1.1f;
                _selectionFillLight.color = new Color(0.72f, 0.8f, 1f, 1f);
                _selectionFillLight.shadows = LightShadows.None;
            }
        }

        void CreateSelectionCamera()
        {
            var go = new GameObject("PlayerSelectionCamera");
            go.transform.SetParent(transform, false);
            _selectionCam = go.AddComponent<Camera>();
            _selectionCam.clearFlags = CameraClearFlags.SolidColor;
            _selectionCam.backgroundColor = new Color(0.04f, 0.045f, 0.06f, 1f);
            _selectionCam.nearClipPlane = 0.1f;
            _selectionCam.farClipPlane = 250f;
            _selectionCam.fieldOfView = 38f;
            _selectionCam.depth = 80f;
            _selectionCam.cullingMask = ~0;
        }

        void DisableOtherCamerasExceptSelection()
        {
            _disabledMainCameras.Clear();
            foreach (var c in Object.FindObjectsByType<Camera>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (c == null || c == _selectionCam || !c.enabled)
                    continue;
                c.enabled = false;
                _disabledMainCameras.Add(c);
            }
        }

        void RestoreDisabledCameras()
        {
            foreach (var c in _disabledMainCameras)
            {
                if (c != null)
                    c.enabled = true;
            }
            _disabledMainCameras.Clear();
            if (_selectionCam != null)
            {
                Object.Destroy(_selectionCam.gameObject);
                _selectionCam = null;
            }
        }

        void TeardownVisuals()
        {
            if (_titleCanvas != null)
            {
                Object.Destroy(_titleCanvas);
                _titleCanvas = null;
            }
            if (_fadeCanvas != null)
            {
                Object.Destroy(_fadeCanvas);
                _fadeCanvas = null;
                _fadeImage = null;
            }
            if (_magicCircleInstance != null)
            {
                Object.Destroy(_magicCircleInstance);
                _magicCircleInstance = null;
            }
            if (_floorGo != null)
            {
                Object.Destroy(_floorGo);
                _floorGo = null;
            }
            if (_selectionKeyLight != null)
            {
                Object.Destroy(_selectionKeyLight.gameObject);
                _selectionKeyLight = null;
            }
            if (_selectionFillLight != null)
            {
                Object.Destroy(_selectionFillLight.gameObject);
                _selectionFillLight = null;
            }
            foreach (var inst in _instances)
            {
                if (inst != null)
                    Object.Destroy(inst);
            }
            _instances.Clear();
            _prefabs.Clear();
        }
    }
}
