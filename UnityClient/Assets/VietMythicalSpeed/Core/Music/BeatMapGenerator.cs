using System;
using System.Collections.Generic;

namespace VMS.Core.Music
{
    public sealed class DifficultyProfile
    {
        /// <summary>Minimum seconds between two notes.</summary>
        public float minGap;
        /// <summary>Share of onset candidates kept (strongest first).</summary>
        public float keepFraction;
        /// <summary>Notes closer than this may only move one lane.</summary>
        public float fastGap;
        public float baseSpeed;
        public float maxYaw;
        /// <summary>Dodge mode: time the player needs to move one lane.</summary>
        public float reactionTime;
        /// <summary>Strength percentile above which a pearl becomes a big pearl.</summary>
        public float bigPercentile;
    }

    /// <summary>Turns a <see cref="SongAnalysis"/> into a playable <see cref="BeatMap"/>. Deterministic for a given seed.</summary>
    public static class BeatMapGenerator
    {
        public const float LeadIn = 1.5f;

        public static DifficultyProfile Profile(Difficulty difficulty)
        {
            switch (difficulty)
            {
                case Difficulty.Easy:
                    return new DifficultyProfile { minGap = 0.42f, keepFraction = 0.45f, fastGap = 0.6f, baseSpeed = 30f, maxYaw = 20f, reactionTime = 0.7f, bigPercentile = 0.9f };
                case Difficulty.Hard:
                    return new DifficultyProfile { minGap = 0.15f, keepFraction = 0.9f, fastGap = 0.24f, baseSpeed = 44f, maxYaw = 32f, reactionTime = 0.34f, bigPercentile = 0.93f };
                default:
                    return new DifficultyProfile { minGap = 0.25f, keepFraction = 0.68f, fastGap = 0.36f, baseSpeed = 37f, maxYaw = 26f, reactionTime = 0.48f, bigPercentile = 0.92f };
            }
        }

        public static BeatMap Generate(SongAnalysis analysis, Difficulty difficulty, GameMode mode, uint seed)
        {
            if (analysis == null) throw new ArgumentNullException(nameof(analysis));
            var profile = Profile(difficulty);
            var map = new BeatMap
            {
                duration = analysis.duration,
                bpm = analysis.bpm,
                beatOffset = analysis.beatOffset,
                laneCount = 3,
                controlStep = 0.1f,
            };
            BuildControls(analysis, profile, seed, map);
            map.sectionTimes = DetectSections(analysis);

            if (mode == GameMode.Zen)
            {
                map.notes = new NoteData[0];
                return map;
            }

            var selected = SelectOnsets(analysis, profile);
            map.notes = mode == GameMode.Dodge
                ? PlaceHazards(selected, profile, seed, map.sectionTimes, difficulty)
                : PlacePearls(selected, profile, seed, map.sectionTimes);
            return map;
        }

        static void BuildControls(SongAnalysis a, DifficultyProfile p, uint seed, BeatMap map)
        {
            int n = Math.Max(2, (int)Math.Ceiling(a.duration / map.controlStep) + 1);
            var yawNoiseA = new ValueNoise1D(seed * 31u + 1u);
            var yawNoiseB = new ValueNoise1D(seed * 31u + 2u);
            var pitchNoise = new ValueNoise1D(seed * 31u + 3u);

            var speed = new float[n];
            var yaw = new float[n];
            var pitch = new float[n];
            var intensity = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i * map.controlStep;
                float inten = a.IntensityAt(t);
                float bright = a.BrightnessAt(t);
                float low = a.LowEnergyAt(t);
                intensity[i] = inten;
                speed[i] = p.baseSpeed * (0.8f + 0.45f * inten);
                float wander = 0.7f * yawNoiseA.Sample(t * 0.06f) + 0.3f * yawNoiseB.Sample(t * 0.19f);
                float ramp = SmoothStep(Math.Min(1f, t / 4f));
                yaw[i] = (p.maxYaw * wander * (0.5f + 0.7f * inten) + (bright - 0.5f) * 6f) * ramp;
                pitch[i] = (6f * pitchNoise.Sample(t * 0.045f) + 5f * (low - 0.5f)) * ramp;
            }
            speed = SongAnalyzer.MovingAverage(speed, 8);
            yaw = SongAnalyzer.MovingAverage(yaw, 8);
            pitch = SongAnalyzer.MovingAverage(pitch, 10);

            // Centre the slope so the track does not drift ever upward (or downward) over a long song.
            double pitchMean = 0;
            for (int i = 0; i < n; i++) pitchMean += pitch[i];
            pitchMean /= n;
            for (int i = 0; i < n; i++) pitch[i] = (pitch[i] - (float)pitchMean) * SmoothStep(Math.Min(1f, i * map.controlStep / 4f));

            for (int i = 0; i < n; i++)
            {
                yaw[i] = Clamp(yaw[i], -p.maxYaw * 1.2f, p.maxYaw * 1.2f);
                pitch[i] = Clamp(pitch[i], -10f, 10f);
            }
            map.speed = speed;
            map.yawRate = yaw;
            map.pitch = pitch;
            map.intensity = intensity;
        }

