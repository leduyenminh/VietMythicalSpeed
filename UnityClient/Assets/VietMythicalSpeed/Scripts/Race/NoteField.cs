using UnityEngine;
using VMS.Core.Music;
using VMS.Rendering;

namespace VMS.Race
{
    /// <summary>Draws pearls (Linh Châu) and ghost fires (ma trơi) and remembers how each was judged.</summary>
    public sealed class NoteField
    {
        public enum State : byte
        {
            Pending,
            Collected,
            Missed,
            Hit,
            Dodged,
        }

        const float Hover = 1.3f;

        readonly NoteData[] _notes;
        readonly TrackPath _path;
        readonly RaceTheme _theme;
        readonly State[] _state;
        int _drawStart;

        public NoteField(NoteData[] notes, TrackPath path, RaceTheme theme)
        {
            _notes = notes;
            _path = path;
            _theme = theme;
            _state = new State[notes.Length];
        }

        public NoteData[] Notes => _notes;

        public void Mark(int index, State state) => _state[index] = state;

        public Vector3 WorldPosition(int index)
        {
            var n = _notes[index];
            return _path.PointOnRoad(n.t, TrackPath.LaneX(n.lane), Hover);
        }

        public void Draw(FxBatch fx, float songTime, float horizon, bool transmute, float clock)
        {
            int count = _notes.Length;
            while (_drawStart < count && _notes[_drawStart].t < songTime - 0.6f) _drawStart++;

            for (int i = _drawStart; i < count; i++)
            {
                var note = _notes[i];
                float ahead = note.t - songTime;
                if (ahead > horizon) break;
                var state = _state[i];
                if (state == State.Collected) continue;

                Vector3 p = _path.PointOnRoad(note.t, TrackPath.LaneX(note.lane), Hover);
                float fade = Mathf.Clamp01((horizon - ahead) / 1.2f);
                if (ahead < 0f) fade *= Mathf.Clamp01(1f + ahead / 0.6f);
                if (fade <= 0.01f) continue;

                bool hazard = note.kind == (int)NoteKind.Hazard && !(transmute && state == State.Pending);
                bool big = note.kind == (int)NoteKind.BigPearl || (transmute && note.kind != (int)NoteKind.Hazard);
                float wobble = Mathf.Sin(clock * 6f + i * 1.7f);

                if (hazard)
                {
                    if (state == State.Dodged || state == State.Hit) fade *= 0.5f;
                    float flicker = 1f + 0.12f * Mathf.Sin(clock * 23f + i * 3.1f);
                    fx.Add(p + Vector3.up * 0.3f, 3.1f * flicker, _theme.hazard * (0.75f * fade), ProceduralTextures.FxCell.Flame, 0.08f * wobble, 0.4f);
                    fx.Add(p, 4.2f, _theme.hazard * (0.35f * fade), ProceduralTextures.FxCell.Dot, 0f, 0.6f);
                    fx.Add(p + Vector3.up * 0.1f, 0.8f, Color.white * (0.5f * fade), ProceduralTextures.FxCell.Dot, 0f, 0f);
                    continue;
                }

                if (state == State.Missed)
                {
                    fx.Add(p, 1.1f, new Color(0.35f, 0.35f, 0.4f) * fade, ProceduralTextures.FxCell.Dot, 0f, 0f);
                    continue;
                }

                Color core = big ? _theme.bigPearl : _theme.pearl;
                // Large and bright enough to read 150+ m ahead at speed, like Music Racer's notes.
                float size = big ? 2.9f : 2.1f;
                float bob = 0.12f * wobble;
                Vector3 c = p + Vector3.up * bob;
                fx.Add(c, size * 2.3f, core * (0.4f * fade), ProceduralTextures.FxCell.Dot, 0f, 0.8f);
                fx.Add(c, size, core * fade, ProceduralTextures.FxCell.Dot, 0f, 0.4f);
                fx.Add(c, size * 0.8f, Color.white * (0.85f * fade), ProceduralTextures.FxCell.Sparkle, clock * 1.5f + i, 0.2f);
                if (big) fx.Add(c, size * 1.4f, core * (0.5f * fade), ProceduralTextures.FxCell.Diamond, -clock * 1.2f, 0.6f);
            }
        }
    }
}
