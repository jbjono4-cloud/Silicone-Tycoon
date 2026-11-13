using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SiliconeTycoon.Sim;

namespace SiliconeTycoon.UI
{
    /// <summary>
    /// Draws a chip's floorplan and, when editable, lets you lay it out: drag blocks (they snap to the die edge and to
    /// each other), right-click to turn a block, scroll over the selected block to change its shape.
    /// </summary>
    public partial class FloorplanCanvas : Control
    {
        public GameData Data;
        public ChipDesign Chip;
        public bool Editable;
        /// <summary>Tint blocks by how close they run to the package's heat limit.</summary>
        public bool ShowHeat;
        /// <summary>Analysis of the chip (for heat); optional.</summary>
        public DesignStats Stats;
        public string Selected;
        /// <summary>The floorplan changed (after a drag, turn or reshape).</summary>
        public Action Changed;
        /// <summary>The selection changed.</summary>
        public Action SelectionChanged;

        const float Margin = 26;
        const double SnapPx = 8;
        const double Grid = 0.05;
        float scale = 40;
        Vector2 origin;
        PlacedBlock dragging;
        Vector2 dragOffsetMm;
        bool moved;

        public FloorplanCanvas()
        {
            CustomMinimumSize = new Vector2(420, 320);
            MouseFilter = MouseFilterEnum.Stop;
            FocusMode = FocusModeEnum.Click;
            TooltipText = " ";
        }

        public static Color KindColor(BlockKind k) => k switch
        {
            BlockKind.Core => Color.FromHtml("#5c8fd6"),
            BlockKind.Ram => Color.FromHtml("#62c17d"),
            BlockKind.Rom => Color.FromHtml("#8fb85a"),
            BlockKind.MemoryInterface => Color.FromHtml("#c88cf0"),
            BlockKind.Io => Color.FromHtml("#8a98a3"),
            BlockKind.Graphics => Color.FromHtml("#f0a35c"),
            _ => Ui.Muted,
        };

        /// <summary>Extent of what to show, in mm: the die plus any blocks hanging off it.</summary>
        (double w, double h) Extent()
        {
            double w = Chip.DieW, h = Chip.DieH;
            foreach (var p in Chip.Blocks)
            {
                w = Math.Max(w, p.X + p.W);
                h = Math.Max(h, p.Y + p.H);
            }
            double minX = Math.Min(0, Chip.Blocks.Count == 0 ? 0 : Chip.Blocks.Min(p => p.X));
            double minY = Math.Min(0, Chip.Blocks.Count == 0 ? 0 : Chip.Blocks.Min(p => p.Y));
            return (w - minX, h - minY);
        }

        void Fit()
        {
            var (w, h) = Extent();
            w = Math.Max(w, 0.5);
            h = Math.Max(h, 0.5);
            scale = (float)Math.Min((Size.X - 2 * Margin) / w, (Size.Y - 2 * Margin) / h);
            double minX = Math.Min(0, Chip.Blocks.Count == 0 ? 0 : Chip.Blocks.Min(p => p.X));
            double minY = Math.Min(0, Chip.Blocks.Count == 0 ? 0 : Chip.Blocks.Min(p => p.Y));
            origin = new Vector2(Margin - (float)minX * scale, Margin - (float)minY * scale);
        }

        Vector2 ToScreen(double x, double y) => origin + new Vector2((float)x, (float)y) * scale;
        Vector2 ToMm(Vector2 screen) => (screen - origin) / scale;
        Rect2 RectOf(PlacedBlock p) => new Rect2(ToScreen(p.X, p.Y), new Vector2((float)p.W, (float)p.H) * scale);

