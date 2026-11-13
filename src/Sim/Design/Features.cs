using System.Collections.Generic;
using System.Linq;

namespace SiliconeTycoon.Sim
{
    /// <summary>
    /// The features blocks give a chip. Markets weigh them (see <see cref="SegmentDef.Wants"/>) and customers' requests
    /// can insist on them. A feature comes from any live block that provides it, so a defect that switches off the only
    /// FPU takes the "fpu" feature with it.
    /// </summary>
    public static class Features
    {
        static readonly Dictionary<string, string> names = new Dictionary<string, string>
        {
            ["4-bit"] = "4-bit core",
            ["8-bit"] = "8-bit or wider core",
            ["16-bit"] = "16-bit or wider core",
            ["32-bit"] = "32-bit core",
            ["cache"] = "on-chip cache",
            ["dram"] = "DRAM controller",
            ["wide-bus"] = "16-bit bus",
            ["video"] = "video output",
            ["sprites"] = "sprites",
            ["tiles"] = "tile maps",
            ["keyboard"] = "keyboard scanning",
            ["display"] = "display driver",
            ["timers"] = "timers",
            ["interrupts"] = "interrupts",
            ["serial"] = "serial port",
            ["multiply"] = "hardware multiply",
            ["analog"] = "A/D converter",
            ["dma"] = "DMA",
            ["prefetch"] = "prefetch queue",
            ["sound"] = "sound",
            ["fpu"] = "floating point",
            ["mmu"] = "memory management",
        };

        public static bool Known(string id) => id != null && names.ContainsKey(id);
        public static string Name(string id) => id != null && names.TryGetValue(id, out var n) ? n : id;
        public static IEnumerable<string> All => names.Keys;
        public static string List(IEnumerable<string> ids) => string.Join(", ", ids.Select(Name));

        /// <summary>Everything a floorplan's blocks provide.</summary>
        public static HashSet<string> Of(GameData d, ChipDesign c) =>
            new HashSet<string>(c.Blocks.Select(p => d.Block(p.BlockId)).Where(b => b != null).SelectMany(b => b.Features));

        /// <summary>Features of chips sold in a bin: the bin's own list, or everything the chip has.</summary>
        public static HashSet<string> OfBin(GameData d, ChipDesign c, string bin)
        {
            var b = Binning.BinsFor(c).FirstOrDefault(x => x.Name == bin);
            return b?.Features != null ? new HashSet<string>(b.Features) : Of(d, c);
        }

        /// <summary>
        /// How a market rates a chip's features: each wanted feature it lacks costs that feature's weight (multiplied up).
        /// Required ones it lacks are handled separately (the chip doesn't qualify at all).
        /// </summary>
        public static double Factor(SegmentDef seg, ICollection<string> have, double year)
        {
            double f = 1;
            foreach (var w in seg.WantsAt(year))
                if (!have.Contains(w.Feature)) f *= 1 - w.Weight;
            return f;
        }
    }
}
