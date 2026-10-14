using System;

namespace VMS.Core.Music
{
    /// <summary>In-place iterative radix-2 complex FFT with precomputed twiddles. Not thread-safe per instance.</summary>
    public sealed class Fft
    {
        readonly int _n;
        readonly int[] _bitReverse;
        readonly float[] _cos;
        readonly float[] _sin;

        public int Size => _n;

        public Fft(int size)
        {
            if (size < 2 || (size & (size - 1)) != 0) throw new ArgumentException("FFT size must be a power of two.");
            _n = size;
            int bits = 0;
            while ((1 << bits) < size) bits++;
            _bitReverse = new int[size];
            for (int i = 0; i < size; i++)
            {
                int r = 0;
                for (int b = 0; b < bits; b++) r |= ((i >> b) & 1) << (bits - 1 - b);
                _bitReverse[i] = r;
            }
            _cos = new float[size / 2];
            _sin = new float[size / 2];
            for (int i = 0; i < size / 2; i++)
            {
                double a = -2.0 * Math.PI * i / size;
                _cos[i] = (float)Math.Cos(a);
                _sin[i] = (float)Math.Sin(a);
            }
        }

        public void Forward(float[] re, float[] im)
        {
            int n = _n;
            for (int i = 0; i < n; i++)
            {
                int j = _bitReverse[i];
                if (j > i)
                {
                    float tr = re[i]; re[i] = re[j]; re[j] = tr;
                    float ti = im[i]; im[i] = im[j]; im[j] = ti;
                }
            }
            for (int len = 2; len <= n; len <<= 1)
            {
                int half = len >> 1;
                int step = n / len;
                for (int start = 0; start < n; start += len)
                {
                    for (int k = 0; k < half; k++)
                    {
                        float wr = _cos[k * step];
                        float wi = _sin[k * step];
                        int a = start + k;
                        int b = a + half;
                        float xr = re[b] * wr - im[b] * wi;
                        float xi = re[b] * wi + im[b] * wr;
                        re[b] = re[a] - xr;
                        im[b] = im[a] - xi;
                        re[a] += xr;
                        im[a] += xi;
                    }
                }
            }
        }

        public static float[] HannWindow(int size)
        {
            var w = new float[size];
            for (int i = 0; i < size; i++) w[i] = 0.5f - 0.5f * (float)Math.Cos(2.0 * Math.PI * i / (size - 1));
            return w;
        }
    }
}
