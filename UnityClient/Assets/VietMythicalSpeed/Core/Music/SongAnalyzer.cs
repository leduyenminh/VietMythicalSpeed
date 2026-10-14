using System;
using System.Collections.Generic;
using System.Threading;

namespace VMS.Core.Music
{
    /// <summary>
    /// Offline audio analysis: band-wise spectral flux onset detection, loudness / brightness curves and a
    /// tempo estimate. Runs on a worker thread; feed it mono samples (any rate, ~22 kHz is plenty).
    /// </summary>
    public static class SongAnalyzer
    {
        const float LowCutHz = 30f, LowHighHz = 180f, MidHighHz = 2500f, HighHighHz = 11000f;

        public static SongAnalysis Analyze(float[] samples, int sampleRate, Action<float> progress = null,
            CancellationToken cancel = default(CancellationToken))
        {
            if (samples == null) throw new ArgumentNullException(nameof(samples));
            if (sampleRate < 8000) throw new ArgumentException("Sample rate too low.");

            int frameSize = NextPow2((int)(sampleRate * 0.046f));
            int hop = frameSize / 2;
            float hopSec = hop / (float)sampleRate;
            int frames = Math.Max(1, (samples.Length - frameSize) / hop + 1);
            int bins = frameSize / 2;
            float binHz = sampleRate / (float)frameSize;
            int lowLo = Math.Max(1, (int)Math.Round(LowCutHz / binHz));
            int lowHi = Math.Max(lowLo + 1, (int)Math.Round(LowHighHz / binHz));
            int midHi = Math.Max(lowHi + 1, (int)Math.Round(MidHighHz / binHz));
            int highHi = Math.Min(bins - 1, Math.Max(midHi + 1, (int)Math.Round(HighHighHz / binHz)));

            var fft = new Fft(frameSize);
            var window = Fft.HannWindow(frameSize);
            var re = new float[frameSize];
            var im = new float[frameSize];
            var prevLog = new float[bins];

            var fluxLow = new float[frames];
            var fluxMid = new float[frames];
            var fluxHigh = new float[frames];
            var rms = new float[frames];
            var centroid = new float[frames];
            var lowEnergy = new float[frames];

            for (int f = 0; f < frames; f++)
            {
                int offset = f * hop;
                double sumSq = 0;
                for (int i = 0; i < frameSize; i++)
                {
                    int idx = offset + i;
                    float s = idx < samples.Length ? samples[idx] : 0f;
                    sumSq += s * s;
                    re[i] = s * window[i];
                    im[i] = 0f;
                }
                rms[f] = (float)Math.Sqrt(sumSq / frameSize);
                fft.Forward(re, im);

                double fl = 0, fm = 0, fh = 0, num = 0, den = 0, le = 0;
                for (int k = 1; k <= highHi; k++)
                {
                    float mag = (float)Math.Sqrt(re[k] * re[k] + im[k] * im[k]);
                    float logMag = (float)Math.Log(1.0 + 10.0 * mag);
                    float diff = logMag - prevLog[k];
                    prevLog[k] = logMag;
                    num += k * mag;
                    den += mag;
                    if (k < lowLo) continue;
                    if (k < lowHi) le += mag * mag;
                    if (diff <= 0f) continue;
                    if (k < lowHi) fl += diff;
                    else if (k < midHi) fm += diff;
                    else fh += diff;
                }
                if (f > 0)
                {
                    fluxLow[f] = (float)fl;
                    fluxMid[f] = (float)fm;
                    fluxHigh[f] = (float)fh;
                }
                centroid[f] = den > 1e-6 ? (float)(num / den * binHz) : 0f;
                lowEnergy[f] = (float)Math.Sqrt(le);

                if ((f & 63) == 0)
                {
                    cancel.ThrowIfCancellationRequested();
                    if (progress != null) progress(0.9f * f / frames);
                }
            }

            NormalizeByMean(fluxLow);
            NormalizeByMean(fluxMid);
            NormalizeByMean(fluxHigh);
            var odf = new float[frames];
            for (int f = 0; f < frames; f++) odf[f] = 0.5f * fluxLow[f] + 0.3f * fluxMid[f] + 0.2f * fluxHigh[f];
            odf = Smooth3(odf);

            var analysis = new SongAnalysis
            {
                version = SongAnalysis.CurrentVersion,
                duration = samples.Length / (float)sampleRate,
                curveStep = 0.1f,
            };
            // Frame f is reported at its window centre, which is where a transient's flux peaks.
            float frameCentre = 0.5f * frameSize / sampleRate;
            analysis.onsets = PickOnsets(odf, fluxLow, fluxMid, fluxHigh, hopSec, frameCentre);

            int curveLen = Math.Max(2, (int)Math.Ceiling(analysis.duration / analysis.curveStep) + 1);
            analysis.intensity = NormalizePercentile(MovingAverage(Resample(rms, hopSec, analysis.curveStep, curveLen), 10));
            analysis.brightness = NormalizePercentile(MovingAverage(Resample(LogHz(centroid), hopSec, analysis.curveStep, curveLen), 15));
            analysis.lowEnergy = NormalizePercentile(MovingAverage(Resample(lowEnergy, hopSec, analysis.curveStep, curveLen), 8));

            float bpm, beatOffset;
            EstimateTempo(odf, hopSec, out bpm, out beatOffset);
            analysis.bpm = bpm;
            analysis.beatOffset = beatOffset + frameCentre;

            if (progress != null) progress(1f);
            return analysis;
        }

