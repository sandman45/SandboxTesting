using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace WowSandbox.EditorTools
{
    /// <summary>
    /// Builds AnimatorControllers from the clips inside a wow.export glTF: Stand/Walk
    /// locomotion for things on land, an idle/cruise/fast blend for things that swim,
    /// plus Death, Hit and (for swimmers) Attack states when the export has those clips.
    /// Shared by every spawner so there's one place that knows how wow.export names its
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

            var locomotion = controller.CreateBlendTreeInController("Locomotion", out BlendTree tree);
            tree.blendType = BlendTreeType.Simple1D;
            tree.blendParameter = "Speed";
            tree.useAutomaticThresholds = false;
            tree.AddChild(idle, 0f);
            tree.AddChild(walk, 1f);

            AddDeath(controller, death, modelPath);
            AddHit(controller, locomotion, hit, modelPath);

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            return controller;
        }

        /// <summary>
        /// For sea creatures: a Speed blend of SwimIdle (0), Swim (1) and a fast swim (2),
        /// plus Attack, Hit and Death. SwimmingCreature drives Speed as its speed over cruise
        /// speed, so 2 is a full chase. Clip names vary by model — sharks have Swim/Sprint,
        /// the whale shark SwimWalk/SwimRun — so each slot tries the likely names in turn.
        /// </summary>
        public static AnimatorController BuildSwimming(string modelPath, string outputDir)
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

            var idle = Find(clips, "SwimIdle") ?? Find(clips, "Stand");
            var cruise = Find(clips, "Swim") ?? Find(clips, "SwimWalk") ?? Find(clips, "Walk");
            var fast = Find(clips, "SwimRun") ?? Find(clips, "Sprint") ?? Find(clips, "Run") ?? cruise;
            if (idle == null || cruise == null)
            {
                Debug.LogWarning($"[M2AnimatorBuilder] Need a swim idle and a swim clip; found: " +
                                 string.Join(", ", clips.Select(c => c.name)));
                return null;
            }

            var attack = Find(clips, "AttackUnarmed") ?? Find(clips, "Attack1H");
            var death = Find(clips, "Death");
            var hit = Find(clips, "CombatWound") ?? Find(clips, "Wound");

            Directory.CreateDirectory(outputDir);
            bool sameFast = fast == cruise;
            idle = CopyLooping(idle, "SwimIdle", outputDir);
            cruise = CopyLooping(cruise, "Swim", outputDir);
            fast = sameFast ? cruise : CopyLooping(fast, "SwimFast", outputDir);
            if (attack != null) attack = CopyOnce(attack, "Attack", outputDir);
            if (death != null) death = CopyOnce(death, "Death", outputDir);
            if (hit != null) hit = CopyOnce(hit, "Hit", outputDir);

            string path = $"{outputDir}/Swimming.controller";
            AssetDatabase.DeleteAsset(path);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);

            var locomotion = controller.CreateBlendTreeInController("Swimming", out BlendTree tree);
            tree.blendType = BlendTreeType.Simple1D;
            tree.blendParameter = "Speed";
            tree.useAutomaticThresholds = false;
            tree.AddChild(idle, 0f);
            tree.AddChild(cruise, 1f);
            tree.AddChild(fast, 2f);

            AddDeath(controller, death, modelPath);
            AddHit(controller, locomotion, hit, modelPath);
            AddOneShot(controller, locomotion, "Attack", attack);

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            return controller;
        }

        static void AddDeath(AnimatorController controller, AnimationClip death, string modelPath)
        {
            if (death == null)
            {
                Debug.Log($"[M2AnimatorBuilder] No Death clip in {Path.GetFileName(modelPath)} — " +
                          "corpses from this model will despawn without a death animation.");
                return;
            }

            controller.AddParameter("Death", AnimatorControllerParameterType.Trigger);
            var stateMachine = controller.layers[0].stateMachine;
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

        static void AddHit(AnimatorController controller, AnimatorState locomotion, AnimationClip hit, string modelPath)
        {
            if (hit == null)
            {
                Debug.Log($"[M2AnimatorBuilder] No CombatWound/Wound clip in " +
                          $"{Path.GetFileName(modelPath)} — no hit reaction for this model.");
                return;
            }

            // Getting hit is a flinch, not the end of the animation — it plays through and
            // hands back to locomotion.
            AddOneShot(controller, locomotion, "Hit", hit);
        }

        /// <summary>An Any State trigger into <paramref name="clip"/> that plays through and returns to locomotion.</summary>
        static void AddOneShot(AnimatorController controller, AnimatorState locomotion, string trigger, AnimationClip clip)
        {
            if (clip == null)
                return;

            controller.AddParameter(trigger, AnimatorControllerParameterType.Trigger);
            var stateMachine = controller.layers[0].stateMachine;
            var state = stateMachine.AddState(trigger);
            state.motion = clip;

            var into = stateMachine.AddAnyStateTransition(state);
            into.AddCondition(AnimatorConditionMode.If, 0f, trigger);
            into.duration = 0.05f;
            into.hasExitTime = false;
            into.canTransitionToSelf = false;

            var back = state.AddTransition(locomotion);
            back.hasExitTime = true;
            back.exitTime = 0.85f;
            back.duration = 0.1f;
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
