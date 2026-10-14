using System;
using System.Collections.Generic;

namespace VMS.Core.Music
{
    /// <summary>
    /// Synthesises the built-in demo track "Lạc Hồng Khúc" so the game is playable before the player imports
    /// any music (and without shipping copyrighted audio). A-minor pentatonic, 124 BPM, ~95 s.
    /// </summary>
    public static class ProceduralSongSynth
    {
        public const int SampleRate = 44100;
        public const float Bpm = 124f;
        public const int Bars = 48;
        public const string Title = "Lạc Hồng Khúc";
        public const string Artist = "Viet Mythical Speed (demo)";

        // Pentatonic pool for the lead, low to high: E4 G4 A4 C5 D5 E5 G5 A5 C6.
        static readonly int[] LeadScale = { 64, 67, 69, 72, 74, 76, 79, 81, 84 };
        // Chord cycle (one chord per bar): Am, C, Gsus, Dsus – voiced inside the pentatonic scale.
        static readonly int[][] Chords =
        {
            new[] { 57, 60, 64 },
            new[] { 60, 64, 67 },
            new[] { 55, 60, 62 },
            new[] { 50, 55, 57 },
        };
        static readonly int[] BassRoots = { 45, 48, 43, 38 };
        static readonly int[][] Rhythms =
        {
            new[] { 4, 2, 2, 4, 4 },
            new[] { 3, 3, 2, 4, 2, 2 },
            new[] { 2, 2, 2, 2, 4, 4 },
            new[] { 6, 2, 4, 4 },
            new[] { 8, 4, 4 },
        };

        public sealed class Arrangement
        {
            public readonly List<float> KickTimes = new List<float>();
            public float DurationSec;
        }

        public static float[] Render(uint seed, Arrangement arrangement = null, Action<float> progress = null)
        {
            float beat = 60f / Bpm;
            float bar = beat * 4f;
            float eighth = beat / 2f;
            float sixteenth = beat / 4f;
            float duration = Bars * bar + 2.5f;
            var buffer = new float[(int)(duration * SampleRate)];
            var rng = new Xoshiro128(seed);
            if (arrangement != null) arrangement.DurationSec = duration;

            Func<float, int> at = seconds => (int)(seconds * SampleRate);

            for (int b = 0; b < Bars; b++)
            {
                float t0 = b * bar;
                int chordIdx = b % 4;
                var chord = Chords[chordIdx];
                bool intro = b < 8, verse = b >= 8 && b < 16, chorusA = b >= 16 && b < 24, breakdown = b >= 24 && b < 28;
                bool build = b >= 28 && b < 32, drop = b >= 32 && b < 44, outro = b >= 44;

                // Pad (everything but the build and the middle of the drop gets a pad).
                if (!build)
                {
                    var notes = new double[chord.Length];
                    for (int i = 0; i < chord.Length; i++) notes[i] = chord[i];
                    float padGain = outro ? 0.32f * (1f - (b - 44) / 4f) : drop ? 0.18f : 0.3f;
                    SynthVoices.Pad(buffer, SampleRate, at(t0), padGain, notes, bar);
                }

                // Drums.
                bool kick = verse || chorusA || build || drop;
                for (int q = 0; q < 4 && kick; q++)
                {
                    float kt = t0 + q * beat;
                    SynthVoices.Kick(buffer, SampleRate, at(kt), 0.95f);
                    if (arrangement != null) arrangement.KickTimes.Add(kt);
                }
                if (chorusA || drop)
                {
                    SynthVoices.Snare(buffer, SampleRate, at(t0 + beat), 0.42f, rng);
                    SynthVoices.Snare(buffer, SampleRate, at(t0 + 3 * beat), 0.42f, rng);
                }
                if (build)
                {
                    int hits = b < 30 ? 8 : 16;
                    float step = bar / hits;
                    for (int i = 0; i < hits; i++)
                        SynthVoices.Snare(buffer, SampleRate, at(t0 + i * step), 0.12f + 0.3f * ((b - 28) * hits + i) / (4f * hits), rng);
                    SynthVoices.Noise(buffer, SampleRate, at(t0), 0.05f + 0.05f * (b - 28), bar, rng, true);
                }
                if ((intro && b >= 4) || verse || chorusA || breakdown || drop)
                {
                    float step = drop ? sixteenth : eighth;
                    int count = (int)Math.Round(bar / step);
                    for (int i = 0; i < count; i++)
                    {
                        bool offbeat = i % 2 == 1;
                        float g = (offbeat ? 0.16f : 0.09f) * (intro || breakdown ? 0.6f : 1f);
                        SynthVoices.Hat(buffer, SampleRate, at(t0 + i * step), g, offbeat && !drop ? 0.05f : 0.025f, rng);
                    }
                }
                if (b == 23 || b == 43)
                {
                    for (int i = 0; i < 4; i++)
                        SynthVoices.Tom(buffer, SampleRate, at(t0 + 3 * beat + i * sixteenth), 0.45f, 160 - i * 25);
                }

                // Bass on eighths.
                if (verse || chorusA || drop || build)
                {
                    int root = BassRoots[chordIdx];
                    for (int i = 0; i < 8; i++)
                    {
                        int note = root + (i % 4 == 3 ? 12 : 0) + (i == 6 ? 7 : 0);
                        SynthVoices.Bass(buffer, SampleRate, at(t0 + i * eighth), 0.32f, note, eighth * 0.85f);
                    }
                }

                // Plucked arpeggios.
                if (breakdown || drop || outro)
                {
                    for (int i = 0; i < 16; i++)
                    {
                        if (breakdown && i % 2 == 1) continue;
                        int tone = chord[i % 3] + 12 * (1 + (i / 3) % 2);
                        float g = outro ? 0.22f * (1f - (b - 44) / 4f) : 0.2f;
                        SynthVoices.Pluck(buffer, SampleRate, at(t0 + i * sixteenth), g, SynthVoices.MidiToHz(tone), 0.9f, rng);
                    }
                }

                if (progress != null) progress(0.8f * (b + 1) / Bars);
            }

            // Lead melody in 8-bar phrases (A A' B A) on intro, chorus A, breakdown and drop.
            RenderLead(buffer, rng, 2, 6, bar, eighth);
            RenderLead(buffer, rng, 16, 8, bar, eighth);
            RenderLead(buffer, rng, 24, 4, bar, eighth);
            RenderLead(buffer, rng, 32, 12, bar, eighth);

            SynthVoices.Master(buffer, 0.92f);
            int fadeStart = (int)((Bars - 2) * bar * SampleRate);
            for (int i = fadeStart; i < buffer.Length; i++)
            {
                float x = (i - fadeStart) / (float)(buffer.Length - fadeStart);
                buffer[i] *= 1f - x;
            }
            if (progress != null) progress(1f);
            return buffer;
        }

