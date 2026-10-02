using System.Collections.Generic;
using UnityEngine;

namespace WowSandbox
{
    /// <summary>
    /// Rain and thunder for StormWeather, synthesised in code — no audio assets, nothing to
    /// import or license, the same reasoning as the generated rain texture and ripple maps.
    ///
    /// Rain is a seamless loop of band-limited noise with individual droplet ticks, its volume
    /// following the rain's own onset on the storm dial. Under a roof (or below deck) it's
    /// low-passed into the dull drum of rain overhead; underwater, nearly gone.
    ///
    /// Thunder answers each lightning strike after the time sound takes to cover the distance,
    /// as a few rolls of brown-noise rumble. Close strikes open with a crack; far ones arrive
    /// quieter and duller, as distance filters the highs out of real thunder.
    /// </summary>
    [DisallowMultipleComponent]
    public class StormAudio : MonoBehaviour
    {
        const int SampleRate = 44100;

        [Header("Rain")]
        [Range(0f, 1f)] public float rainVolume = 0.3f;
        [Tooltip("0 = dull, heavy patter; 1 = bright and hissy. Takes effect the next time " +
                 "Play starts — the rain is synthesised once, then looped.")]
        [Range(0f, 1f)] public float rainBrightness = 0.25f;
        [Tooltip("How far above the camera to look for a roof. Under one, the rain is muffled.")]
        public float roofCheckHeight = 40f;

        [Header("Thunder")]
        [Range(0f, 1f)] public float thunderVolume = 0.9f;
        [Tooltip("World units per second sound travels. Real sound is ~343 m/s, which makes these " +
                 "strike distances nearly instant; slower lets the thunder lag the flash the way " +
                 "it does in a real storm.")]
        public float soundSpeed = 120f;
        [Tooltip("Strikes closer than this get the sharp opening crack.")]
        public float crackDistance = 160f;

        StormWeather _weather;
        Camera _camera;

        AudioSource _rain;
        AudioLowPassFilter _rainFilter;
        readonly List<AudioSource> _thunderSources = new();
        readonly List<AudioLowPassFilter> _thunderFilters = new();
        AudioClip _rainClip;
        AudioClip[] _rumbles;
        AudioClip[] _cracks;

        // Strikes waiting for their sound to arrive: when, how far.
        readonly List<(float time, float distance)> _pending = new();

        float _rainCutoff = 22000f;

        void OnEnable()
        {
            _weather = GetComponent<StormWeather>();
            if (_weather == null)
            {
                Debug.LogWarning("[StormAudio] Needs a StormWeather on the same object.", this);
                enabled = false;
                return;
            }

            BuildClips();
            BuildSources();
            _weather.LightningStruck += OnStrike;
        }

        void OnDisable()
        {
            if (_weather != null)
                _weather.LightningStruck -= OnStrike;

            foreach (var source in _thunderSources)
                if (source != null) Destroy(source.gameObject);
            _thunderSources.Clear();
            _thunderFilters.Clear();

            if (_rain != null) Destroy(_rain.gameObject);
            DestroyClip(_rainClip);
            if (_rumbles != null) foreach (var clip in _rumbles) DestroyClip(clip);
            if (_cracks != null) foreach (var clip in _cracks) DestroyClip(clip);
            _pending.Clear();
        }

        static void DestroyClip(AudioClip clip)
        {
            if (clip != null) Destroy(clip);
        }

        void OnStrike(Vector3 ground, float distance)
        {
            _pending.Add((Time.time + distance / Mathf.Max(soundSpeed, 1f), distance));
        }

        void Update()
        {
            if (_camera == null)
                _camera = Camera.main;
            if (_camera == null)
                return;

            Vector3 listener = _camera.transform.position;
            bool submerged = WaterVolume.Containing(listener) != null;
            bool underRoof = !submerged && Physics.Raycast(listener, Vector3.up, roofCheckHeight, ~0,
                                                           QueryTriggerInteraction.Ignore);

            UpdateRain(submerged, underRoof);

            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                if (Time.time < _pending[i].time)
                    continue;

                PlayThunder(_pending[i].distance, submerged, underRoof);
                _pending.RemoveAt(i);
            }
        }

