using System;
using System.Collections.Generic;
using System.Linq;

namespace SiliconeTycoon.Sim
{
    /// <summary>One die position on a simulated wafer.</summary>
    public class WaferDie
    {
        /// <summary>Corner of the die nearest the wafer's lower-left, in mm from the wafer centre.</summary>
        public double X, Y;
        public string Bin;
        public List<Binning.Defect> Defects = new List<Binning.Defect>();
    }

    /// <summary>A whole wafer worked die by die: what the lot binning does statistically, laid out so you can see it.</summary>
    public class WaferMapResult
    {
        public double WaferMm;
        public double UsableRadius;
        public double DieW, DieH;
        public double D0;
        public List<WaferDie> Dies = new List<WaferDie>();
        /// <summary>Bin names in order (best first), then <see cref="Binning.Scrap"/>.</summary>
        public List<string> BinOrder = new List<string>();
        public Dictionary<string, int> Counts = new Dictionary<string, int>();
        /// <summary>The die-count formula used for costing, for comparison with the dies that actually fit.</summary>
        public int FormulaDies;

        public int Good => Counts.Where(kv => kv.Key != Binning.Scrap).Sum(kv => kv.Value);
    }

    /// <summary>
    /// Simulates a single wafer for display. It uses its own random numbers (seeded from the fab and the month) so looking
    /// at a wafer never changes the game.
    /// </summary>
    public static class WaferMap
    {
        /// <summary>Die used for test wafers (square, mm per side).</summary>
        public static readonly double TestDieSide = Math.Sqrt(10);
        public const string TestGood = "Working";

        /// <summary>Stable 64-bit hash of a string (string.GetHashCode changes between runs).</summary>
        public static ulong Hash(string text)
        {
            ulong h = 14695981039346656037UL;
            foreach (char ch in text ?? "")
            {
                h ^= ch;
                h *= 1099511628211UL;
            }
            return h;
        }

        /// <summary>Die positions that fit wholly inside the usable area, on a grid aligned for the most dies.</summary>
        public static List<(double x, double y)> Grid(double waferMm, double dieW, double dieH, double edgeMm = YieldModel.EdgeExclusionMm)
        {
            double r = waferMm / 2 - edgeMm;
            var best = new List<(double, double)>();
            if (r <= 0 || dieW <= 0 || dieH <= 0) return best;
            double px = dieW + YieldModel.ScribeMm, py = dieH + YieldModel.ScribeMm;
            foreach (double ox in new[] { 0, 0.5 })
                foreach (double oy in new[] { 0, 0.5 })
                {
                    var list = new List<(double, double)>();
                    int nx = (int)Math.Ceiling(r / px) + 1, ny = (int)Math.Ceiling(r / py) + 1;
                    for (int i = -nx; i <= nx; i++)
                        for (int j = -ny; j <= ny; j++)
                        {
                            double x = (i + ox) * px - dieW / 2, y = (j + oy) * py - dieH / 2;
                            if (Inside(x, y, r) && Inside(x + dieW, y, r) && Inside(x, y + dieH, r) && Inside(x + dieW, y + dieH, r))
                                list.Add((x, y));
                        }
                    if (list.Count > best.Count) best = list;
                }
            return best;
        }

        static bool Inside(double x, double y, double r) => x * x + y * y <= r * r;

        /// <summary>
        /// Simulates one wafer of a chip (or of test dies when <paramref name="c"/> is null) at defect density
        /// <paramref name="d0"/>.
        /// </summary>
        public static WaferMapResult Simulate(GameData d, ProcessNodeDef n, ChipDesign c, double d0, ulong seed)
        {
            var rng = new Rng(seed);
            double w = c?.DieW ?? TestDieSide, h = c?.DieH ?? TestDieSide;
            var res = new WaferMapResult
            {
                WaferMm = n.WaferMm,
                UsableRadius = n.WaferMm / 2 - YieldModel.EdgeExclusionMm,
                DieW = w,
                DieH = h,
                D0 = d0,
                FormulaDies = YieldModel.DiesPerWafer(n.WaferMm, YieldModel.WithScribe(w * h)),
            };

            Binning.DieModel model = null;
            if (c != null)
            {
                model = new Binning.DieModel(d, c, d0);
                res.BinOrder.AddRange(model.Bins.Select(b => b.Name));
            }
            else res.BinOrder.Add(TestGood);
            res.BinOrder.Add(Binning.Scrap);
            foreach (var b in res.BinOrder) res.Counts[b] = 0;

            double mean = d0 * w * h / 100.0;
            foreach (var (x, y) in Grid(n.WaferMm, w, h))
            {
                var die = new WaferDie { X = x, Y = y };
                if (model != null) die.Bin = Binning.Die(rng, model, die.Defects);
                else
                {
                    int k = mean > 0 ? rng.Poisson(rng.Gamma(YieldModel.DefaultAlpha, mean / YieldModel.DefaultAlpha)) : 0;
                    for (int i = 0; i < k; i++)
                        die.Defects.Add(new Binning.Defect { X = rng.NextDouble() * w, Y = rng.NextDouble() * h, Effect = Binning.DefectEffect.Fatal });
                    die.Bin = k == 0 ? TestGood : Binning.Scrap;
                }
                res.Counts[die.Bin]++;
                res.Dies.Add(die);
            }
            return res;
        }
    }
}
