using System;

namespace VMS.Core.Music
{
    [Serializable]
    public class OnsetCandidate
    {
        public float t;
        /// <summary>0..1, relative to the strongest onsets of the song.</summary>
        public float strength;
        /// <summary>Share of the onset's spectral flux in the low / mid / high band (sums to ~1).</summary>
        public float low;
        public float mid;
        public float high;
    }

    /// <summary>
    /// Everything the game needs to know about a song, computed once at import and cached as JSON
    /// (difficulty-independent; beatmaps are generated from it at race start).
    /// </summary>
    [Serializable]
    public class SongAnalysis
    {
        public const int CurrentVersion = 1;

        public int version;
        public float duration;
        public float bpm;
        /// <summary>Time of a reference beat; beats occur at beatOffset + k * 60 / bpm.</summary>
        public float beatOffset;
        /// <summary>Seconds between samples of the curves below.</summary>
        public float curveStep = 0.1f;
        /// <summary>Loudness, smoothed and normalised to 0..1.</summary>
        public float[] intensity;
        /// <summary>Spectral centroid, normalised to 0..1.</summary>
        public float[] brightness;
        /// <summary>Bass energy, normalised to 0..1.</summary>
        public float[] lowEnergy;
        public OnsetCandidate[] onsets;

        public float Sample(float[] curve, float t)
        {
            if (curve == null || curve.Length == 0) return 0f;
            float x = t / curveStep;
            if (x <= 0) return curve[0];
            int i = (int)x;
            if (i >= curve.Length - 1) return curve[curve.Length - 1];
            float f = x - i;
            return curve[i] + (curve[i + 1] - curve[i]) * f;
        }

        public float IntensityAt(float t) => Sample(intensity, t);
        public float BrightnessAt(float t) => Sample(brightness, t);
        public float LowEnergyAt(float t) => Sample(lowEnergy, t);
    }
}