        /// <summary>Section boundaries where loudness changes noticeably (used for lane remaps and gates).</summary>
        public static float[] DetectSections(SongAnalysis a)
        {
            var times = new List<float>();
            const float window = 4f, minSpacing = 8f, minDelta = 0.2f;
            float last = -minSpacing;
            for (float t = window; t < a.duration - window; t += 0.5f)
            {
                float before = MeanIntensity(a, t - window, t);
                float after = MeanIntensity(a, t, t + window);
                if (Math.Abs(after - before) >= minDelta && t - last >= minSpacing)
                {
                    times.Add(t);
                    last = t;
                }
            }
            return times.ToArray();
        }

        static float MeanIntensity(SongAnalysis a, float t0, float t1)
        {
            float s = 0;
            int c = 0;
            for (float t = t0; t < t1; t += a.curveStep) { s += a.IntensityAt(t); c++; }
            return c > 0 ? s / c : 0f;
        }

        public static List<OnsetCandidate> SelectOnsets(SongAnalysis a, DifficultyProfile p)
        {
            var candidates = new List<OnsetCandidate>();
            if (a.onsets != null)
                foreach (var o in a.onsets)
                    if (o.t >= LeadIn && o.t <= a.duration - 0.5f) candidates.Add(o);

            var accepted = new List<OnsetCandidate>();
            var acceptedTimes = new List<float>();
            if (candidates.Count > 0)
            {
                var strengths = new float[candidates.Count];
                for (int i = 0; i < candidates.Count; i++) strengths[i] = candidates[i].strength;
                float q = SongAnalyzer.Percentile(strengths, 1f - p.keepFraction);

                var eligible = new List<OnsetCandidate>();
                foreach (var c in candidates)
                    if (c.strength >= q * (1.25f - 0.5f * a.IntensityAt(c.t))) eligible.Add(c);
                eligible.Sort((x, y) => y.strength.CompareTo(x.strength));
                foreach (var c in eligible)
                    if (TryInsert(acceptedTimes, c.t, p.minGap)) accepted.Add(c);
            }

            // Ambient or very quiet songs produce few onsets – fill with a beat grid so there is always something to play.
            float wanted = (a.duration - LeadIn) / (p.minGap * 6f);
            if (accepted.Count < wanted && a.bpm > 30f)
            {
                float period = 60f / a.bpm;
                float step = period * (p.minGap < 0.2f ? 1f : 2f);
                float first = a.beatOffset;
                while (first < LeadIn) first += step;
                for (float t = first; t <= a.duration - 0.5f; t += step)
                {
                    if (!TryInsert(acceptedTimes, t, p.minGap)) continue;
                    accepted.Add(new OnsetCandidate { t = t, strength = 0.4f, low = 0.34f, mid = 0.33f, high = 0.33f });
                }
            }

            accepted.Sort((x, y) => x.t.CompareTo(y.t));
            return accepted;
        }

        static bool TryInsert(List<float> sortedTimes, float t, float minGap)
        {
            int idx = sortedTimes.BinarySearch(t);
            if (idx >= 0) return false;
            idx = ~idx;
            if (idx > 0 && t - sortedTimes[idx - 1] < minGap) return false;
            if (idx < sortedTimes.Count && sortedTimes[idx] - t < minGap) return false;
            sortedTimes.Insert(idx, t);
            return true;
        }

        /// <summary>Average low/mid/high share over the selected onsets.</summary>
        static float[] MeanShares(List<OnsetCandidate> onsets)
        {
            var mean = new float[3];
            if (onsets.Count == 0) return mean;
            foreach (var o in onsets)
            {
                mean[0] += o.low;
                mean[1] += o.mid;
                mean[2] += o.high;
            }
            for (int i = 0; i < 3; i++) mean[i] /= onsets.Count;
            return mean;
        }

        /// <summary>
        /// The band that stands out most compared with the song's typical balance. Using the deviation instead of
        /// the raw maximum keeps bass-heavy songs from mapping almost everything to one lane.
        /// </summary>
        static int DominantBand(OnsetCandidate o, float[] mean)
        {
            float dl = o.low - mean[0], dm = o.mid - mean[1], dh = o.high - mean[2];
            if (dl >= dm && dl >= dh) return 0;
            return dm >= dh ? 1 : 2;
        }

        /// <summary>Most-used lane in the recent history, or -1 when nothing dominates.</summary>
        static int OverusedLane(Queue<int> recent)
        {
            if (recent.Count < 6) return -1;
            var counts = new int[3];
            foreach (int l in recent) counts[l]++;
            for (int l = 0; l < 3; l++) if (counts[l] * 2 > recent.Count) return l;
            return -1;
        }

        static int[] Shuffle3(IRandomSource rng)
        {
            var perm = new[] { 0, 1, 2 };
            for (int i = 2; i > 0; i--)
            {
                int j = rng.NextInt(i + 1);
                int tmp = perm[i]; perm[i] = perm[j]; perm[j] = tmp;
            }
            return perm;
        }

