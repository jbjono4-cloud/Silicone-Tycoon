using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace SiliconeTycoon.UI
{
    /// <summary>Minimal line chart for history series. (From Console Tycoon.)</summary>
    public partial class LineChart : Control
    {
        public readonly List<(string name, Color color, List<double> values)> Series = new List<(string, Color, List<double>)>();
        public List<string> XLabels = new List<string>();
        public Func<double, string> ValueFormat = v => v.ToString("0");
        public bool ZeroLine = true;

        public LineChart()
        {
            CustomMinimumSize = new Vector2(300, 180);
        }

        public void SetData(List<string> xLabels, params (string name, Color color, List<double> values)[] series)
        {
            XLabels = xLabels;
            Series.Clear();
            Series.AddRange(series);
            QueueRedraw();
        }

        static double NiceStep(double x)
        {
            if (x <= 0) return 1;
            double p = Math.Pow(10, Math.Floor(Math.Log10(x)));
            double n = x / p;
            return (n <= 1 ? 1 : n <= 2 ? 2 : n <= 5 ? 5 : 10) * p;
        }

        public override void _Draw()
        {
            var font = ThemeDB.FallbackFont;
            float l = 64, r = 10, t = 10, b = 26;
            float w = Size.X - l - r, h = Size.Y - t - b;
            if (w < 20 || h < 20) return;
            var all = Series.SelectMany(s => s.values).ToList();
            if (all.Count < 2) { DrawString(font, new Vector2(l, t + h / 2), "Not enough history yet.", HorizontalAlignment.Left, -1, 13, Ui.Muted); return; }
            double min = Math.Min(0, all.Min()), max = Math.Max(0, all.Max());
            if (max - min < 1e-9) max = min + 1;
            double step = NiceStep((max - min) / 4);
            min = Math.Floor(min / step) * step;
            max = Math.Ceiling(max / step) * step;
            float Y(double v) => t + h - (float)((v - min) / (max - min)) * h;
            for (double v = min; v <= max + 1e-9; v += step)
            {
                DrawLine(new Vector2(l, Y(v)), new Vector2(l + w, Y(v)), Ui.Line, 1);
                DrawString(font, new Vector2(4, Y(v) + 4), ValueFormat(v), HorizontalAlignment.Right, l - 10, 11, Ui.Muted);
            }
            if (ZeroLine && min < 0) DrawLine(new Vector2(l, Y(0)), new Vector2(l + w, Y(0)), Ui.Muted, 1);
            int n = Series.Max(s => s.values.Count);
            float X(int i) => l + (n <= 1 ? 0 : i * w / (n - 1));
            foreach (var (name, color, values) in Series)
            {
                for (int i = 1; i < values.Count; i++) DrawLine(new Vector2(X(i - 1), Y(values[i - 1])), new Vector2(X(i), Y(values[i])), color, 2, true);
                if (values.Count > 0) DrawCircle(new Vector2(X(values.Count - 1), Y(values[values.Count - 1])), 3.5f, color);
            }
            int every = Math.Max(1, XLabels.Count / 6);
            for (int i = 0; i < XLabels.Count; i += every)
                DrawString(font, new Vector2(X(i) - 14, t + h + 18), XLabels[i], HorizontalAlignment.Left, -1, 11, Ui.Muted);
            float lx = l + 6;
            foreach (var (name, color, _) in Series)
            {
                if (Series.Count < 2) break;
                DrawRect(new Rect2(lx, t + 4, 10, 10), color);
                DrawString(font, new Vector2(lx + 14, t + 13), name, HorizontalAlignment.Left, -1, 11, Ui.Fg);
                lx += 24 + font.GetStringSize(name, HorizontalAlignment.Left, -1, 11).X;
            }
        }
    }
}
