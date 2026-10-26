using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace WowSandbox
{
    /// <summary>
    /// The character sheet's paper doll, as in WoW: a copy of the player's model — wearing
    /// what he wears, weapon in hand or put away — standing idle on a little stage far below
    /// the world, filmed by a camera of its own into the sheet. Drag on it to turn him round.
    /// The copy is only the meshes, bones and Animator (no scripts, lights or colliders), made
    /// on Show and again whenever his gear changes, and gone again on Hide.
    /// </summary>
    public class CharacterPreview
    {
        // An unnamed layer only the preview camera draws, so it sees nothing of the world.
        const int StageLayer = 31;
        static readonly Vector3 StagePosition = new(0f, -2000f, 0f);
        static readonly Color Backdrop = new(0.09f, 0.08f, 0.07f);
        const float FieldOfView = 30f;
        const float TurnPerPixel = 0.5f;

        static readonly int GroundedHash = Animator.StringToHash("Grounded");

        readonly RenderTexture _texture;
        readonly Camera _camera;
        readonly Transform _stage;
        readonly Transform _turntable;
        // Where the copy is made, switched off, so none of its scripts wake before they're stripped.
        readonly Transform _workshop;
        readonly WowCharacterController _player;
        readonly EquipmentVisuals _visuals;

        GameObject _copy;
        float _yaw;
        int _dirtyFrame = -1;
        bool _sheathed, _holding;

        public CharacterPreview(RawImage picture, Vector2 size, WowCharacterController player, EquipmentVisuals visuals)
        {
            _player = player;
            _visuals = visuals;

            _texture = new RenderTexture(Mathf.RoundToInt(size.x * 2f), Mathf.RoundToInt(size.y * 2f), 24)
                { name = "Character Preview" };
            picture.texture = _texture;
            picture.color = Color.white;
            picture.gameObject.AddComponent<PreviewDrag>().Dragged = dx => _yaw -= dx * TurnPerPixel;

            _stage = new GameObject("Character Preview").transform;
            _stage.position = StagePosition;
            _turntable = new GameObject("Turntable").transform;
            _turntable.SetParent(_stage, false);
            _workshop = new GameObject("Workshop").transform;
            _workshop.SetParent(_stage, false);
            _workshop.gameObject.SetActive(false);

            var cameraObject = new GameObject("Preview Camera");
            cameraObject.transform.SetParent(_stage, false);
            _camera = cameraObject.AddComponent<Camera>();
            _camera.targetTexture = _texture;
            _camera.fieldOfView = FieldOfView;
            _camera.nearClipPlane = 0.05f;
            _camera.farClipPlane = 20f;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = Backdrop;
            _camera.cullingMask = 1 << StageLayer;
            // The world sits between the sun and the stage: with shadows on he'd stand in the island's.
            var data = _camera.GetUniversalAdditionalCameraData();
            data.renderShadows = false;
            data.renderPostProcessing = false;
            _camera.enabled = false;

            // A key light and a fill, short-ranged, so he's lit at midnight too and nothing above is touched.
            Lamp("Key Light", new Vector3(1.4f, 2.4f, 2.2f), 2.2f);
            Lamp("Fill Light", new Vector3(-1.8f, 1.2f, 1.6f), 0.9f);

            RenderPipelineManager.beginCameraRendering += BeforeRender;
            RenderPipelineManager.endCameraRendering += AfterRender;
        }

        // Fog is the whole scene's (sea mist, the murk underwater); the doll is drawn without it.
        bool _fogWas;

        void BeforeRender(ScriptableRenderContext context, Camera camera)
        {
            if (camera != _camera)
                return;
            _fogWas = RenderSettings.fog;
            RenderSettings.fog = false;
        }

        void AfterRender(ScriptableRenderContext context, Camera camera)
        {
            if (camera == _camera)
                RenderSettings.fog = _fogWas;
        }

        void Lamp(string name, Vector3 position, float intensity)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_stage, false);
            go.transform.localPosition = position;
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 8f;
            light.intensity = intensity;
            light.color = new Color(1f, 0.95f, 0.88f);
            light.shadows = LightShadows.None;
            light.cullingMask = 1 << StageLayer;
        }

        public void Show()
        {
            // The view you play in never draws the stage, should it ever look that far down.
            var main = Camera.main;
            if (main != null)
                main.cullingMask &= ~(1 << StageLayer);
            Rebuild();
            _camera.enabled = true;
        }

        public void Hide()
        {
            _camera.enabled = false;
            if (_copy != null)
                UnityEngine.Object.Destroy(_copy);
            _copy = null;
        }

        /// <summary>His gear changed: dress the copy again next frame, once the old pieces are really gone.</summary>
        public void MarkDirty()
        {
            if (_dirtyFrame < 0)
                _dirtyFrame = Time.frameCount;
        }

        /// <summary>Each frame while shown: turn him, and remake him when his looks have changed.</summary>
        public void Tick()
        {
            // Putting the weapon away (Z) or taking out the fishing pole moves pieces without a gear change.
            if (_visuals != null && (_visuals.Sheathed != _sheathed || _visuals.Holding != _holding))
            {
                _sheathed = _visuals.Sheathed;
                _holding = _visuals.Holding;
                MarkDirty();
            }
            if (_dirtyFrame >= 0 && Time.frameCount > _dirtyFrame)
                Rebuild();
            _turntable.localRotation = Quaternion.Euler(0f, _yaw, 0f);
        }

        public void Dispose()
        {
            RenderPipelineManager.beginCameraRendering -= BeforeRender;
            RenderPipelineManager.endCameraRendering -= AfterRender;
            if (_stage != null)
                UnityEngine.Object.Destroy(_stage.gameObject);
            if (_texture != null)
                _texture.Release();
        }

        void Rebuild()
        {
            _dirtyFrame = -1;
            if (_visuals != null)
            {
                _sheathed = _visuals.Sheathed;
                _holding = _visuals.Holding;
            }
            if (_copy != null)
                UnityEngine.Object.Destroy(_copy);
            _copy = null;

            var model = _visuals != null && _visuals.model != null ? _visuals.model
                      : _player != null ? _player.modelRoot : null;
            if (model == null)
                return;

            _copy = UnityEngine.Object.Instantiate(model.gameObject, _workshop);
            _copy.name = "Preview of " + model.name;
            Strip(_copy);
            foreach (var t in _copy.GetComponentsInChildren<Transform>(true))
                t.gameObject.layer = StageLayer;

            // Stood as the model stands under him at rest, without any swimming lean.
            _copy.transform.SetParent(_turntable, false);
            _copy.transform.localPosition = _player != null ? _player.ModelRestPosition : Vector3.zero;
            _copy.transform.localRotation = _player != null ? _player.ModelRestRotation : model.localRotation;
            _copy.transform.localScale = model.localScale;

            var animator = _copy.GetComponent<Animator>() ?? _copy.GetComponentInChildren<Animator>();
            if (animator != null)
            {
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                foreach (var parameter in animator.parameters)
                    if (parameter.nameHash == GroundedHash)
                        animator.SetBool(GroundedHash, true);
            }

            Frame();
        }

        /// <summary>Takes everything off the copy but how it looks and moves.</summary>
        static void Strip(GameObject copy)
        {
            // Scripts first: some require the colliders and lights below.
            foreach (var behaviour in copy.GetComponentsInChildren<MonoBehaviour>(true))
                UnityEngine.Object.DestroyImmediate(behaviour);
            foreach (var component in copy.GetComponentsInChildren<Component>(true))
            {
                if (component is Collider or Rigidbody or Light or AudioSource or CharacterController)
                    UnityEngine.Object.DestroyImmediate(component);
            }
        }

        /// <summary>Stands the camera in front of him, his whole height in the picture.</summary>
        void Frame()
        {
            // The body's skinned meshes, not a weapon or a torch's flames held out.
            float height = 1.9f, middle = 0.95f;
            var renderers = _copy.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (renderers.Length > 0)
            {
                var bounds = renderers[0].bounds;
                foreach (var renderer in renderers)
                    bounds.Encapsulate(renderer.bounds);
                height = Mathf.Clamp(bounds.size.y, 0.5f, 4f);
                middle = bounds.center.y - _stage.position.y;
            }

            float distance = height * 0.56f / Mathf.Tan(FieldOfView * 0.5f * Mathf.Deg2Rad);
            var look = Vector3.up * middle;
            _camera.transform.localPosition = look + Vector3.forward * distance;
            _camera.transform.localRotation = Quaternion.LookRotation(-Vector3.forward, Vector3.up);
        }

        /// <summary>Turns drags on the picture into turns of the doll.</summary>
        class PreviewDrag : MonoBehaviour, IBeginDragHandler, IDragHandler
        {
            public Action<float> Dragged;

            public void OnBeginDrag(PointerEventData eventData) { }

            public void OnDrag(PointerEventData eventData)
            {
                if (eventData.button == PointerEventData.InputButton.Left)
                    Dragged?.Invoke(eventData.delta.x);
            }
        }
    }
}
