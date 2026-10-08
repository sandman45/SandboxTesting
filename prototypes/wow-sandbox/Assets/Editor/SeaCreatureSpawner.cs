using System.IO;
using UnityEditor;
using UnityEngine;

namespace WowSandbox.EditorTools
{
    /// <summary>
    /// Spawns sea creatures from a wow.export glTF into the water around the island:
    /// SwimmingCreature to move them, D&D 5e stats from a preset, and a Sea loot table.
    ///
    /// The presets are 5e SRD stat blocks, approximated onto CharacterStats (level stands
    /// in for hit dice, armorBonus for natural armour): Reef Shark for the toothed shark,
    /// Hunter Shark for the thresher, Giant Shark for the whale shark — neutral, since a
    /// real whale shark is a gentle filter feeder, but a brute if you start something.
    /// </summary>
    public class SeaCreatureSpawner : EditorWindow
    {
        /// <summary>Same -X facing convention as every other M2; see WarriorSetup.ModelYawOffset.</summary>
        const float ModelYawOffset = 90f;

        enum Preset { ReefShark, HunterShark, GiantShark }

        struct Stats
        {
            public string Name;
            public int Level, Str, Dex, Con, HitDie, NaturalArmor, DieCount, DieSides, Xp;
            public Reaction Reaction;
            public float Scale, Cruise, Chase;
        }

        static Stats For(Preset preset) => preset switch
        {
            // CR 1/2: AC 12, ~22 HP, bite +4 for 1d8+2.
            Preset.ReefShark => new Stats
            {
                Name = "Reef Shark", Level = 3, Str = 14, Dex = 13, Con = 13, HitDie = 8, NaturalArmor = 1,
                DieCount = 1, DieSides = 8, Xp = 100, Reaction = Reaction.Hostile, Scale = 1f, Cruise = 3f, Chase = 8f,
            },
            // CR 2: AC 12, ~45 HP (36 here), bite +6 for 2d8+4.
            Preset.HunterShark => new Stats
            {
                Name = "Hunter Shark", Level = 4, Str = 18, Dex = 13, Con = 14, HitDie = 10, NaturalArmor = 1,
                DieCount = 2, DieSides = 8, Xp = 450, Reaction = Reaction.Hostile, Scale = 1f, Cruise = 3.5f, Chase = 9f,
            },
            // CR 5: AC 13, ~126 HP, bite +9 for 3d10+6 (+5 here — scores cap at 20).
            _ => new Stats
            {
                Name = "Giant Shark", Level = 10, Str = 20, Dex = 11, Con = 20, HitDie = 12, NaturalArmor = 3,
                DieCount = 3, DieSides = 10, Xp = 1800, Reaction = Reaction.Neutral, Scale = 3f, Cruise = 2f, Chase = 5f,
            },
        };

        GameObject _model;
        Preset _preset;
        int _count = 3;
        Vector2 _ring = new(150f, 280f);
        Vector2 _depth = new(3f, 12f);
        float _roamRadius = 50f;
        float _aggroRange = 25f;

        [MenuItem("WoW Sandbox/Spawn Sea Creatures")]
        public static void ShowWindow()
        {
            var window = GetWindow<SeaCreatureSpawner>(true, "Spawn Sea Creatures");
            window.minSize = new Vector2(380f, 320f);
        }

        void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "Drag a wow.export creature .gltf into Model. Spawns swimmers in a ring of sea " +
                "around the island (distances from the terrain's centre), below the waves and " +
                "clear of the seabed. Hostile ones hunt anyone swimming nearby.",
                MessageType.None);

            EditorGUILayout.Space();
            EditorGUI.BeginChangeCheck();
            _model = (GameObject)EditorGUILayout.ObjectField("Model (.gltf)", _model, typeof(GameObject), false);
            if (EditorGUI.EndChangeCheck() && _model != null)
                _preset = Guess(_model.name);

            _preset = (Preset)EditorGUILayout.EnumPopup("Stat block", _preset);
            var stats = For(_preset);
            EditorGUILayout.LabelField(" ",
                $"{stats.Reaction}, level {stats.Level}, bite {stats.DieCount}d{stats.DieSides}, " +
                $"{stats.Xp} XP, scale {stats.Scale}×", EditorStyles.miniLabel);

