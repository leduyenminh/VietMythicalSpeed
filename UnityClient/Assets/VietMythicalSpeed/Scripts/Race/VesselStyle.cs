using UnityEngine;
using VMS.Core;
using VMS.Rendering;

namespace VMS.Race
{
    /// <summary>How the engine trail looks: colours, size and the embers it sheds (from an "exhaust" shop item).</summary>
    public sealed class ExhaustStyle
    {
        public Color head;
        public Color tail;
        public float width = 0.45f;
        public float length = 0.28f;
        /// <summary>Embers per second per trail point (0 = none).</summary>
        public float embers;
        public ProceduralTextures.FxCell cell = ProceduralTextures.FxCell.Dot;

        /// <summary>The look of <paramref name="item"/>; empty colours fall back to the vessel's glow.</summary>
        public static ExhaustStyle From(ShopItem item, VesselDef vessel)
        {
            var style = new ExhaustStyle { head = vessel.glow, tail = vessel.glow * 0.5f };
            var s = item != null ? item.style : null;
            if (s == null) return style;
            style.head = VesselStyles.Hex(s.colorA, vessel.glow);
            style.tail = VesselStyles.Hex(s.colorB, style.head * 0.5f);
            if (s.width > 0f) style.width = s.width;
            if (s.length > 0f) style.length = s.length;
            style.embers = Mathf.Max(0f, s.embers);
            if (!string.IsNullOrEmpty(s.cell) && System.Enum.TryParse(s.cell, out ProceduralTextures.FxCell cell)) style.cell = cell;
            return style;
        }
    }

    public static class VesselStyles
    {
        public static Color Hex(string hex, Color fallback) =>
            !string.IsNullOrEmpty(hex) && ColorUtility.TryParseHtmlString(hex, out var c) ? c : fallback;

        /// <summary>The vessel repainted with a "vesselKit" shop item (null = stock look).</summary>
        public static VesselDef WithKit(VesselDef vessel, ShopItem kit)
        {
            if (kit == null || kit.style == null || kit.target != vessel.id) return vessel;
            return new VesselDef
            {
                id = vessel.id,
                nameKey = vessel.nameKey,
                body = Hex(kit.style.colorA, vessel.body),
                trim = Hex(kit.style.colorB, vessel.trim),
                glow = Hex(kit.style.colorC, vessel.glow),
                ornament = kit.style.ornament,
            };
        }
    }
}