        public override void _Draw()
        {
            if (Chip == null || Data == null) return;
            if (dragging == null) Fit();
            var font = ThemeDB.FallbackFont;

            // die and a 1 mm grid
            var die = new Rect2(ToScreen(0, 0), new Vector2((float)Chip.DieW, (float)Chip.DieH) * scale);
            DrawRect(die, Color.FromHtml("#202a33"));
            for (int i = 1; i < Chip.DieW; i++) DrawLine(ToScreen(i, 0), ToScreen(i, Chip.DieH), new Color(1, 1, 1, 0.05f));
            for (int j = 1; j < Chip.DieH; j++) DrawLine(ToScreen(0, j), ToScreen(Chip.DieW, j), new Color(1, 1, 1, 0.05f));
            DrawRect(die, Ui.Accent, false, 2);
            DrawString(font, die.Position + new Vector2(0, -6), $"{Chip.DieW:0.00} × {Chip.DieH:0.00} mm = {Chip.DieW * Chip.DieH:0.0} mm²", HorizontalAlignment.Left, -1, 11, Ui.Muted);
            // mm rulers along the bottom and left of the die, spaced so labels don't crowd
            double step = new[] { 0.5, 1, 2, 5, 10 }.FirstOrDefault(v => v * scale >= 34, 10);
            for (double x = 0; x <= Chip.DieW + 1e-6; x += step)
            {
                var p = ToScreen(x, Chip.DieH);
                DrawLine(p, p + new Vector2(0, 4), Ui.Muted, 1);
                DrawString(font, p + new Vector2(-6, 15), $"{x:0.#}", HorizontalAlignment.Left, -1, 10, Ui.Muted);
            }
            for (double y = step; y <= Chip.DieH + 1e-6; y += step)
            {
                var p = ToScreen(0, y);
                DrawLine(p, p - new Vector2(4, 0), Ui.Muted, 1);
                DrawString(font, p + new Vector2(-24, 4), $"{y:0.#}", HorizontalAlignment.Left, -1, 10, Ui.Muted);
            }

            var defs = Chip.Blocks.Select(p => (p, b: Data.Block(p.BlockId))).Where(x => x.b != null).ToList();

            // the busy links; colour by how much clock they cost (gap × how busy the link is)
            foreach (var (pa, pb, w) in DesignLab.Connections(Data, Chip))
            {
                double gap = DesignLab.Gap(pa, pb);
                double cost = gap * w;
                var a = RectOf(pa).GetCenter();
                var b = RectOf(pb).GetCenter();
                var col = gap <= 1e-6 ? new Color(Ui.Ok, 0.35f) : cost < 0.5 ? new Color(Ui.Warn, 0.6f) : new Color(Ui.Bad, 0.8f);
                DrawDashedLine(a, b, col, w >= 0.75 ? 1.8f : 1f, 5);
                if (cost > 0.3) DrawString(font, (a + b) / 2 + new Vector2(3, -3), $"{gap:0.00} mm", HorizontalAlignment.Left, -1, 10, col);
            }

            foreach (var (p, b) in defs)
            {
                var r = RectOf(p);
                bool bad = !DesignLab.InsideDie(Chip, p) || Chip.Blocks.Any(o => o != p && DesignLab.Overlaps(o, p))
                           || (b.Kind == BlockKind.Io && !DesignLab.OnEdge(Chip, p));
                var col = KindColor(b.Kind);
                DrawRect(r, new Color(col, b.Kind == BlockKind.Io ? 0.35f : 0.55f));
                if (ShowHeat && Stats != null && Stats.HeatLimit > 0 && !double.IsInfinity(Stats.HeatLimit) && Stats.BlockHeat.TryGetValue(p.Id ?? "", out var heat))
                {
                    float t = (float)Math.Clamp(heat / Stats.HeatLimit, 0, 1.2);
                    DrawRect(r, new Color(1f, 0.25f, 0.1f, Math.Min(0.85f, t * 0.7f)));
                    if (r.Size.X > 40 && r.Size.Y > 40) DrawString(font, r.Position + new Vector2(4, 39), $"{heat / Stats.HeatLimit:P0} heat", HorizontalAlignment.Left, r.Size.X - 6, 10, t > 1 ? Colors.White : Ui.Fg);
                }
                DrawRect(r, bad ? Ui.Bad : p.Id == Selected ? Colors.White : col, false, p.Id == Selected || bad ? 2.5f : 1);
                int spare = DesignLab.Eff(b, p).Repair;
                if (spare > 0 && r.Size.Y > 24) DrawString(font, r.Position + new Vector2(4, r.Size.Y - 5), $"{spare} spare", HorizontalAlignment.Left, -1, 10, Ui.Muted);
                if (r.Size.X > 30 && r.Size.Y > 14)
                {
                    string label = Short(b) + Tuning(b, p);
                    DrawString(font, r.Position + new Vector2(4, 13), label, HorizontalAlignment.Left, r.Size.X - 6, 11, Ui.Fg);
                    if (r.Size.Y > 28) DrawString(font, r.Position + new Vector2(4, 26), $"{p.W * p.H:0.0} mm²", HorizontalAlignment.Left, r.Size.X - 6, 10, Ui.Muted);
                }
            }
        }

        static string Tuning(BlockDef b, PlacedBlock p)
        {
            var parts = new List<string>();
            if (p.Pipeline > 1) parts.Add($"{p.Pipeline}-stage");
            if (p.Scale > 1) parts.Add($"×{p.Scale}");
            if (p.Cells == CellLibrary.Fast) parts.Add("fast");
            else if (p.Cells == CellLibrary.Dense) parts.Add("dense");
            return parts.Count == 0 ? "" : " (" + string.Join(", ", parts) + ")";
        }

        static string Short(BlockDef b) => b.Kind switch
        {
            BlockKind.Io => $"I/O {b.Pins}",
            BlockKind.MemoryInterface => b.Name.Replace(" interface", " i/f"),
            _ => b.Name,
        };

