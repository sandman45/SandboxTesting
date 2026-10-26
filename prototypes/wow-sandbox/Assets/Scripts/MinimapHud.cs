using UnityEngine;
using UnityEngine.UI;

namespace WowSandbox
{
    /// <summary>
    /// WoW-style round minimap, top-right: zone name above, coordinates below, the area
    /// around you with north up, your arrow in the middle and NPC dots around it. + and −
    /// zoom. Draws from WorldMap's snapshot, so it shows the island as it was when Play
    /// started — anything that moves is a marker, not part of the picture.
    ///
    /// Also the clock: a half-dome dial with the sun and moon wheeling across it, and the time
    /// under it, in the top-right corner beside the zone name with the minimap below them both.
    /// The clock shows from the start; the rest waits until you find a map.
    /// </summary>
    [DefaultExecutionOrder(1000)]
    [RequireComponent(typeof(TargetingController))]
    public class MinimapHud : MonoBehaviour
    {
        public string zoneName = "Tidegrave Isle";
        [Tooltip("World units from the centre to the edge of the minimap.")]
        public float viewRadius = 60f;
        public Vector2 zoomRange = new(20f, 240f);
        [Tooltip("Anchored from the top-right of the screen.")]
        public Vector2 topRightOffset = new(-20f, -20f);

        const float Diameter = 200f;
        const float Width = Diameter + 10f;
        const float DialSize = 50f;
        const float ClockWidth = 64f;
        // The row with the zone name and the clock, above the circle.
        const float Header = 48f;

        TargetingController _targeting;
        RectTransform _frame;
        RawImage _map;
        MapMarkers _markers;
        Text _coordinates;

        RectTransform _clock;
        ClockDial _dial;
        Text _time;

        void Awake()
        {
            _targeting = GetComponent<TargetingController>();
            Build();
        }

        void Start() => _map.texture = WorldMap.Texture;

        void LateUpdate()
        {
            UpdateClock();

            // Hidden until the player finds a map.
            _frame.gameObject.SetActive(WorldMap.MinimapShown);
            if (!WorldMap.MinimapShown)
                return;

            Vector3 position = transform.position;
            var bounds = WorldMap.Bounds;

            var uv = WorldMap.ToUv(position);
            float span = viewRadius * 2f / bounds.width;
            _map.uvRect = new Rect(uv.x - span * 0.5f, uv.y - span * 0.5f, span, span);

            float scale = Diameter * 0.5f / viewRadius;
            float edge = Diameter * 0.5f - 5f;
            _markers.Update(transform, _targeting.Target, world =>
            {
                var offset = new Vector2(world.x - position.x, world.z - position.z) * scale;
                return offset.magnitude <= edge ? offset : (Vector2?)null;
            }, 7f, world =>
            {
                var offset = new Vector2(world.x - position.x, world.z - position.z);
                return offset.normalized * edge;
            });

            var coordinates = WorldMap.ToCoordinates(position);
            _coordinates.text = $"{coordinates.x:0.0}, {coordinates.y:0.0}";
        }

        void UpdateClock()
        {
            var cycle = DayNightCycle.Active;
            bool shown = cycle != null && DevOptions.ShowClock;
            _clock.gameObject.SetActive(shown);
            if (!shown)
                return;

            _dial.Show(cycle.hour, cycle.Daylight);
            _time.text = ClockDial.Format(cycle.hour);
        }

        void Zoom(float factor) =>
            viewRadius = Mathf.Clamp(viewRadius * factor, zoomRange.x, zoomRange.y);

        void Build()
        {
            var topLeft = new Vector2(0f, 1f);
            var frame = Hud.Rect("Minimap", Hud.Layer(HudLayer.Frames), Vector2.one, topRightOffset,
                new Vector2(Width, Header + Diameter + 36f));
            _frame = frame;

            // Left of the clock, which sits in the corner above the circle's right side.
            Hud.Label("Zone", frame, topLeft, new Vector2(0f, 0f), new Vector2(Width - ClockWidth, Header - 8f),
                15, TextAnchor.MiddleCenter, HudTheme.Heading, FontStyle.Bold).text = zoneName;

            // The round window: a circle that masks the map and markers inside it.
            var circle = Hud.Rect("Circle", frame, topLeft, new Vector2(5f, -Header - 4f), new Vector2(Diameter, Diameter));
            var back = circle.gameObject.AddComponent<Image>();
            back.sprite = Hud.Circle;
            back.color = new Color(0.05f, 0.08f, 0.1f, 1f);
            back.raycastTarget = false;
            circle.gameObject.AddComponent<Mask>().showMaskGraphic = true;

            _map = Hud.Stretch("Map", circle).gameObject.AddComponent<RawImage>();
            _map.raycastTarget = false;
            _markers = new MapMarkers(Hud.Stretch("Markers", circle), 18f);

            var ring = Hud.Rect("Ring", frame, topLeft, new Vector2(3f, -Header - 2f), new Vector2(Diameter + 4f, Diameter + 4f))
                .gameObject.AddComponent<Image>();
            ring.sprite = Hud.CircleRing;
            ring.color = HudTheme.PanelBorder;
            ring.raycastTarget = false;

            Hud.TextButton("+", frame, Vector2.one, new Vector2(0f, -Header - Diameter + 42f), new Vector2(24f, 24f),
                () => Zoom(1f / 1.5f));
            Hud.TextButton("-", frame, Vector2.one, new Vector2(0f, -Header - Diameter + 70f), new Vector2(24f, 24f),
                () => Zoom(1.5f));

            _coordinates = Hud.Label("Coordinates", frame, topLeft, new Vector2(0f, -Header - Diameter - 10f),
                new Vector2(Width, 20f), 13, TextAnchor.MiddleCenter, HudTheme.TextDim);

            BuildClock();
        }

        void BuildClock()
        {
            _clock = Hud.Rect("Clock", Hud.Layer(HudLayer.Frames), Vector2.one, topRightOffset,
                new Vector2(ClockWidth, DialSize * 0.5f + 20f));

            // The window shows the top half of a turning disc.
            var top = new Vector2(0.5f, 1f);
            _dial = new ClockDial(_clock, top, Vector2.zero, DialSize);

            // A dark tab behind the time, so it reads over the map once docked.
            var tab = Hud.Rect("Tab", _clock, top, new Vector2(0f, -DialSize * 0.5f - 2f), new Vector2(ClockWidth - 4f, 18f))
                .gameObject.AddComponent<Image>();
            tab.color = new Color(0f, 0f, 0f, 0.6f);
            tab.raycastTarget = false;

            _time = Hud.Label("Time", _clock, top, new Vector2(0f, -DialSize * 0.5f - 2f), new Vector2(ClockWidth, 18f),
                13, TextAnchor.MiddleCenter, HudTheme.Text);
        }
    }
}
