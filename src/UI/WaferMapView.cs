using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SiliconeTycoon.Sim;

namespace SiliconeTycoon.UI
{
    /// <summary>Draws a simulated wafer: every die coloured by its bin, with its defects. Hover a die for details.</summary>
    public partial class WaferMapView : Control
    {
        WaferMapResult map;
        float scale = 1;
        Vector2 centre;

        public WaferMapView()
        {
            CustomMinimumSize = new Vector2(360, 360);
            MouseFilter = MouseFilterEnum.Stop;
            TooltipText = " ";   // Godot only asks _GetTooltip when there is some tooltip text
        }

        public void SetMap(WaferMapResult m)
        {
            map = m;
            QueueRedraw();
        }

        /// <summary>Colour for a bin by its place in the bin table (best first); scrap is red.</summary>
        public static Color BinColor(IList<string> order, string bin)
        {
            if (bin == Binning.Scrap) return Ui.Bad.Darkened(0.35f);
            int i = order.IndexOf(bin);
            Color[] palette = { Ui.Ok, Ui.Accent, Ui.Warn, Ui.Rival1, Ui.Rival2 };
            return palette[Math.Clamp(i, 0, palette.Length - 1)];
        }

        static Color DefectColor(Binning.DefectEffect e) => e switch
        {
            Binning.DefectEffect.Fatal => Colors.White,
            Binning.DefectEffect.Disabled => Ui.Warn.Lightened(0.4f),
            Binning.DefectEffect.Repaired => Ui.Accent.Lightened(0.5f),
            _ => new Color(1, 1, 1, 0.35f),
        };

        Rect2 DieRect(WaferDie d) =>
            new Rect2(centre + new Vector2((float)d.X, -(float)(d.Y + map.DieH)) * scale, new Vector2((float)map.DieW, (float)map.DieH) * scale);

        public override void _Draw()
        {
            if (map == null) return;
            float side = Math.Min(Size.X, Size.Y) - 8;
            scale = side / (float)map.WaferMm;
            centre = new Vector2(Size.X / 2, Size.Y / 2);
            float r = (float)map.WaferMm / 2 * scale;
            DrawCircle(centre, r, Color.FromHtml("#2a3440"));
            DrawArc(centre, r, 0, Mathf.Tau, 96, Ui.Line, 2);
            DrawArc(centre, (float)map.UsableRadius * scale, 0, Mathf.Tau, 96, new Color(1, 1, 1, 0.12f), 1);
            // the notch at the bottom
            DrawCircle(centre + new Vector2(0, r), Math.Max(2, r * 0.03f), Ui.Bg);

            foreach (var d in map.Dies)
            {
                var rect = DieRect(d);
                var col = BinColor(map.BinOrder, d.Bin);
                DrawRect(rect, new Color(col, 0.75f));
                if (rect.Size.X > 5) DrawRect(rect, new Color(0, 0, 0, 0.35f), false, 1);
                foreach (var f in d.Defects)
                {
                    var p = rect.Position + new Vector2((float)(f.X / map.DieW) * rect.Size.X, (float)(1 - f.Y / map.DieH) * rect.Size.Y);
                    DrawCircle(p, Math.Max(1.2f, Math.Min(3, rect.Size.X * 0.08f)), DefectColor(f.Effect));
                }
            }
        }

        public override string _GetTooltip(Vector2 atPosition)
        {
            if (map == null) return "";
            foreach (var d in map.Dies)
                if (DieRect(d).HasPoint(atPosition))
                {
                    if (d.Defects.Count == 0) return $"{d.Bin}: no defects";
                    var parts = d.Defects.GroupBy(x => x.Effect).Select(g => $"{g.Count()} {Describe(g.Key)}");
                    return $"{d.Bin}: {d.Defects.Count} defect{(d.Defects.Count == 1 ? "" : "s")} ({string.Join(", ", parts)})";
                }
            return "";
        }

        static string Describe(Binning.DefectEffect e) => e switch
        {
            Binning.DefectEffect.Fatal => "fatal",
            Binning.DefectEffect.Disabled => "switched a block off",
            Binning.DefectEffect.Repaired => "repaired by spare rows",
            _ => "in empty silicon",
        };
    }
}
