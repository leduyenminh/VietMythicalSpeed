using System;

namespace VMS.Core.Music
{
    /// <summary>Small offline synthesis toolkit shared by the demo song and the sound effects.</summary>
    public static class SynthVoices
    {
        const int SineTableSize = 4096;
        static readonly float[] SineTable = BuildSineTable();

        static float[] BuildSineTable()
        {
            var t = new float[SineTableSize + 1];
            for (int i = 0; i <= SineTableSize; i++) t[i] = (float)Math.Sin(2.0 * Math.PI * i / SineTableSize);
            return t;
        }

        /// <summary>Table sine for a phase in cycles (any real value).</summary>
        public static float Sin01(double phase)
        {
            double p = phase - Math.Floor(phase);
            double x = p * SineTableSize;
            int i = (int)x;
            float f = (float)(x - i);
            return SineTable[i] + (SineTable[i + 1] - SineTable[i]) * f;
        }

        public static double MidiToHz(double midi) => 440.0 * Math.Pow(2.0, (midi - 69.0) / 12.0);

        static float PolyBlep(double t, double dt)
        {
            if (t < dt)
            {
                t /= dt;
                return (float)(t + t - t * t - 1.0);
            }
            if (t > 1.0 - dt)
            {
                t = (t - 1.0) / dt;
                return (float)(t * t + t + t + 1.0);
            }
            return 0f;
        }

        /// <summary>Band-limited sawtooth step; phase in cycles [0,1).</summary>
        public static float Saw(ref double phase, double inc)
        {
            float v = (float)(2.0 * phase - 1.0) - PolyBlep(phase, inc);
            phase += inc;
            if (phase >= 1.0) phase -= 1.0;
            return v;
        }

        static void Mix(float[] buffer, int start, float[] voice, float gain)
        {
            for (int i = 0; i < voice.Length; i++)
            {
                int idx = start + i;
                if (idx < 0) continue;
                if (idx >= buffer.Length) break;
                buffer[idx] += voice[i] * gain;
            }
        }

        public static void Kick(float[] buffer, int sampleRate, int start, float gain)
        {
            int len = (int)(0.45f * sampleRate);
            var v = new float[len];
            double phase = 0;
            for (int i = 0; i < len; i++)
            {
                double t = i / (double)sampleRate;
                double f = 45.0 + 85.0 * Math.Exp(-t / 0.03);
                phase += f / sampleRate;
                float amp = (float)Math.Exp(-t / 0.28);
                float click = i < sampleRate * 0.003 ? 0.3f * (1f - i / (float)(sampleRate * 0.003)) : 0f;
                v[i] = Sin01(phase) * amp + click;
            }
            Mix(buffer, start, v, gain);
        }

        public static void Snare(float[] buffer, int sampleRate, int start, float gain, Xoshiro128 rng)
        {
            int len = (int)(0.25f * sampleRate);
            var v = new float[len];
            float prev = 0f;
            double phase = 0;
            for (int i = 0; i < len; i++)
            {
                double t = i / (double)sampleRate;
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                float hp = noise - prev;
                prev = noise;
                phase += 185.0 / sampleRate;
                v[i] = 0.5f * Sin01(phase) * (float)Math.Exp(-t / 0.05) + 0.55f * hp * (float)Math.Exp(-t / 0.11);
            }
            Mix(buffer, start, v, gain);
        }

        public static void Hat(float[] buffer, int sampleRate, int start, float gain, float decay, Xoshiro128 rng)
        {
            int len = (int)(decay * 4f * sampleRate);
            var v = new float[len];
            float p1 = 0f, p2 = 0f;
            for (int i = 0; i < len; i++)
            {
                double t = i / (double)sampleRate;
                float n = (float)(rng.NextDouble() * 2.0 - 1.0);
                float h1 = n - p1;
                p1 = n;
                float h2 = h1 - p2;
                p2 = h1;
                v[i] = 0.5f * h2 * (float)Math.Exp(-t / decay);
            }
            Mix(buffer, start, v, gain);
        }

        public static void Tom(float[] buffer, int sampleRate, int start, float gain, double baseHz)
        {
            int len = (int)(0.35f * sampleRate);
            var v = new float[len];
            double phase = 0;
            for (int i = 0; i < len; i++)
            {
                double t = i / (double)sampleRate;
                phase += (baseHz + baseHz * 1.2 * Math.Exp(-t / 0.04)) / sampleRate;
                v[i] = Sin01(phase) * (float)Math.Exp(-t / 0.16);
            }
            Mix(buffer, start, v, gain);
        }

        /// <summary>Plucked string (Karplus–Strong) – reads like a đàn tranh zither.</summary>
        public static float[] Pluck(int sampleRate, double hz, float seconds, Xoshiro128 rng, float damping = 0.996f)
        {
            int len = (int)(seconds * sampleRate);
            var v = new float[len];
            int period = Math.Max(2, (int)Math.Round(sampleRate / hz));
            var ring = new float[period];
            for (int i = 0; i < period; i++) ring[i] = (float)(rng.NextDouble() * 2.0 - 1.0);
            int idx = 0;
            for (int i = 0; i < len; i++)
            {
                int next = (idx + 1) % period;
                float y = damping * 0.5f * (ring[idx] + ring[next]);
                v[i] = ring[idx];
                ring[idx] = y;
                idx = next;
            }
            int fade = Math.Min(len, sampleRate / 50);
            for (int i = 0; i < fade; i++) v[len - 1 - i] *= i / (float)fade;
            return v;
        }

