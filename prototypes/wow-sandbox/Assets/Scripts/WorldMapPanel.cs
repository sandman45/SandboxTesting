using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace WowSandbox
{
    /// <summary>
    /// The full map, toggled with M: the whole island from WorldMap's snapshot, your arrow
    /// and every living NPC, your coordinates, and a key to the dot colours.
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

        protected override Vector2 PanelSize => new(MapSize + Pad * 2f, MapSize + 96f);
        protected override Vector2 PanelAnchor => new(0.5f, 0.5f);
        protected override Vector2 PanelPosition => Vector2.zero;

        void Reset() => toggleKey = Key.M;

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
            _map.raycastTarget = false;
            Hud.Ring(Hud.Stretch("Border", frame, -2f).gameObject, HudTheme.PanelBorder, 4f);
            _markers = new MapMarkers(Hud.Stretch("Markers", frame), 24f);

            _coordinates = Hud.Label("Coordinates", Root, new Vector2(1f, 0f), new Vector2(-Pad, 12f),
                new Vector2(260f, 24f), 15, TextAnchor.MiddleRight, HudTheme.Text);

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

            _markers.Update(transform, _targeting.Target, world =>
            {
                var uv = WorldMap.ToUv(world);
                if (uv.x < 0f || uv.x > 1f || uv.y < 0f || uv.y > 1f)
                    return (Vector2?)null;
                return (uv - new Vector2(0.5f, 0.5f)) * MapSize;
            }, 10f);

            var coordinates = WorldMap.ToCoordinates(transform.position);
            _coordinates.text = $"You are here: {coordinates.x:0.0}, {coordinates.y:0.0}";
        }
    }
}
