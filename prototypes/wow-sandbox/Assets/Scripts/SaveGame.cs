using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace WowSandbox
{
    /// <summary>
    /// The saved game: one save, a JSON file in Application.persistentDataPath. It holds the
    /// Captain — where he stands (on a ship's deck, it's a spot on that ship), his level, health,
    /// coin, bags and gear — the story (journal pages, flags, the map), the time and the sky, and
    /// the world: what's been taken, each chest and what's left in it, which enemies are dead,
    /// and where the ships are.
    ///
    /// It saves itself a couple of seconds after anything happens in the story, every few
    /// minutes, and as the game quits; the Esc menu's Save Game saves now. Not while the title's
    /// up, he's still lying in the sand, dead, or talking — and the timed saves wait out a fight.
    /// Saved swimming, climbing or mid-jump, he goes back to the last ground he stood on.
    ///
    /// The title screen's Continue applies it (<see cref="Load"/>), skipping the shipwreck
    /// opening. Created in every scene with a player, which is when it learns the scene's chests,
    /// pickups, enemies and ships, before anything moves — that's how they're told apart.
    /// </summary>
    public class SaveGame : MonoBehaviour
    {
        const int Version = 1;
        const float AutosaveSeconds = 300f;
        // After a story change, so a run of them (a conversation's flags) makes one save.
        const float StoryDelay = 2f;
        const float NoteSeconds = 1.6f;

        public static string FilePath => Path.Combine(Application.persistentDataPath, "save.json");

        public static bool HasSave => File.Exists(FilePath);

        static SaveGame _instance;

        WowCharacterController _player;
        CharacterController _body;
        HealthController _health;
        CharacterStats _stats;
        Inventory _inventory;

        // The scene's own things, by an id made before any of them has moved.
        readonly Dictionary<string, Interactable> _pickups = new();
        readonly Dictionary<string, TreasureChest> _chests = new();
        readonly Dictionary<string, Health> _creatures = new();
        readonly Dictionary<string, ShipBuoyancy> _ships = new();

        // The last place he stood on solid ground (or a deck), for a save made in the water.
        Spot _safe;
        bool _hasSafe;

        int _storyVersion;
        float _storyChangedAt = -1f;
        float _nextAutosave;
        Text _note;
        float _noteShownAt = -100f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void CreateInScene()
        {
            if (FindAnyObjectByType<WowCharacterController>() == null || FindAnyObjectByType<SaveGame>() != null)
                return;
            new GameObject("Save Game").AddComponent<SaveGame>();
        }

        void Awake()
        {
            _instance = this;
            _player = FindAnyObjectByType<WowCharacterController>();
            _body = _player.GetComponent<CharacterController>();
            _health = _player.GetComponent<HealthController>();
            _stats = _player.GetComponent<CharacterStats>();
            _inventory = _player.GetComponent<Inventory>();

            // After every Awake and before any Start: all where the scene put them.
            foreach (var thing in FindObjectsByType<Interactable>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (thing is TreasureChest chest)
                    Register(_chests, chest);
                else if (thing is GroundItem or MapPickup or JournalPagePickup)
                    Register(_pickups, thing);
            }
            foreach (var creature in FindObjectsByType<Health>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                Register(_creatures, creature);
            foreach (var ship in FindObjectsByType<ShipBuoyancy>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                Register(_ships, ship, byName: true);

            _nextAutosave = Time.unscaledTime + AutosaveSeconds;
        }

        System.Collections.IEnumerator Start()
        {
            _storyVersion = StoryState.Version;
            // No title screen to choose from (Developer → Skip Title): straight back into the save,
            // once everything's had its Start.
            if (TitleScreen.Showing || !HasSave)
                yield break;
            yield return null;
            Load();
        }

        void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
        }

        static void Register<T>(Dictionary<string, T> into, T thing, bool byName = false) where T : Component
        {
            var p = thing.transform.position;
            string id = byName ? thing.name : $"{thing.GetType().Name}:{thing.name}@{p.x:0},{p.y:0},{p.z:0}";
            string unique = id;
            for (int n = 2; into.ContainsKey(unique); n++)
                unique = $"{id}#{n}";
            into[unique] = thing;
        }

        void Update()
        {
            TrackSafeSpot();

            if (StoryState.Version != _storyVersion)
            {
                _storyVersion = StoryState.Version;
                _storyChangedAt = Time.unscaledTime;
            }

            bool storyDue = _storyChangedAt >= 0f && Time.unscaledTime - _storyChangedAt >= StoryDelay;
            bool timerDue = Time.unscaledTime >= _nextAutosave;
            if ((storyDue || timerDue) && CanSave(out _) && !_player.InCombat)
            {
                _storyChangedAt = -1f;
                Save(announce: false);
            }

            if (_note != null)
            {
                float age = Time.unscaledTime - _noteShownAt;
                var colour = _note.color;
                colour.a = Mathf.Clamp01(Mathf.Min(age * 4f, (NoteSeconds - age) * 2f));
                _note.color = colour;
            }
        }

        void OnApplicationQuit()
        {
            if (CanSave(out _))
                Save(announce: false);
        }

        /// <summary>Whether now's a time the game can be saved; if not, why.</summary>
        public static bool CanSave(out string reason)
        {
            reason = _instance == null ? "There's nothing to save."
                   : TitleScreen.Showing || ShipwreckWake.Lying ? "You can't save yet."
                   : _instance._health != null && _instance._health.IsDead ? "You can't save while dead."
                   : DialoguePanel.Talking ? "You can't save while talking."
                   : null;
            return reason == null;
        }

        /// <summary>The Esc menu's Save Game: saves now, or says why it can't.</summary>
        public static void SaveNow()
        {
            if (!CanSave(out var reason))
                Hud.Error(reason);
            else if (_instance.Save(announce: true))
                Hud.Info("Game saved.");
        }

        /// <summary>Puts the saved game back, if there is one. True if it did.</summary>
        public static bool Load()
        {
            var data = Peek();
            if (data == null || _instance == null)
                return false;
            _instance.Apply(data);
            Debug.Log($"[SaveGame] Loaded the game saved {data.savedAt}.");
            return true;
        }

        /// <summary>The saved game, read but not applied (the title screen's summary), or null.</summary>
        public static SaveData Peek()
        {
            if (!HasSave)
                return null;
            try
            {
                var data = JsonUtility.FromJson<SaveData>(File.ReadAllText(FilePath));
                if (data != null && data.version == Version)
                    return data;
                Debug.LogWarning($"[SaveGame] {FilePath} is from another version of the game — ignored.");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SaveGame] Couldn't read {FilePath}: {e.Message}");
            }
            return null;
        }

        /// <summary>Wipes the saved game — New Game, or the Developer panel.</summary>
        public static void Delete()
        {
            if (HasSave)
                File.Delete(FilePath);
            if (_instance != null)
                _instance._nextAutosave = Time.unscaledTime + AutosaveSeconds;
        }

        bool Save(bool announce)
        {
            _nextAutosave = Time.unscaledTime + AutosaveSeconds;
            try
            {
                var data = Capture();
                // Written aside first, so a crash mid-write can't leave half a save.
                string temp = FilePath + ".tmp";
                File.WriteAllText(temp, JsonUtility.ToJson(data, prettyPrint: true));
                if (File.Exists(FilePath))
                    File.Delete(FilePath);
                File.Move(temp, FilePath);
            }
            catch (Exception e)
            {
                Debug.LogError($"[SaveGame] Couldn't save to {FilePath}: {e.Message}");
                if (announce)
                    Hud.Error("The game couldn't be saved.");
                return false;
            }
            ShowNote();
            return true;
        }

        // --- What's saved --------------------------------------------------------------------

        SaveData Capture()
        {
            var data = new SaveData
            {
                version = Version,
                savedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
            };

            var clock = DayNightCycle.Active;
            data.hour = clock != null ? clock.hour : 12f;
            var weather = WeatherSystem.Active;
            if (weather != null)
            {
                data.sky = (int)weather.SavedSky;
                data.hoursToFront = weather.HoursToNextFront;
            }

            StoryState.Snapshot(out data.pages, out data.flags);
            data.mapFound = WorldMap.Found;

            data.player = new PlayerData
            {
                spot = CurrentSpot(),
                level = _stats.level,
                experience = _stats.experience,
                health = _health != null ? _health.Current : 0f,
                copper = _inventory.copper,
                bag = Enumerable.Range(0, _inventory.BagSize)
                    .Where(i => _inventory.BagItem(i) != null)
                    .Select(i => new SlotData { index = i, item = _inventory.BagItem(i) }).ToArray(),
                equipped = ((EquipSlot[])Enum.GetValues(typeof(EquipSlot)))
                    .Where(slot => _inventory.Equipped(slot) != null)
                    .Select(slot => new SlotData { index = (int)slot, item = _inventory.Equipped(slot) }).ToArray(),
            };

            data.taken = _pickups.Where(pair => pair.Value == null || !pair.Value.gameObject.activeSelf)
                .Select(pair => pair.Key).ToArray();
            data.chests = _chests.Where(pair => pair.Value != null).Select(pair =>
            {
                var loot = pair.Value.GetComponent<LootDrop>();
                return new ChestData
                {
                    id = pair.Key,
                    opened = pair.Value.Opened,
                    items = loot.Items.ToArray(),
                    copper = loot.Copper,
                };
            }).ToArray();
            data.dead = _creatures.Where(pair => pair.Value == null || pair.Value.IsDead)
                .Select(pair => pair.Key).ToArray();
            data.ships = _ships.Where(pair => pair.Value != null).Select(pair =>
            {
                var sailing = pair.Value.GetComponent<ShipSailing>();
                return new ShipData
                {
                    name = pair.Key,
                    position = pair.Value.transform.position,
                    heading = pair.Value.Heading,
                    anchored = sailing == null || sailing.anchored,
                };
            }).ToArray();
            return data;
        }

        /// <summary>Where he is now if that's somewhere to stand, else the last place that was.</summary>
        Spot CurrentSpot()
        {
            // Steering: behind the wheel, on that ship.
            foreach (var helm in FindObjectsByType<ShipHelm>(FindObjectsSortMode.None))
            {
                var ship = helm.IsDriving ? helm.GetComponentInParent<ShipBuoyancy>() : null;
                if (ship != null)
                    return SpotOn(ship.transform);
            }
            if (Standing())
                return SpotHere();
            return _hasSafe ? _safe : SpotHere();
        }

        bool Standing() =>
            !_player.IsSwimming && !_player.IsClimbing && _body != null && _body.isGrounded &&
            (_health == null || !_health.IsDead);

        void TrackSafeSpot()
        {
            if (_player == null || _player.IsLocked || TitleScreen.Showing || !Standing())
                return;
            _safe = SpotHere();
            _hasSafe = true;
        }

        Spot SpotHere()
        {
            var deck = _player.Platform;
            var ship = deck != null ? deck.GetComponentInParent<ShipBuoyancy>() : null;
            return ship != null ? SpotOn(ship.transform) : new Spot
            {
                position = _player.transform.position,
                yaw = _player.transform.eulerAngles.y,
            };
        }

        Spot SpotOn(Transform ship)
        {
            string name = _ships.FirstOrDefault(pair => pair.Value != null && pair.Value.transform == ship).Key;
            if (name == null)
                return new Spot { position = _player.transform.position, yaw = _player.transform.eulerAngles.y };
            return new Spot
            {
                ship = name,
                position = ship.InverseTransformPoint(_player.transform.position),
                yaw = Mathf.DeltaAngle(ship.eulerAngles.y, _player.transform.eulerAngles.y),
            };
        }

        // --- Putting it back -----------------------------------------------------------------

        void Apply(SaveData data)
        {
            // The opening's over: no lying in the sand, no wreck's storm, no leviathan out in it.
            ShipwreckWake.SkipOpening();
            if (StormWeather.Active != null)
            {
                StormWeather.Active.StopStorm();
                StormWeather.Active.GetComponent<StormLeviathan>()?.HideNow();
            }
            DayNightCycle.Active?.SetHour(data.hour);
            WeatherSystem.Active?.Restore((WeatherSystem.Sky)data.sky, data.hoursToFront);

            StoryState.Restore(data.pages, data.flags);
            WorldMap.Found = data.mapFound;

            foreach (var helm in FindObjectsByType<ShipHelm>(FindObjectsSortMode.None))
                if (helm.IsDriving)
                    helm.LeaveHelm();
            if (_health != null && _health.IsDead)
                _health.Respawn();

            var player = data.player;
            _stats.Restore(player.level, player.experience);
            _inventory.Restore(player.bag.Select(slot => (slot.index, slot.item)),
                player.equipped.Select(slot => ((EquipSlot)slot.index, slot.item)), player.copper);
            // After the gear, whose Constitution can change how much health there is to have.
            _health?.SetCurrent(player.health);

            var taken = new HashSet<string>(data.taken);
            foreach (var (id, pickup) in _pickups)
                if (pickup != null)
                    pickup.gameObject.SetActive(!taken.Contains(id));
            foreach (var chest in data.chests)
                if (_chests.TryGetValue(chest.id, out var found) && found != null)
                    found.Restore(chest.opened, chest.items, chest.copper);
            foreach (var id in data.dead)
                if (_creatures.TryGetValue(id, out var creature) && creature != null)
                    Destroy(creature.gameObject);

            foreach (var ship in data.ships)
            {
                if (!_ships.TryGetValue(ship.name, out var found) || found == null)
                    continue;
                found.Heading = ship.heading;
                found.transform.SetPositionAndRotation(ship.position, Quaternion.Euler(0f, ship.heading, 0f));
                var sailing = found.GetComponent<ShipSailing>();
                if (sailing != null)
                    sailing.anchored = ship.anchored;
            }
            Physics.SyncTransforms();

            Place(player.spot);
            _safe = player.spot;
            _hasSafe = true;
            _storyVersion = StoryState.Version;
            _storyChangedAt = -1f;
            _nextAutosave = Time.unscaledTime + AutosaveSeconds;
        }

        void Place(Spot spot)
        {
            Vector3 position = spot.position;
            float yaw = spot.yaw;
            if (!string.IsNullOrEmpty(spot.ship) && _ships.TryGetValue(spot.ship, out var ship) && ship != null)
            {
                position = ship.transform.TransformPoint(spot.position);
                yaw += ship.transform.eulerAngles.y;
            }

            if (_body != null)
                _body.enabled = false;
            _player.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            if (_body != null)
                _body.enabled = true;
            Physics.SyncTransforms();
        }

        void ShowNote()
        {
            if (_note == null)
            {
                _note = Hud.Label("Saving", Hud.Layer(HudLayer.Overlay), new Vector2(1f, 0f), new Vector2(-24f, 18f),
                    new Vector2(200f, 24f), 15, TextAnchor.MiddleRight, HudTheme.TextDim, FontStyle.Italic);
                _note.text = "Game saved";
            }
            _noteShownAt = Time.unscaledTime;
        }

        // --- The file ------------------------------------------------------------------------

        [Serializable]
        public class SaveData
        {
            public int version;
            public string savedAt;
            public float hour;
            public int sky;
            public float hoursToFront;
            public int[] pages = Array.Empty<int>();
            public string[] flags = Array.Empty<string>();
            public bool mapFound;
            public PlayerData player;
            public string[] taken = Array.Empty<string>();
            public ChestData[] chests = Array.Empty<ChestData>();
            public string[] dead = Array.Empty<string>();
            public ShipData[] ships = Array.Empty<ShipData>();
        }

        [Serializable]
        public struct Spot
        {
            [Tooltip("The ship it's a spot on, or empty for the world.")]
            public string ship;
            public Vector3 position;
            public float yaw;
        }

        [Serializable]
        public class PlayerData
        {
            public Spot spot;
            public int level = 1;
            public int experience;
            public float health;
            public int copper;
            public SlotData[] bag = Array.Empty<SlotData>();
            public SlotData[] equipped = Array.Empty<SlotData>();
        }

        [Serializable]
        public class SlotData
        {
            public int index;
            public Item item;
        }

        [Serializable]
        public class ChestData
        {
            public string id;
            public bool opened;
            public Item[] items = Array.Empty<Item>();
            public int copper;
        }

        [Serializable]
        public class ShipData
        {
            public string name;
            public Vector3 position;
            public float heading;
            public bool anchored;
        }
    }
}