        public static void Pluck(float[] buffer, int sampleRate, int start, float gain, double hz, float seconds, Xoshiro128 rng)
        {
            Mix(buffer, start, Pluck(sampleRate, hz, seconds, rng), gain);
        }

        /// <summary>
        /// Monochord voice in the spirit of the đàn bầu: sine-rich tone that glides into the note and
        /// develops a slow vibrato, the instrument's signature bend.
        /// </summary>
        public static void DanBau(float[] buffer, int sampleRate, int start, float gain, double midi, float seconds, float bendFrom)
        {
            int len = (int)((seconds + 0.08f) * sampleRate);
            var v = new float[len];
            double phase = 0;
            for (int i = 0; i < len; i++)
            {
                double t = i / (double)sampleRate;
                double glide = bendFrom * Math.Exp(-t / 0.05);
                double vibDepth = Math.Min(1.0, Math.Max(0.0, (t - 0.15) / 0.4)) * 0.35;
                double m = midi + glide + vibDepth * Math.Sin(2.0 * Math.PI * 5.5 * t);
                phase += MidiToHz(m) / sampleRate;
                float tone = Sin01(phase) + 0.25f * Sin01(phase * 2.0) + 0.08f * Sin01(phase * 3.0);
                float env = (float)(Math.Min(1.0, t / 0.012) * Math.Exp(-t / 1.1));
                if (t > seconds) env *= (float)Math.Max(0.0, 1.0 - (t - seconds) / 0.08);
                v[i] = tone * env;
            }
            Mix(buffer, start, v, gain);
        }

        public static void Bass(float[] buffer, int sampleRate, int start, float gain, double midi, float seconds)
        {
            int len = (int)((seconds + 0.04f) * sampleRate);
            double hz = MidiToHz(midi);
            double phase = 0, inc = hz / sampleRate;
            float lp = 0f;
            float alpha = 1f - (float)Math.Exp(-2.0 * Math.PI * 420.0 / sampleRate);
            var v = new float[len];
            for (int i = 0; i < len; i++)
            {
                double t = i / (double)sampleRate;
                float s = Saw(ref phase, inc) * 0.6f + Sin01(phase) * 0.6f;
                lp += alpha * (s - lp);
                float env = (float)(Math.Min(1.0, t / 0.005) * (0.6 + 0.4 * Math.Exp(-t / 0.08)));
                if (t > seconds) env *= (float)Math.Max(0.0, 1.0 - (t - seconds) / 0.04);
                v[i] = lp * env;
            }
            Mix(buffer, start, v, gain);
        }

        public static void Pad(float[] buffer, int sampleRate, int start, float gain, double[] midiNotes, float seconds)
        {
            int len = (int)((seconds + 0.5f) * sampleRate);
            var v = new float[len];
            float alpha = 1f - (float)Math.Exp(-2.0 * Math.PI * 1100.0 / sampleRate);
            var phases = new double[midiNotes.Length * 2];
            var incs = new double[midiNotes.Length * 2];
            for (int n = 0; n < midiNotes.Length; n++)
            {
                incs[n * 2] = MidiToHz(midiNotes[n] + 0.07) / sampleRate;
                incs[n * 2 + 1] = MidiToHz(midiNotes[n] - 0.07) / sampleRate;
                phases[n * 2 + 1] = 0.37 * n;
            }
            float lp = 0f;
            for (int i = 0; i < len; i++)
            {
                double t = i / (double)sampleRate;
                float s = 0f;
                for (int k = 0; k < phases.Length; k++) s += Saw(ref phases[k], incs[k]);
                lp += alpha * (s - lp);
                float env = (float)Math.Min(1.0, t / 0.4);
                if (t > seconds) env *= (float)Math.Max(0.0, 1.0 - (t - seconds) / 0.5);
                v[i] = lp * env / phases.Length;
            }
            Mix(buffer, start, v, gain);
        }

        public static void Noise(float[] buffer, int sampleRate, int start, float gain, float seconds, Xoshiro128 rng, bool rising)
        {
            int len = (int)(seconds * sampleRate);
            var v = new float[len];
            float prev = 0f;
            for (int i = 0; i < len; i++)
            {
                float x = i / (float)len;
                float n = (float)(rng.NextDouble() * 2.0 - 1.0);
                float hp = n - prev;
                prev = n;
                float env = rising ? x * x : (1f - x) * (1f - x);
                v[i] = hp * env;
            }
            Mix(buffer, start, v, gain);
        }

        /// <summary>Soft-clips and normalises to the given peak.</summary>
        public static void Master(float[] buffer, float peak)
        {
            float max = 0f;
            for (int i = 0; i < buffer.Length; i++)
            {
                buffer[i] = (float)Math.Tanh(buffer[i] * 1.1);
                float a = Math.Abs(buffer[i]);
                if (a > max) max = a;
            }
            if (max <= 1e-6f) return;
            float g = peak / max;
            for (int i = 0; i < buffer.Length; i++) buffer[i] *= g;
        }
    }
}
