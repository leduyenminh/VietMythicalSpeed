using System.Collections.Generic;
using UnityEngine;
using VMS.Rendering;

namespace VMS.Race
{
    /// <summary>The mounts players ride. Purely cosmetic; meshes are procedural placeholders until art arrives.</summary>
    public sealed class VesselDef
    {
        public string id;
        public string nameKey;
        public Color body, trim, glow;
        /// <summary>Kit add-on: lanterns | banners | horns (empty = none).</summary>
        public string ornament;

        public static readonly VesselDef[] All =
        {
            new VesselDef { id = "thuyen_rong", nameKey = "vessel.thuyen_rong", body = RaceTheme.Hex("#B3261E"), trim = RaceTheme.Hex("#F2C94C"), glow = RaceTheme.Hex("#7CF3FF") },
            new VesselDef { id = "dieu_sao", nameKey = "vessel.dieu_sao", body = RaceTheme.Hex("#F4F1E8"), trim = RaceTheme.Hex("#2E9BFF"), glow = RaceTheme.Hex("#9FD3FF") },
            new VesselDef { id = "xe_lan", nameKey = "vessel.xe_lan", body = RaceTheme.Hex("#E2463C"), trim = RaceTheme.Hex("#FFD24A"), glow = RaceTheme.Hex("#FFB347") },
            new VesselDef { id = "be_quy", nameKey = "vessel.be_quy", body = RaceTheme.Hex("#2F7D4F"), trim = RaceTheme.Hex("#F2C94C"), glow = RaceTheme.Hex("#B6FF7A") },
        };

        public static VesselDef Find(string id)
        {
            foreach (var v in All) if (v.id == id) return v;
            return All[0];
        }
    }

    /// <summary>Builds low-poly vessel meshes. Local space: +z forward, +y up, about 3.4 m long.</summary>
    public static class VesselFactory
    {
        public sealed class Result
        {
            public Mesh body;
            public Mesh glow;
            /// <summary>Local points where engine trails start.</summary>
            public Vector3[] trailPoints;
        }

        public static Result Build(VesselDef def)
        {
            var body = new MeshBuilder(1);
            var glow = new MeshBuilder(1);
            var trails = new List<Vector3>();
            Color32 b = def.body, t = def.trim, g = def.glow;
            switch (def.id)
            {
                case "dieu_sao": Kite(body, glow, b, t, g, trails); break;
                case "xe_lan": QilinCart(body, glow, b, t, g, trails); break;
                case "be_quy": TurtleRaft(body, glow, b, t, g, trails); break;
                default: DragonBoat(body, glow, b, t, g, trails); break;
            }
            Ornament(def.ornament, body, glow, b, t, g);
            return new Result { body = body.ToMesh(def.id), glow = glow.ToMesh(def.id + "_glow"), trailPoints = trails.ToArray() };
        }

        static Matrix4x4 M(Vector3 pos, Vector3 euler, Vector3 scale) => Matrix4x4.TRS(pos, Quaternion.Euler(euler), scale);
        static Matrix4x4 M(Vector3 pos, Vector3 euler) => M(pos, euler, Vector3.one);

        static void GlowStrip(MeshBuilder glow, Vector3 a, Vector3 b, float width, Color32 color)
        {
            Vector3 dir = (b - a).normalized;
            Vector3 side = Vector3.Cross(dir, Vector3.up);
            if (side.sqrMagnitude < 0.01f) side = Vector3.right;
            side = side.normalized * width * 0.5f;
            var r = ProceduralTextures.AtlasRect(ProceduralTextures.FxCell.Square);
            int i = glow.AddVertex(a - side, color, new Vector2(r.xMin, r.yMin));
            glow.AddVertex(b - side, color, new Vector2(r.xMin, r.yMax));
            glow.AddVertex(b + side, color, new Vector2(r.xMax, r.yMax));
            glow.AddVertex(a + side, color, new Vector2(r.xMax, r.yMin));
            glow.AddTriangle(i, i + 1, i + 2);
            glow.AddTriangle(i, i + 2, i + 3);
        }

