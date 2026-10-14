using System.Collections.Generic;
using UnityEngine;
using VMS.Core.Music;
using VMS.Rendering;

namespace VMS.Race
{
    /// <summary>
    /// Streams the road in 2-second chunks around the player: a road surface (opaque, beat lines in the shader),
    /// glowing lane lines and rails (additive), plus a merged scenery mesh per chunk.
    /// </summary>
    public sealed class TrackRenderer : MonoBehaviour
    {
        public const float ChunkSeconds = 2f;
        const float SampleStep = 0.05f;
        const float RailHeight = 0.9f;

        sealed class Chunk
        {
            public int index;
            public GameObject root;
            public Mesh roadMesh;
            public Mesh propMesh;
        }

        struct Line
        {
            public float x, width, pulse;
            public Color32 color;
        }

        readonly Dictionary<int, Chunk> _active = new Dictionary<int, Chunk>();
        readonly Stack<Chunk> _pool = new Stack<Chunk>();
        readonly List<int> _toRemove = new List<int>();
        readonly MeshBuilder _roadBuilder = new MeshBuilder(2);
        readonly MeshBuilder _propBuilder = new MeshBuilder(2);

        TrackPath _path;
        BeatMap _map;
        RaceTheme _theme;
        PropFactory _props;
        Material _roadMaterial;
        Line[] _lines;
        int _lastIndex;

        public void Init(TrackPath path, BeatMap map, RaceTheme theme, PropFactory props)
        {
            _path = path;
            _map = map;
            _theme = theme;
            _props = props;
            _lastIndex = Mathf.CeilToInt((map.duration + 6f) / ChunkSeconds);

            _roadMaterial = new Material(VmsGraphics.Road) { name = "Road (" + theme.id + ")" };
            _roadMaterial.SetColor("_BaseColor", theme.roadBase);
            _roadMaterial.SetColor("_GridColor", theme.roadGrid);
            _roadMaterial.SetColor("_BeatColor", theme.roadBeat);

            float lw = TrackPath.LaneWidth;
            _lines = new[]
            {
                new Line { x = -1.5f * lw, width = 0.36f, pulse = 0.8f, color = theme.edgeLine },
                new Line { x = -0.5f * lw, width = 0.14f, pulse = 0.35f, color = theme.laneLine },
                new Line { x = 0.5f * lw, width = 0.14f, pulse = 0.35f, color = theme.laneLine },
                new Line { x = 1.5f * lw, width = 0.36f, pulse = 0.8f, color = theme.edgeLine },
            };
        }

        /// <summary>Seconds of track kept built ahead of the player (draw distance).</summary>
        public float Ahead = 7f;

        /// <summary>
        /// Keeps chunks around <paramref name="songTime"/> built. At most <paramref name="buildBudget"/> chunks are
        /// built per call (the chunk under the player is always built) so streaming never causes a frame spike;
        /// pass int.MaxValue while loading to prewarm.
        /// </summary>
        public void UpdateVisible(float songTime, int buildBudget = 1, float behind = 1.5f)
        {
            int first = Mathf.Max(-1, Mathf.FloorToInt((songTime - behind) / ChunkSeconds));
            int last = Mathf.Min(_lastIndex, Mathf.FloorToInt((songTime + Ahead) / ChunkSeconds));
            int current = Mathf.FloorToInt(songTime / ChunkSeconds);

            _toRemove.Clear();
            foreach (var kv in _active)
                if (kv.Key < first || kv.Key > last) _toRemove.Add(kv.Key);
            foreach (int idx in _toRemove)
            {
                var c = _active[idx];
                _active.Remove(idx);
                c.root.SetActive(false);
                _pool.Push(c);
            }
            int built = 0;
            for (int i = first; i <= last; i++)
            {
                if (_active.ContainsKey(i)) continue;
                if (built >= buildBudget && i > current + 1) break;
                built++;
                var c = _pool.Count > 0 ? _pool.Pop() : CreateChunk();
                Build(c, i);
                c.root.SetActive(true);
                _active[i] = c;
            }
        }