        static NoteData[] PlacePearls(List<OnsetCandidate> onsets, DifficultyProfile p, uint seed, float[] sections)
        {
            var rng = new Xoshiro128(seed ^ 0xA5A5A5A5u);
            var notes = new List<NoteData>(onsets.Count);
            float bigThreshold = StrengthPercentile(onsets, p.bigPercentile);
            float[] meanShares = MeanShares(onsets);
            var recent = new Queue<int>();
            int[] perm = { 0, 1, 2 };
            int sectionIdx = 0, prevLane = 1, sameLaneRun = 0;
            float prevT = -10f;

            foreach (var o in onsets)
            {
                while (sections != null && sectionIdx < sections.Length && o.t >= sections[sectionIdx])
                {
                    perm = Shuffle3(rng);
                    sectionIdx++;
                }
                float gap = o.t - prevT;
                int preferred = perm[DominantBand(o, meanShares)];
                int overused = OverusedLane(recent);
                if (preferred == overused)
                {
                    int alt = (preferred + 1 + rng.NextInt(2)) % 3;
                    preferred = alt;
                }
                int lane;
                if (gap < 0.13f) lane = prevLane;
                else if (gap < p.fastGap) lane = Clamp(preferred, prevLane - 1, prevLane + 1);
                else lane = preferred;

                if (lane == prevLane && gap >= 0.13f) sameLaneRun++;
                else sameLaneRun = 0;
                if (sameLaneRun >= 3)
                {
                    int dir = rng.NextInt(2) == 0 ? -1 : 1;
                    if (lane + dir < 0 || lane + dir > 2) dir = -dir;
                    lane += dir;
                    sameLaneRun = 0;
                }
                recent.Enqueue(lane);
                if (recent.Count > 8) recent.Dequeue();

                notes.Add(new NoteData
                {
                    t = o.t,
                    lane = lane,
                    kind = (int)(o.strength >= bigThreshold ? NoteKind.BigPearl : NoteKind.Pearl),
                });
                prevLane = lane;
                prevT = o.t;
            }
            return notes.ToArray();
        }

        static NoteData[] PlaceHazards(List<OnsetCandidate> onsets, DifficultyProfile p, uint seed, float[] sections,
            Difficulty difficulty)
        {
            var rng = new Xoshiro128(seed ^ 0x5A5A5A5Au);
            var notes = new List<NoteData>();
            float bigThreshold = StrengthPercentile(onsets, p.bigPercentile);
            float[] meanShares = MeanShares(onsets);
            int[] perm = { 0, 1, 2 };
            int sectionIdx = 0, safe = 1, events = 0;
            float prevT = -10f;
            float minSpacing = p.reactionTime * 0.6f;

            foreach (var o in onsets)
            {
                float gap = o.t - prevT;
                if (gap < minSpacing) continue;
                while (sections != null && sectionIdx < sections.Length && o.t >= sections[sectionIdx])
                {
                    perm = Shuffle3(rng);
                    sectionIdx++;
                }
                int desire = perm[DominantBand(o, meanShares)];
                safe = gap < p.reactionTime ? Clamp(desire, safe - 1, safe + 1) : desire;

                bool wall = difficulty == Difficulty.Hard && o.strength >= 0.6f && gap >= p.reactionTime;
                if (wall)
                {
                    for (int lane = 0; lane < 3; lane++)
                        if (lane != safe) notes.Add(new NoteData { t = o.t, lane = lane, kind = (int)NoteKind.Hazard });
                }
                else
                {
                    int a = (safe + 1) % 3, b = (safe + 2) % 3;
                    int lane = rng.NextInt(2) == 0 ? a : b;
                    notes.Add(new NoteData { t = o.t, lane = lane, kind = (int)NoteKind.Hazard });
                }

                events++;
                if (events % 3 == 0)
                {
                    notes.Add(new NoteData
                    {
                        t = o.t,
                        lane = safe,
                        kind = (int)(o.strength >= bigThreshold ? NoteKind.BigPearl : NoteKind.Pearl),
                    });
                }
                prevT = o.t;
            }
            notes.Sort((x, y) => x.t != y.t ? x.t.CompareTo(y.t) : x.lane.CompareTo(y.lane));
            return notes.ToArray();
        }

        static float StrengthPercentile(List<OnsetCandidate> onsets, float q)
        {
            if (onsets.Count == 0) return 1f;
            var s = new float[onsets.Count];
            for (int i = 0; i < s.Length; i++) s[i] = onsets[i].strength;
            return SongAnalyzer.Percentile(s, q);
        }

        static float SmoothStep(float x) => x * x * (3f - 2f * x);
        static float Clamp(float v, float lo, float hi) => v < lo ? lo : v > hi ? hi : v;
        static int Clamp(int v, int lo, int hi) => v < lo ? lo : v > hi ? hi : v;
    }
}
