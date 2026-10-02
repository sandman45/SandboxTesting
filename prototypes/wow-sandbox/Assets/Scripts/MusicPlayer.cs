using System.Collections.Generic;
using UnityEngine;

namespace WowSandbox
{
    /// <summary>
    /// Background music: works through a playlist, crossfading from one track to the next.
    ///
    /// Two AudioSources, alternating — the only way to crossfade, since one source can play
    /// one clip. Tracks live in Assets/WowExports/sound/music (gitignored, like every other
    /// WoW asset), and WoW Sandbox → Add Music Player fills the playlist from that folder.
    /// </summary>
    [DisallowMultipleComponent]
    public class MusicPlayer : MonoBehaviour
    {
        public List<AudioClip> playlist = new();

        [Range(0f, 1f)] public float volume = 0.2f;
        public bool shuffle = true;
        [Tooltip("Seconds each track takes to fade in at its start and out at its end.")]
        public float crossfadeSeconds = 4f;
        [Tooltip("How long after one track starts fading out the next one begins. Equal to " +
                 "Crossfade Seconds: a true crossfade. Twice it: the next starts as the last ends. " +
                 "More: a silent pause between them.")]
        public float gapSeconds = 4f;

        readonly AudioSource[] _sources = new AudioSource[2];
        int _current;
        int _trackIndex = -1;
        readonly List<int> _order = new();
        float _nextTrackAt;

        void Start()
        {
            for (int i = 0; i < 2; i++)
            {
                var source = gameObject.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.loop = false;
                source.spatialBlend = 0f;
                source.volume = 0f;
                _sources[i] = source;
            }

            playlist.RemoveAll(clip => clip == null);
            if (playlist.Count == 0)
            {
                Debug.LogWarning("[MusicPlayer] Playlist is empty. Put tracks in Assets/WowExports/sound/music " +
                                 "and run WoW Sandbox → Add Music Player.", this);
                enabled = false;
                return;
            }

            PlayNext();
        }

        void Update()
        {
            // Each source shapes its own volume from how far into its track it is, so a track
            // is always silent by the time it ends — whatever the next one is doing. (The
            // first version faded the old track on the new one's schedule, and with a gap
            // shorter than the crossfade the old one hit its end still loud and cut off.)
            foreach (var source in _sources)
            {
                if (source.isPlaying && source.clip != null)
                    source.volume = volume * Envelope(source.time, source.clip.length);
            }

            if (_sources[_current].clip != null && Time.time >= _nextTrackAt)
                PlayNext();
        }

        void PlayNext()
        {
            if (_order.Count == 0)
                RefillOrder();

            _trackIndex = _order[0];
            _order.RemoveAt(0);

            _current = 1 - _current;
            var source = _sources[_current];
            source.clip = playlist[_trackIndex];
            source.volume = 0f;
            source.Play();
            // The next track starts once this one has begun fading out, plus the gap beyond that.
            float length = source.clip.length;
            _nextTrackAt = Time.time + Mathf.Max(length - crossfadeSeconds + (gapSeconds - crossfadeSeconds), 1f);
        }

        /// <summary>Sine fade in over the first crossfade, out over the last: 0 at both ends.</summary>
        float Envelope(float time, float length)
        {
            float fade = Mathf.Max(crossfadeSeconds, 0.01f);
            float edge = Mathf.Clamp01(Mathf.Min(time, length - time) / fade);
            return Mathf.Sin(edge * Mathf.PI * 0.5f);
        }

        void RefillOrder()
        {
            for (int i = 0; i < playlist.Count; i++)
                _order.Add(i);

            if (!shuffle)
                return;

            for (int i = _order.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (_order[i], _order[j]) = (_order[j], _order[i]);
            }

            // Never the same track twice in a row across a reshuffle.
            if (_order.Count > 1 && _order[0] == _trackIndex)
                (_order[0], _order[1]) = (_order[1], _order[0]);
        }
    }
}