            EditorGUILayout.Space();
            _count = EditorGUILayout.IntSlider("Count", _count, 1, 20);
            _ring = MinMax("Distance from centre", _ring, 0f, 600f);
            _depth = MinMax("Depth below sea level", _depth, 1f, 60f);
            _roamRadius = EditorGUILayout.Slider("Roam radius", _roamRadius, 5f, 200f);
            _aggroRange = EditorGUILayout.Slider("Aggro range", _aggroRange, 5f, 80f);

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(_model == null))
            {
                if (GUILayout.Button("Spawn", GUILayout.Height(30f)))
                    Spawn();
            }
        }

        static Vector2 MinMax(string label, Vector2 value, float min, float max)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel(label);
                value.x = EditorGUILayout.FloatField(value.x, GUILayout.Width(60f));
                EditorGUILayout.MinMaxSlider(ref value.x, ref value.y, min, max);
                value.y = EditorGUILayout.FloatField(value.y, GUILayout.Width(60f));
            }
            return value;
        }

        static Preset Guess(string modelName)
        {
            string lower = modelName.ToLowerInvariant();
            if (lower.Contains("whale"))
                return Preset.GiantShark;
            if (lower.Contains("thresh"))
                return Preset.HunterShark;
            return Preset.ReefShark;
        }

        void Spawn()
        {
            string modelPath = AssetDatabase.GetAssetPath(_model);
            var water = Object.FindAnyObjectByType<WaterVolume>();
            var terrain = Terrain.activeTerrain;
            if (string.IsNullOrEmpty(modelPath) || water == null || terrain == null)
            {
                Debug.LogError("[SeaCreatureSpawner] Needs a model asset, a WaterVolume (Setup Water) and a Terrain.");
                return;
            }

            string modelName = Path.GetFileNameWithoutExtension(modelPath);
            var controller = M2AnimatorBuilder.BuildSwimming(modelPath, $"Assets/WowExports/_Generated/{modelName}");
            var stats = For(_preset);

            var origin = terrain.GetPosition();
            var size = terrain.terrainData.size;
            var centre = new Vector3(origin.x + size.x * 0.5f, 0f, origin.z + size.z * 0.5f);

            var group = new GameObject($"{stats.Name}s");
            Undo.RegisterCreatedObjectUndo(group, "Spawn Sea Creatures");

            int placed = 0;
            for (int attempt = 0; attempt < _count * 40 && placed < _count; attempt++)
            {
                float angle = Random.Range(0f, Mathf.PI * 2f);
                float distance = Random.Range(_ring.x, _ring.y);
                var point = new Vector3(
                    centre.x + Mathf.Cos(angle) * distance,
                    water.SurfaceY - Random.Range(_depth.x, _depth.y),
                    centre.z + Mathf.Sin(angle) * distance);

                if (!DeepEnough(point, water))
                    continue;

                Create(stats, controller, point, group.transform, placed);
                placed++;
            }

            if (placed == 0)
            {
                Debug.LogError("[SeaCreatureSpawner] Found no water deep enough in that ring — try a " +
                               "wider distance or a shallower depth.");
                Undo.DestroyObjectImmediate(group);
                return;
            }

            Selection.activeGameObject = group;
            Debug.Log($"[SeaCreatureSpawner] Placed {placed} {stats.Name}(s)" +
                      (controller == null ? " (no animations — see warning above)." : "."));
        }

        /// <summary>Inside the sea, with a few units of water under the spawn point.</summary>
        static bool DeepEnough(Vector3 point, WaterVolume water)
        {
            if (!water.Contains(point))
                return false;

            return point.y > Seabed.HeightAt(point, water) + 3f;
        }

        void Create(Stats stats, RuntimeAnimatorController controller, Vector3 position, Transform parent, int index)
        {
            // Built facing +Z, then turned: the collider is measured in this pose.
            var root = new GameObject($"{stats.Name}_{index:00}");
            root.transform.SetParent(parent);
            root.transform.position = position;

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(_model, root.transform);
            instance.transform.localRotation = Quaternion.Euler(0f, ModelYawOffset, 0f);
            instance.transform.localScale = Vector3.one * stats.Scale;
            instance.transform.localPosition = Vector3.zero;

            // Centre the body on the root — wow.export's origin sits under the belly.
            var bounds = MeasureBounds(instance);
            instance.transform.localPosition = root.transform.InverseTransformVector(position - bounds.center);
            bounds = MeasureBounds(instance);

            var collider = root.AddComponent<CapsuleCollider>();
            collider.direction = 2; // lengthwise, along +Z
            collider.height = Mathf.Max(bounds.size.z, 0.2f);
            collider.radius = Mathf.Min(Mathf.Max(bounds.size.x, bounds.size.y) * 0.4f, collider.height * 0.5f);
            collider.center = root.transform.InverseTransformPoint(bounds.center);

            var animator = instance.GetComponentInChildren<Animator>();
            if (controller != null)
            {
                if (animator == null)
                    animator = instance.AddComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
            }

            var characterStats = root.AddComponent<CharacterStats>();
            characterStats.characterName = stats.Name;
            characterStats.className = "Beast";
            characterStats.level = stats.Level;
            characterStats.strength = stats.Str;
            characterStats.dexterity = stats.Dex;
            characterStats.constitution = stats.Con;
            characterStats.intelligence = 1;
            characterStats.wisdom = 10;
            characterStats.charisma = 4;
            characterStats.hitDie = stats.HitDie;
            characterStats.armorBonus = stats.NaturalArmor;
            characterStats.weaponDieCount = stats.DieCount;
            characterStats.weaponDieSides = stats.DieSides;
            characterStats.experienceValue = stats.Xp;

            var health = root.AddComponent<Health>(); // max HP comes from the stats above
            health.reaction = stats.Reaction;
            root.AddComponent<LootDrop>().table = LootTable.Sea;

            var swimmer = root.AddComponent<SwimmingCreature>();
            swimmer.cruiseSpeed = stats.Cruise;
            swimmer.chaseSpeed = stats.Chase;
            swimmer.roamRadius = _roamRadius;
            swimmer.depthRange = _depth;
            swimmer.aggroRange = _aggroRange;
            swimmer.clearance = collider.radius + 0.5f;
            swimmer.biteReach = 1f + collider.radius * 0.3f;

            root.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
        }

        static Bounds MeasureBounds(GameObject instance)
        {
            var renderers = instance.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
                return new Bounds(instance.transform.position, Vector3.one);

            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }
    }
}
