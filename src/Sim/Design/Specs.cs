using System;
using System.Collections.Generic;
using System.Linq;

namespace SiliconeTycoon.Sim
{
    /// <summary>What a chip has to be: a market's brief for the design house, or a customer's request for quotes.</summary>
    public class ChipSpec
    {
        public Metric Metric;
        public double MinMetric;
        public double MinClockMHz;
        public double MaxPowerW = double.PositiveInfinity;
        /// <summary>Most package pins the board takes.</summary>
        public int MaxPins = 999;
        public int MinRamBytes;
        public int MinRomBytes;
        /// <summary>Must-have features.</summary>
        public List<string> Features = new List<string>();
        /// <summary>Nice-to-have features (they raise the chip's score; the design house includes them if it can).</summary>
        public List<string> Wanted = new List<string>();

        public static ChipSpec ForSegment(SegmentDef seg, double year) => new ChipSpec
        {
            Metric = seg.Metric,
            MaxPowerW = seg.MaxPowerW,
            MaxPins = seg.MaxPins,
            Features = seg.RequiredAt(year).ToList(),
            Wanted = seg.WantsAt(year).Select(w => w.Feature).ToList(),
        };

        public static ChipSpec ForRfq(GameData d, Rfq r)
        {
            var seg = d.Segment(r.SegmentId);
            return new ChipSpec
            {
                Metric = seg?.Metric ?? Metric.Control,
                MinMetric = r.MinMetric,
                MinClockMHz = r.MinClockMHz,
                MaxPowerW = r.MaxPowerW,
                MaxPins = r.MaxPins > 0 ? r.MaxPins : 999,
                Features = r.Features.ToList(),
                Wanted = seg == null ? new List<string>() : seg.WantsAt(Market.YearOfMonth(r.OpenMonth)).Select(w => w.Feature).ToList(),
            };
        }

        /// <summary>One line per requirement, for showing a brief.</summary>
        public IEnumerable<string> Describe()
        {
            if (MinMetric > 0) yield return $"{Metric.ToString().ToLower()} {MinMetric:0.00}+";
            if (MinClockMHz > 0) yield return $"at least {MinClockMHz:0.##} MHz";
            if (!double.IsInfinity(MaxPowerW)) yield return $"under {MaxPowerW:0.#} W";
            if (MaxPins < 999) yield return $"{MaxPins} pins at most";
            if (MinRamBytes > 0) yield return $"{MinRamBytes:N0} B RAM";
            if (MinRomBytes > 0) yield return $"{MinRomBytes:N0} B ROM";
            if (Features.Count > 0) yield return "must have " + Sim.Features.List(Features);
        }
    }

    public static class Specs
    {
        /// <summary>Everything a chip sold in a bin misses in a spec; empty if it meets it.</summary>
        public static List<string> Check(GameData d, ChipSpec spec, ChipDesign c, string bin, bool checkMetric = true)
        {
            var gaps = new List<string>();
            if (c?.Stats == null) { gaps.Add("Choose a chip that's in development or released."); return gaps; }
            if (checkMetric)
            {
                double perf = Market.Perf(c, bin, spec.Metric);
                if (perf < spec.MinMetric - 1e-9) gaps.Add($"needs {spec.Metric.ToString().ToLower()} of {spec.MinMetric:0.00}; the {c.Name} {bin} manages {perf:0.00}");
            }
            double clock = Market.BinClock(c, bin);
            if (clock < spec.MinClockMHz - 1e-9) gaps.Add($"needs at least {spec.MinClockMHz:0.##} MHz; the {c.Name} {bin} runs at {clock:0.##}");
            double watts = Market.Power(c, bin);
            if (watts > spec.MaxPowerW + 1e-9) gaps.Add($"can shed {spec.MaxPowerW:0.#} W; the {c.Name} {bin} draws {watts:0.00} W");
            int pins = d.Package(c.PackageId)?.Pins ?? 0;
            if (pins > spec.MaxPins) gaps.Add($"takes packages of up to {spec.MaxPins} pins; the {c.Name} has {pins}");
            if (c.Stats.RamBytes < spec.MinRamBytes) gaps.Add($"needs {spec.MinRamBytes:N0} bytes of on-chip RAM; the {c.Name} has {c.Stats.RamBytes:N0}");
            if (c.Stats.RomBytes < spec.MinRomBytes) gaps.Add($"needs {spec.MinRomBytes:N0} bytes of ROM; the {c.Name} has {c.Stats.RomBytes:N0}");
            var have = Features.OfBin(d, c, bin);
            var missing = spec.Features.Where(f => !have.Contains(f)).ToList();
            if (missing.Count > 0) gaps.Add($"must have {Features.List(missing)}");
            return gaps;
        }
    }
}
