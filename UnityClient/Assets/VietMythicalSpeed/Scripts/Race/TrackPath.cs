using UnityEngine;
using VMS.Core.Music;

namespace VMS.Race
{
    /// <summary>
    /// The 3D centre line of the road, integrated from the beatmap's speed / yaw-rate / pitch curves and
    /// parameterised by song time. Before t = 0 and after the end it continues in a straight line.
    /// </summary>
    public sealed class TrackPath
    {
        public const float Step = 0.05f;
        public const float LaneWidth = 3.2f;
        public const float RoadHalfWidth = 1.5f * LaneWidth + 0.6f;
        const float TailSeconds = 8f;

        readonly Vector3[] _pos;
        readonly Quaternion[] _rot;
        readonly float[] _dist;
        readonly float[] _speed;

        public readonly float Duration;

        public TrackPath(BeatMap map)
        {
            Duration = map.duration;
            int n = Mathf.CeilToInt((map.duration + TailSeconds) / Step) + 2;
            _pos = new Vector3[n];
            _rot = new Quaternion[n];
            _dist = new float[n];
            _speed = new float[n];

            float yaw = 0f;
            Vector3 p = Vector3.zero;
            float d = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = Mathf.Min(i * Step, map.duration);
                float yawRate = i * Step > map.duration ? 0f : map.Sample(map.yawRate, t);
                float pitch = i * Step > map.duration ? 0f : map.Sample(map.pitch, t);
                float speed = map.Sample(map.speed, t);
                yaw += yawRate * Step;
                var dir = Quaternion.Euler(-pitch, yaw, 0f) * Vector3.forward;
                float bank = Mathf.Clamp(-yawRate * 0.9f, -22f, 22f);
                _pos[i] = p;
                _rot[i] = Quaternion.LookRotation(dir, Vector3.up) * Quaternion.Euler(0f, 0f, bank);
                _dist[i] = d;
                _speed[i] = speed;
                p += dir * speed * Step;
                d += speed * Step;
            }
        }

        public float EndTime => (_pos.Length - 2) * Step;

        public void Sample(float t, out Vector3 position, out Quaternion rotation)
        {
            if (t <= 0f)
            {
                rotation = _rot[0];
                position = _pos[0] + (_rot[0] * Vector3.forward) * (_speed[0] * t);
                return;
            }
            float x = t / Step;
            int i = (int)x;
            if (i >= _pos.Length - 2)
            {
                int last = _pos.Length - 1;
                rotation = _rot[last];
                position = _pos[last] + (_rot[last] * Vector3.forward) * (_speed[last] * (t - last * Step));
                return;
            }
            float f = x - i;
            Vector3 p0 = _pos[Mathf.Max(0, i - 1)], p1 = _pos[i], p2 = _pos[i + 1], p3 = _pos[Mathf.Min(_pos.Length - 1, i + 2)];
            position = CatmullRom(p0, p1, p2, p3, f);
            rotation = Quaternion.Slerp(_rot[i], _rot[i + 1], f);
        }

        public Vector3 Position(float t)
        {
            Vector3 p;
            Quaternion q;
            Sample(t, out p, out q);
            return p;
        }

        /// <summary>World position of a point on the road surface: lateral metres to the right, height above it.</summary>
        public Vector3 PointOnRoad(float t, float lateral, float height)
        {
            Vector3 p;
            Quaternion q;
            Sample(t, out p, out q);
            return p + q * new Vector3(lateral, height, 0f);
        }

        public float Distance(float t)
        {
            if (t <= 0f) return _speed[0] * t;
            float x = t / Step;
            int i = Mathf.Min((int)x, _dist.Length - 2);
            return Mathf.Lerp(_dist[i], _dist[i + 1], x - i);
        }

        public float Speed(float t)
        {
            int i = Mathf.Clamp((int)(t / Step), 0, _speed.Length - 1);
            return _speed[i];
        }

        public static float LaneX(int lane) => (lane - 1) * LaneWidth;

        static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return 0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }
    }

    /// <summary>
    /// Song position for gameplay. Audio is scheduled on the DSP clock; the game clock advances with frame time
    /// and is nudged toward the DSP clock so it stays smooth but never drifts.
    /// </summary>
    public sealed class SongClock
    {
        readonly AudioSource _source;
        double _dspStart;
        float _time;
        bool _running;
        bool _scheduled;

        public SongClock(AudioSource source)
        {
            _source = source;
        }

        /// <summary>Raw song time (audio position), negative during the lead-in.</summary>
        public float Time => _time;
        public bool Running => _running;

        public void Start(float leadIn)
        {
            _dspStart = AudioSettings.dspTime + leadIn;
            _source.PlayScheduled(_dspStart);
            _scheduled = true;
            _time = -leadIn;
            _running = true;
        }

        public float Tick(float deltaTime)
        {
            if (!_running) return _time;
            float dsp = (float)(AudioSettings.dspTime - _dspStart);
            _time += deltaTime;
            float error = dsp - _time;
            if (Mathf.Abs(error) > 0.08f) _time = dsp;
            else _time += error * 0.08f;
            return _time;
        }

        public void Pause()
        {
            if (!_running) return;
            _running = false;
            if (_time < 0f)
            {
                _source.Stop();
                _scheduled = false;
            }
            else
            {
                _source.Pause();
                if (_source.clip != null) _time = _source.timeSamples / (float)_source.clip.frequency;
            }
        }

        public void Resume()
        {
            if (_running) return;
            if (_time < 0f || !_scheduled)
            {
                Start(Mathf.Max(0.05f, -_time));
                return;
            }
            _dspStart = AudioSettings.dspTime - _time;
            _source.UnPause();
            _running = true;
        }

        public void Stop()
        {
            _running = false;
            _source.Stop();
        }
    }
}
