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
            AddSleepStates(controller, modelPath);

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            return controller;
        }

        /// <summary>
        /// For sleeping at night (WanderingNpc, Vendor), driven by Asleep (bool): lie down
        /// (SleepDown), sleep (Sleep, looping), get up (SleepUp) and back to locomotion — or,
        /// for a model without Sleep, the same with SitGround. Edits in place, so NPCs already in
        /// the scene keep their controller; does nothing if it's there already or the model has
        /// neither. Returns whether anything was added.
        /// </summary>
        public static bool AddSleepStates(AnimatorController controller, string modelPath)
        {
            if (controller.parameters.Any(parameter => parameter.name == "Asleep"))
                return false;

            var clips = AssetDatabase.LoadAllAssetRepresentationsAtPath(modelPath)
                                     .OfType<AnimationClip>()
                                     .ToArray();
            string family = Find(clips, "Sleep") != null ? "Sleep" : Find(clips, "SitGround") != null ? "SitGround" : null;
            if (family == null)
            {
                Debug.Log($"[M2AnimatorBuilder] No Sleep or SitGround clip in {Path.GetFileName(modelPath)} — " +
                          "it'll stand still at night instead.");
                return false;
            }

            string outputDir = Path.GetDirectoryName(AssetDatabase.GetAssetPath(controller))?.Replace('\\', '/');
            var loop = CopyLooping(Find(clips, family), "Sleep", outputDir);
            var downClip = Find(clips, family + "Down");
            var upClip = Find(clips, family + "Up");

            controller.AddParameter("Asleep", AnimatorControllerParameterType.Bool);
            var stateMachine = controller.layers[0].stateMachine;
            var locomotion = stateMachine.defaultState;

            var sleep = stateMachine.AddState("Sleep");
            sleep.motion = loop;
            AnimatorState lieDown = sleep;
            if (downClip != null)
            {
                lieDown = stateMachine.AddState("LieDown");
                lieDown.motion = CopyOnce(downClip, "LieDown", outputDir);
                var settle = lieDown.AddTransition(sleep);
                settle.hasExitTime = true;
                settle.exitTime = 0.9f;
                settle.duration = 0.1f;
            }
            var down = locomotion.AddTransition(lieDown);
            down.AddCondition(AnimatorConditionMode.If, 0f, "Asleep");
            down.hasExitTime = false;
            down.duration = 0.25f;

            AnimatorState getUp = locomotion;
            if (upClip != null)
            {
                getUp = stateMachine.AddState("GetUp");
                getUp.motion = CopyOnce(upClip, "GetUp", outputDir);
                var back = getUp.AddTransition(locomotion);
                back.hasExitTime = true;
                back.exitTime = 0.85f;
                back.duration = 0.15f;
            }
            // From lying down too, so a sleeper woken mid-way gets straight back up.
            foreach (var state in lieDown == sleep ? new[] { sleep } : new[] { lieDown, sleep })
            {
                var up = state.AddTransition(getUp);
                up.AddCondition(AnimatorConditionMode.IfNot, 0f, "Asleep");
                up.hasExitTime = false;
                up.duration = 0.15f;
            }

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            return true;
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

        /// <summary>
        /// For NPCs that fight (NpcCombat): a Run clip at Speed 2 in the locomotion blend, for
        /// chasing, and an Attack one-shot. Works on a controller already in use — it edits in
        /// place, adding only what's missing — so NPCs already in the scene keep theirs.
        /// Returns whether anything was added.
        /// </summary>
        public static bool AddCombatStates(AnimatorController controller, string modelPath)
        {
            var clips = AssetDatabase.LoadAllAssetRepresentationsAtPath(modelPath)
                                     .OfType<AnimationClip>()
                                     .ToArray();
            string outputDir = Path.GetDirectoryName(AssetDatabase.GetAssetPath(controller))?.Replace('\\', '/');
            var stateMachine = controller.layers[0].stateMachine;
            var locomotion = stateMachine.defaultState;
            bool changed = false;

            if (locomotion != null && locomotion.motion is BlendTree tree &&
                tree.children.All(child => child.threshold < 1.5f))
            {
                var run = Find(clips, "Run");
                if (run != null)
                {
                    tree.AddChild(CopyLooping(run, "Run", outputDir), 2f);
                    changed = true;
                }
            }

            if (controller.parameters.All(parameter => parameter.name != "Attack"))
            {
                var attack = Find(clips, "Attack1H") ?? Find(clips, "AttackUnarmed");
                if (attack != null)
                {
                    AddOneShot(controller, locomotion, "Attack", CopyOnce(attack, "Attack", outputDir));
                    changed = true;
                }
            }

            if (changed)
            {
                EditorUtility.SetDirty(controller);
                AssetDatabase.SaveAssets();
            }
            return changed;
        }

        /// <summary>
        /// For animated props that only ever idle — a swirl of fish, say: one state looping
        /// the Stand clip. Null if the model has none.
        /// </summary>
        public static AnimatorController BuildIdle(string modelPath, string outputDir)
        {
            var clips = AssetDatabase.LoadAllAssetRepresentationsAtPath(modelPath)
                                     .OfType<AnimationClip>()
                                     .ToArray();
            var stand = Find(clips, "Stand");
            if (stand == null)
            {
                Debug.LogWarning($"[M2AnimatorBuilder] No Stand clip in {Path.GetFileName(modelPath)}; found: " +
                                 string.Join(", ", clips.Select(c => c.name)) +
                                 ". If the export has _anim*.bin files, right-click the glTF and Reimport.");
                return null;
            }

            Directory.CreateDirectory(outputDir);
            stand = CopyLooping(stand, "Stand", outputDir);

            string path = $"{outputDir}/Idle.controller";
            AssetDatabase.DeleteAsset(path);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            controller.layers[0].stateMachine.AddState("Stand").motion = stand;

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

        /// <summary>
        /// Extra states for the Ancient Leviathan's storm appearance, on top of BuildSwimming's:
        /// Cast0..CastN one-shots from whichever of its SpellCast/CustomSpell clips exist (it
        /// picks among them at random), and Submerge, which plays once and settles into the
        /// looping Submerged — no way back out, it's going under and then gets hidden.
        /// </summary>
        public static void AddLeviathanStates(AnimatorController controller, string modelPath, string outputDir)
        {
            var clips = AssetDatabase.LoadAllAssetRepresentationsAtPath(modelPath)
                                     .OfType<AnimationClip>()
                                     .ToArray();
            var stateMachine = controller.layers[0].stateMachine;
            var locomotion = stateMachine.defaultState;

            int casts = 0;
            foreach (string name in new[] { "SpellCast", "CustomSpell01", "CustomSpell02", "CustomSpell03",
                                            "CustomSpell04", "CustomSpell05", "CustomSpell06", "CustomSpell07" })
            {
                var clip = Find(clips, name);
                if (clip == null)
                    continue;
                string trigger = "Cast" + casts++;
                AddOneShot(controller, locomotion, trigger, CopyOnce(clip, trigger, outputDir));
            }

            var submerge = Find(clips, "Submerge");
            if (submerge == null)
            {
                Debug.Log($"[M2AnimatorBuilder] No Submerge clip in {Path.GetFileName(modelPath)} — " +
                          "the leviathan will sink without one.");
            }
            else
            {
                controller.AddParameter("Submerge", AnimatorControllerParameterType.Trigger);
                var sinking = stateMachine.AddState("Submerge");
                sinking.motion = CopyOnce(submerge, "Submerge", outputDir);

                var into = stateMachine.AddAnyStateTransition(sinking);
                into.AddCondition(AnimatorConditionMode.If, 0f, "Submerge");
                into.duration = 0.2f;
                into.hasExitTime = false;
                into.canTransitionToSelf = false;

                var held = Find(clips, "Submerged");
                if (held != null)
                {
                    var under = stateMachine.AddState("Submerged");
                    under.motion = CopyLooping(held, "Submerged", outputDir);
                    var settle = sinking.AddTransition(under);
                    settle.hasExitTime = true;
                    settle.exitTime = 1f;
                    settle.duration = 0.2f;
                }
            }

            Debug.Log($"[M2AnimatorBuilder] Leviathan storm states: {casts} cast clip(s)" +
                      (submerge != null ? " and Submerge." : "."));
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// The talk gestures a speaking NPC picks from (Speaker.Talk): plain, emphatic, asking and
        /// hushed, for whichever of the clips the model has.
        /// </summary>
        public static readonly (string trigger, string clip)[] TalkGestures =
        {
            ("Talk", "EmoteTalk"),
            ("TalkExclamation", "EmoteTalkExclamation"),
            ("TalkQuestion", "EmoteTalkQuestion"),
            ("TalkSubdued", "EmoteTalkSubdued"),
        };

        /// <summary>
        /// Adds a play-once state per (trigger, clip name) pair, for whichever of the clips the
        /// model has — a vendor's wave and talk, say. Each plays through and returns to the
        /// default (locomotion) state; triggers the controller already has are skipped, so it's
        /// safe to run again on one in use. Returns how many were added.
        /// </summary>
        public static int AddOneShots(AnimatorController controller, string modelPath, string outputDir,
                                      params (string trigger, string clip)[] shots)
        {
            var clips = AssetDatabase.LoadAllAssetRepresentationsAtPath(modelPath)
                                     .OfType<AnimationClip>()
                                     .ToArray();
            var locomotion = controller.layers[0].stateMachine.defaultState;
            int added = 0;
            foreach (var (trigger, clipName) in shots)
            {
                var clip = Find(clips, clipName);
                // Already there, from an earlier run on this controller.
                if (clip == null || controller.parameters.Any(parameter => parameter.name == trigger))
                    continue;
                AddOneShot(controller, locomotion, trigger, CopyOnce(clip, trigger, outputDir));
                added++;
            }

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            return added;
        }

        /// <summary>
        /// A looping state held while the bool <paramref name="parameter"/> is on — a tinkerer's
        /// work at the bench, say — blending in and out of locomotion. False if the model has no
        /// such clip.
        /// </summary>
        public static bool AddLoop(AnimatorController controller, string modelPath, string outputDir,
                                   string parameter, string clipName)
        {
            var clips = AssetDatabase.LoadAllAssetRepresentationsAtPath(modelPath)
                                     .OfType<AnimationClip>()
                                     .ToArray();
            var clip = Find(clips, clipName);
            if (clip == null)
                return false;

            controller.AddParameter(parameter, AnimatorControllerParameterType.Bool);
            var stateMachine = controller.layers[0].stateMachine;
            var locomotion = stateMachine.defaultState;
            var state = stateMachine.AddState(parameter);
            state.motion = CopyLooping(clip, parameter, outputDir);

            var into = locomotion.AddTransition(state);
            into.AddCondition(AnimatorConditionMode.If, 0f, parameter);
            into.hasExitTime = false;
            into.duration = 0.25f;
            var back = state.AddTransition(locomotion);
            back.AddCondition(AnimatorConditionMode.IfNot, 0f, parameter);
            back.hasExitTime = false;
            back.duration = 0.25f;

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            return true;
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
