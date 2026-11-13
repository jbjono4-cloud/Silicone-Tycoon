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
        public string Selected;
        /// <summary>The floorplan changed (after a drag, turn or reshape).</summary>
        public Action Changed;
        /// <summary>The selection changed.</summary>
        public Action SelectionChanged;

        const float Margin = 14;
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
            DrawString(font, die.Position + new Vector2(0, -3), $"{Chip.DieW:0.00} × {Chip.DieH:0.00} mm", HorizontalAlignment.Left, -1, 11, Ui.Muted);

            var defs = Chip.Blocks.Select(p => (p, b: Data.Block(p.BlockId))).Where(x => x.b != null).ToList();

            // wires from each core to its nearest partner of each kind
            foreach (var core in defs.Where(x => x.b.Kind == BlockKind.Core))
                foreach (var kind in new[] { BlockKind.Ram, BlockKind.Rom, BlockKind.MemoryInterface, BlockKind.Graphics })
                {
                    var partners = defs.Where(x => x.b.Kind == kind).ToList();
                    if (partners.Count == 0) continue;
                    var near = partners.OrderBy(x => DesignLab.Gap(core.p, x.p)).First();
                    double gap = DesignLab.Gap(core.p, near.p);
                    var a = RectOf(core.p).GetCenter();
                    var b = RectOf(near.p).GetCenter();
                    var col = gap <= 1e-6 ? new Color(Ui.Ok, 0.5f) : gap < 0.5 ? new Color(Ui.Warn, 0.7f) : new Color(Ui.Bad, 0.8f);
                    DrawDashedLine(a, b, col, 1.5f, 5);
                    if (gap > 1e-6) DrawString(font, (a + b) / 2 + new Vector2(3, -3), $"{gap:0.00} mm", HorizontalAlignment.Left, -1, 10, col);
                }

            foreach (var (p, b) in defs)
            {
                var r = RectOf(p);
                bool bad = !DesignLab.InsideDie(Chip, p) || Chip.Blocks.Any(o => o != p && DesignLab.Overlaps(o, p))
                           || (b.Kind == BlockKind.Io && !DesignLab.OnEdge(Chip, p));
                var col = KindColor(b.Kind);
                DrawRect(r, new Color(col, b.Kind == BlockKind.Io ? 0.35f : 0.55f));
                DrawRect(r, bad ? Ui.Bad : p.Id == Selected ? Colors.White : col, false, p.Id == Selected || bad ? 2.5f : 1);
                if (b.RepairCapacity > 0 && r.Size.Y > 24) DrawString(font, r.Position + new Vector2(4, r.Size.Y - 5), $"{b.RepairCapacity} spare", HorizontalAlignment.Left, -1, 10, Ui.Muted);
                if (r.Size.X > 30 && r.Size.Y > 14)
                {
                    string label = Short(b);
                    DrawString(font, r.Position + new Vector2(4, 13), label, HorizontalAlignment.Left, r.Size.X - 6, 11, Ui.Fg);
                    if (r.Size.Y > 28) DrawString(font, r.Position + new Vector2(4, 26), $"{p.W * p.H:0.0} mm²", HorizontalAlignment.Left, r.Size.X - 6, 10, Ui.Muted);
                }
            }
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
            string extra = b.RepairCapacity > 0 ? $" · spare rows repair {b.RepairCapacity} defect(s)" : b.Disableable ? " · can be switched off if a defect hits it" : "";
            return $"{b.Name}: {p.W:0.00} × {p.H:0.00} mm ({p.W * p.H:0.00} mm²) at ({p.X:0.00}, {p.Y:0.00}){extra}\n{b.Description}";
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
