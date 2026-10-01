using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace WowSandbox.EditorTools
{
    /// <summary>
    /// Drops a StormWeather into the scene. Everything else it needs — sun, sky, water,
    /// camera — it finds for itself at Play time, so there's nothing to wire up here and
    /// regenerating any of those parts doesn't leave it pointing at a deleted object.
    /// </summary>
    public static class WeatherSetup
    {
        [MenuItem("WoW Sandbox/Add Storm Weather")]
        public static void AddStormWeather()
        {
            var existing = Object.FindFirstObjectByType<StormWeather>();
            if (existing != null)
            {
                Selection.activeGameObject = existing.gameObject;
                Debug.Log("[WeatherSetup] The scene already has a StormWeather — selected it.", existing);
                return;
            }

            var go = new GameObject("StormWeather");
            var weather = go.AddComponent<StormWeather>();

            // Start from the waves' wind, so adding weather doesn't swing the swell around.
            var waves = Object.FindFirstObjectByType<WaterWaves>();
            if (waves != null)
                weather.windDirection = waves.windDirection;

            Undo.RegisterCreatedObjectUndo(go, "Add Storm Weather");
            Selection.activeGameObject = go;

            EditorSceneManager.MarkSceneDirty(go.scene);
            if (Application.isPlaying)
                Debug.LogWarning("[WeatherSetup] Added during Play mode — Unity discards it on exit. " +
                                 "Add it in Edit mode to keep it.");

            Debug.Log("[WeatherSetup] Added StormWeather. Press Play: the storm builds from " +
                      "light to full over a minute.", go);
        }
    }
}
