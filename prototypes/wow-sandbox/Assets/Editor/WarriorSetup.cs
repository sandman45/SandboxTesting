using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using WowSandbox;

namespace WowSandbox.EditorTools
{
    /// <summary>
    /// One-click setup for a playable WoW character: builds an AnimatorController from
    /// the clips inside a wow.export glTF, then assembles the rig in the open scene.
    ///
    /// Generated assets land inside Assets/WowExports/ on purpose — that path is
    /// gitignored, and the looping clip copies are WoW-derived data that must not be
    /// committed to this public repo.
    /// </summary>
    public static class WarriorSetup
    {
        const string ModelPath =
            "Assets/WowExports/creature/humanmalewarriorlight/humanmalewarriorlight_skin01.gltf";
        const string GeneratedRoot = "Assets/WowExports/_Generated/Warrior";

        /// <summary>
        /// wow.export's glTF has the model facing -X, while Unity's transform.forward is +Z,
        /// so the mesh sits 90 degrees off from the direction the controller drives it.
        /// Rotating +90 brings -X around to +Z. This goes on the model child rather than the
        /// logic root so transform.forward stays honest and the movement maths needs no fudge.
        ///
        /// The axis is certain from the mesh bounds — 1.15 units across on Z versus 0.52 on X,
        /// with the vertices exactly symmetric about Z — so Z is left/right and X is the facing
        /// axis. The sign was confirmed in the editor: centroid heuristics mislead here, because
        /// the calf bulges rearward and the head carries hair/helm mass behind the skull, which
        /// both drag the apparent "front" the wrong way.
        /// </summary>
        const float ModelYawOffset = 90f;

        // wow.export names clips "Stand (ID 0 variation 0)" — match on the leading name.
        const string IdleClip = "Stand";
        const string WalkClip = "Walk";
        const string RunClip = "Run";
        const string AttackClip = "Attack1H";

        // The warrior ships SwimIdle (ID 41) and Swim (ID 42). Note that CopyClip matches on
        // the prefix plus a SPACE — that trailing space is the only thing stopping "Swim" from
        // also matching "SwimIdle (ID 41 variation 0)". Don't tidy it away.
        const string SwimIdleClip = "SwimIdle";
        const string SwimClip = "Swim";

        [MenuItem("WoW Sandbox/Build Warrior Animator")]
        public static void BuildAnimator()
        {
            if (BuildAnimatorController() != null)
                Debug.Log($"[WarriorSetup] Animator controller written to {GeneratedRoot}.");
        }

        [MenuItem("WoW Sandbox/Spawn Warrior Player")]
        public static void SpawnPlayer()
        {
            var controller = BuildAnimatorController();
            if (controller == null)
                return;

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null)
            {
                Debug.LogError($"[WarriorSetup] Could not load the model at {ModelPath}.");
                return;
            }

            var root = new GameObject("WarriorPlayer");
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.Euler(0f, ModelYawOffset, 0f);

            // Measure the model so the capsule and camera fit whatever scale it imported at.
            float height = MeasureHeight(instance);

            var capsule = root.AddComponent<CharacterController>();
            capsule.height = height;
            capsule.radius = Mathf.Max(height * 0.18f, 0.05f);
            capsule.center = new Vector3(0f, height * 0.5f, 0f);
            capsule.slopeLimit = 50f;
            capsule.stepOffset = Mathf.Min(0.3f, height * 0.25f);

            var animator = instance.GetComponentInChildren<Animator>();
            if (animator == null)
                animator = instance.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;

            var avatar = LoadSubAssets<Avatar>().FirstOrDefault();
            if (avatar != null)
                animator.avatar = avatar;

            var movement = root.AddComponent<WowCharacterController>();
            // The model child carries the facing offset above and the swim pitch at runtime.
            movement.modelRoot = instance.transform;

            var camera = Camera.main;
            if (camera == null)
            {
                Debug.LogWarning("[WarriorSetup] No camera tagged MainCamera; skipping camera setup.");
            }
            else
            {
                var orbit = camera.GetComponent<WowOrbitCamera>();
                if (orbit == null)
                    orbit = camera.gameObject.AddComponent<WowOrbitCamera>();
                orbit.target = root.transform;
                orbit.targetHeight = height * 0.85f;
                orbit.distance = Mathf.Max(height * 3f, 3f);
                orbit.maxDistance = Mathf.Max(height * 7f, 8f);
                orbit.minDistance = Mathf.Max(height * 0.75f, 1f);
            }

            // Speeds are authored in metres; scale them to whatever units the model uses.
            float scale = height / 1.8f;
            movement.runSpeed *= scale;
            movement.walkSpeed *= scale;
            movement.swimSpeed *= scale;
            movement.jumpHeight *= scale;
            movement.gravity *= scale;

            Undo.RegisterCreatedObjectUndo(root, "Spawn Warrior Player");
            Selection.activeGameObject = root;
            Debug.Log($"[WarriorSetup] Spawned WarriorPlayer (model height {height:F2} units). " +
                      "Press Play: W/S move, A/D turn, Q/E strafe, right-drag steers, left-drag orbits. " +
                      "In water: Space swims up, X swims down.");
        }

