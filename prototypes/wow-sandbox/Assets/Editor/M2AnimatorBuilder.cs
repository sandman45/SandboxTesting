using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace WowSandbox.EditorTools
{
    /// <summary>
    /// Builds a Stand/Walk locomotion AnimatorController from the clips inside a
    /// wow.export glTF, plus a Death state if the export has that clip. Shared by the
    /// chicken and NPC spawners so there's one place that knows how wow.export names its
    /// clips and how to make them loop (or, for Death, deliberately not).
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

            // Optional — not every creature exports one, and Health copes fine without it,
            // just despawning the corpse in whatever pose it died in.
            var death = Find(clips, "Death");
            // wow.export names this one of two ways depending on the model.
            var hit = Find(clips, "CombatWound") ?? Find(clips, "Wound");

            Directory.CreateDirectory(outputDir);

            idle = CopyLooping(idle, "Stand", outputDir);
            walk = CopyLooping(walk, "Walk", outputDir);
            if (death != null)
                death = CopyOnce(death, "Death", outputDir);
            if (hit != null)
                hit = CopyOnce(hit, "Hit", outputDir);

            string path = $"{outputDir}/Locomotion.controller";
            AssetDatabase.DeleteAsset(path);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);

            var stateMachine = controller.layers[0].stateMachine;

            var locomotion = controller.CreateBlendTreeInController("Locomotion", out BlendTree tree);
            tree.blendType = BlendTreeType.Simple1D;
            tree.blendParameter = "Speed";
            tree.useAutomaticThresholds = false;
            tree.AddChild(idle, 0f);
            tree.AddChild(walk, 1f);

            if (death != null)
            {
                controller.AddParameter("Death", AnimatorControllerParameterType.Trigger);

                var deathState = stateMachine.AddState("Death");
                deathState.motion = death;

                var toDeath = stateMachine.AddAnyStateTransition(deathState);
                toDeath.AddCondition(AnimatorConditionMode.If, 0f, "Death");
                toDeath.duration = 0.1f;
                toDeath.hasExitTime = false;
                toDeath.canTransitionToSelf = false;

                // No transition back out of Death — loopTime is off on the clip, so once it
                // finishes playing it holds the final pose. Health destroys the GameObject
                // outright once the corpse timer runs out; nothing needs to leave this state.
            }
            else
            {
                Debug.Log($"[M2AnimatorBuilder] No Death clip in {Path.GetFileName(modelPath)} — " +
                          "corpses from this model will despawn without a death animation.");
            }

            if (hit != null)
            {
                controller.AddParameter("Hit", AnimatorControllerParameterType.Trigger);

                var hitState = stateMachine.AddState("Hit");
                hitState.motion = hit;

                var toHit = stateMachine.AddAnyStateTransition(hitState);
                toHit.AddCondition(AnimatorConditionMode.If, 0f, "Hit");
                toHit.duration = 0.05f;
                toHit.hasExitTime = false;
                toHit.canTransitionToSelf = false;

                // Unlike Death, this one plays through and hands back to locomotion —
                // getting hit is a flinch, not the end of the animation.
                var backToLocomotion = hitState.AddTransition(locomotion);
                backToLocomotion.hasExitTime = true;
                backToLocomotion.exitTime = 0.85f;
                backToLocomotion.duration = 0.1f;
            }
            else
            {
                Debug.Log($"[M2AnimatorBuilder] No CombatWound/Wound clip in " +
                          $"{Path.GetFileName(modelPath)} — no hit reaction for this model.");
            }

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

        /// <summary>Same copy-out as CopyLooping, but leaves loopTime off so the clip holds
        /// its last frame instead of restarting — what a death pose needs.</summary>
        static AnimationClip CopyOnce(AnimationClip original, string name, string outputDir)
        {
            string path = $"{outputDir}/{name}.anim";
            AssetDatabase.DeleteAsset(path);

            var copy = Object.Instantiate(original);
            copy.name = name;

            var settings = AnimationUtility.GetAnimationClipSettings(copy);
            settings.loopTime = false;
            AnimationUtility.SetAnimationClipSettings(copy, settings);

            AssetDatabase.CreateAsset(copy, path);
            return copy;
        }
    }
}
