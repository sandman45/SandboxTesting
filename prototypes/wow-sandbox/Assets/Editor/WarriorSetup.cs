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
        // A player-race character export, unequipped: the body texture is bare so gear can be
        // layered onto it. Unlike the old creature warrior it carries the real fishing and jump
        // clips. wow.export always writes this model to thinhuman/male, so each export is renamed
        // afterwards (base, shirt, shorts, ...) to stop the next one overwriting it.
        const string ModelPath =
            "Assets/WowExports/character/thinhuman/base/thinhumanmale.gltf";
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

        /// <summary>
        /// Adds the fishing and hit-reaction states to the warrior controller already in use,
        /// in place — unlike Build Warrior Animator, which deletes and recreates the asset, so a
        /// player already in the scene would lose its controller. Safe to run again.
        /// </summary>
        [MenuItem("WoW Sandbox/Update Warrior Animations")]
        public static void UpdateWarriorAnimations()
        {
            string controllerPath = $"{GeneratedRoot}/Warrior.controller";
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (controller == null)
            {
                Debug.LogError($"[WarriorSetup] No warrior controller at {controllerPath} — run Spawn Warrior Player first.");
                return;
            }

            var clips = LoadSubAssets<AnimationClip>();
            bool fishing = AddFishingStates(controller, clips);
            bool hit = AddHitState(controller, clips);
            bool combat = AddCombatStates(controller, clips);
            bool loot = AddLootStates(controller, clips);
            // The abilities that play the new states, on the player already in the scene.
            foreach (var player in Object.FindObjectsByType<WowCharacterController>())
            {
                if (player.GetComponent<WarriorAbilities>() == null)
                    Undo.AddComponent<WarriorAbilities>(player.gameObject);
            }
            if (fishing || hit || combat || loot)
            {
                EditorUtility.SetDirty(controller);
                AssetDatabase.SaveAssets();
                Debug.Log("[WarriorSetup] Warrior controller updated:" + (fishing ? " fishing" : "") +
                          (hit ? " hit reaction" : "") + (combat ? " combat" : "") + (loot ? " looting" : "") + ".", controller);
            }
        }

        /// <summary>
        /// Hit (trigger): a flinch from the CombatWound clip that plays through and hands back to
        /// locomotion. HealthController fires it when a melee blow lands. Replaces any Hit state
        /// already there.
        /// </summary>
        static bool AddHitState(AnimatorController controller, AnimationClip[] clips)
        {
            var wound = CopyClip(clips, "CombatWound", loop: false);
            if (wound == null)
                return false;

            var stateMachine = controller.layers[0].stateMachine;
            var locomotion = stateMachine.defaultState;
            foreach (var child in stateMachine.states)
            {
                if (child.state.name == "Hit")
                    stateMachine.RemoveState(child.state);
            }
            foreach (var parameter in controller.parameters)
            {
                if (parameter.name == "Hit")
                    controller.RemoveParameter(parameter);
            }

            controller.AddParameter("Hit", AnimatorControllerParameterType.Trigger);
            var hitState = stateMachine.AddState("Hit");
            hitState.motion = wound;

            var toHit = stateMachine.AddAnyStateTransition(hitState);
            toHit.AddCondition(AnimatorConditionMode.If, 0f, "Hit");
            toHit.duration = 0.05f;
            toHit.hasExitTime = false;
            toHit.canTransitionToSelf = false;

            var back = hitState.AddTransition(locomotion);
            back.hasExitTime = true;
            back.exitTime = 0.85f;
            back.duration = 0.1f;
            return true;
        }

        // Player-race models carry FishingCast/FishingLoop. Creature models (the old warrior)
        // don't, so those fall back to an overhand throw for the cast and the thrown-weapon ready
        // pose held while you wait. A cheer for a prize fish either way.
        const string FishCastClip = "FishingCast";
        const string FishWaitClip = "FishingLoop";
        const string FishCastFallback = "AttackThrown";
        const string FishWaitFallback = "ReadyThrown";
        const string CheerClip = "EmoteCheer";
        static readonly string[] FishingStates = { "FishCast", "FishWait", "Cheer" };
        static readonly string[] FishingParameters = { "FishCast", "Fishing", "Cheer" };

        /// <summary>
        /// FishCast (trigger) plays the cast and settles into the wait pose, held while Fishing
        /// (bool) is true; Cheer (trigger) plays once. All return to locomotion. Replaces any
        /// fishing states already there. False if the model lacks the clips.
        /// </summary>
        static bool AddFishingStates(AnimatorController controller, AnimationClip[] clips)
        {
            var cast = CopyClip(clips, HasClip(clips, FishCastClip) ? FishCastClip : FishCastFallback, loop: false);
            var wait = CopyClip(clips, HasClip(clips, FishWaitClip) ? FishWaitClip : FishWaitFallback, loop: true);
            var cheer = CopyClip(clips, CheerClip, loop: false);
            if (cast == null || wait == null)
            {
                Debug.LogWarning($"[WarriorSetup] No {FishCastClip}/{FishWaitClip} (or {FishCastFallback}/{FishWaitFallback}) clips — the player will fish standing still.");
                return false;
            }

            var stateMachine = controller.layers[0].stateMachine;
            var locomotion = stateMachine.defaultState;
            foreach (var child in stateMachine.states)
            {
                if (FishingStates.Contains(child.state.name))
                    stateMachine.RemoveState(child.state);
            }
            foreach (var parameter in controller.parameters)
            {
                if (FishingParameters.Contains(parameter.name))
                    controller.RemoveParameter(parameter);
            }

            controller.AddParameter("FishCast", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Fishing", AnimatorControllerParameterType.Bool);

            var castState = stateMachine.AddState("FishCast");
            castState.motion = cast;
            var waitState = stateMachine.AddState("FishWait");
            waitState.motion = wait;

            var toCast = stateMachine.AddAnyStateTransition(castState);
            toCast.AddCondition(AnimatorConditionMode.If, 0f, "FishCast");
            toCast.duration = 0.1f;
            toCast.hasExitTime = false;
            toCast.canTransitionToSelf = false;

            var settle = castState.AddTransition(waitState);
            settle.hasExitTime = true;
            settle.exitTime = 0.9f;
            settle.duration = 0.2f;

            // Interrupted mid-throw, or done waiting.
            foreach (var state in new[] { castState, waitState })
            {
                var stop = state.AddTransition(locomotion);
                stop.AddCondition(AnimatorConditionMode.IfNot, 0f, "Fishing");
                stop.hasExitTime = false;
                stop.duration = 0.2f;
            }

            if (cheer != null)
            {
                controller.AddParameter("Cheer", AnimatorControllerParameterType.Trigger);
                var cheerState = stateMachine.AddState("Cheer");
                cheerState.motion = cheer;

                var toCheer = stateMachine.AddAnyStateTransition(cheerState);
                toCheer.AddCondition(AnimatorConditionMode.If, 0f, "Cheer");
                toCheer.duration = 0.1f;
                toCheer.hasExitTime = false;
                toCheer.canTransitionToSelf = false;

                var back = cheerState.AddTransition(locomotion);
                back.hasExitTime = true;
                back.exitTime = 0.9f;
                back.duration = 0.2f;
            }

            return true;
        }

        static readonly string[] LootStates = { "LootKneel", "LootHold", "LootRise", "LootReach" };
        static readonly string[] LootParameters = { "Looting", "LootDown", "LootGrab" };

        /// <summary>
        /// Looting, as in WoW: LootDown (trigger) kneels (KneelStart) and you stay down
        /// (KneelLoop) while Looting (bool) is true — the loot window open, you standing still —
        /// then get up when it ends (KneelEnd). A trigger starts it, not the bool, so the hold
        /// doesn't restart the kneel. LootGrab (trigger) is the quick reach for a corpse with only coin
        /// on it (Loot). Replaces any looting states already there. False without the clips.
        /// </summary>
        static bool AddLootStates(AnimatorController controller, AnimationClip[] clips)
        {
            var kneel = CopyVariation(clips, "KneelStart", 0);
            var hold = CopyVariation(clips, "KneelLoop", 0, loop: true);
            var rise = CopyVariation(clips, "KneelEnd", 0);
            var reach = CopyVariation(clips, "Loot", 0);
            if (kneel == null || hold == null)
            {
                Debug.LogWarning("[WarriorSetup] No KneelStart/KneelLoop clips — looting won't kneel.");
                return false;
            }

            var stateMachine = controller.layers[0].stateMachine;
            var locomotion = stateMachine.defaultState;
            foreach (var child in stateMachine.states)
                if (LootStates.Contains(child.state.name))
                    stateMachine.RemoveState(child.state);
            foreach (var parameter in controller.parameters)
                if (LootParameters.Contains(parameter.name))
                    controller.RemoveParameter(parameter);
            controller.AddParameter("Looting", AnimatorControllerParameterType.Bool);
            controller.AddParameter("LootDown", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("LootGrab", AnimatorControllerParameterType.Trigger);

            var kneelState = stateMachine.AddState("LootKneel");
            kneelState.motion = kneel;
            var holdState = stateMachine.AddState("LootHold");
            holdState.motion = hold;

            var down = stateMachine.AddAnyStateTransition(kneelState);
            down.AddCondition(AnimatorConditionMode.If, 0f, "LootDown");
            down.duration = 0.15f;
            down.hasExitTime = false;
            down.canTransitionToSelf = false;

            var stay = kneelState.AddTransition(holdState);
            stay.hasExitTime = true;
            stay.exitTime = 0.95f;
            stay.duration = 0.1f;

            // Getting up: the rise if there is one, then back to locomotion.
            AnimatorState up = locomotion;
            if (rise != null)
            {
                up = stateMachine.AddState("LootRise");
                up.motion = rise;
                PlayThrough(up, locomotion, 0.85f);
            }
            foreach (var state in new[] { kneelState, holdState })
            {
                var stop = state.AddTransition(up);
                stop.AddCondition(AnimatorConditionMode.IfNot, 0f, "Looting");
                stop.hasExitTime = false;
                stop.duration = 0.15f;
            }

            if (reach != null)
            {
                var reachState = stateMachine.AddState("LootReach");
                reachState.motion = reach;
                var grab = stateMachine.AddAnyStateTransition(reachState);
                grab.AddCondition(AnimatorConditionMode.If, 0f, "LootGrab");
                grab.duration = 0.1f;
                grab.hasExitTime = false;
                grab.canTransitionToSelf = false;
                PlayThrough(reachState, locomotion, 0.9f);
            }
            return true;
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

            // Re-running replaces the player already in the scene, on the same spot. Nothing holds
            // a reference to it — everything finds the player by type at Play.
            Vector3 spawnPosition = Vector3.zero;
            Quaternion spawnRotation = Quaternion.identity;
            foreach (var existing in Object.FindObjectsByType<WowCharacterController>())
            {
                spawnPosition = existing.transform.position;
                spawnRotation = existing.transform.rotation;
                Undo.DestroyObjectImmediate(existing.gameObject);
            }

            var root = new GameObject("WarriorPlayer");
            root.transform.SetPositionAndRotation(spawnPosition, spawnRotation);
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

            // Character exports leave any equipped helm at the feet — put it back on the head.
            HelmFitter.Fit(root, instance);

            var movement = root.AddComponent<WowCharacterController>();
            // The model child carries the facing offset above and the swim pitch at runtime.
            movement.modelRoot = instance.transform;
            // Set explicitly rather than relying on the component default, so a re-import
            // keeps the tuned swim pose even if that default changes later.
            movement.swimPitch = 0f;

            // A starting human fighter: 5e's standard array, STR first. HealthController
            // takes its max HP from these, so they go on before it.
            var stats = root.AddComponent<CharacterStats>();
            stats.characterName = "Warrior";
            stats.className = "Fighter";
            stats.strength = 15;
            stats.constitution = 14;
            stats.dexterity = 13;
            stats.wisdom = 12;
            stats.intelligence = 10;
            stats.charisma = 8;
            stats.hitDie = 10;
            // Weapon and armour come from Inventory's starter kit (chain mail, longsword, shield).

            root.AddComponent<HealthController>();
            root.AddComponent<BreathController>();
            root.AddComponent<TargetingController>();
            root.AddComponent<WarriorAbilities>();
            root.AddComponent<PlayerFrameHud>();
            root.AddComponent<TargetFrameHud>();
            var nameplates = root.AddComponent<NameplateHud>();
            root.AddComponent<CombatTextHud>();
            root.AddComponent<Inventory>();
            // Shows what's worn, from WoW Sandbox → Harvest Gear's library (bare until that's run).
            root.AddComponent<EquipmentVisuals>().library = GearHarvester.LoadLibrary();
            root.AddComponent<CharacterSheetPanel>();
            root.AddComponent<InventoryPanel>();
            root.AddComponent<LootPanel>();
            // Add Vendor puts this on the player too, but a respawn replaces the player, so it
            // goes on here as well; it's idle until there's a vendor to click.
            root.AddComponent<VendorPanel>();
            root.AddComponent<MinimapHud>();
            root.AddComponent<WorldMapPanel>();
            root.AddComponent<DeepSeaDanger>();
            root.AddComponent<DeathScreen>();

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
            movement.attackRange *= scale;
            nameplates.maxDistance *= scale;

            Undo.RegisterCreatedObjectUndo(root, "Spawn Warrior Player");
            Selection.activeGameObject = root;
            Debug.Log($"[WarriorSetup] Spawned WarriorPlayer (model height {height:F2} units). " +
                      "Press Play: W/S move, A/D turn, Q/E strafe, right-drag steers, left-drag orbits, " +
                      "left-click targets, 1 attacks, C opens the character sheet, B the bags, M the map. " +
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
            // Held, not looped: the body stays down until the death screen's Respawn.
            var death = CopyClip(clips, "Death", loop: false);

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

            if (death != null)
            {
                controller.AddParameter("Death", AnimatorControllerParameterType.Trigger);
                var deathState = stateMachine.AddState("Death");
                deathState.motion = death;

                // From anywhere, swimming included, and no way back out: HealthController
                // rebinds the Animator to its default state when you respawn.
                var toDeath = stateMachine.AddAnyStateTransition(deathState);
                toDeath.AddCondition(AnimatorConditionMode.If, 0f, "Death");
                toDeath.duration = 0.1f;
                toDeath.hasExitTime = false;
                toDeath.canTransitionToSelf = false;
            }

            AddJumpStates(controller, clips);
            AddFishingStates(controller, clips);
            AddHitState(controller, clips);
            AddCombatStates(controller, clips);

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return controller;
        }

        /// <summary>
        /// Jump (trigger) plays JumpStart, then the airborne Jump loop if still in the air, and
        /// JumpEnd the moment Grounded comes back, from either. The clips run long next to a real
        /// jump (take-off 0.83 s, landing 1.5 s, for a hop of about 0.75 s), so landing never
        /// waits for the take-off to finish, and the landing hands back to locomotion early, at
        /// once if you're moving. WowCharacterController holds Grounded false briefly after
        /// take-off, so the first frames can't land it. Landing in water goes straight to
        /// swimming. Only jumps are animated — walking off a ledge keeps the running clip (no Fall
        /// state). Skipped if the model lacks the clips.
        /// </summary>
        static void AddJumpStates(AnimatorController controller, AnimationClip[] clips)
        {
            if (!HasClip(clips, "JumpStart") || !HasClip(clips, "Jump") || !HasClip(clips, "JumpEnd"))
                return;

            var start = CopyClip(clips, "JumpStart", loop: false);
            var air = CopyClip(clips, "Jump", loop: true);
            var land = CopyClip(clips, "JumpEnd", loop: false);

            var stateMachine = controller.layers[0].stateMachine;
            var locomotion = stateMachine.defaultState;
            var swim = stateMachine.states.Select(s => s.state).FirstOrDefault(s => s.name == "Swim");

            var startState = stateMachine.AddState("JumpStart");
            startState.motion = start;
            var airState = stateMachine.AddState("JumpAir");
            airState.motion = air;
            var landState = stateMachine.AddState("JumpEnd");
            landState.motion = land;

            var takeOff = locomotion.AddTransition(startState);
            takeOff.AddCondition(AnimatorConditionMode.If, 0f, "Jump");
            takeOff.hasExitTime = false;
            takeOff.duration = 0.05f;

            var rise = startState.AddTransition(airState);
            rise.hasExitTime = true;
            rise.exitTime = 0.9f;
            rise.duration = 0.1f;

            foreach (var state in new[] { startState, airState })
            {
                var touchDown = state.AddTransition(landState);
                touchDown.AddCondition(AnimatorConditionMode.If, 0f, "Grounded");
                touchDown.hasExitTime = false;
                touchDown.duration = 0.05f;
            }

            // Standing still: just the impact and a little of the recovery.
            var recover = landState.AddTransition(locomotion);
            recover.hasExitTime = true;
            recover.exitTime = 0.3f;
            recover.duration = 0.15f;

            // Moving: straight back into the run.
            var runOn = landState.AddTransition(locomotion);
            runOn.AddCondition(AnimatorConditionMode.Greater, 0.25f, "Speed");
            runOn.hasExitTime = false;
            runOn.duration = 0.1f;

            if (swim != null)
            {
                foreach (var state in new[] { startState, airState, landState })
                {
                    var splash = state.AddTransition(swim);
                    splash.AddCondition(AnimatorConditionMode.If, 0f, "Swimming");
                    splash.hasExitTime = false;
                    splash.duration = 0.25f;
                }
            }
        }

        // Weapon styles, as WowCharacterController sets the WeaponStyle parameter.
        const int StyleUnarmed = 0, StyleOneHand = 1, StyleTwoHand = 2, StylePierce = 3;

        static readonly string[] CombatStates =
        {
            "Attack", "AttackUnarmed0", "AttackUnarmed1", "Attack1H0", "Attack1H1", "Attack2H0", "Attack2H1",
            "AttackPierce", "AttackCrit", "CombatUnarmed", "Combat1H", "Combat2H",
            "ParryUnarmed", "Parry1H", "Parry2H", "ShieldBlock", "Dodge",
            "HeroicStrike1H", "HeroicStrike2H", "ShieldBash", "Whirlwind", "ChargeLoop", "ChargeEnd",
        };
        static readonly string[] CombatParameters =
        {
            "WeaponStyle", "AttackVariant", "AttackCrit", "InCombat", "Parry", "Block", "Dodge",
            "HeroicStrike", "ShieldBash", "Whirlwind", "Charging",
        };

        /// <summary>
        /// The fight, per weapon. WowCharacterController sets WeaponStyle from the main hand
        /// (0 fists, 1 one-hander, 2 two-hander, 3 dagger) and flips AttackVariant each swing:
        /// - Attack (trigger) swings with that weapon's clip, alternating the two variations
        ///   where the model has them; AttackCrit (trigger) plays CombatCritical instead.
        /// - InCombat (bool) swaps the idle for that weapon's fighting stance (Ready1H, Ready2H,
        ///   ReadyUnarmed), walking and running as usual.
        /// - Parry, Block and Dodge (triggers) are the reactions to a blow that misses.
        /// - WarriorAbilities' moves: HeroicStrike, ShieldBash and Whirlwind (triggers), and
        ///   Charging (bool) — the charge's run, then its strike when it ends.
        /// Replaces the plain Attack state and anything this added before; returns false, leaving
        /// the controller alone, if the model lacks the clips (the old creature warrior).
        /// </summary>
        static bool AddCombatStates(AnimatorController controller, AnimationClip[] clips)
        {
            if (!HasClip(clips, "Attack1H") || !HasClip(clips, "Ready1H"))
                return false;

            var stateMachine = controller.layers[0].stateMachine;
            var locomotion = stateMachine.defaultState;
            if (locomotion == null || locomotion.motion is not BlendTree baseTree || baseTree.children.Length < 3)
                return false;

            foreach (var child in stateMachine.states)
            {
                if (CombatStates.Contains(child.state.name))
                    stateMachine.RemoveState(child.state);
            }
            foreach (var parameter in controller.parameters)
            {
                if (CombatParameters.Contains(parameter.name))
                    controller.RemoveParameter(parameter);
            }
            controller.AddParameter("WeaponStyle", AnimatorControllerParameterType.Int);
            controller.AddParameter("AttackVariant", AnimatorControllerParameterType.Int);
            controller.AddParameter("AttackCrit", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("InCombat", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Parry", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Block", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Dodge", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("HeroicStrike", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("ShieldBash", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Whirlwind", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Charging", AnimatorControllerParameterType.Bool);

            // --- Swings --------------------------------------------------------------------
            void Swing(string stateName, string clip, int variation, int style, int? variant)
            {
                var motion = CopyVariation(clips, clip, variation) ?? CopyVariation(clips, clip, 0);
                if (motion == null)
                    return;
                var state = stateMachine.AddState(stateName);
                state.motion = motion;
                var into = stateMachine.AddAnyStateTransition(state);
                into.AddCondition(AnimatorConditionMode.If, 0f, "Attack");
                into.AddCondition(AnimatorConditionMode.Equals, style, "WeaponStyle");
                if (variant.HasValue)
                    into.AddCondition(AnimatorConditionMode.Equals, variant.Value, "AttackVariant");
                into.duration = 0.05f;
                into.hasExitTime = false;
                into.canTransitionToSelf = false;
                PlayThrough(state, locomotion, 0.85f);
            }
            Swing("AttackUnarmed0", "AttackUnarmed", 0, StyleUnarmed, 0);
            Swing("AttackUnarmed1", "AttackUnarmed", 1, StyleUnarmed, 1);
            Swing("Attack1H0", "Attack1H", 0, StyleOneHand, 0);
            Swing("Attack1H1", "Attack1H", 1, StyleOneHand, 1);
            Swing("Attack2H0", "Attack2H", 0, StyleTwoHand, 0);
            Swing("Attack2H1", "Attack2H", 1, StyleTwoHand, 1);
            Swing("AttackPierce", "Attack1HPierce", 0, StylePierce, null);

            var critClip = CopyVariation(clips, "CombatCritical", 0);
            if (critClip != null)
            {
                var crit = stateMachine.AddState("AttackCrit");
                crit.motion = critClip;
                var into = stateMachine.AddAnyStateTransition(crit);
                into.AddCondition(AnimatorConditionMode.If, 0f, "AttackCrit");
                into.duration = 0.05f;
                into.hasExitTime = false;
                into.canTransitionToSelf = false;
                PlayThrough(crit, locomotion, 0.85f);
            }

            // --- Fighting stance -----------------------------------------------------------
            // A copy of the locomotion tree with the weapon's ready pose as its idle.
            var walk = baseTree.children[1].motion;
            var run = baseTree.children[2].motion;
            var swim = stateMachine.states.Select(c => c.state).FirstOrDefault(st => st.name == "Swim");
            var jump = stateMachine.states.Select(c => c.state).FirstOrDefault(st => st.name == "JumpStart");
            void Stance(string stateName, string readyClip, params int[] styles)
            {
                var ready = CopyVariation(clips, readyClip, 0, loop: true);
                if (ready == null)
                    return;
                var state = controller.CreateBlendTreeInController(stateName, out BlendTree tree);
                tree.blendType = BlendTreeType.Simple1D;
                tree.blendParameter = "Speed";
                tree.useAutomaticThresholds = false;
                tree.AddChild(ready, 0f);
                tree.AddChild(walk, 0.5f);
                tree.AddChild(run, 1f);

                foreach (int style in styles)
                {
                    var into = locomotion.AddTransition(state);
                    into.AddCondition(AnimatorConditionMode.If, 0f, "InCombat");
                    into.AddCondition(AnimatorConditionMode.Equals, style, "WeaponStyle");
                    into.hasExitTime = false;
                    into.duration = 0.25f;
                }

                // Out of the fight, or a weapon this stance isn't for: back to locomotion,
                // which picks the right stance again on the next frame.
                var calm = state.AddTransition(locomotion);
                calm.AddCondition(AnimatorConditionMode.IfNot, 0f, "InCombat");
                calm.hasExitTime = false;
                calm.duration = 0.4f;
                foreach (int other in new[] { StyleUnarmed, StyleOneHand, StyleTwoHand, StylePierce }.Except(styles))
                {
                    var swap = state.AddTransition(locomotion);
                    swap.AddCondition(AnimatorConditionMode.Equals, other, "WeaponStyle");
                    swap.hasExitTime = false;
                    swap.duration = 0.2f;
                }

                if (swim != null)
                {
                    var splash = state.AddTransition(swim);
                    splash.AddCondition(AnimatorConditionMode.If, 0f, "Swimming");
                    splash.hasExitTime = false;
                    splash.duration = 0.25f;
                }
                if (jump != null)
                {
                    var takeOff = state.AddTransition(jump);
                    takeOff.AddCondition(AnimatorConditionMode.If, 0f, "Jump");
                    takeOff.hasExitTime = false;
                    takeOff.duration = 0.05f;
                }
            }
            Stance("CombatUnarmed", "ReadyUnarmed", StyleUnarmed);
            Stance("Combat1H", "Ready1H", StyleOneHand, StylePierce);
            Stance("Combat2H", "Ready2H", StyleTwoHand);

            // --- Reactions to a miss -------------------------------------------------------
            void Reaction(string stateName, string clip, string trigger, params int[] styles)
            {
                var motion = CopyVariation(clips, clip, 0);
                if (motion == null)
                    return;
                var state = stateMachine.AddState(stateName);
                state.motion = motion;
                // Any weapon when no styles are given; otherwise one transition per style.
                foreach (int? style in styles.Length == 0 ? new int?[] { null } : styles.Select(x => (int?)x))
                {
                    var into = stateMachine.AddAnyStateTransition(state);
                    into.AddCondition(AnimatorConditionMode.If, 0f, trigger);
                    if (style.HasValue)
                        into.AddCondition(AnimatorConditionMode.Equals, style.Value, "WeaponStyle");
                    into.duration = 0.05f;
                    into.hasExitTime = false;
                    into.canTransitionToSelf = false;
                }
                PlayThrough(state, locomotion, 0.8f);
            }
            Reaction("ParryUnarmed", "ParryUnarmed", "Parry", StyleUnarmed);
            Reaction("Parry1H", "Parry1H", "Parry", StyleOneHand, StylePierce);
            Reaction("Parry2H", "Parry2H", "Parry", StyleTwoHand);
            Reaction("ShieldBlock", "ShieldBlock", "Block");
            Reaction("Dodge", "Dodge", "Dodge");

            // --- Abilities -----------------------------------------------------------------
            // One-shots like the reactions: a trigger from anywhere, then back to locomotion.
            Reaction("HeroicStrike1H", "Special1H", "HeroicStrike", StyleUnarmed, StyleOneHand, StylePierce);
            Reaction("HeroicStrike2H", "Special2H", "HeroicStrike", StyleTwoHand);
            Reaction("ShieldBash", "ShieldBash", "ShieldBash");
            Reaction("Whirlwind", "CombatWhirlwind", "Whirlwind");

            var chargeRun = CopyVariation(clips, "CombatChargeLoop", 0, loop: true);
            var chargeHit = CopyVariation(clips, "CombatChargeEnd", 0);
            if (chargeRun != null)
            {
                var loop = stateMachine.AddState("ChargeLoop");
                loop.motion = chargeRun;
                var into = stateMachine.AddAnyStateTransition(loop);
                into.AddCondition(AnimatorConditionMode.If, 0f, "Charging");
                into.duration = 0.1f;
                into.hasExitTime = false;
                into.canTransitionToSelf = false;

                // Arriving (or giving up) ends the run with the charge's strike.
                AnimatorState end = locomotion;
                if (chargeHit != null)
                {
                    end = stateMachine.AddState("ChargeEnd");
                    end.motion = chargeHit;
                    PlayThrough(end, locomotion, 0.85f);
                }
                var stop = loop.AddTransition(end);
                stop.AddCondition(AnimatorConditionMode.IfNot, 0f, "Charging");
                stop.hasExitTime = false;
                stop.duration = 0.05f;
            }
            return true;
        }

        /// <summary>A one-shot state that plays (most of) its clip and hands back to <paramref name="to"/>.</summary>
        static void PlayThrough(AnimatorState state, AnimatorState to, float exitTime)
        {
            var back = state.AddTransition(to);
            back.hasExitTime = true;
            back.exitTime = exitTime;
            back.duration = 0.1f;
        }

        /// <summary>
        /// A looping-or-not copy of one variation of a clip ("Attack1H (ID 17 variation 1)"),
        /// saved as Attack1H_v1. Null if the model hasn't that variation.
        /// </summary>
        static AnimationClip CopyVariation(AnimationClip[] source, string namePrefix, int variation, bool loop = false)
        {
            var original = source.FirstOrDefault(c => c.name.StartsWith(namePrefix + " (") &&
                                                      c.name.EndsWith($"variation {variation})"));
            if (original == null)
                return null;

            string path = $"{GeneratedRoot}/{namePrefix}_v{variation}.anim";
            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (existing != null)
            {
                // Reused rather than recreated, so states elsewhere that point at it stay pointed.
                EditorUtility.CopySerialized(original, existing);
                existing.name = $"{namePrefix}_v{variation}";
                SetLoop(existing, loop);
                return existing;
            }

            var copy = Object.Instantiate(original);
            copy.name = $"{namePrefix}_v{variation}";
            SetLoop(copy, loop);
            AssetDatabase.CreateAsset(copy, path);
            return copy;
        }

        static void SetLoop(AnimationClip clip, bool loop)
        {
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = loop;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
        }

        static bool HasClip(AnimationClip[] clips, string namePrefix) =>
            clips.Any(c => c.name.StartsWith(namePrefix + " ") || c.name == namePrefix);

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
