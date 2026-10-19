using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace WowSandbox
{
    /// <summary>
    /// The full map, toggled with M: WorldMap's snapshot, your arrow and every living NPC,
    /// your coordinates, and a key to the dot colours. It opens on the island; the mouse wheel
    /// zooms out as far as the whole sea and back. Whenever you're outside what it's showing —
    /// sailed off past the island, say — it centres on you instead.
    /// </summary>
    [RequireComponent(typeof(TargetingController))]
    public class WorldMapPanel : HudPanel
    {
        const float MapSize = 700f;
        const float Pad = 20f;

        TargetingController _targeting;
        MinimapHud _minimap;
        RawImage _map;
        MapMarkers _markers;
        Text _title;
        Text _coordinates;

        float _span;                // world units across what the map's showing
        int _lastRefreshFrame = -10;
        Rect _window;               // the part of WorldMap.Bounds on show, in world XZ

        protected override Vector2 PanelSize => new(MapSize + Pad * 2f, MapSize + 96f);
        protected override Vector2 PanelAnchor => new(0.5f, 0.5f);
        protected override Vector2 PanelPosition => Vector2.zero;

        void Reset() => toggleKey = Key.M;

        protected override bool CanOpen()
        {
            if (WorldMap.MapUsable)
                return true;
            Hud.Error("You don't have a map.");
            return false;
        }

        protected override void Awake()
        {
            _targeting = GetComponent<TargetingController>();
            _minimap = GetComponent<MinimapHud>();
            base.Awake();
        }

        protected override void Build()
        {
            var topLeft = new Vector2(0f, 1f);
            _title = Hud.Label("Title", Root, topLeft, new Vector2(Pad, -12f), new Vector2(MapSize - 40f, 28f),
                22, TextAnchor.MiddleLeft, HudTheme.Text, FontStyle.Bold);

            var frame = Hud.Rect("Map", Root, topLeft, new Vector2(Pad, -48f), new Vector2(MapSize, MapSize));
            _map = frame.gameObject.AddComponent<RawImage>();
            _map.raycastTarget = true;     // so the wheel over it counts as over the UI
            Hud.Ring(Hud.Stretch("Border", frame, -2f).gameObject, HudTheme.PanelBorder, 4f);
            _markers = new MapMarkers(Hud.Stretch("Markers", frame), 24f);

            _coordinates = Hud.Label("Coordinates", Root, new Vector2(1f, 0f), new Vector2(-Pad, 12f),
                new Vector2(260f, 24f), 15, TextAnchor.MiddleRight, HudTheme.Text);

            Hud.Label("ZoomHint", Root, new Vector2(1f, 1f), new Vector2(-Pad - 36f, -12f), new Vector2(220f, 28f),
                13, TextAnchor.MiddleRight, HudTheme.TextDim).text = "Scroll to zoom";

            var key = Hud.Label("Key", Root, new Vector2(0f, 0f), new Vector2(Pad, 12f), new Vector2(420f, 24f),
                14, TextAnchor.MiddleLeft, HudTheme.TextDim);
            key.supportRichText = true;
            key.text = $"<color=#{HudTheme.Hex(HudTheme.Hostile)}>●</color> Hostile    " +
                       $"<color=#{HudTheme.Hex(HudTheme.Neutral)}>●</color> Neutral    " +
                       $"<color=#{HudTheme.Hex(HudTheme.Friendly)}>●</color> Friendly";
        }

        protected override void Refresh()
        {
            if (_map.texture == null)
                _map.texture = WorldMap.Texture;
            _title.text = _minimap != null ? _minimap.zoneName : "Map";

            UpdateWindow();

            _markers.Update(transform, _targeting.Target, world =>
            {
                var uv = WindowUv(world);
                if (uv.x < 0f || uv.x > 1f || uv.y < 0f || uv.y > 1f)
                    return (Vector2?)null;
                return (uv - new Vector2(0.5f, 0.5f)) * MapSize;
            }, 10f, world =>
            {
                var uv = WindowUv(world);
                return (new Vector2(Mathf.Clamp01(uv.x), Mathf.Clamp01(uv.y)) - new Vector2(0.5f, 0.5f)) * MapSize;
            });

            var coordinates = WorldMap.ToCoordinates(transform.position);
            _coordinates.text = $"You are here: {coordinates.x:0.0}, {coordinates.y:0.0}";
        }

        /// <summary>Zoom from the wheel, and which part of the sea to show.</summary>
        void UpdateWindow()
        {
            var sea = WorldMap.Bounds;
            var island = WorldMap.IslandBounds;
            float closest = Mathf.Min(island.width, sea.width);

            // Opens on the island each time.
            if (_lastRefreshFrame < Time.frameCount - 1 || _span <= 0f)
                _span = closest;
            _lastRefreshFrame = Time.frameCount;

            var mouse = Mouse.current;
            float scroll = mouse != null && Hud.PointerOverUi ? mouse.scroll.ReadValue().y : 0f;
            if (Mathf.Abs(scroll) > 0.01f)
                _span = Mathf.Clamp(_span * (scroll > 0f ? 1f / 1.25f : 1.25f), closest, sea.width);

            // On the island while you're in sight of it; otherwise on you, kept inside the sea.
            var you = new Vector2(transform.position.x, transform.position.z);
            var centre = island.center;
            var view = new Rect(centre - Vector2.one * (_span * 0.5f), Vector2.one * _span);
            float inset = _span * 0.08f;
            if (you.x < view.xMin + inset || you.x > view.xMax - inset || you.y < view.yMin + inset || you.y > view.yMax - inset)
                centre = you;
            float half = _span * 0.5f;
            centre.x = Mathf.Clamp(centre.x, sea.xMin + half, sea.xMax - half);
            centre.y = Mathf.Clamp(centre.y, sea.yMin + half, sea.yMax - half);
            _window = new Rect(centre - Vector2.one * half, Vector2.one * _span);

            _map.uvRect = new Rect((_window.min - sea.min) / sea.width, Vector2.one * (_span / sea.width));
        }

        Vector2 WindowUv(Vector3 world) =>
            new((world.x - _window.xMin) / _window.width, (world.z - _window.yMin) / _window.height);
    }
}