        Chunk CreateChunk()
        {
            var c = new Chunk { root = new GameObject("Chunk") };
            c.root.transform.SetParent(transform, false);

            var road = new GameObject("Road");
            road.transform.SetParent(c.root.transform, false);
            c.roadMesh = new Mesh { name = "Road" };
            c.roadMesh.MarkDynamic();
            road.AddComponent<MeshFilter>().sharedMesh = c.roadMesh;
            var rr = road.AddComponent<MeshRenderer>();
            rr.sharedMaterials = new[] { _roadMaterial, VmsGraphics.Additive };
            ConfigureRenderer(rr);

            var props = new GameObject("Props");
            props.transform.SetParent(c.root.transform, false);
            c.propMesh = new Mesh { name = "Props" };
            c.propMesh.MarkDynamic();
            props.AddComponent<MeshFilter>().sharedMesh = c.propMesh;
            var pr = props.AddComponent<MeshRenderer>();
            pr.sharedMaterials = new[] { VmsGraphics.Opaque, VmsGraphics.Fx };
            ConfigureRenderer(pr);
            return c;
        }

        static void ConfigureRenderer(MeshRenderer r)
        {
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        }

        void Build(Chunk c, int index)
        {
            c.index = index;
            float t0 = index * ChunkSeconds, t1 = t0 + ChunkSeconds;
            BuildRoad(t0, t1);
            _roadBuilder.ApplyTo(c.roadMesh);

            _propBuilder.Clear();
            _props.Populate(_propBuilder, index, t0, t1);
            _propBuilder.ApplyTo(c.propMesh, null, false);
        }

        void BuildRoad(float t0, float t1)
        {
            var mb = _roadBuilder;
            mb.Clear();
            int rows = Mathf.RoundToInt((t1 - t0) / SampleStep) + 1;
            float beatsPerSecond = _map.bpm > 30f ? _map.bpm / 60f : 2f;
            int stripVerts = 2 + _lines.Length * 2 + 4;
            Color32 rail = _theme.rail;
            rail.a = 255;

            for (int r = 0; r < rows; r++)
            {
                float t = t0 + r * SampleStep;
                Vector3 p;
                Quaternion q;
                _path.Sample(t, out p, out q);
                Vector3 right = q * Vector3.right, up = q * Vector3.up;
                float dist = _path.Distance(t);
                float intensity = _map.Sample(_map.intensity, Mathf.Max(0f, t));
                var beat = new Vector2((t - _map.beatOffset) * beatsPerSecond, intensity);

                mb.AddVertex(p - right * TrackPath.RoadHalfWidth, Color.white, new Vector2(0f, dist), beat);
                mb.AddVertex(p + right * TrackPath.RoadHalfWidth, Color.white, new Vector2(1f, dist), beat);

                Vector3 lift = up * 0.03f;
                foreach (var line in _lines)
                {
                    var c = line.color;
                    c.a = (byte)(255 * line.pulse);
                    mb.AddVertex(p + right * (line.x - line.width * 0.5f) + lift, c, new Vector2(0f, 0f));
                    mb.AddVertex(p + right * (line.x + line.width * 0.5f) + lift, c, new Vector2(1f, 0f));
                }
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector3 basePos = p + right * side * (TrackPath.RoadHalfWidth + 0.25f);
                    mb.AddVertex(basePos + up * 0.1f, rail, new Vector2(0f, 0f));
                    mb.AddVertex(basePos + up * RailHeight, rail, new Vector2(1f, 0f));
                }

                if (r == 0) continue;
                int cur = r * stripVerts, prev = cur - stripVerts;
                for (int s = 0; s < stripVerts; s += 2)
                {
                    int submesh = s == 0 ? 0 : 1;
                    mb.AddTriangle(prev + s, cur + s, cur + s + 1, submesh);
                    mb.AddTriangle(prev + s, cur + s + 1, prev + s + 1, submesh);
                }
            }
        }

        static void DestroyChunk(Chunk c)
        {
            Destroy(c.roadMesh);
            Destroy(c.propMesh);
            Destroy(c.root);
        }

        void OnDestroy()
        {
            foreach (var c in _active.Values) DestroyChunk(c);
            _active.Clear();
            while (_pool.Count > 0) DestroyChunk(_pool.Pop());
            if (_roadMaterial != null) Destroy(_roadMaterial);
        }
    }
}
