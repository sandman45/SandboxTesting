using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace WowSandbox.EditorTools
{
    /// <summary>
    /// Adds (or refreshes) a MusicPlayer and fills its playlist with every track in the music
    /// folder, setting each to stream. Re-run it after adding tracks.
    /// </summary>
    public static class MusicSetup
    {
        // Gitignored with the rest of WowExports — music is Blizzard content like the models.
        const string MusicFolder = "Assets/WowExports/sound/music";

        [MenuItem("WoW Sandbox/Add Music Player")]
        public static void AddMusicPlayer()
        {
            if (!AssetDatabase.IsValidFolder(MusicFolder))
            {
                Debug.LogWarning($"[MusicSetup] No {MusicFolder} folder. Put your tracks there first.");
                return;
            }

            var clips = AssetDatabase.FindAssets("t:AudioClip", new[] { MusicFolder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(path => path)
                .Select(path =>
                {
                    ConfigureForMusic(path);
                    return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                })
                .Where(clip => clip != null)
                .ToList();

            if (clips.Count == 0)
            {
                Debug.LogWarning($"[MusicSetup] No audio in {MusicFolder} yet — if you just copied files " +
                                 "in, click into Unity (or Ctrl+R) so it imports them, then run this again.");
                return;
            }

            var player = Object.FindFirstObjectByType<MusicPlayer>();
            if (player == null)
            {
                var go = new GameObject("Music");
                Undo.RegisterCreatedObjectUndo(go, "Add Music Player");
                player = go.AddComponent<MusicPlayer>();
            }
            else
            {
                Undo.RecordObject(player, "Refresh Music Playlist");
            }

            player.playlist = clips;
            EditorUtility.SetDirty(player);
            EditorSceneManager.MarkSceneDirty(player.gameObject.scene);
            Selection.activeGameObject = player.gameObject;

            Debug.Log($"[MusicSetup] Playlist set to {clips.Count} track(s): " +
                      string.Join(", ", clips.Select(c => c.name)), player);
        }

        /// <summary>
        /// Music wants streaming: decoded from disk as it plays, not loaded whole into memory
        /// up front, which for a few minutes of audio is tens of megabytes per track.
        /// </summary>
        static void ConfigureForMusic(string path)
        {
            if (AssetImporter.GetAtPath(path) is not AudioImporter importer)
                return;

            var settings = importer.defaultSampleSettings;
            if (settings.loadType == AudioClipLoadType.Streaming &&
                settings.compressionFormat == AudioCompressionFormat.Vorbis &&
                !settings.preloadAudioData)
                return;

            settings.loadType = AudioClipLoadType.Streaming;
            settings.compressionFormat = AudioCompressionFormat.Vorbis;
            settings.quality = 0.7f;
            settings.preloadAudioData = false;
            importer.defaultSampleSettings = settings;
            importer.SaveAndReimport();
        }
    }
}
