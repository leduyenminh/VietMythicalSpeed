using System;

namespace VMS.Core.Music
{
    /// <summary>Procedural sound effects so the game ships without third-party audio assets.</summary>
    public static class SfxSynth
    {
        public const int SampleRate = 44100;

        // A-minor pentatonic across two octaves; the collect sound climbs it with the combo.
        static readonly int[] PentatonicMidi = { 69, 72, 74, 76, 79, 81, 84, 86, 88, 91 };

        public static int CollectVariants => PentatonicMidi.Length;

        public static float[] Collect(int step, bool big)
        {
            var rng = new Xoshiro128(1000u + (uint)step);
            int midi = PentatonicMidi[Math.Abs(step) % PentatonicMidi.Length];
            var pluck = SynthVoices.Pluck(SampleRate, SynthVoices.MidiToHz(midi), big ? 0.9f : 0.45f, rng, 0.994f);
            var buf = new float[pluck.Length];
            for (int i = 0; i < buf.Length; i++) buf[i] = pluck[i] * 0.5f;
            if (big)
            {
                var octave = SynthVoices.Pluck(SampleRate, SynthVoices.MidiToHz(midi + 12), 0.9f, rng, 0.994f);
                for (int i = 0; i < buf.Length && i < octave.Length; i++) buf[i] += octave[i] * 0.3f;
            }
            SynthVoices.Master(buf, 0.8f);
            return buf;
        }

        public static float[] Miss()
        {
            int len = (int)(0.18f * SampleRate);
            var buf = new float[len];
            double phase = 0;
            for (int i = 0; i < len; i++)
            {
                double t = i / (double)SampleRate;
                phase += (220.0 - 300.0 * t) / SampleRate;
                buf[i] = 0.35f * SynthVoices.Sin01(phase) * (float)Math.Exp(-t / 0.06);
            }
            return buf;
        }

        public static float[] HazardHit()
        {
            var buf = new float[(int)(0.5f * SampleRate)];
            var rng = new Xoshiro128(77u);
            SynthVoices.Tom(buf, SampleRate, 0, 0.7f, 70);
            SynthVoices.Noise(buf, SampleRate, 0, 0.35f, 0.3f, rng, false);
            SynthVoices.Master(buf, 0.85f);
            return buf;
        }

        /// <summary>Temple bell for gacha reveals; pitch rises with rarity.</summary>
        public static float[] Bell(int rarity)
        {
            float seconds = 2.2f;
            var buf = new float[(int)(seconds * SampleRate)];
            double baseHz = rarity >= 3 ? 523.25 : rarity == 2 ? 440.0 : 349.23;
            double[] partials = { 1.0, 2.0, 2.76, 5.4, 8.93 };
            float[] gains = { 0.5f, 0.25f, 0.18f, 0.08f, 0.05f };
            for (int i = 0; i < buf.Length; i++)
            {
                double t = i / (double)SampleRate;
                float s = 0f;
                for (int p = 0; p < partials.Length; p++)
                    s += gains[p] * SynthVoices.Sin01(baseHz * partials[p] * t) * (float)Math.Exp(-t / (1.4 / (p + 1)));
                buf[i] = s * (float)Math.Min(1.0, t / 0.003);
            }
            SynthVoices.Master(buf, 0.8f);
            return buf;
        }

        /// <summary>Bamboo fortune sticks rattling in their cylinder (xin xăm).</summary>
        public static float[] StickRattle(float seconds)
        {
            var buf = new float[(int)(seconds * SampleRate)];
            var rng = new Xoshiro128(4242u);
            float t = 0f;
            while (t < seconds - 0.05f)
            {
                double hz = 900 + rng.NextDouble() * 900;
                var click = SynthVoices.Pluck(SampleRate, hz, 0.05f, rng, 0.9f);
                int start = (int)(t * SampleRate);
                float g = 0.25f + (float)rng.NextDouble() * 0.2f;
                for (int i = 0; i < click.Length && start + i < buf.Length; i++) buf[start + i] += click[i] * g;
                t += 0.035f + (float)rng.NextDouble() * 0.05f;
            }
            SynthVoices.Master(buf, 0.7f);
            return buf;
        }

        public static float[] UiTap()
        {
            var rng = new Xoshiro128(9u);
            var pluck = SynthVoices.Pluck(SampleRate, SynthVoices.MidiToHz(84), 0.12f, rng, 0.97f);
            for (int i = 0; i < pluck.Length; i++) pluck[i] *= 0.35f;
            return pluck;
        }

        public static float[] Countdown(bool go)
        {
            var buf = new float[(int)(0.35f * SampleRate)];
            double hz = go ? 880 : 587.33;
            for (int i = 0; i < buf.Length; i++)
            {
                double t = i / (double)SampleRate;
                buf[i] = 0.35f * SynthVoices.Sin01(hz * t) * (float)Math.Exp(-t / 0.12);
            }
            return buf;
        }
    }
}
