using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace WowSandbox.EditorTools
{
    /// <summary>
    /// Builds a Stand/Walk locomotion AnimatorController from the clips inside a
    /// wow.export glTF. Shared by the chicken and NPC spawners so there's one place
    /// that knows how wow.export names its clips and how to make them loop.
    /// </summary>
    public static class M2AnimatorBuilder
    {
        /// <summary>
        /// Returns null if the model has no usable clips — the caller should carry on and
        /// spawn an un-animated model rather than fail, since a stale import is the usual
        /// cause and it's easily fixed with a Reimport.
        /// </summary>
        public static AnimatorController BuildLocomotion(string modelPath, string outputDir)
        {
            var clips = AssetDatabase.LoadAllAssetRepresentationsAtPath(modelPath)
                                     .OfType<AnimationClip>()
                                     .ToArray();
            if (clips.Length == 0)
            {
                Debug.LogWarning($"[M2AnimatorBuilder] No animation clips in {Path.GetFileName(modelPath)}. " +
                                 "If the export has _anim*.bin files, Unity's import is stale — " +
                                 "right-click the glTF in the Project window and choose Reimport.");
                return null;
            }

            var idle = Find(clips, "Stand");
            var walk = Find(clips, "Walk") ?? Find(clips, "Run");
            if (idle == null || walk == null)
            {
                Debug.LogWarning($"[M2AnimatorBuilder] Need Stand and Walk clips; found: " +
                                 string.Join(", ", clips.Select(c => c.name)));
                return null;
            }

            Directory.CreateDirectory(outputDir);

            idle = CopyLooping(idle, "Stand", outputDir);
            walk = CopyLooping(walk, "Walk", outputDir);

            string path = $"{outputDir}/Locomotion.controller";
            AssetDatabase.DeleteAsset(path);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);

            controller.CreateBlendTreeInController("Locomotion", out BlendTree tree);
            tree.blendType = BlendTreeType.Simple1D;
            tree.blendParameter = "Speed";
            tree.useAutomaticThresholds = false;
            tree.AddChild(idle, 0f);
            tree.AddChild(walk, 1f);

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            return controller;
        }

        /// <summary>wow.export names clips "Stand (ID 0 variation 0)" — match the leading name.</summary>
        static AnimationClip Find(AnimationClip[] clips, string prefix) =>
            clips.FirstOrDefault(c => c.name.StartsWith(prefix + " ") || c.name == prefix);

        /// <summary>
        /// Imported clips are read-only and not marked looping, so idle/walk would play
        /// once and freeze. Copy them out and set loopTime on the copies.
        /// </summary>
        static AnimationClip CopyLooping(AnimationClip original, string name, string outputDir)
        {
            string path = $"{outputDir}/{name}.anim";
            AssetDatabase.DeleteAsset(path);

            var copy = Object.Instantiate(original);
            copy.name = name;

            var settings = AnimationUtility.GetAnimationClipSettings(copy);
            settings.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(copy, settings);

            AssetDatabase.CreateAsset(copy, path);
            return copy;
        }
    }
}