        static AnimatorController BuildAnimatorController()
        {
            var clips = LoadSubAssets<AnimationClip>();
            if (clips.Length == 0)
            {
                Debug.LogError($"[WarriorSetup] No AnimationClips found in {ModelPath}. " +
                               "Check the glTF importer's Animation Method is set to Mecanim.");
                return null;
            }

            Directory.CreateDirectory(GeneratedRoot);

            var idle = CopyClip(clips, IdleClip, loop: true);
            var walk = CopyClip(clips, WalkClip, loop: true);
            var run = CopyClip(clips, RunClip, loop: true);
            var attack = CopyClip(clips, AttackClip, loop: false);
            var swimIdle = CopyClip(clips, SwimIdleClip, loop: true);
            var swim = CopyClip(clips, SwimClip, loop: true);

            if (idle == null || walk == null || run == null)
            {
                Debug.LogError("[WarriorSetup] Missing one of the required locomotion clips " +
                               $"({IdleClip}/{WalkClip}/{RunClip}). Re-export with those animations included.");
                return null;
            }

            string controllerPath = $"{GeneratedRoot}/Warrior.controller";
            AssetDatabase.DeleteAsset(controllerPath);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);

            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("Grounded", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Jump", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Swimming", AnimatorControllerParameterType.Bool);

            var stateMachine = controller.layers[0].stateMachine;

            var locomotion = controller.CreateBlendTreeInController("Locomotion", out BlendTree tree);
            tree.blendType = BlendTreeType.Simple1D;
            tree.blendParameter = "Speed";
            tree.useAutomaticThresholds = false;
            tree.AddChild(idle, 0f);
            tree.AddChild(walk, 0.5f);
            tree.AddChild(run, 1f);
            stateMachine.defaultState = locomotion;

            // Swimming is a second locomotion tree on the same Speed parameter, so the
            // controller drives one value and the state decides what it means.
            if (swimIdle != null && swim != null)
            {
                var swimming = controller.CreateBlendTreeInController("Swim", out BlendTree swimTree);
                swimTree.blendType = BlendTreeType.Simple1D;
                swimTree.blendParameter = "Speed";
                swimTree.useAutomaticThresholds = false;
                swimTree.AddChild(swimIdle, 0f);
                swimTree.AddChild(swim, 1f);

                var intoWater = locomotion.AddTransition(swimming);
                intoWater.AddCondition(AnimatorConditionMode.If, 0f, "Swimming");
                intoWater.hasExitTime = false;
                intoWater.duration = 0.25f;

                var outOfWater = swimming.AddTransition(locomotion);
                outOfWater.AddCondition(AnimatorConditionMode.IfNot, 0f, "Swimming");
                outOfWater.hasExitTime = false;
                outOfWater.duration = 0.25f;
            }
            else
            {
                Debug.LogWarning("[WarriorSetup] No SwimIdle/Swim clips in the model — swimming " +
                                 "will work but the character will keep running on the spot. " +
                                 "Re-export with animations 41 and 42 included.");
            }

            if (attack != null)
            {
                var attackState = stateMachine.AddState("Attack");
                attackState.motion = attack;

                var toAttack = stateMachine.AddAnyStateTransition(attackState);
                toAttack.AddCondition(AnimatorConditionMode.If, 0f, "Attack");
                toAttack.duration = 0.05f;
                toAttack.hasExitTime = false;
                toAttack.canTransitionToSelf = false;

                var backToLocomotion = attackState.AddTransition(locomotion);
                backToLocomotion.hasExitTime = true;
                backToLocomotion.exitTime = 0.85f;
                backToLocomotion.duration = 0.1f;
            }

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return controller;
        }

        /// <summary>
        /// Imported clips are read-only and not marked as looping, so idle/walk/run would
        /// play once and freeze. Copy them out and set loopTime on the copies.
        /// </summary>
        static AnimationClip CopyClip(AnimationClip[] source, string namePrefix, bool loop)
        {
            var original = source.FirstOrDefault(c => c.name.StartsWith(namePrefix + " ")
                                                   || c.name == namePrefix);
            if (original == null)
            {
                Debug.LogWarning($"[WarriorSetup] No clip starting with \"{namePrefix}\" in the model.");
                return null;
            }

            string path = $"{GeneratedRoot}/{namePrefix}.anim";
            AssetDatabase.DeleteAsset(path);

            var copy = Object.Instantiate(original);
            copy.name = namePrefix;

            var settings = AnimationUtility.GetAnimationClipSettings(copy);
            settings.loopTime = loop;
            AnimationUtility.SetAnimationClipSettings(copy, settings);

            AssetDatabase.CreateAsset(copy, path);
            return copy;
        }

        static T[] LoadSubAssets<T>() where T : Object
        {
            return AssetDatabase.LoadAllAssetRepresentationsAtPath(ModelPath)
                                .OfType<T>()
                                .ToArray();
        }

        /// <summary>Height of the model in whatever units it imported at.</summary>
        static float MeasureHeight(GameObject instance)
        {
            var renderers = instance.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
                return 1.8f;

            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);

            return Mathf.Max(bounds.size.y, 0.1f);
        }
    }
}
