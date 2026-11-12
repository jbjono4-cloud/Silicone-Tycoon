using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SiliconeTycoon.Sim.Tests
{
    public class BinningTests
    {
        static GameData D => TestData.Load();

        static ChipDesign Analyzed(GameData d, params string[] blocks)
        {
            var s = TestData.NewGame();
            var c = DesignLab.Starter(d, "Test", "n10um", "dip40c", blocks);
            c.Stats = DesignLab.Analyze(d, s, c);
            return c;
        }

        static double GoodShare(GameData d, ChipDesign c, double d0, ulong seed = 1, int dies = 20000)
        {
            var counts = Binning.Sample(d, new Rng(seed), c, d0, dies);
            return Binning.Good(counts) / (double)dies;
        }

        [Fact]
        public void PerfectProcessFillsTheTopBin()
        {
            var c = Analyzed(D, "core4", "memif", "io16");
            c.Bins = new List<BinDef> { new BinDef { Name = "Any", MinCores = 1 } };
            var counts = Binning.Sample(D, new Rng(1), c, 0, 2000);
            Assert.Equal(2000, counts["Any"]);
            Assert.Equal(0, counts[Binning.Scrap]);
        }

        [Fact]
        public void LotCountsAddUpToGrossDies()
        {
            var c = Analyzed(D, "core4", "memif", "io16");
            var lot = Binning.Lot(D, new Rng(3), c, 6, 5000);
            Assert.Equal(5000, lot.Values.Sum());
            Assert.True(lot[Binning.Scrap] > 0);
            Assert.True(Binning.Good(lot) > 0);
        }

        [Fact]
        public void DefaultBinRequiresTheRatedClock()
        {
            var c = Analyzed(D, "core4", "memif", "io16");
            var counts = Binning.Sample(D, new Rng(4), c, 0, 20000);
            // rated at 90% of typical with a 6% spread: about 95% make it
            Assert.InRange(counts["Standard"] / 20000.0, 0.92, 0.98);
        }

        [Fact]
        public void SpeedBinsSplitAroundTheTypicalClock()
        {
            var c = Analyzed(D, "core4", "memif", "io16");
            c.Bins = new List<BinDef>
            {
                new BinDef { Name = "Fast", MinCores = 1, MinClockMHz = c.Stats.MaxClockMHz },
                new BinDef { Name = "Slow", MinCores = 1 },
            };
            var counts = Binning.Sample(D, new Rng(5), c, 0, 20000);
            Assert.InRange(counts["Fast"] / 20000.0, 0.47, 0.53);
            Assert.Equal(20000, counts["Fast"] + counts["Slow"]);
        }

        [Fact]
        public void HarvestingSellsDiesWithADeadCore()
        {
            var c = Analyzed(D, "core4", "core4", "memif", "io16");
            c.Bins = new List<BinDef> { new BinDef { Name = "Dual", MinCores = 2 } };
            double fullOnly = GoodShare(D, c, 5);
            c.Bins.Add(new BinDef { Name = "Single", MinCores = 1 });
            double withHarvest = GoodShare(D, c, 5);
            Assert.True(withHarvest > fullOnly + 0.05, $"{fullOnly:P1} vs {withHarvest:P1}");
        }

        [Fact]
        public void SpareRowsRepairRamDefects()
        {
            var r = D.Block("ram256");
            var noSpares = new BlockDef
            {
                Id = "ram_norep", Name = r.Name, Kind = r.Kind, Year = r.Year, LogicTransistors = r.LogicTransistors,
                SramTransistors = r.SramTransistors, Activity = r.Activity, LogicDepth = r.LogicDepth, RamBytes = r.RamBytes,
            };
            var d2 = new GameData { Nodes = D.Nodes, Locations = D.Locations, Packages = D.Packages, Blocks = new List<BlockDef>(D.Blocks) { noSpares } };

            var repaired = Analyzed(d2, "core4", "ram256", "memif", "io16");
            var plain = Analyzed(d2, "core4", "ram_norep", "memif", "io16");
            foreach (var c in new[] { repaired, plain }) c.Bins = new List<BinDef> { new BinDef { Name = "Any", MinCores = 1 } };
            Assert.True(GoodShare(d2, repaired, 4) > GoodShare(d2, plain, 4) + 0.05);
        }

        [Fact]
        public void DefectsInEmptySiliconAreHarmless()
        {
            var tight = Analyzed(D, "core4", "memif", "io16");
            tight.Bins = new List<BinDef> { new BinDef { Name = "Any", MinCores = 1 } };
            var loose = tight.Clone();
            loose.DieW *= 2;   // same blocks, twice the silicon, the extra half empty
            double a = GoodShare(D, tight, 4, seed: 8);
            double b = GoodShare(D, loose, 4, seed: 9);
            Assert.InRange(b - a, -0.03, 0.03);
        }
    }
}
