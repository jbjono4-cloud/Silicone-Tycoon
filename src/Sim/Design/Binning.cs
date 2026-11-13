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

        /// <summary>What a defect did to the die it landed on.</summary>
        public enum DefectEffect { Harmless, Repaired, Disabled, Fatal }

        /// <summary>A defect at a spot on a die (mm from the die's corner).</summary>
        public struct Defect
        {
            public double X, Y;
            public DefectEffect Effect;
        }

        /// <summary>Precomputed facts about a design used for every die.</summary>
        public sealed class DieModel
        {
            public ChipDesign Chip;
            public BlockDef[] Defs;
            public List<BinDef> Bins;
            public int CoresTotal;
            public double Mean;
            public double Typical;
            public double Alpha;
            /// <summary>Spare-row repairs each placed block can make (after its size setting).</summary>
            public int[] RepairCap;
            /// <summary>Everything the chip provides when every block works.</summary>
            public HashSet<string> AllFeatures;
            internal bool[] Dead;
            internal int[] Repaired;

            public DieModel(GameData d, ChipDesign c, double d0, double alpha = YieldModel.DefaultAlpha)
            {
                Chip = c;
                Bins = BinsFor(c);
                Defs = c.Blocks.Select(p => d.Block(p.BlockId)).ToArray();
                RepairCap = c.Blocks.Select((p, i) => DesignLab.Eff(Defs[i], p).Repair).ToArray();
                AllFeatures = new HashSet<string>(Defs.SelectMany(b => b.Features));
                CoresTotal = Defs.Count(b => b.Kind == BlockKind.Core);
                Mean = d0 * c.DieAreaMm2 / 100.0;
                Typical = c.Stats?.MaxClockMHz ?? 0;
                Alpha = alpha;
                Dead = new bool[c.Blocks.Count];
                Repaired = new int[c.Blocks.Count];
            }
        }

        /// <summary>
        /// Makes one die: draws its defects, works out what each one hit, then its top speed, and returns its bin.
        /// With <paramref name="record"/> null it stops at the first fatal defect (as lots are binned); otherwise it places
        /// every defect so they can be drawn.
        /// </summary>
        public static string Die(Rng rng, DieModel m, List<Defect> record = null)
        {
            var c = m.Chip;
            var placed = c.Blocks;
            Array.Clear(m.Dead, 0, m.Dead.Length);
            Array.Clear(m.Repaired, 0, m.Repaired.Length);
            bool fatal = false;
            int defects = m.Mean > 0 ? rng.Poisson(rng.Gamma(m.Alpha, m.Mean / m.Alpha)) : 0;
            for (int k = 0; k < defects && (record != null || !fatal); k++)
            {
                double x = rng.NextDouble() * c.DieW;
                double y = rng.NextDouble() * c.DieH;
                int hit = -1;
                for (int b = 0; b < placed.Count; b++)
                    if (placed[b].Contains(x, y)) { hit = b; break; }
                DefectEffect effect;
                if (hit < 0) effect = DefectEffect.Harmless;                                        // empty silicon
                else if (m.Repaired[hit] < m.RepairCap[hit]) { m.Repaired[hit]++; effect = DefectEffect.Repaired; }
                else if (m.Defs[hit].Disableable) { m.Dead[hit] = true; effect = DefectEffect.Disabled; }
                else { fatal = true; effect = DefectEffect.Fatal; }
                record?.Add(new Defect { X = x, Y = y, Effect = effect });
            }

            if (fatal) return Scrap;
            int cores = 0, gfx = 0;
            bool anyDead = false;
            for (int b = 0; b < placed.Count; b++)
            {
                if (m.Dead[b]) { anyDead = true; continue; }
                if (m.Defs[b].Kind == BlockKind.Core) cores++;
                else if (m.Defs[b].Kind == BlockKind.Graphics) gfx++;
            }
            if (m.CoresTotal > 0 && cores == 0) return Scrap;
            HashSet<string> live = m.AllFeatures;
            if (anyDead)
            {
                live = new HashSet<string>();
                for (int b = 0; b < placed.Count; b++)
                    if (!m.Dead[b]) live.UnionWith(m.Defs[b].Features);
            }
            double speed = m.Typical * (1 + rng.Normal(0, SpeedSigma));
            foreach (var b in m.Bins)
                if (cores >= b.MinCores && gfx >= b.MinGraphics && speed >= b.MinClockMHz
                    && (b.Features ?? (IEnumerable<string>)m.AllFeatures).All(live.Contains)) return b.Name;
            return Scrap;
        }

        /// <summary>Bins a sample of dies. Returns counts per bin name plus <see cref="Scrap"/>.</summary>
        public static Dictionary<string, int> Sample(GameData d, Rng rng, ChipDesign c, double d0, int dies, double alpha = YieldModel.DefaultAlpha)
        {
            var m = new DieModel(d, c, d0, alpha);
            var counts = new Dictionary<string, int>();
            foreach (var b in m.Bins) counts[b.Name] = 0;
            counts[Scrap] = 0;
            for (int i = 0; i < dies; i++) counts[Die(rng, m)]++;
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
