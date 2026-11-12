using System;
using System.Collections.Generic;
using System.Linq;

namespace SiliconeTycoon.Sim
{
    /// <summary>
    /// Sorts dies into bins. Each sampled die gets clustered defects dropped at random spots on the die: a defect in empty
    /// silicon is harmless, spare rows repair defects in RAM, a defect in a disableable block (an extra core or graphics
    /// unit) switches that block off, and anything else kills the die. Each die also gets a top speed around the design's
    /// typical clock. The die goes into the first bin it qualifies for, or scrap.
    /// </summary>
    public static class Binning
    {
        public const string Scrap = "Scrap";
        /// <summary>Die-to-die spread in top clock speed (standard deviation as a share of typical).</summary>
        public const double SpeedSigma = 0.06;

        /// <summary>The bins a design uses: its own table, or a single bin at the rated clock with everything working.</summary>
        public static List<BinDef> BinsFor(ChipDesign c)
        {
            if (c.Bins.Count > 0) return c.Bins;
            var st = c.Stats;
            return new List<BinDef>
            {
                new BinDef { Name = "Standard", MinCores = st?.Cores ?? 0, MinGraphics = st?.GraphicsUnits ?? 0, MinClockMHz = st?.ClockMHz ?? 0 },
            };
        }

        /// <summary>Bins a sample of dies. Returns counts per bin name plus <see cref="Scrap"/>.</summary>
        public static Dictionary<string, int> Sample(GameData d, Rng rng, ChipDesign c, double d0, int dies, double alpha = YieldModel.DefaultAlpha)
        {
            var bins = BinsFor(c);
            var counts = new Dictionary<string, int>();
            foreach (var b in bins) counts[b.Name] = 0;
            counts[Scrap] = 0;

            var placed = c.Blocks;
            var defs = placed.Select(p => d.Block(p.BlockId)).ToArray();
            int coresTotal = defs.Count(b => b.Kind == BlockKind.Core);
            double mean = d0 * c.DieAreaMm2 / 100.0;
            double typical = c.Stats?.MaxClockMHz ?? 0;
            var dead = new bool[placed.Count];
            var repaired = new int[placed.Count];

            for (int i = 0; i < dies; i++)
            {
                Array.Clear(dead, 0, dead.Length);
                Array.Clear(repaired, 0, repaired.Length);
                bool fatal = false;
                int defects = mean > 0 ? rng.Poisson(rng.Gamma(alpha, mean / alpha)) : 0;
                for (int k = 0; k < defects && !fatal; k++)
                {
                    double x = rng.NextDouble() * c.DieW;
                    double y = rng.NextDouble() * c.DieH;
                    int hit = -1;
                    for (int b = 0; b < placed.Count; b++)
                        if (placed[b].Contains(x, y)) { hit = b; break; }
                    if (hit < 0) continue;                                    // empty silicon
                    if (repaired[hit] < defs[hit].RepairCapacity) { repaired[hit]++; continue; }
                    if (defs[hit].Disableable) { dead[hit] = true; continue; }
                    fatal = true;
                }

                string bin = Scrap;
                if (!fatal)
                {
                    int cores = 0, gfx = 0;
                    for (int b = 0; b < placed.Count; b++)
                    {
                        if (dead[b]) continue;
                        if (defs[b].Kind == BlockKind.Core) cores++;
                        else if (defs[b].Kind == BlockKind.Graphics) gfx++;
                    }
                    if (coresTotal == 0 || cores > 0)
                    {
                        double speed = typical * (1 + rng.Normal(0, SpeedSigma));
                        foreach (var b in bins)
                            if (cores >= b.MinCores && gfx >= b.MinGraphics && speed >= b.MinClockMHz) { bin = b.Name; break; }
                    }
                }
                counts[bin]++;
            }
            return counts;
        }

        /// <summary>Bins a whole lot: samples up to <paramref name="sample"/> dies and scales to the lot. Counts add up to the gross dies.</summary>
        public static Dictionary<string, int> Lot(GameData d, Rng rng, ChipDesign c, double d0, int grossDies, int sample = FabOps.SampleDies)
        {
            int n = Math.Min(grossDies, sample);
            var raw = Sample(d, rng, c, d0, n);
            var result = new Dictionary<string, int>();
            int assigned = 0;
            foreach (var kv in raw)
            {
                if (kv.Key == Scrap) continue;
                int v = n > 0 ? (int)Math.Round(grossDies * (double)kv.Value / n) : 0;
                v = Math.Min(v, grossDies - assigned);
                result[kv.Key] = v;
                assigned += v;
            }
            result[Scrap] = grossDies - assigned;
            return result;
        }

        /// <summary>Good (non-scrap) dies in a bin count.</summary>
        public static int Good(Dictionary<string, int> counts) => counts.Where(kv => kv.Key != Scrap).Sum(kv => kv.Value);
    }
}
