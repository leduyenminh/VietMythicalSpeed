using UnityEngine;
using VMS.Rendering;

namespace VMS.Race
{
    /// <summary>
    /// One dynamic mesh of camera-facing additive billboards, rebuilt every frame: pearls, ghost fires,
    /// sparks, engine glow, the spirit dragon's scales. A single draw call for all of it.
    /// </summary>
    public sealed class FxBatch : MonoBehaviour
    {
        readonly MeshBuilder _builder = new MeshBuilder(1);
        Mesh _mesh;
        Camera _camera;
        Vector3 _right, _up;

        public void Init(Camera camera)
        {
            _camera = camera;
            _mesh = new Mesh { name = "FxBatch" };
            _mesh.MarkDynamic();
            gameObject.AddComponent<MeshFilter>().sharedMesh = _mesh;
            var r = gameObject.AddComponent<MeshRenderer>();
            r.sharedMaterial = VmsGraphics.Fx;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        public Vector3 CameraRight => _right;
        public Vector3 CameraUp => _up;

        public void Begin()
        {
            _builder.Clear();
            _right = _camera.transform.right;
            _up = _camera.transform.up;
        }

        public void Add(Vector3 position, float size, Color color, ProceduralTextures.FxCell cell, float rotation = 0f, float pulse = 0f)
        {
            var c = (Color32)color;
            c.a = (byte)(Mathf.Clamp01(pulse) * 255);
            _builder.AddBillboard(position, _right, _up, size, c, ProceduralTextures.AtlasRect(cell), rotation);
        }

        /// <summary>Ribbon segment between two points, widened perpendicular to the view direction.</summary>
        public void AddRibbon(Vector3 a, Vector3 b, float widthA, float widthB, Color colorA, Color colorB)
        {
            Vector3 toCam = _camera.transform.position - (a + b) * 0.5f;
            Vector3 side = Vector3.Cross(b - a, toCam).normalized;
            var r = ProceduralTextures.AtlasRect(ProceduralTextures.FxCell.Square);
            Color32 ca = colorA, cb = colorB;
            ca.a = 0;
            cb.a = 0;
            int i = _builder.AddVertex(a - side * widthA * 0.5f, ca, new Vector2(r.xMin, r.yMin));
            _builder.AddVertex(b - side * widthB * 0.5f, cb, new Vector2(r.xMin, r.yMax));
            _builder.AddVertex(b + side * widthB * 0.5f, cb, new Vector2(r.xMax, r.yMax));
            _builder.AddVertex(a + side * widthA * 0.5f, ca, new Vector2(r.xMax, r.yMin));
            _builder.AddTriangle(i, i + 1, i + 2);
            _builder.AddTriangle(i, i + 2, i + 3);
        }

        static readonly Bounds Everywhere = new Bounds(Vector3.zero, Vector3.one * 1e7f);

        public void End()
        {
            // Rebuilt every frame around the camera: skip RecalculateBounds and the unused second UV channel.
            _builder.ApplyTo(_mesh, Everywhere, false);
        }

        void OnDestroy()
        {
            if (_mesh != null) Destroy(_mesh);
        }
    }

    /// <summary>Tiny CPU particle system for collect bursts, drawn through <see cref="FxBatch"/>.</summary>
    public sealed class Sparks
    {
        struct Particle
        {
            public Vector3 position, velocity;
            public float life, maxLife, size, spin;
            public Color color;
            public ProceduralTextures.FxCell cell;
            public bool grow;
        }

        readonly Particle[] _particles;
        int _count;

        public Sparks(int capacity = 512)
        {
            _particles = new Particle[capacity];
        }

        public void Burst(Vector3 at, Vector3 forwardVelocity, Color color, int count, float speed, float size)
        {
            for (int i = 0; i < count; i++)
            {
                var dir = Random.onUnitSphere;
                Spawn(new Particle
                {
                    position = at,
                    velocity = forwardVelocity + dir * speed * Random.Range(0.4f, 1f),
                    life = 0f,
                    maxLife = Random.Range(0.25f, 0.55f),
                    size = size * Random.Range(0.6f, 1.2f),
                    spin = Random.Range(-6f, 6f),
                    color = color,
                    cell = i % 3 == 0 ? ProceduralTextures.FxCell.Sparkle : ProceduralTextures.FxCell.Dot,
                });
            }
        }

        public void Ring(Vector3 at, Vector3 velocity, Color color, float size)
        {
            Spawn(new Particle
            {
                position = at,
                velocity = velocity,
                maxLife = 0.35f,
                size = size,
                color = color,
                cell = ProceduralTextures.FxCell.Ring,
                grow = true,
            });
        }

        /// <summary>One slow, drifting particle (exhaust embers).</summary>
        public void Ember(Vector3 at, Vector3 velocity, Color color, ProceduralTextures.FxCell cell, float size)
        {
            Spawn(new Particle
            {
                position = at,
                velocity = velocity,
                maxLife = Random.Range(0.45f, 0.8f),
                size = size,
                spin = Random.Range(-3f, 3f),
                color = color,
                cell = cell,
            });
        }

        void Spawn(Particle p)
        {
            if (_count >= _particles.Length) return;
            _particles[_count++] = p;
        }

        public void Update(float dt, FxBatch batch)
        {
            for (int i = 0; i < _count; i++)
            {
                var p = _particles[i];
                p.life += dt;
                if (p.life >= p.maxLife)
                {
                    _particles[i] = _particles[--_count];
                    i--;
                    continue;
                }
                p.position += p.velocity * dt;
                p.velocity *= 1f - Mathf.Min(1f, dt * 3f);
                _particles[i] = p;
                float k = p.life / p.maxLife;
                float size = p.grow ? p.size * (0.4f + 1.6f * k) : p.size * (1f - k * 0.6f);
                batch.Add(p.position, size, p.color * (1f - k), p.cell, p.spin * p.life, 0f);
            }
        }

        public void Clear() => _count = 0;
    }
}
