using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SiliconeTycoon.Sim.Tests
{
    public class DesignDepthTests
    {
        static GameData D => TestData.Load();

        static ChipDesign Chip(GameState s, string node, string package, params string[] blocks)
        {
            var c = DesignLab.Starter(D, "T", node, package, blocks);
            c.UseDesignHouse = false;
            return c;
        }

        static PlacedBlock Find(ChipDesign c, string id) => c.Blocks.First(b => b.BlockId == id);

        [Fact]
        public void PipeliningTradesWorkPerClockForClockSpeed()
        {
            var s = TestData.NewGame(year: 1976);
            var c = Chip(s, "n6um", "dip40c", "core8", "rom512", "memif", "io40");
            var flat = DesignLab.Analyze(D, s, c);
            Find(c, "core8").Pipeline = 2;
            DesignLab.AutoPlace(D, c);
            c.UseDesignHouse = false;
            var piped = DesignLab.Analyze(D, s, c);
            Assert.True(piped.Valid, piped.FirstError);
            Assert.True(piped.MaxClockMHz > flat.MaxClockMHz * 1.02, $"{piped.MaxClockMHz} vs {flat.MaxClockMHz}");
            Assert.True(piped.Transistors > flat.Transistors);
            var core = D.Block("core8");
            Assert.True(DesignLab.Eff(core, new PlacedBlock { Pipeline = 2 }).Ipc < core.Ipc);
            Find(c, "core8").Pipeline = 3;
            Assert.Contains(DesignLab.Analyze(D, s, c).Notes, n => n.Severity == 2 && n.Text.Contains("pipeline"));
        }

        [Fact]
        public void CellLibrariesTradeSpeedAreaAndPower()
        {
            var b = D.Block("core8");
            var n = D.Node("n6um");
            var std = DesignLab.Eff(b, new PlacedBlock());
            var fast = DesignLab.Eff(b, new PlacedBlock { Cells = CellLibrary.Fast });
            var dense = DesignLab.Eff(b, new PlacedBlock { Cells = CellLibrary.Dense });
            Assert.True(fast.Depth < std.Depth && dense.Depth > std.Depth);
            Assert.True(fast.Activity > std.Activity && dense.Activity < std.Activity);
            Assert.True(DesignLab.BlockArea(fast, n) > DesignLab.BlockArea(std, n));
            Assert.True(DesignLab.BlockArea(dense, n) < DesignLab.BlockArea(std, n));
        }

        [Fact]
        public void BiggerMemoriesHoldMoreAndRepairMore()
        {
            var ram = D.Block("ram256");
            var e = DesignLab.Eff(ram, new PlacedBlock { Scale = 4 });
            Assert.Equal(ram.RamBytes * 4, e.RamBytes);
            Assert.Equal(ram.RepairCapacity * 4, e.Repair);
            Assert.True(DesignLab.BlockArea(e, D.Node("n5um")) > DesignLab.BlockArea(ram, D.Node("n5um")) * 3);
            var bus = DesignLab.Eff(D.Block("memif_dram"), new PlacedBlock { Scale = 2 });
            Assert.Equal(D.Block("memif_dram").Pins * 2, bus.Pins);
            Assert.Equal(D.Block("memif_dram").BytesPerClock * 2, bus.BytesPerClock, 9);
        }

        [Fact]
        public void PackedDiesLoseClockToCongestionAndCantBeRoutedFull()
        {
            var s = TestData.NewGame();
            var c = DesignLab.Blank(D, s, "T", "n10um");
            var p = DesignLab.AddBlock(D, c, "core4");
            p.X = 0; p.Y = 0;
            c.DieW = p.W; c.DieH = p.H;
            Assert.Contains(DesignLab.Analyze(D, s, c).Notes, n => n.Severity == 2 && n.Text.Contains("tightly packed"));
            c.DieW = p.W / 0.85; c.DieH = p.H;
            var st = DesignLab.Analyze(D, s, c);
            Assert.InRange(st.CongestionLoss, 0.09, 0.11);
            Assert.DoesNotContain(st.Notes, n => n.Text.Contains("tightly packed"));
        }

        [Fact]
        public void TinyDiesCantFitManyPads()
        {
            var s = TestData.NewGame();
            var c = DesignLab.Blank(D, s, "T", "n10um");
            c.PackageId = "dip40c";
            var io = DesignLab.AddBlock(D, c, "io40");
            c.DieW = 2; c.DieH = 1.5;
            Assert.Contains(DesignLab.Analyze(D, s, c).Notes, n => n.Severity == 2 && n.Text.Contains("Pad-limited"));
        }

        [Fact]
        public void HotSpotsLimitTheClockAndSpreadingHelps()
        {
            var s = TestData.NewGame(year: 1976);
            var c = Chip(s, "n6um", "dip40c", "core8", "rom512", "memif", "io40");
            var st = DesignLab.Analyze(D, s, c);
            Assert.True(st.PeakHeat > 0 && st.ThermalClockMHz > 0);
            c.TargetClockMHz = st.ClockMHz * 2;
            Assert.Equal(st.PeakHeat * 2, DesignLab.Analyze(D, s, c).PeakHeat, 9);
            c.TargetClockMHz = Math.Ceiling(st.ThermalClockMHz * 1.1 * 100) / 100;
            Assert.Contains(DesignLab.Analyze(D, s, c).Notes, n => n.Severity == 2 && n.Text.Contains("Hot spot"));

            // pull every block well apart: neighbours stop heating each other
            c.TargetClockMHz = 0;
            double near = DesignLab.Analyze(D, s, c).PeakHeat;
            c.DieW *= 4; c.DieH *= 4;
            int k = 0;
            foreach (var p in c.Blocks) { p.X = (k % 2) * c.DieW / 2; p.Y = (k / 2) * c.DieH / 2; k++; }
            Assert.True(DesignLab.Analyze(D, s, c).PeakHeat < near);
        }

        [Fact]
        public void TimingClosureBuysClockWithEngineering()
        {
            var s = TestData.NewGame(year: 1976);
            var c = Chip(s, "n6um", "dip40c", "core8", "rom512", "memif", "io40");
            var a = DesignLab.Analyze(D, s, c);
            c.TimingEffort = 0.2;
            var b = DesignLab.Analyze(D, s, c);
            Assert.Equal(a.MaxClockMHz * 1.1, b.MaxClockMHz, 6);
            Assert.True(b.EngineerMonths > a.EngineerMonths * 1.25);
            c.TimingEffort = 0.5;
            Assert.Contains(DesignLab.Analyze(D, s, c).Notes, n => n.Severity == 2 && n.Text.Contains("Timing"));
        }

        [Fact]
        public void BlocksGiveFeatures()
        {
            var s = TestData.NewGame(year: 1978);
            var c = Chip(s, "n5um", "dip40c", "core8e", "rom2k", "keyscan", "lcd", "timer", "io40");
            var st = DesignLab.Analyze(D, s, c);
            Assert.Contains("keyboard", st.Features);
            Assert.Contains("display", st.Features);
            Assert.Contains("timers", st.Features);
            Assert.Contains("8-bit", st.Features);
            Assert.DoesNotContain("serial", st.Features);
        }

        [Fact]
        public void HarvestBinsSellDiesWithoutTheFpu()
        {
            var s = TestData.NewGame(year: 1982);
            var c = Chip(s, "n3um", "pga68", "core16", "fpu", "memif_wide", "io40");
            c.Stats = DesignLab.Analyze(D, s, c);
            Assert.True(c.Stats.Valid, c.Stats.FirstError);
            var all = Features.Of(D, c).ToList();
            c.Bins.Add(new BinDef { Name = "Full", MinCores = 1 });
            c.Bins.Add(new BinDef { Name = "NoFPU", MinCores = 1, Features = all.Where(f => f != "fpu").ToList() });
            var mix = Binning.Sample(D, new Rng(5), c, 6, 4000);
            Assert.True(mix["NoFPU"] > 0 && mix["Full"] > 0);
            Assert.True(Market.Perf(c, "NoFPU", Metric.Compute) < Market.Perf(c, "Full", Metric.Compute) * 0.8);
            Assert.DoesNotContain("fpu", Features.OfBin(D, c, "NoFPU"));
        }

        [Fact]
        public void DistributorsWantTheFeaturesTheirMarketInsistsOn()
        {
            var s = TestData.NewGame(year: 1975);
            var seg = D.Segment("embedded");
            var bare = Chip(s, "n6um", "dip40c", "core4", "rom512", "memif", "io40");
            bare.Stats = DesignLab.Analyze(D, s, bare);
            Assert.True(Market.Perf(bare, "Standard", Metric.Control) >= seg.NeedAt(s.YearF) * Market.OpenMarketMinShare);
            Assert.False(Market.Qualifies(D, seg, bare, "Standard", s.YearF));

            var planned = DesignLab.PlanForSegment(D, s, "n6um", Metric.Control, "P", maxPowerW: seg.MaxPowerW);
            planned.Stats = DesignLab.Analyze(D, s, planned);
            Assert.True(Market.Qualifies(D, seg, planned, "Standard", s.YearF));
            Assert.True(Market.FeatureFactor(D, seg, planned, "Standard", s.YearF) > Market.FeatureFactor(D, seg, bare, "Standard", s.YearF));
        }

        [Fact]
        public void BidsMustHaveTheFeaturesClockAndPinsAsked()
        {
            var s = TestData.NewGame(year: 1975);
            var c = DesignLab.PlanForSegment(D, s, "n6um", Metric.Control, "P", maxPowerW: 1);
            c.Id = s.NewId("chip");
            c.Stats = DesignLab.Analyze(D, s, c);
            c.Stage = DesignStage.Released;
            s.Designs.Add(c);
            var r = new Rfq { Id = "r", CustomerId = "brightline", SegmentId = "embedded", OpenMonth = s.MonthIndex, CloseMonth = s.MonthIndex + 2, MinMetric = 0.01, MaxPowerW = 5, MaxPrice = 100, ExpectedPrice = 10 };
            Assert.Null(Market.CheckBid(D, r, c, "Standard", 5));
            r.Features = new List<string> { "serial" };
            Assert.Contains("serial port", Market.CheckBid(D, r, c, "Standard", 5));
            r.Features.Clear();
            r.MinClockMHz = c.Stats.ClockMHz + 1;
            Assert.Contains("MHz", Market.CheckBid(D, r, c, "Standard", 5));
            r.MinClockMHz = 0;
            r.MaxPins = 16;
            Assert.Contains("pins", Market.CheckBid(D, r, c, "Standard", 5));
        }

        [Fact]
        public void CustomersAskForFeaturesClockAndPins()
        {
            var s = TestData.NewGame(year: 1980);
            for (int i = 0; i < 48 && !s.Rfqs.Any(r => r.CustomerId == "apex"); i++) Simulation.AdvanceMonths(D, s, 1);
            var rfq = s.Rfqs.First(r => r.CustomerId == "apex");
            Assert.Contains("8-bit", rfq.Features);
            Assert.Contains("dram", rfq.Features);
            Assert.True(rfq.MinClockMHz > 0);
            Assert.Equal(68, rfq.MaxPins);
        }

        [Fact]
        public void TheDesignHouseMeetsABrief()
        {
            var s = TestData.NewGame(year: 1977);
            var spec = new ChipSpec { Metric = Metric.Control, MaxPowerW = 1, MaxPins = 40, MinRomBytes = 4096, Features = new List<string> { "serial", "keyboard" } };
            var c = DesignLab.PlanForSpec(D, s, "n5um", spec, "B", out string why);
            Assert.True(c != null, why);
            c.Stats = DesignLab.Analyze(D, s, c);
            Assert.Empty(Specs.Check(D, spec, c, "Standard"));
            Assert.True(c.Stats.RomBytes >= 4096);
            Assert.Contains("serial", c.Stats.Features);

            spec.Features.Add("fpu");   // not invented until 1980
            Assert.Null(DesignLab.PlanForSpec(D, s, "n5um", spec, "B", out why));
            Assert.Contains("floating point", why);
        }

        [Fact]
        public void OldSavesKeepTheirChips()
        {
            var s = TestData.NewGame();
            var c = DesignLab.Starter(D, "Old", "n10um", "dip16p", "core4", "memif", "io16");
            c.Id = "chip1";
            s.Designs.Add(c);
            var json = SaveGame.Serialize(s).Replace("\"Pipeline\":1,", "").Replace("\"Scale\":1,", "");
            var loaded = SaveGame.Deserialize(json);
            Assert.All(loaded.Designs[0].Blocks, p => { Assert.Equal(1, p.Pipeline); Assert.Equal(1, p.Scale); Assert.Equal(CellLibrary.Standard, p.Cells); });
        }
    }
}