        void UpdateRain(bool submerged, bool underRoof)
        {
            if (_rain == null)
                return;

            float amount = _weather.RainAmount;
            float volume = amount * rainVolume;
            float cutoff = 22000f;

            if (submerged)
            {
                volume *= 0.15f;
                cutoff = 350f;
            }
            else if (underRoof)
            {
                // Rain drumming on the roof: still loud, but all the hiss gone.
                volume *= 0.75f;
                cutoff = 900f;
            }

            // Ease the filter rather than snapping it, or walking through a doorway clicks.
            _rainCutoff = Mathf.Lerp(_rainCutoff, cutoff, 1f - Mathf.Exp(-Time.deltaTime * 6f));
            _rainFilter.cutoffFrequency = _rainCutoff;
            _rain.volume = Mathf.MoveTowards(_rain.volume, volume, Time.deltaTime * 0.8f);

            if (_rain.volume > 0.001f && !_rain.isPlaying)
                _rain.Play();
            else if (_rain.volume <= 0.001f && _rain.isPlaying)
                _rain.Stop();
        }

        void PlayThunder(float distance, bool submerged, bool underRoof)
        {
            AudioSource source = null;
            AudioLowPassFilter filter = null;
            for (int i = 0; i < _thunderSources.Count; i++)
            {
                if (!_thunderSources[i].isPlaying)
                {
                    source = _thunderSources[i];
                    filter = _thunderFilters[i];
                    break;
                }
            }

            // All busy (a strike every few seconds at full storm): reuse the oldest-started.
            if (source == null)
            {
                source = _thunderSources[0];
                filter = _thunderFilters[0];
                _thunderSources.Add(_thunderSources[0]);
                _thunderSources.RemoveAt(0);
                _thunderFilters.Add(_thunderFilters[0]);
                _thunderFilters.RemoveAt(0);
            }

            // 0 = right overhead, 1 = as far as strikes land.
            float far = Mathf.InverseLerp(40f, 400f, distance);
            bool close = distance < crackDistance;

            var clips = close ? _cracks : _rumbles;
            source.clip = clips[Random.Range(0, clips.Length)];
            source.volume = thunderVolume * Mathf.Lerp(1f, 0.45f, far) * (submerged ? 0.3f : 1f);
            source.pitch = Random.Range(0.85f, 1.1f);

            float cutoff = Mathf.Lerp(5000f, 700f, far);
            if (submerged) cutoff = 300f;
            else if (underRoof) cutoff = Mathf.Min(cutoff, 1500f);
            filter.cutoffFrequency = cutoff;

            source.Play();
        }

        void BuildSources()
        {
            var rainGo = new GameObject("StormRainAudio") { hideFlags = HideFlags.DontSave };
            rainGo.transform.SetParent(transform, false);
            _rain = rainGo.AddComponent<AudioSource>();
            _rain.clip = _rainClip;
            _rain.loop = true;
            _rain.volume = 0f;
            _rain.spatialBlend = 0f;   // all around you, not from a point
            _rain.playOnAwake = false;
            _rainFilter = rainGo.AddComponent<AudioLowPassFilter>();
            _rainFilter.cutoffFrequency = 22000f;

            // A few voices, so a new strike doesn't cut off the rumble of the last one.
            for (int i = 0; i < 3; i++)
            {
                var go = new GameObject("StormThunderAudio") { hideFlags = HideFlags.DontSave };
                go.transform.SetParent(transform, false);
                var source = go.AddComponent<AudioSource>();
                source.spatialBlend = 0f;
                source.playOnAwake = false;
                _thunderSources.Add(source);
                _thunderFilters.Add(go.AddComponent<AudioLowPassFilter>());
            }

            if (FindFirstObjectByType<AudioListener>() == null)
                Debug.LogWarning("[StormAudio] No AudioListener in the scene, so nothing will be heard. " +
                                 "Add one to the main camera.", this);
        }

        // --- Synthesis -------------------------------------------------------------------

