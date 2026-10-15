using System.IO;
using UnityEditor;
using UnityEngine;

namespace WowSandbox.EditorTools
{
    /// <summary>
    /// Builds a ready-to-open chest prefab for every exported chest model, each time Play is
    /// pressed (only the ones that are missing), into a Resources folder under the gitignored
    /// _Generated — so the Developer panel can spawn them at Play, when models can no longer be
    /// looked up by path. Delete the folder to rebuild them after changing how chests are set up.
    /// </summary>
    [InitializeOnLoad]
    static class DevChestBuilder
    {
        internal const string Folder = "Assets/WowExports/_Generated/Resources/" + DevChests.ResourceFolder;
        const float Height = 0.9f;

        static DevChestBuilder()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.ExitingEditMode)
                    Build();
            };
        }

        [MenuItem("WoW Sandbox/Rebuild Dev Chests")]
        static void Rebuild()
        {
            if (AssetDatabase.IsValidFolder(Folder))
                AssetDatabase.DeleteAsset(Folder);
            Build();
        }

        static void Build()
        {
            int built = 0;
            foreach (string model in InteractableSetup.ChestModels())
            {
                string path = $"{Folder}/{Path.GetFileNameWithoutExtension(model)}.prefab";
                if (File.Exists(path))
                    continue;
                Directory.CreateDirectory(Folder);
                var chest = InteractableSetup.BuildChest(model, Height);
                if (chest == null)
                    continue;
                chest.name = Path.GetFileNameWithoutExtension(model);
                PrefabUtility.SaveAsPrefabAsset(chest, path);
                Object.DestroyImmediate(chest);
                built++;
            }
            if (built > 0)
            {
                AssetDatabase.SaveAssets();
                Debug.Log($"[DevChestBuilder] Built {built} chest(s) for the Developer panel.");
            }
        }
    }
}
