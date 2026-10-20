using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace WowSandbox.EditorTools
{
    /// <summary>
    /// WoW Sandbox → Preview Night Lights: shows in Edit mode the lights NightLight gives the
    /// scene's lanterns and torches at Play — same places, brightness and reach — and dims the
    /// sun so they show up, for placing lanterns without pressing Play. Toggle it off to put
    /// things back.
    ///
    /// Nothing of it is saved. The preview lights are DontSave — listed in the Hierarchy under
    /// "Night Light Preview (not saved)", one per source, so you can select one and check where
    /// it sits, but never written to the scene. They follow their lanterns. Drag one, or change
    /// its Intensity, Range or Color in the Inspector, and its lantern keeps it: a NightLight is
    /// added to it (if it hasn't one) holding the Light Offset, Intensity, Range and Color, which
    /// is what Play uses (undoable; zero an offset, intensity or range to go back to automatic). They're destroyed before Play, so they can't leak into it. The sun and ambient
    /// are put back before every scene save (and dimmed again after), before Play, and on a
    /// script reload, which would otherwise forget what to put back.
    /// </summary>
    [InitializeOnLoad]
    static class NightLightPreview
    {
        const string MenuPath = "WoW Sandbox/Preview Night Lights";
        const string RootName = "Night Light Preview (not saved)";
        // SessionState survives a script reload; a static field doesn't.
        const string OnKey = "WowSandbox.NightLightPreview.On";
        const string SunKey = "WowSandbox.NightLightPreview.SunIntensity";
        const string AmbientKey = "WowSandbox.NightLightPreview.AmbientIntensity";

        static readonly List<(Light light, Transform source, NightLight.Kind kind)> _lights = new();
        // What Follow last set on each light: anything different, and it's been edited.
        static readonly Dictionary<Light, (Vector3 position, float intensity, float range, Color color)> _placed = new();

        static bool On
        {
            get => SessionState.GetBool(OnKey, false);
            set => SessionState.SetBool(OnKey, value);
        }

        static NightLightPreview()
        {
            // After a script reload the old preview lights are orphans and the sun is still
            // dimmed: clear up, then carry on if it was on.
            EditorApplication.delayCall += () =>
            {
                DestroyPreviewLights();
                RestoreDaylight();
                if (On && !EditorApplication.isPlayingOrWillChangePlaymode)
                    Show();
            };
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.ExitingEditMode && On)
                    Hide();
                else if (state == PlayModeStateChange.EnteredEditMode && On)
                    Show();
            };
            EditorSceneManager.sceneSaving += (_, _) => { if (On) RestoreDaylight(); };
            EditorSceneManager.sceneSaved += _ => { if (On) DimDaylight(); };
            EditorSceneManager.sceneOpened += (_, _) => { if (On) Show(); };
            EditorApplication.update += Follow;
        }

        [MenuItem(MenuPath)]
        static void Toggle()
        {
            On = !On;
            if (On)
                Show();
            else
                Hide();
        }

        [MenuItem(MenuPath, true)]
        static bool CanToggle()
        {
            Menu.SetChecked(MenuPath, On);
            return !EditorApplication.isPlaying;
        }

        static void Show()
        {
            Hide();
            var root = new GameObject(RootName) { hideFlags = HideFlags.DontSave };
            foreach (var (source, kind) in NightLight.FindSources())
            {
                var go = new GameObject($"Light — {source.name}") { hideFlags = HideFlags.DontSave };
                go.transform.SetParent(root.transform, true);
                var light = go.AddComponent<Light>();
                light.type = LightType.Point;
                light.renderMode = LightRenderMode.ForcePixel;
                light.shadows = LightShadows.None;
                _lights.Add((light, source, kind));
            }
            Follow();
            DimDaylight();
            // First in the Hierarchy, and flashed there, so it's easy to find among the island's objects.
            root.transform.SetAsFirstSibling();
            EditorApplication.delayCall += () => { if (root != null) EditorGUIUtility.PingObject(root); };
            SceneView.RepaintAll();
            Debug.Log($"[NightLightPreview] Showing {_lights.Count} night light(s). Toggle WoW Sandbox → Preview Night Lights to put the daylight back.");
        }

        static void Hide()
        {
            DestroyPreviewLights();
            RestoreDaylight();
            SceneView.RepaintAll();
        }

        /// <summary>Keeps each light where NightLight would put it, so moving a lantern moves its light.</summary>
        static void Follow()
        {
            if (_lights.Count == 0)
                return;
            foreach (var (light, source, kind) in _lights)
            {
                if (light == null || source == null)
                    continue;
                var settings = source.GetComponent<NightLight>();

                if (_placed.TryGetValue(light, out var placed))
                    settings = Remember(source, kind, settings, light, placed);

                var (intensity, range) = settings != null ? settings.Brightness() : NightLight.Defaults(kind, source.name);
                light.transform.position = settings != null ? settings.LightPoint() : NightLight.ModelLightPoint(source, kind);
                light.intensity = intensity;
                light.range = range;
                light.color = settings != null ? settings.color : new Color(1f, 0.62f, 0.3f);
                _placed[light] = (light.transform.position, light.intensity, light.range, light.color);
            }
        }

        /// <summary>
        /// Whatever's been changed on a preview light since Follow set it — moved, or its intensity,
        /// range or colour edited — goes onto its lantern's NightLight. Returns that NightLight
        /// (null if nothing changed and it had none).
        /// </summary>
        static NightLight Remember(Transform source, NightLight.Kind kind, NightLight settings, Light light,
                                   (Vector3 position, float intensity, float range, Color color) placed)
        {
            bool moved = (light.transform.position - placed.position).sqrMagnitude > 1e-8f;
            bool brighter = !Mathf.Approximately(light.intensity, placed.intensity);
            bool reach = !Mathf.Approximately(light.range, placed.range);
            bool tint = light.color != placed.color;
            if (!moved && !brighter && !reach && !tint)
                return settings;

            if (settings == null)
            {
                settings = Undo.AddComponent<NightLight>(source.gameObject);
                settings.kind = kind;
            }
            Undo.RecordObject(settings, "Adjust Night Light");
            if (moved)
                settings.lightOffset = source.InverseTransformPoint(light.transform.position);
            if (brighter)
                settings.intensity = Mathf.Max(light.intensity, 0.01f);
            if (reach)
                settings.range = Mathf.Max(light.range, 0.01f);
            if (tint)
                settings.color = light.color;
            // The ship's lanterns are parts of its prefab; this keeps the change on the instance.
            PrefabUtility.RecordPrefabInstancePropertyModifications(settings);
            EditorSceneManager.MarkSceneDirty(source.gameObject.scene);
            return settings;
        }

        static void DestroyPreviewLights()
        {
            _lights.Clear();
            _placed.Clear();
            // Every root, also ones a script reload has orphaned.
            foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (go != null && go.name == RootName && (go.hideFlags & HideFlags.DontSave) == HideFlags.DontSave)
                    Object.DestroyImmediate(go);
            }
        }

        /// <summary>The sun off and the ambient low — roughly a night — remembering what they were.</summary>
        static void DimDaylight()
        {
            if (SessionState.GetFloat(SunKey, -1f) >= 0f)
                return;
            var sun = StormWeather.FindSun();
            SessionState.SetFloat(SunKey, sun != null ? sun.intensity : 0f);
            SessionState.SetFloat(AmbientKey, RenderSettings.ambientIntensity);
            if (sun != null)
                sun.intensity = 0.05f;
            RenderSettings.ambientIntensity = 0.15f;
        }

        static void RestoreDaylight()
        {
            float sunIntensity = SessionState.GetFloat(SunKey, -1f);
            if (sunIntensity < 0f)
                return;
            var sun = StormWeather.FindSun();
            if (sun != null)
                sun.intensity = sunIntensity;
            RenderSettings.ambientIntensity = SessionState.GetFloat(AmbientKey, 1f);
            SessionState.EraseFloat(SunKey);
            SessionState.EraseFloat(AmbientKey);
        }
    }
}