        static void RenderLead(float[] buffer, Xoshiro128 rng, int startBar, int bars, float bar, float eighth)
        {
            // Build a two-bar motif and its variation, then lay them out as A A' B A.
            int[] rhythmA = Rhythms[rng.NextInt(Rhythms.Length)];
            int[] rhythmB = Rhythms[rng.NextInt(Rhythms.Length)];
            int[] pitchesA = Walk(rng, rhythmA.Length, 3);
            int[] pitchesA2 = (int[])pitchesA.Clone();
            for (int i = Math.Max(0, pitchesA2.Length - 2); i < pitchesA2.Length; i++)
                pitchesA2[i] = Clamp(pitchesA2[i] + rng.NextInt(5) - 2, 0, LeadScale.Length - 1);
            int[] pitchesB = Walk(rng, rhythmB.Length, 5);

            for (int phrase = 0; phrase * 2 < bars; phrase++)
            {
                int[] rhythm, pitches;
                switch (phrase % 4)
                {
                    case 1: rhythm = rhythmA; pitches = pitchesA2; break;
                    case 2: rhythm = rhythmB; pitches = pitchesB; break;
                    default: rhythm = rhythmA; pitches = pitchesA; break;
                }
                float t = (startBar + phrase * 2) * bar;
                for (int n = 0; n < rhythm.Length; n++)
                {
                    float len = rhythm[n] * eighth;
                    float bend = n == 0 ? -1f : (rng.NextInt(3) == 0 ? 1f : -0.5f);
                    SynthVoices.DanBau(buffer, SampleRate, (int)(t * SampleRate), 0.26f, LeadScale[pitches[n]], len * 0.92f, bend);
                    t += len;
                }
            }
        }

        static int[] Walk(Xoshiro128 rng, int length, int start)
        {
            var p = new int[length];
            int idx = start;
            for (int i = 0; i < length; i++)
            {
                p[i] = idx;
                int step = rng.NextInt(5) - 2;
                idx = Clamp(idx + step, 1, LeadScale.Length - 2);
            }
            return p;
        }

        static int Clamp(int v, int lo, int hi) => v < lo ? lo : v > hi ? hi : v;
    }
}