        static OnsetCandidate[] PickOnsets(float[] odf, float[] low, float[] mid, float[] high, float hopSec, float timeOffset)
        {
            int n = odf.Length;
            var prefix = new double[n + 1];
            for (int i = 0; i < n; i++) prefix[i + 1] = prefix[i] + odf[i];
            double globalMean = prefix[n] / Math.Max(1, n);

            int before = Math.Max(1, (int)(0.5f / hopSec));
            int after = Math.Max(1, (int)(0.25f / hopSec));
            int peakRadius = Math.Max(1, (int)(0.07f / hopSec));

            var list = new List<OnsetCandidate>();
            var raw = new List<float>();
            for (int i = 1; i < n - 1; i++)
            {
                float v = odf[i];
                bool isPeak = true;
                for (int k = Math.Max(0, i - peakRadius); k <= Math.Min(n - 1, i + peakRadius); k++)
                {
                    if (odf[k] > v || (odf[k] == v && k < i)) { isPeak = false; break; }
                }
                if (!isPeak) continue;
                int a = Math.Max(0, i - before), b = Math.Min(n, i + after + 1);
                double localMean = (prefix[b] - prefix[a]) / (b - a);
                double threshold = localMean * 1.15 + 0.05 * globalMean;
                if (v <= threshold) continue;

                float sum = low[i] + mid[i] + high[i] + 1e-6f;
                list.Add(new OnsetCandidate
                {
                    t = i * hopSec + timeOffset,
                    low = low[i] / sum,
                    mid = mid[i] / sum,
                    high = high[i] / sum,
                });
                raw.Add((float)(v - localMean));
            }

            if (list.Count == 0) return list.ToArray();
            float p98 = Percentile(raw.ToArray(), 0.98f);
            if (p98 <= 1e-6f) p98 = 1f;
            for (int i = 0; i < list.Count; i++)
                list[i].strength = Math.Min(1f, raw[i] / p98);
            return list.ToArray();
        }

        static void EstimateTempo(float[] odf, float hopSec, out float bpm, out float beatOffset)
        {
            bpm = 120f;
            beatOffset = 0f;
            int n = odf.Length;
            int minLag = Math.Max(1, (int)Math.Floor(60f / 190f / hopSec));
            int maxLag = (int)Math.Ceiling(60f / 65f / hopSec);
            if (n < maxLag * 4) return;

            double mean = 0;
            for (int i = 0; i < n; i++) mean += odf[i];
            mean /= n;

            var ac = new double[maxLag + 2];
            for (int lag = minLag; lag <= maxLag + 1; lag++)
            {
                double s = 0;
                for (int i = 0; i + lag < n; i++) s += (odf[i] - mean) * (odf[i + lag] - mean);
                ac[lag] = s / (n - lag);
            }

            int best = -1;
            double bestScore = double.MinValue;
            for (int lag = minLag + 1; lag <= maxLag; lag++)
            {
                double candidateBpm = 60.0 / (lag * hopSec);
                double octaves = Math.Log(candidateBpm / 120.0, 2.0);
                double prior = Math.Exp(-0.5 * (octaves / 0.8) * (octaves / 0.8));
                double score = ac[lag] * prior;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = lag;
                }
            }
            if (best < 0) return;