        /// <summary>Vessel-kit decorations, placed around the deck (rear corners, front) so they suit every hull.</summary>
        static void Ornament(string kind, MeshBuilder m, MeshBuilder glow, Color32 body, Color32 trim, Color32 g)
        {
            switch (kind)
            {
                case "lanterns":
                    for (int side = -1; side <= 1; side += 2)
                    {
                        m.AddBox(M(new Vector3(side * 0.55f, 1.05f, -1.15f), Vector3.zero), new Vector3(0.05f, 0.6f, 0.05f), trim);
                        m.AddBox(M(new Vector3(side * 0.55f, 1.32f, -1.0f), Vector3.zero), new Vector3(0.05f, 0.05f, 0.35f), trim);
                        m.AddCylinder(M(new Vector3(side * 0.55f, 1.1f, -0.85f), Vector3.zero), 0.14f, 0.1f, 0.3f, 8, new Color32(226, 70, 60, 255), new Color32(255, 210, 74, 255));
                        GlowStrip(glow, new Vector3(side * 0.55f, 0.98f, -0.85f), new Vector3(side * 0.55f, 1.22f, -0.85f), 0.3f, new Color32(255, 190, 90, 255));
                    }
                    break;
                case "banners":
                    for (int side = -1; side <= 1; side += 2)
                    {
                        m.AddBox(M(new Vector3(side * 0.5f, 1.25f, -1.45f), Vector3.zero), new Vector3(0.06f, 1.4f, 0.06f), trim);
                        // Slanted festival flag streaming backwards.
                        var top = new Vector3(side * 0.5f, 1.9f, -1.45f);
                        var bottom = new Vector3(side * 0.5f, 1.35f, -1.45f);
                        var tip = new Vector3(side * 0.5f, 1.55f, -2.25f);
                        m.AddQuad(bottom, top, tip, tip, body);
                        m.AddQuad(tip, top, bottom, bottom, MeshBuilder.Shade(body, 0.8f));
                        GlowStrip(glow, top, tip, 0.06f, g);
                    }
                    break;
                case "horns":
                    for (int side = -1; side <= 1; side += 2)
                    {
                        m.AddTaper(M(new Vector3(side * 0.28f, 1.35f, 1.25f), new Vector3(-50f, side * 12f, 0f)), new Vector2(0.16f, 0.16f), new Vector2(0.02f, 0.02f), 0.7f, trim);
                        GlowStrip(glow, new Vector3(side * 0.28f, 1.4f, 1.3f), new Vector3(side * 0.34f, 1.85f, 1.6f), 0.07f, g);
                    }
                    break;
            }
        }

        static void DragonBoat(MeshBuilder m, MeshBuilder glow, Color32 body, Color32 trim, Color32 g, List<Vector3> trails)
        {
            m.AddTaper(M(new Vector3(0, 0.35f, -0.2f), Vector3.zero), new Vector2(1.3f, 0.5f), new Vector2(0.5f, 0.45f), 3.0f, body);
            m.AddBox(M(new Vector3(0, 0.62f, -0.3f), Vector3.zero), new Vector3(1.1f, 0.1f, 2.2f), trim);
            // Raised dragon neck and head at the prow.
            m.AddTaper(M(new Vector3(0, 0.95f, 1.45f), new Vector3(-55f, 0, 0)), new Vector2(0.4f, 0.35f), new Vector2(0.28f, 0.25f), 1.0f, body);
            m.AddBox(M(new Vector3(0, 1.38f, 1.85f), new Vector3(10f, 0, 0)), new Vector3(0.42f, 0.32f, 0.6f), trim);
            m.AddBox(M(new Vector3(0, 1.62f, 1.72f), new Vector3(-30f, 0, 0)), new Vector3(0.08f, 0.35f, 0.08f), trim);
            // Curled tail.
            m.AddTaper(M(new Vector3(0, 0.85f, -1.9f), new Vector3(50f, 0, 0)), new Vector2(0.35f, 0.3f), new Vector2(0.15f, 0.12f), 0.9f, body);
            GlowStrip(glow, new Vector3(-0.66f, 0.62f, -1.6f), new Vector3(-0.3f, 0.62f, 1.2f), 0.18f, g);
            GlowStrip(glow, new Vector3(0.66f, 0.62f, -1.6f), new Vector3(0.3f, 0.62f, 1.2f), 0.18f, g);
            trails.Add(new Vector3(-0.55f, 0.3f, -1.7f));
            trails.Add(new Vector3(0.55f, 0.3f, -1.7f));
        }

