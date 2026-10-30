using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace WowSandbox.EditorTools
{
    /// <summary>
    /// Keeps a sunk wreck's extras where they belong when the wreck is moved or turned by hand: the
    /// floating wreckage back on the surface and level, the bubbles back on top of the hull pointing
    /// up, the fish school's home over the hull. They're children of the wreck's group (so they go
    /// with it), which otherwise carries them into the sky or tips them over with the hull.
    /// </summary>
    [InitializeOnLoad]
    static class SunkWreckExtras
    {
        const string BubblesName = "Bubbles";
        const string SchoolName = "Fish School";
        const string FloatingName = "Floating Wreckage";

        static SunkWreckExtras() => ObjectChangeEvents.changesPublished += OnChanges;

        static bool _settling;

        static void OnChanges(ref ObjectChangeEventStream stream)
        {
            if (_settling || EditorApplication.isPlayingOrWillChangePlaymode)
                return;
            for (int i = 0; i < stream.length; i++)
            {
                if (stream.GetEventType(i) != ObjectChangeKind.ChangeGameObjectOrComponentProperties)
                    continue;
                stream.GetChangeGameObjectOrComponentPropertiesEvent(i, out var change);
                var changed = EditorUtility.EntityIdToObject(change.entityId);
                var go = changed as GameObject ?? (changed as Component)?.gameObject;
                var group = go != null ? SunkGroup(go.transform) : null;
                if (group != null)
                    Settle(group);
            }
        }

        [MenuItem("WoW Sandbox/Settle Sunk Wrecks")]
        static void SettleAll()
        {
            int count = 0;
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
            {
                if (t.name.Contains(" - Sunk ") && SunkGroup(t.parent) == null)
                {
                    Settle(t);
                    count++;
                }
            }
            Debug.Log($"[SunkWreckExtras] Settled the extras round {count} sunk wreck(s). Save the scene to keep it.");
        }

        /// <summary>
        /// Makes the selected sunk wreck Sprocket's chartered ship (docs/LORE.md): named "Shipwreck -
        /// Sunk Pale Lament", with the Captain's thought on first swimming up to her (ThoughtSpot).
        /// </summary>
        [MenuItem("WoW Sandbox/Make Selected Sunk Wreck the Pale Lament")]
        static void MakePaleLament()
        {
            var group = Selection.activeTransform != null ? SunkGroup(Selection.activeTransform) : null;
            if (group == null)
            {
                Debug.LogError("[SunkWreckExtras] Select a sunk wreck (Add Shipwreck, Where = Sunk) or anything in it first.");
                return;
            }
            Undo.RecordObject(group.gameObject, "Make Pale Lament");
            group.name = "Shipwreck - Sunk Pale Lament";
            if (group.GetComponent<ThoughtSpot>() == null)
                Undo.AddComponent<ThoughtSpot>(group.gameObject);
            EditorSceneManager.MarkSceneDirty(group.gameObject.scene);
            Debug.Log("[SunkWreckExtras] That's the Pale Lament now, with the Captain's thought. Save the scene to keep it.", group);
        }

        /// <summary>The sunk wreck group <paramref name="t"/> is or is in, if any.</summary>
        static Transform SunkGroup(Transform t)
        {
            for (; t != null; t = t.parent)
                if (t.name.Contains(" - Sunk "))
                    return t;
            return null;
        }

        /// <summary>Puts the extras of the sunk wreck <paramref name="group"/> back round its hull.</summary>
        public static void Settle(Transform group)
        {
            var water = Object.FindAnyObjectByType<WaterVolume>();
            if (water == null || !HullBounds(group, out var hull))
                return;

            _settling = true;
            try
            {
                var bubbles = group.Find(BubblesName);
                if (bubbles != null)
                    Put(bubbles, new Vector3(hull.center.x, Mathf.Min(hull.max.y, water.SurfaceY - 1f), hull.center.z), Quaternion.identity);

                var school = group.Find(SchoolName);
                if (school != null)
                    Put(school, new Vector3(hull.center.x, Mathf.Min(school.position.y, water.SurfaceY - 1f), hull.center.z), Level(school));

                // Where it is across the water, but on the surface and floating flat.
                var floating = group.Find(FloatingName);
                if (floating != null)
                    Put(floating, new Vector3(floating.position.x, water.SurfaceY, floating.position.z), Level(floating));
            }
            finally
            {
                _settling = false;
            }
        }

        static void Put(Transform t, Vector3 position, Quaternion rotation)
        {
            if ((t.position - position).sqrMagnitude < 0.0001f && Quaternion.Angle(t.rotation, rotation) < 0.01f)
                return;
            Undo.RecordObject(t, "Settle Sunk Wreck");
            t.SetPositionAndRotation(position, rotation);
        }

        static Quaternion Level(Transform t) => Quaternion.Euler(0f, t.eulerAngles.y, 0f);

        /// <summary>The wreck's own bounds, leaving out its extras.</summary>
        static bool HullBounds(Transform group, out Bounds bounds)
        {
            bounds = default;
            bool any = false;
            foreach (Transform child in group)
            {
                if (child.name == BubblesName || child.name == SchoolName || child.name == FloatingName)
                    continue;
                foreach (var renderer in child.GetComponentsInChildren<Renderer>())
                {
                    if (!any)
                        bounds = renderer.bounds;
                    else
                        bounds.Encapsulate(renderer.bounds);
                    any = true;
                }
            }
            return any;
        }
    }
}