        void BuildClips()
        {
            var random = new System.Random(20261001);
            _rainClip = BuildRain(random, rainBrightness);
            _rumbles = new AudioClip[3];
            _cracks = new AudioClip[3];
            for (int i = 0; i < 3; i++)
            {
                _rumbles[i] = BuildThunder(random, "Rumble" + i, crack: false);
                _cracks[i] = BuildThunder(random, "Crack" + i, crack: true);
            }
        }

        /// <summary>
        /// Rain as texture, not tones: a soft pink-noise wash, a dense scatter of tiny noise
        /// taps for the individual drops, and a slow swell so it breathes like gusts. Levelled
        /// by RMS and soft-limited, so a cluster of drops can never jump out of the mix — the
        /// first version used sine "drops" up to four times the hiss level, which pinged and
        /// stacked into loud bursts. Written long and cross-faded at equal power, so the loop
        /// has no seam and no dip.
        /// </summary>
        static AudioClip BuildRain(System.Random random, float brightness)
        {
            // One-pole coefficients. Two poles in series give a steeper roll-off than one, so the
            // top end is actually removed rather than just tilted down: roughly 1.2 kHz at 0,
            // 6 kHz at 1. The drops follow the same brightness.
            float washCut = Mathf.Lerp(0.16f, 0.6f, brightness);
            float dropCut = Mathf.Lerp(0.12f, 0.5f, brightness);

            const float seconds = 8f;
            const float fade = 1f;
            int length = (int)(SampleRate * seconds);
            int fadeLength = (int)(SampleRate * fade);
            var samples = new float[length + fadeLength];

            // Pink noise (Paul Kellet's filter), minus the lowest rumble, minus the harshest top.
            float b0 = 0, b1 = 0, b2 = 0, b3 = 0, b4 = 0, b5 = 0, b6 = 0;
            float highPass = 0f, lowPass = 0f, lowPass2 = 0f;
            for (int i = 0; i < samples.Length; i++)
            {
                float white = (float)(random.NextDouble() * 2.0 - 1.0);
                b0 = 0.99886f * b0 + white * 0.0555179f;
                b1 = 0.99332f * b1 + white * 0.0750759f;
                b2 = 0.96900f * b2 + white * 0.1538520f;
                b3 = 0.86650f * b3 + white * 0.3104856f;
                b4 = 0.55000f * b4 + white * 0.5329522f;
                b5 = -0.7616f * b5 - white * 0.0168980f;
                float pink = (b0 + b1 + b2 + b3 + b4 + b5 + b6 + white * 0.5362f) * 0.11f;
                b6 = white * 0.115926f;

                highPass += (pink - highPass) * 0.015f;    // keep the body, lose only rumble below ~100 Hz
                float band = pink - highPass;
                lowPass += (band - lowPass) * washCut;
                lowPass2 += (lowPass - lowPass2) * washCut;
                samples[i] = lowPass2;
            }

            // Drops: lots of them, each a few milliseconds of decaying noise, all quiet.
            int drops = (int)(samples.Length / (float)SampleRate * 2500f);
            for (int d = 0; d < drops; d++)
            {
                int start = random.Next(samples.Length);
                float amplitude = (float)(0.03 + random.NextDouble() * random.NextDouble() * 0.12);
                int decay = (int)(SampleRate * (0.0008 + random.NextDouble() * 0.0025));
                // Low-passed noise: a dull tap. The first version high-passed these, and every
                // drop became a bright click that added to the hiss.
                float tapLow = 0f;
                for (int i = 0; i < decay * 5 && start + i < samples.Length; i++)
                {
                    float white = (float)(random.NextDouble() * 2.0 - 1.0);
                    tapLow += (white - tapLow) * dropCut;
                    samples[start + i] += tapLow * amplitude * 2f * Mathf.Exp(-i / (float)decay);
                }
            }

            // Gusts: a slow swell with a whole number of cycles in the loop, so it repeats cleanly.
            for (int i = 0; i < samples.Length; i++)
            {
                float t = (i % length) / (float)length;
                float swell = 1f + 0.12f * Mathf.Sin(2f * Mathf.PI * 2f * t)
                                 + 0.06f * Mathf.Sin(2f * Mathf.PI * 5f * t + 1.3f);
                samples[i] *= swell;
            }

            var loop = new float[length];
            for (int i = 0; i < length; i++)
                loop[i] = samples[i];
            for (int i = 0; i < fadeLength; i++)
            {
                // Equal-power: two unrelated noises summed linearly dip in the middle.
                float t = i / (float)fadeLength;
                loop[i] = samples[length + i] * Mathf.Cos(t * Mathf.PI * 0.5f) + samples[i] * Mathf.Sin(t * Mathf.PI * 0.5f);
            }

            NormaliseRms(loop, 0.18f);
            var clip = AudioClip.Create("StormRain", length, 1, SampleRate, false);
            clip.SetData(loop, 0);
            return clip;
        }

