using System;

namespace VMS.Core.Music
{
    public enum Difficulty
    {
        Easy = 0,
        Normal = 1,
        Hard = 2,
    }

    public enum GameMode
    {
        /// <summary>Collect pearls, navigator skills disabled (fair scores).</summary>
        Classic = 0,
        /// <summary>Collect pearls with the navigator's active and passive skills.</summary>
        Skill = 1,
        /// <summary>Dodge ghost fires (ma trơi), occasional pearls in the safe lane.</summary>
        Dodge = 2,
        /// <summary>No notes, no score – just ride the music.</summary>
        Zen = 3,
    }

    public enum NoteKind
    {
        Pearl = 0,
        BigPearl = 1,
        Hazard = 2,
    }

    [Serializable]
    public class NoteData
    {
        public float t;
        public int lane;
        public int kind;

        public NoteKind Kind => (NoteKind)kind;
    }

    /// <summary>A playable chart: notes plus the control curves the track is built from.</summary>
    [Serializable]
    public class BeatMap
    {
        public float duration;
        public float bpm;
        public float beatOffset;
        public int laneCount = 3;
        public float controlStep = 0.1f;
        /// <summary>Forward speed in m/s.</summary>
        public float[] speed;
        /// <summary>Heading change in degrees per second (positive turns right).</summary>
        public float[] yawRate;
        /// <summary>Slope in degrees (positive climbs).</summary>
        public float[] pitch;
        public float[] intensity;
        public float[] sectionTimes;
        public NoteData[] notes;

        public float Sample(float[] curve, float t)
        {
            if (curve == null || curve.Length == 0) return 0f;
            float x = t / controlStep;
            if (x <= 0) return curve[0];
            int i = (int)x;
            if (i >= curve.Length - 1) return curve[curve.Length - 1];
            float f = x - i;
            return curve[i] + (curve[i + 1] - curve[i]) * f;
        }

        public int CountKind(NoteKind kind)
        {
            int c = 0;
            if (notes != null) foreach (var n in notes) if (n.kind == (int)kind) c++;
            return c;
        }

        /// <summary>Phase within the current beat in [0,1). 0 is on the beat.</summary>
        public float BeatPhase(float t)
        {
            if (bpm <= 1f) return 0f;
            float period = 60f / bpm;
            float x = (t - beatOffset) / period;
            return x - (float)Math.Floor(x);
        }
    }
}