            double y0 = ac[best - 1], y1 = ac[best], y2 = ac[best + 1];
            double denom = y0 - 2 * y1 + y2;
            double shift = Math.Abs(denom) > 1e-12 ? 0.5 * (y0 - y2) / denom : 0.0;
            if (shift > 0.5) shift = 0.5;
            if (shift < -0.5) shift = -0.5;
            double period = best + shift;
            bpm = (float)(60.0 / (period * hopSec));

            int steps = Math.Max(1, (int)Math.Round(period));
            double bestPhaseScore = double.MinValue;
            for (int o = 0; o < steps; o++)
            {
                double s = 0;
                for (double p = o; p < n; p += period) s += odf[(int)p];
                if (s > bestPhaseScore)
                {
                    bestPhaseScore = s;
                    beatOffset = o * hopSec;
                }
            }
        }

        static float[] LogHz(float[] hz)
        {
            var o = new float[hz.Length];
            for (int i = 0; i < hz.Length; i++) o[i] = (float)Math.Log(1.0 + hz[i]);
            return o;
        }

        static float[] Resample(float[] src, float srcStep, float dstStep, int dstLen)
        {
            var dst = new float[dstLen];
            for (int i = 0; i < dstLen; i++)
            {
                float t0 = (i - 0.5f) * dstStep, t1 = (i + 0.5f) * dstStep;
                int a = Math.Max(0, (int)(t0 / srcStep));
                int b = Math.Min(src.Length - 1, (int)(t1 / srcStep));
                if (a >= src.Length) { dst[i] = src.Length > 0 ? src[src.Length - 1] : 0f; continue; }
                double s = 0;
                int c = 0;
                for (int k = a; k <= b; k++) { s += src[k]; c++; }
                dst[i] = c > 0 ? (float)(s / c) : src[Math.Min(a, src.Length - 1)];
            }
            return dst;
        }

        internal static float[] MovingAverage(float[] src, int radius)
        {
            int n = src.Length;
            var prefix = new double[n + 1];
            for (int i = 0; i < n; i++) prefix[i + 1] = prefix[i] + src[i];
            var dst = new float[n];
            for (int i = 0; i < n; i++)
            {
                int a = Math.Max(0, i - radius), b = Math.Min(n, i + radius + 1);
                dst[i] = (float)((prefix[b] - prefix[a]) / (b - a));
            }
            return dst;
        }

        static float[] Smooth3(float[] src)
        {
            var dst = new float[src.Length];
            for (int i = 0; i < src.Length; i++)
            {
                float a = src[Math.Max(0, i - 1)], b = src[i], c = src[Math.Min(src.Length - 1, i + 1)];
                dst[i] = 0.25f * a + 0.5f * b + 0.25f * c;
            }
            return dst;
        }

        static void NormalizeByMean(float[] values)
        {
            double sum = 0;
            for (int i = 0; i < values.Length; i++) sum += values[i];
            double mean = sum / Math.Max(1, values.Length);
            if (mean <= 1e-9) return;
            float inv = (float)(1.0 / mean);
            for (int i = 0; i < values.Length; i++) values[i] *= inv;
        }

        internal static float[] NormalizePercentile(float[] values)
        {
            float lo = Percentile(values, 0.05f), hi = Percentile(values, 0.95f);
            var o = new float[values.Length];
            float range = hi - lo;
            for (int i = 0; i < values.Length; i++)
            {
                float v = range > 1e-9f ? (values[i] - lo) / range : 0.5f;
                o[i] = v < 0f ? 0f : v > 1f ? 1f : v;
            }
            return o;
        }

        internal static float Percentile(float[] values, float q)
        {
            if (values.Length == 0) return 0f;
            var copy = (float[])values.Clone();
            Array.Sort(copy);
            int idx = (int)Math.Round(q * (copy.Length - 1));
            return copy[Math.Max(0, Math.Min(copy.Length - 1, idx))];
        }

        static int NextPow2(int v)
        {
            int p = 1;
            while (p < v) p <<= 1;
            return p;
        }
    }
}
