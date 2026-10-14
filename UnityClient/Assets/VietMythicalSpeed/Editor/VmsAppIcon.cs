using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace VMS.EditorTools
{
    /// <summary>
    /// Generates a placeholder launcher icon (neon lanes converging on a golden spirit pearl under a halo) and
    /// assigns it as the default icon. Replace Assets/VietMythicalSpeed/Art/AppIcon.png with real art at any time;
    /// an existing file is never overwritten.
    /// </summary>
    public static class VmsAppIcon
    {
        public const string IconPath = "Assets/VietMythicalSpeed/Art/AppIcon.png";
        const int Size = 1024;

        [MenuItem("VietMythicalSpeed/Setup/Generate Placeholder App Icon", priority = 5)]
        public static void EnsureAssigned()
        {
            if (!File.Exists(IconPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(IconPath));
                File.WriteAllBytes(IconPath, Render().EncodeToPNG());
                AssetDatabase.ImportAsset(IconPath);
            }
            var importer = (TextureImporter)AssetImporter.GetAtPath(IconPath);
            if (importer != null && (importer.mipmapEnabled || importer.textureCompression != TextureImporterCompression.Uncompressed))
            {
                importer.textureType = TextureImporterType.Default;
                importer.mipmapEnabled = false;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
            if (icon != null) PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { icon }, IconKind.Any);
        }

        static Texture2D Render()
        {
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            var px = new Color[Size * Size];
            Color top = new Color(0.04f, 0.07f, 0.22f), bottom = new Color(0.17f, 0.06f, 0.28f);
            Color cyan = new Color(0.35f, 0.95f, 1f), gold = new Color(1f, 0.82f, 0.3f), rose = new Color(1f, 0.35f, 0.55f);
            var pearl = new Vector2(0.5f, 0.56f);
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float u = (x + 0.5f) / Size, v = (y + 0.5f) / Size;
                    Color c = Color.Lerp(bottom, top, v);
                    // Horizon glow.
                    c += rose * Mathf.Exp(-Mathf.Abs(v - 0.46f) * 18f) * 0.35f;
                    // Road: three lanes converging toward the pearl from the bottom edge.
                    if (v < 0.5f)
                    {
                        float depth = (0.5f - v) / 0.5f; // 0 at horizon, 1 at bottom
                        for (int line = -2; line <= 2; line++)
                        {
                            float lx = 0.5f + line * 0.11f * depth * 2.2f;
                            float width = 0.004f + 0.012f * depth;
                            float d = Mathf.Abs(u - lx);
                            float glow = Mathf.Clamp01(1f - d / width) + 0.35f * Mathf.Exp(-d / (width * 3f));
                            c += (Mathf.Abs(line) == 2 ? cyan : cyan * 0.7f) * glow * (0.3f + 0.7f * depth);
                        }
                        // Beat lines across the road.
                        float beat = Mathf.Repeat(Mathf.Log(depth + 0.05f) * 6f, 1f);
                        if (Mathf.Abs(u - 0.5f) < 0.24f * depth * 2.2f) c += rose * Mathf.Clamp01(1f - beat * 12f) * 0.25f * depth;
                    }
                    // Golden spirit pearl with halo.
                    float r = Vector2.Distance(new Vector2(u, v), pearl);
                    c += gold * (Mathf.Clamp01((0.085f - r) * 60f) + Mathf.Exp(-r * 14f) * 0.6f);
                    float halo = Mathf.Abs(Vector2.Distance(new Vector2(u, (v - 0.78f) * 3.2f + 0.78f), new Vector2(0.5f, 0.78f)) - 0.14f);
                    c += gold * Mathf.Clamp01(1f - halo * 90f) * 0.9f;
                    // Rounded-square mask (Android adaptive icons crop further; keep content centred).
                    float ax = Mathf.Abs(u - 0.5f) * 2f, ay = Mathf.Abs(v - 0.5f) * 2f;
                    float corner = Mathf.Pow(Mathf.Pow(ax, 8f) + Mathf.Pow(ay, 8f), 1f / 8f);
                    c.a = Mathf.Clamp01((1f - corner) * 120f);
                    px[y * Size + x] = c;
                }
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }
    }
}