        PlacedBlock HitTest(Vector2 pos)
        {
            for (int i = Chip.Blocks.Count - 1; i >= 0; i--)
                if (RectOf(Chip.Blocks[i]).HasPoint(pos)) return Chip.Blocks[i];
            return null;
        }

        public override string _GetTooltip(Vector2 atPosition)
        {
            if (Chip == null) return "";
            var p = HitTest(atPosition);
            var b = p == null ? null : Data.Block(p.BlockId);
            if (b == null) return Editable ? "Drag blocks to move them. Right-click turns a block; scroll over the selected block to reshape it." : "";
            var e = DesignLab.Eff(b, p);
            string extra = e.Repair > 0 ? $" · spare rows repair {e.Repair} defect(s)" : b.Disableable ? " · can be switched off if a defect hits it" : "";
            string feats = b.Features.Count > 0 ? $"\nGives: {Features.List(b.Features)}" : "";
            string heat = Stats != null && Stats.BlockHeat.TryGetValue(p.Id ?? "", out var h) ? $"\nHeat {h:0.000} W/mm² of {Stats.HeatLimit:0.00} the package takes" : "";
            return $"{b.Name}{Tuning(b, p)}: {p.W:0.00} × {p.H:0.00} mm ({p.W * p.H:0.00} mm²) at ({p.X:0.00}, {p.Y:0.00}){extra}{feats}{heat}\n{b.Description}";
        }

        public override void _GuiInput(InputEvent e)
        {
            if (Chip == null || !Editable) return;
            if (e is InputEventMouseButton mb)
            {
                if (mb.ButtonIndex == MouseButton.Left && mb.Pressed)
                {
                    var hit = HitTest(mb.Position);
                    Select(hit?.Id);
                    if (hit != null)
                    {
                        dragging = hit;
                        moved = false;
                        dragOffsetMm = ToMm(mb.Position) - new Vector2((float)hit.X, (float)hit.Y);
                    }
                    AcceptEvent();
                }
                else if (mb.ButtonIndex == MouseButton.Left && !mb.Pressed && dragging != null)
                {
                    dragging = null;
                    if (moved) Changed?.Invoke();
                    QueueRedraw();
                    AcceptEvent();
                }
                else if (mb.ButtonIndex == MouseButton.Right && mb.Pressed)
                {
                    var hit = HitTest(mb.Position);
                    if (hit != null)
                    {
                        Select(hit.Id);
                        DesignLab.SetAspect(hit, 1 / hit.Aspect);
                        Changed?.Invoke();
                        QueueRedraw();
                    }
                    AcceptEvent();
                }
                else if ((mb.ButtonIndex == MouseButton.WheelUp || mb.ButtonIndex == MouseButton.WheelDown) && mb.Pressed)
                {
                    var hit = HitTest(mb.Position);
                    if (hit != null && hit.Id == Selected)
                    {
                        double f = mb.ButtonIndex == MouseButton.WheelUp ? 1.1 : 1 / 1.1;
                        DesignLab.SetAspect(hit, Math.Clamp(hit.Aspect * f, 0.25, 4));
                        Changed?.Invoke();
                        QueueRedraw();
                        AcceptEvent();
                    }
                }
            }
            else if (e is InputEventMouseMotion mm && dragging != null)
            {
                var at = ToMm(mm.Position) - dragOffsetMm;
                double x = Math.Round(at.X / Grid) * Grid, y = Math.Round(at.Y / Grid) * Grid;
                (x, y) = Snap(dragging, x, y);
                if (Math.Abs(x - dragging.X) > 1e-9 || Math.Abs(y - dragging.Y) > 1e-9)
                {
                    dragging.X = Math.Round(x, 3);
                    dragging.Y = Math.Round(y, 3);
                    moved = true;
                    QueueRedraw();
                }
                AcceptEvent();
            }
        }

        /// <summary>Pulls a dragged block onto the die edges and onto other blocks' edges when it comes within a few pixels.</summary>
        (double, double) Snap(PlacedBlock p, double x, double y)
        {
            double tol = SnapPx / Math.Max(1, scale);
            var xs = new List<double> { 0, Chip.DieW - p.W };
            var ys = new List<double> { 0, Chip.DieH - p.H };
            foreach (var o in Chip.Blocks.Where(o => o != p))
            {
                xs.AddRange(new[] { o.X + o.W, o.X - p.W, o.X, o.X + o.W - p.W });
                ys.AddRange(new[] { o.Y + o.H, o.Y - p.H, o.Y, o.Y + o.H - p.H });
            }
            double bx = xs.OrderBy(v => Math.Abs(v - x)).First(), by = ys.OrderBy(v => Math.Abs(v - y)).First();
            return (Math.Abs(bx - x) <= tol ? bx : x, Math.Abs(by - y) <= tol ? by : y);
        }

        void Select(string id)
        {
            if (Selected == id) return;
            Selected = id;
            QueueRedraw();
            SelectionChanged?.Invoke();
        }
    }
}