        /// <summary>Scale to a target RMS, then soft-clip so nothing can spike.</summary>
        static void NormaliseRms(float[] samples, float targetRms)
        {
            double sum = 0;
            foreach (float s in samples)
                sum += s * s;
            float rms = Mathf.Sqrt((float)(sum / samples.Length));
            if (rms < 1e-6f)
                return;

            float scale = targetRms / rms;
            for (int i = 0; i < samples.Length; i++)
                samples[i] = (float)System.Math.Tanh(samples[i] * scale * 1.2f) / 1.2f;
        }

        /// <summary>
        /// Brown noise shaped by a handful of overlapping rolls — real thunder is a long
        /// channel of sound arriving from different points along the bolt, so it swells and
        /// fades several times rather than decaying once. A crack adds a bright snap first.
        /// </summary>
        static AudioClip BuildThunder(System.Random random, string name, bool crack)
        {
            float seconds = (float)(5.5 + random.NextDouble() * 2.5);
            int length = (int)(SampleRate * seconds);
            var samples = new float[length];

            // The rolls: when each swells, how big, how long.
            int rolls = 3 + random.Next(4);
            var centres = new float[rolls];
            var heights = new float[rolls];
            var widths = new float[rolls];
            for (int r = 0; r < rolls; r++)
            {
                centres[r] = (float)(0.15 + random.NextDouble() * (seconds * 0.6));
                heights[r] = (float)(0.4 + random.NextDouble() * 0.6) * Mathf.Exp(-centres[r] * 0.35f);
                widths[r] = (float)(0.25 + random.NextDouble() * 0.7);
            }

            float brown = 0f, smooth = 0f;
            for (int i = 0; i < length; i++)
            {
                float t = i / (float)SampleRate;
                float white = (float)(random.NextDouble() * 2.0 - 1.0);

                // Leaky integration: brown noise, all low end.
                brown = brown * 0.998f + white * 0.06f;
                smooth += (brown - smooth) * 0.2f;

                float envelope = 0f;
                for (int r = 0; r < rolls; r++)
                {
                    float x = (t - centres[r]) / widths[r];
                    envelope += heights[r] * Mathf.Exp(-x * x);
                }

                // Fade in over a few ms (no click), out over the last second.
                envelope *= Mathf.Clamp01(t / 0.03f) * Mathf.Clamp01((seconds - t) / 1f);
                samples[i] = smooth * envelope;
            }

            if (crack)
            {
                int crackLength = (int)(SampleRate * 0.25f);
                for (int i = 0; i < crackLength && i < length; i++)
                {
                    float white = (float)(random.NextDouble() * 2.0 - 1.0);
                    float envelope = Mathf.Exp(-i / (SampleRate * 0.04f));
                    samples[i] += white * envelope * 0.9f;
                }
            }

            Normalise(samples, 0.9f);
            var clip = AudioClip.Create("Thunder" + name, length, 1, SampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        static void Normalise(float[] samples, float peak)
        {
            float max = 0f;
            foreach (float s in samples)
                max = Mathf.Max(max, Mathf.Abs(s));
            if (max < 1e-6f)
                return;

            float scale = peak / max;
            for (int i = 0; i < samples.Length; i++)
                samples[i] *= scale;
        }
    }
}