        static void Kite(MeshBuilder m, MeshBuilder glow, Color32 body, Color32 trim, Color32 g, List<Vector3> trails)
        {
            // Diamond kite wing with a small bamboo spine; the "flute" sits under the nose.
            var top = new Vector3(0, 0.75f, 1.6f);
            var left = new Vector3(-1.6f, 0.6f, -0.2f);
            var right = new Vector3(1.6f, 0.6f, -0.2f);
            var tail = new Vector3(0, 0.65f, -1.5f);
            var hub = new Vector3(0, 0.95f, 0.1f);
            m.AddQuad(left, top, hub, hub, body);
            m.AddQuad(hub, top, right, right, body);
            m.AddQuad(left, hub, tail, tail, MeshBuilder.Shade(body, 0.85f));
            m.AddQuad(tail, hub, right, right, MeshBuilder.Shade(body, 0.85f));
            m.AddBox(M(new Vector3(0, 0.82f, 0f), Vector3.zero), new Vector3(0.1f, 0.1f, 3.1f), trim);
            m.AddBox(M(new Vector3(0, 0.78f, -0.15f), Vector3.zero), new Vector3(3.2f, 0.08f, 0.08f), trim);
            m.AddTaper(M(new Vector3(0, 0.45f, 0.6f), Vector3.zero), new Vector2(0.25f, 0.25f), new Vector2(0.18f, 0.18f), 1.4f, trim);
            GlowStrip(glow, left, top, 0.12f, g);
            GlowStrip(glow, top, right, 0.12f, g);
            GlowStrip(glow, right, tail, 0.12f, g);
            GlowStrip(glow, tail, left, 0.12f, g);
            trails.Add(new Vector3(-1.5f, 0.6f, -0.3f));
            trails.Add(new Vector3(1.5f, 0.6f, -0.3f));
            trails.Add(new Vector3(0f, 0.65f, -1.5f));
        }

        static void QilinCart(MeshBuilder m, MeshBuilder glow, Color32 body, Color32 trim, Color32 g, List<Vector3> trails)
        {
            m.AddBox(M(new Vector3(0, 0.6f, -0.3f), Vector3.zero), new Vector3(1.5f, 0.7f, 2.2f), body);
            m.AddTaper(M(new Vector3(0, 1.15f, -0.5f), Vector3.zero), new Vector2(1.7f, 0.25f), new Vector2(1.3f, 0.25f), 1.8f, trim);
            // Lion-dance head at the front: big box face with a horn.
            m.AddBox(M(new Vector3(0, 0.95f, 1.15f), new Vector3(-8f, 0, 0)), new Vector3(1.2f, 0.95f, 0.8f), body);
            m.AddBox(M(new Vector3(0, 1.55f, 1.15f), new Vector3(-25f, 0, 0)), new Vector3(0.14f, 0.45f, 0.14f), trim);
            m.AddBox(M(new Vector3(-0.35f, 1.1f, 1.57f), Vector3.zero), new Vector3(0.25f, 0.2f, 0.06f), trim);
            m.AddBox(M(new Vector3(0.35f, 1.1f, 1.57f), Vector3.zero), new Vector3(0.25f, 0.2f, 0.06f), trim);
            for (int side = -1; side <= 1; side += 2)
                for (int axle = 0; axle < 2; axle++)
                    m.AddCylinder(M(new Vector3(side * 0.85f, 0.32f, axle == 0 ? 0.5f : -1.0f), new Vector3(0, 0, 90f)), 0.32f, 0.32f, 0.18f, 10, trim, trim);
            GlowStrip(glow, new Vector3(-0.78f, 0.28f, -1.4f), new Vector3(-0.78f, 0.28f, 0.9f), 0.15f, g);
            GlowStrip(glow, new Vector3(0.78f, 0.28f, -1.4f), new Vector3(0.78f, 0.28f, 0.9f), 0.15f, g);
            trails.Add(new Vector3(-0.85f, 0.3f, -1.4f));
            trails.Add(new Vector3(0.85f, 0.3f, -1.4f));
        }

        static void TurtleRaft(MeshBuilder m, MeshBuilder glow, Color32 body, Color32 trim, Color32 g, List<Vector3> trails)
        {
            m.AddBox(M(new Vector3(0, 0.25f, 0f), Vector3.zero), new Vector3(1.9f, 0.2f, 2.8f), MeshBuilder.Shade(trim, 0.8f));
            m.AddDome(M(new Vector3(0, 0.35f, -0.1f), Vector3.zero, new Vector3(1f, 1f, 1.35f)), 0.95f, 0.75f, 12, body);
            m.AddTaper(M(new Vector3(0, 0.5f, 1.45f), new Vector3(-12f, 0, 0)), new Vector2(0.45f, 0.4f), new Vector2(0.35f, 0.32f), 0.6f, trim);
            m.AddBox(M(new Vector3(0, 1.15f, -0.1f), Vector3.zero), new Vector3(0.12f, 0.6f, 0.12f), trim);
            m.AddBox(M(new Vector3(0, 1.45f, -0.1f), Vector3.zero), new Vector3(0.5f, 0.06f, 0.5f), trim);
            GlowStrip(glow, new Vector3(-0.95f, 0.37f, -1.3f), new Vector3(-0.95f, 0.37f, 1.3f), 0.14f, g);
            GlowStrip(glow, new Vector3(0.95f, 0.37f, -1.3f), new Vector3(0.95f, 0.37f, 1.3f), 0.14f, g);
            trails.Add(new Vector3(-0.7f, 0.25f, -1.4f));
            trails.Add(new Vector3(0.7f, 0.25f, -1.4f));
        }
    }
}
