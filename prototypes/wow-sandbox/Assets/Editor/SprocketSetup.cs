using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace WowSandbox.EditorTools
{
    /// <summary>
    /// Puts Sprocket Wizzlecrank in the gnome hut: the gnome export, standing on the hut's floor
    /// in the middle of its interior, with a Tinkerer (works at the bench, turns to you), a
    /// Friendly Health, a Speaker framed for a gnome's big head, and the purple-glow goggles on
    /// his head. Move and turn him to face his bench, then save the scene; he faces that way
    /// while he works.
    ///
    /// Running it again with Sprocket already in the scene refits his goggles (after exporting
    /// the gnome goggles or the raw gnome model) and selects him.
    /// </summary>
    public static class SprocketSetup
    {
        const string ModelPath = "Assets/WowExports/character/gnome/male/gnomemale_hd.gltf";
        /// <summary>WoW makes a helm model per race; this is the gnome male's (exported under its texture's name).</summary>
        const string GogglesDir = "Assets/WowExports/item/objectcomponents/head";
        /// <summary>The purple-glow pair, or failing that any gnome male's goggles.</summary>
        static readonly string[] GogglesPatterns = { "helm_goggles_c_01_gnm*.gltf", "helm_goggles_*_gnm*.gltf" };
        /// <summary>The raw gnome model, for WoW's helmet attachment point (the glTF leaves it out).</summary>
        const string RawModelName = "gnomemale_hd";
        /// <summary>Without the raw export: how far below the top of the head (of its height) the goggles' top sits.</summary>
        const float GuessLower = 0.22f;
        // His dialogue portrait: where it aims and how far back it stands, as fractions of his height.
        const float PortraitHeight = 0.62f;
        const float PortraitDistance = 0.9f;
        const string GeneratedDir = "Assets/WowExports/_Generated/Sprocket";
        const string NpcName = "Sprocket Wizzlecrank";
        const string HutName = "gnomehut";
        const string HeadBoneName = "bone_Head";
        const string GogglesPrefix = "Goggles_";
        /// <summary>Same -X facing convention as every other M2; see WarriorSetup.ModelYawOffset.</summary>
        const float ModelYawOffset = 90f;

        const string WorkshopName = "Sprocket's Workshop";
        const string BenchPath = "Assets/WowExports/world/expansion09/doodads/profession/10pr_engineeringtable03.gltf";
        const string GearStackPath = "Assets/WowExports/world/expansion07/doodads/gnome/8gn_gnome_gearcogstack01.gltf";
        static readonly string[] LooseGearPaths =
        {
            "Assets/WowExports/world/expansion07/doodads/gnome/8gn_gnome_gearcogsmall01.gltf",
            "Assets/WowExports/world/expansion07/doodads/gnome/8gn_gnome_gearcogsmall02.gltf",
        };
        /// <summary>The gap between Sprocket and his bench's near edge.</summary>
        const float BenchGap = 0.25f;

        [MenuItem("WoW Sandbox/Add Sprocket")]
        static void AddSprocket()
        {
            var existing = Object.FindAnyObjectByType<Tinkerer>();
            if (existing != null)
            {
                var model = existing.GetComponentInChildren<Animator>();
                if (model != null)
                {
                    FitGoggles(existing.gameObject, model.gameObject);
                    if (model.runtimeAnimatorController is UnityEditor.Animations.AnimatorController existingController)
                        M2AnimatorBuilder.AddOneShots(existingController, ModelPath, GeneratedDir, M2AnimatorBuilder.TalkGestures);
                }
                if (existing.TryGetComponent<Speaker>(out var existingSpeaker))
                    FramePortrait(existingSpeaker);
                Selection.activeGameObject = existing.gameObject;
                EditorSceneManager.MarkSceneDirty(existing.gameObject.scene);
                Debug.Log("[SprocketSetup] Sprocket's already here — refitted his goggles, reframed his portrait, added any missing talk gestures and selected him. " +
                          "Delete him first to place a fresh one.", existing);
                return;
            }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (prefab == null)
            {
                Debug.LogError($"[SprocketSetup] No model at {ModelPath}. Export the gnome male from wow.export as glTF there.");
                return;
            }

            var controller = M2AnimatorBuilder.BuildLocomotion(ModelPath, GeneratedDir);
            if (controller != null)
            {
                M2AnimatorBuilder.AddOneShots(controller, ModelPath, GeneratedDir, ("Wave", "EmoteWave"));
                M2AnimatorBuilder.AddOneShots(controller, ModelPath, GeneratedDir, M2AnimatorBuilder.TalkGestures);
                if (!M2AnimatorBuilder.AddLoop(controller, ModelPath, GeneratedDir, "Working", "EmoteWork"))
                    Debug.LogWarning("[SprocketSetup] No EmoteWork clip in the gnome export — he'll just stand at his bench.");
            }

            var hut = GameObject.Find(HutName);
            Vector3 spot;
            Quaternion facing;
            if (hut != null)
                InsideHut(hut, out spot, out facing);
            else
            {
                Debug.LogWarning($"[SprocketSetup] No \"{HutName}\" in the scene — placing him where the Scene view is looking.");
                var view = SceneView.lastActiveSceneView;
                spot = view != null ? view.pivot : Vector3.zero;
                facing = Quaternion.identity;
            }

            var root = new GameObject(NpcName);
            Undo.RegisterCreatedObjectUndo(root, "Add Sprocket");
            root.transform.SetPositionAndRotation(spot, facing);

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.transform);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.Euler(0f, ModelYawOffset, 0f);
            float height = MeasureHeight(instance);
            Vendor.HideEyeGlow(instance);

            // Something for TargetingController's click to hit.
            var capsule = root.AddComponent<CapsuleCollider>();
            capsule.height = height;
            capsule.radius = Mathf.Max(height * 0.25f, 0.1f);
            capsule.center = new Vector3(0f, height * 0.5f, 0f);

            var health = root.AddComponent<Health>();
            health.reaction = Reaction.Friendly;
            root.AddComponent<Tinkerer>();
            FramePortrait(root.AddComponent<Speaker>());

            if (controller != null)
            {
                var animator = instance.GetComponentInChildren<Animator>();
                if (animator == null)
                    animator = instance.AddComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
            }

            FitGoggles(root, instance);

            Selection.activeGameObject = root;
            EditorSceneManager.MarkSceneDirty(root.scene);
            Debug.Log($"[SprocketSetup] Placed {NpcName} in the gnome hut. Move him to his bench and turn him to " +
                      "face it, then save the scene.", root);
        }

        /// <summary>
        /// Sets out Sprocket's workshop round him as he stands: the engineering table in front of
        /// him, its long side across his front, the gear stack at its right-hand end and a couple
        /// of loose cogs on the floor by it. All under one "Sprocket's Workshop" object (in the
        /// hut, so it moves with it), replaced on each run — move Sprocket, then run it again.
        /// </summary>
        [MenuItem("WoW Sandbox/Dress Sprocket's Workshop")]
        static void DressWorkshop()
        {
            var sprocket = Object.FindAnyObjectByType<Tinkerer>();
            if (sprocket == null)
            {
                Debug.LogError("[SprocketSetup] No Sprocket in the scene — run Add Sprocket first.");
                return;
            }

            var hut = GameObject.Find(HutName);
            var old = hut != null ? hut.transform.Find(WorkshopName)?.gameObject : GameObject.Find(WorkshopName);
            if (old != null)
                Undo.DestroyObjectImmediate(old);
            var workshop = new GameObject(WorkshopName);
            Undo.RegisterCreatedObjectUndo(workshop, "Dress Sprocket's Workshop");
            if (hut != null)
                workshop.transform.SetParent(hut.transform, true);

            var him = sprocket.transform;
            var forward = Vector3.ProjectOnPlane(him.forward, Vector3.up).normalized;
            var right = Vector3.Cross(Vector3.up, forward);
            var floors = hut != null ? hut.GetComponentsInChildren<Collider>() : new Collider[0];

            var bench = Place(BenchPath, workshop.transform, him.position, forward, floors, solid: true);
            if (bench == null)
            {
                Debug.LogError($"[SprocketSetup] No bench model at {BenchPath} — export it from wow.export.");
                return;
            }
            // Long side across his front, then pushed out till its near edge is BenchGap away.
            var flat = InteractableSetup.RendererBounds(bench);
            if (Mathf.Abs(Vector3.Dot(flat.size, forward)) > Mathf.Abs(Vector3.Dot(flat.size, right)))
                bench.transform.rotation = Quaternion.AngleAxis(90f, Vector3.up) * bench.transform.rotation;
            flat = InteractableSetup.RendererBounds(bench);
            float depth = Mathf.Abs(Vector3.Dot(flat.size, forward));
            float width = Mathf.Abs(Vector3.Dot(flat.size, right));
            bench.transform.position += him.position + forward * (BenchGap + depth * 0.5f) - flat.center;
            Settle(bench, floors);
            Object.DestroyImmediate(bench.GetComponent<BoxCollider>());
            InteractableSetup.AddBoxCollider(bench, trigger: false, minSize: 0.1f);

            // At the bench's right-hand end, as he faces it, and level with its middle.
            var benchMiddle = him.position + forward * (BenchGap + depth * 0.5f);
            var stack = Place(GearStackPath, workshop.transform, benchMiddle + right * (width * 0.5f + 0.45f),
                              forward, floors, solid: true);
            int loose = 0;
            foreach (var path in LooseGearPaths)
            {
                var spot = benchMiddle + right * (width * 0.5f + 0.35f) - forward * (0.45f + 0.3f * loose);
                var yaw = Quaternion.AngleAxis(Random.Range(0f, 360f), Vector3.up) * forward;
                if (Place(path, workshop.transform, spot, yaw, floors, solid: false) != null)
                    loose++;
            }

            Selection.activeGameObject = workshop;
            EditorSceneManager.MarkSceneDirty(workshop.scene);
            Debug.Log($"[SprocketSetup] Set out Sprocket's workshop: the bench{(stack != null ? ", the gear stack" : "")}" +
                      $"{(loose > 0 ? $" and {loose} loose cog(s)" : "")}. Nudge any of them, then save the scene.", workshop);
        }

        /// <summary>
        /// An instance of the model at <paramref name="path"/> at <paramref name="at"/>, facing
        /// <paramref name="facing"/>, sat on the floor below it; null if it isn't exported.
        /// </summary>
        static GameObject Place(string path, Transform parent, Vector3 at, Vector3 facing, Collider[] floors, bool solid)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (model == null)
            {
                Debug.LogWarning($"[SprocketSetup] No model at {path} — skipped.");
                return null;
            }
            var placed = (GameObject)PrefabUtility.InstantiatePrefab(model, parent);
            Undo.RegisterCreatedObjectUndo(placed, "Dress Sprocket's Workshop");
            placed.transform.SetPositionAndRotation(at, Quaternion.LookRotation(facing, Vector3.up));
            InteractableSetup.Glow(placed);
            InteractableSetup.Cutouts(placed);
            Settle(placed, floors);
            if (solid)
                InteractableSetup.AddBoxCollider(placed, trigger: false, minSize: 0.1f);
            return placed;
        }

        /// <summary>Drops <paramref name="placed"/> so its bottom sits on the hut floor beneath it.</summary>
        static void Settle(GameObject placed, Collider[] floors)
        {
            var bounds = InteractableSetup.RendererBounds(placed);
            var from = new Vector3(bounds.center.x, bounds.max.y + 1f, bounds.center.z);
            var hits = Physics.RaycastAll(from, Vector3.down, bounds.size.y + 4f)
                              .Where(hit => floors.Contains(hit.collider))
                              .OrderBy(hit => hit.distance);
            foreach (var hit in hits)
            {
                // The first hut surface below the model's own top: the floor (or a rug).
                if (hit.point.y > bounds.max.y)
                    continue;
                placed.transform.position += Vector3.up * (hit.point.y - bounds.min.y);
                return;
            }
        }

        /// <summary>
        /// On the hut's floor under the middle of its interior groups (the WMO's Int meshes),
        /// facing the hut's forward. Falls back to the hut's own position.
        /// </summary>
        static void InsideHut(GameObject hut, out Vector3 spot, out Quaternion facing)
        {
            facing = hut.transform.rotation;
            spot = hut.transform.position;
            var interior = hut.GetComponentsInChildren<Renderer>().Where(r => r.name.Contains("_Int")).ToArray();
            if (interior.Length == 0)
                return;
            var bounds = interior[0].bounds;
            foreach (var renderer in interior)
                bounds.Encapsulate(renderer.bounds);

            spot = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            var hutColliders = new HashSet<Collider>(hut.GetComponentsInChildren<Collider>());
            var hits = Physics.RaycastAll(bounds.center, Vector3.down, bounds.extents.y + 1f)
                              .Where(hit => hutColliders.Contains(hit.collider))
                              .OrderBy(hit => hit.distance);
            foreach (var hit in hits)
            {
                spot = hit.point;
                return;
            }
        }

        /// <summary>
        /// Puts the gnome goggles on <paramref name="model"/>'s head, through a HelmAttachment so
        /// they follow the head once Play starts. Replaces any goggles already fitted. Call with
        /// the model in its rest pose.
        ///
        /// WoW's own fit when there's a raw export of the gnome (the .m2 and .skel, anywhere under
        /// WowExports): the goggles go on its helmet attachment point, unscaled, as the game draws
        /// them. Without one, HelmFitter's guess: centred on the head, level with its top.
        /// </summary>
        static void FitGoggles(GameObject root, GameObject model)
        {
            var old = root.GetComponent<HelmAttachment>();
            if (old != null)
            {
                foreach (var piece in old.pieces)
                    if (piece != null)
                        Undo.DestroyObjectImmediate(piece.gameObject);
                Undo.DestroyObjectImmediate(old);
            }

            string gogglesPath = Directory.Exists(GogglesDir)
                ? GogglesPatterns.SelectMany(pattern => Directory.GetFiles(GogglesDir, pattern))
                                 .Select(f => f.Replace('\\', '/')).FirstOrDefault()
                : null;
            var goggles = gogglesPath != null ? AssetDatabase.LoadAssetAtPath<GameObject>(gogglesPath) : null;
            if (goggles == null)
            {
                Debug.LogWarning($"[SprocketSetup] No gnome goggles (helm_goggles_*_gnm) in {GogglesDir} — bare-headed. " +
                                 "Export helm_goggles_c_01_gnm from wow.export as glTF.");
                return;
            }

            // A character export's body mesh sits at the model's origin, in its space: where WoW
            // lays out the attachment points, and where an item model's own space goes.
            var body = model.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                            .FirstOrDefault(r => r.name.EndsWith("_Geoset0"));
            var space = body != null ? body.transform : model.transform;
            var pieces = new List<Transform>();
            foreach (var renderer in goggles.GetComponentsInChildren<Renderer>(true))
            {
                var mesh = renderer is SkinnedMeshRenderer skin ? skin.sharedMesh
                    : renderer.GetComponent<MeshFilter>() is { } filter ? filter.sharedMesh : null;
                if (mesh == null)
                    continue;
                var piece = new GameObject(GogglesPrefix + renderer.name);
                Undo.RegisterCreatedObjectUndo(piece, "Fit Goggles");
                piece.transform.SetParent(space, false);
                piece.AddComponent<MeshFilter>().sharedMesh = mesh;
                piece.AddComponent<MeshRenderer>().sharedMaterials = renderer.sharedMaterials;
                pieces.Add(piece.transform);
            }
            if (pieces.Count == 0)
            {
                Debug.LogWarning($"[SprocketSetup] No meshes in {gogglesPath} — bare-headed.");
                return;
            }

            Transform bone = null;
            Vector3 position = default;
            Quaternion rotation = Quaternion.identity;
            Vector3 scale = Vector3.one;
            string how;

            var attachments = M2Attachments.Read(FindRaw(".m2"), FindRaw(".skel"));
            if (body != null && attachments.TryGetValue(M2Attachments.Helmet, out var point) &&
                point.bone < body.bones.Length && body.bones[point.bone] != null)
            {
                // As GearHarvester.TryAttach: glTFast mirrors glTF's X into Unity's.
                bone = body.bones[point.bone];
                var offset = new Vector3(-point.gltfPosition.x, point.gltfPosition.y, point.gltfPosition.z);
                position = bone.InverseTransformPoint(space.TransformPoint(offset));
                rotation = Quaternion.Inverse(bone.rotation) * space.rotation;
                var lossy = bone.lossyScale;
                scale = new Vector3(space.lossyScale.x / lossy.x, space.lossyScale.y / lossy.y, space.lossyScale.z / lossy.z);
                how = $"on WoW's helmet attachment ({bone.name})";
            }
            else
            {
                bone = model.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == HeadBoneName);
                if (bone == null || !HelmFitter.TryFitPose(model, bone, pieces.ToArray(), usedVerticesOnly: true,
                        out position, out rotation, out scale))
                {
                    Debug.LogWarning("[SprocketSetup] No helmet attachment and no head to fit the goggles to — bare-headed.");
                    foreach (var piece in pieces)
                        Object.DestroyImmediate(piece.gameObject);
                    return;
                }
                // The guess sits them level with the top of the head, which on a gnome is the top
                // of his spiky hair: lowered this much (of the head's height) onto his brow.
                if (HelmFitter.TryHeadBounds(model, bone, space, usedVerticesOnly: true, out var headBounds))
                    position += bone.InverseTransformVector(space.TransformVector(Vector3.down * (headBounds.size.y * GuessLower)));
                how = $"by guess on {bone.name} — export {RawModelName}.m2 raw (with its .skel) for WoW's own placement";
            }

            // Shown where they'll ride from the first frame of Play, not just after Awake.
            foreach (var piece in pieces)
            {
                piece.SetParent(bone, false);
                piece.SetLocalPositionAndRotation(position, rotation);
                piece.localScale = scale;
            }

            var attachment = Undo.AddComponent<HelmAttachment>(root);
            attachment.headBone = bone;
            attachment.pieces = pieces.ToArray();
            attachment.localPosition = position;
            attachment.localRotation = rotation;
            attachment.localScale = scale;
            Debug.Log($"[SprocketSetup] Goggles from {Path.GetFileName(gogglesPath)}, {how}.", root);
        }

        /// <summary>
        /// A gnome's head is a bigger share of his height than a human's: the dialogue portrait
        /// aims lower and stands further back than Speaker's defaults (framed for Briggs).
        /// </summary>
        static void FramePortrait(Speaker speaker)
        {
            Undo.RecordObject(speaker, "Frame Portrait");
            speaker.portraitHeight = PortraitHeight;
            speaker.portraitDistance = PortraitDistance;
            EditorUtility.SetDirty(speaker);
        }

        /// <summary>The raw gnome export's file with this extension, anywhere under WowExports, or null.</summary>
        static string FindRaw(string extension) =>
            Directory.GetFiles("Assets/WowExports", RawModelName + extension, SearchOption.AllDirectories).FirstOrDefault();

        static float MeasureHeight(GameObject instance)
        {
            var renderers = instance.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
                return 1f;
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers)
                bounds.Encapsulate(renderer.bounds);
            return Mathf.Max(bounds.size.y, 0.1f);
        }
    }
}
