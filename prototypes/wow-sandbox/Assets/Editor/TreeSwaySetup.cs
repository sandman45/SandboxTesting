using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace WowSandbox.EditorTools
{
    /// <summary>
    /// Gives every standing palm in the scene a TreeSway — any placed glTF whose asset path
    /// names a palm tree, except the fallen trunk and the log, which lie on the ground. Safe
    /// to run again; re-run after placing more palms.
    /// </summary>
    public static class TreeSwaySetup
    {
        static readonly string[] Trees = { "palmtree" };
        static readonly string[] Lying = { "fallen", "log" };

        [MenuItem("WoW Sandbox/Add Tree Sway")]
        static void AddTreeSway()
        {
            var roots = new HashSet<GameObject>();
            foreach (var skinned in Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsInactive.Include))
            {
                var root = PrefabUtility.GetNearestPrefabInstanceRoot(skinned.gameObject);
                if (root == null || roots.Contains(root))
                    continue;

                string path = AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(root)).ToLowerInvariant();
                if (Matches(path, Trees) && !Matches(path, Lying))
                    roots.Add(root);
            }

            int added = 0;
            foreach (var root in roots)
            {
                if (root.GetComponent<TreeSway>() != null)
                    continue;
                Undo.AddComponent<TreeSway>(root);
                added++;
            }

            if (added > 0)
                EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log($"[TreeSwaySetup] Found {roots.Count} standing palm(s); added TreeSway to {added}. " +
                      "It sways in Play mode. Save the scene to keep it.");
        }

        static bool Matches(string path, string[] words)
        {
            foreach (string word in words)
            {
                if (path.Contains(word))
                    return true;
            }
            return false;
        }
    }
}
