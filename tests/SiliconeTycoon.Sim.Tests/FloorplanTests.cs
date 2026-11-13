using System;
using System.Linq;
using Xunit;

namespace SiliconeTycoon.Sim.Tests
{
    public class FloorplanTests
    {
        static GameData D => TestData.Load();

        [Fact]
        public void AddedBlocksLandInAFreeSpot()
        {
            var s = TestData.NewGame();
            var c = DesignLab.Blank(D, s, "H", "n10um");
            c.DieW = 8;
            c.DieH = 8;
            var a = DesignLab.AddBlock(D, c, "core4");
            var b = DesignLab.AddBlock(D, c, "rom512");
            Assert.NotEqual(a.Id, b.Id);
            Assert.True(DesignLab.InsideDie(c, a) && DesignLab.InsideDie(c, b));
            Assert.False(DesignLab.Overlaps(a, b));
        }

        [Fact]
        public void BlocksThatDontFitGoBesideTheDie()
        {
            var s = TestData.NewGame();
            var c = DesignLab.Blank(D, s, "H", "n10um");
            c.DieW = 1;
            c.DieH = 1;
            var p = DesignLab.AddBlock(D, c, "core4");   // ~9.5 mm² on 10 µm
            Assert.False(DesignLab.InsideDie(c, p));
            Assert.Contains(DesignLab.Analyze(D, s, c).Notes, n => n.Severity == 2 && n.Text.Contains("off the edge"));
        }

        [Fact]
        public void ReshapingKeepsAreaAndCentre()
        {
            var p = new PlacedBlock { X = 1, Y = 1, Aspect = 1 };
            DesignLab.Shape(p, 4);
            DesignLab.SetAspect(p, 4);
            Assert.Equal(4, p.W * p.H, 9);
            Assert.Equal(4, p.W / p.H, 9);
            Assert.Equal(2, p.X + p.W / 2, 3);
            Assert.Equal(2, p.Y + p.H / 2, 3);
        }

        [Fact]
        public void FittingTheDieTrimsItToTheBlocks()
        {
            var s = TestData.NewGame();
            var c = DesignLab.Starter(D, "T", "n10um", "dip16p", "core4", "memif", "io16");
            foreach (var p in c.Blocks) { p.X += 1; p.Y += 2; }
            c.DieW += 5;
            DesignLab.FitDie(c);
            Assert.Equal(0, c.Blocks.Min(p => p.X), 9);
            Assert.Equal(0, c.Blocks.Min(p => p.Y), 9);
            Assert.True(c.Blocks.Max(p => p.X + p.W) <= c.DieW + 1e-9 && c.DieW - c.Blocks.Max(p => p.X + p.W) < 0.011);
        }

        [Fact]
        public void HandLayoutBeatsTheDesignHouse()
        {
            var s = TestData.NewGame();
            var house = DesignLab.Starter(D, "T", "n10um", "dip16p", "core4", "memif", "io16");
            var mine = DesignLab.CopyAsNewChip(D, house, "Mine");
            mine.UseDesignHouse = false;
            var a = DesignLab.Analyze(D, s, house);
            var b = DesignLab.Analyze(D, s, mine);
            Assert.True(a.Valid && b.Valid);
            Assert.True(b.MaxClockMHz > a.MaxClockMHz * 1.04);
            Assert.Equal(0, b.DesignHouseFee);
            Assert.True(b.EngineerMonths > a.EngineerMonths);   // but your team does all of the work
        }

        [Fact]
        public void CopiesStartFreshAndShrinkOnANewerNode()
        {
            var s = TestData.NewGame();
            var src = DesignLab.Starter(D, "Old", "n10um", "dip16p", "core4", "memif", "io16");
            src.Id = "chip1";
            src.Stage = DesignStage.Released;
            src.Stepping = 2;
            src.FoundBugs = 3;
            var c = DesignLab.CopyAsNewChip(D, src, "New", "n8um");
            Assert.Null(c.Id);
            Assert.Equal(DesignStage.Draft, c.Stage);
            Assert.Equal(0, c.Stepping);
            Assert.Equal(0, c.FoundBugs);
            Assert.Equal("n8um", c.NodeId);
            Assert.True(c.Blocks.Sum(p => p.W * p.H) < src.Blocks.Sum(p => p.W * p.H) * 0.9);
            Assert.Equal(src.Blocks[0].X, c.Blocks[0].X, 9);   // positions kept: a shrink leaves gaps to tighten
            Assert.NotSame(src.Blocks[0], c.Blocks[0]);
        }

        [Fact]
        public void BinsSortDiesByWhatWorks()
        {
            var s = TestData.NewGame();
            var c = DesignLab.Starter(D, "T", "n10um", "dip16p", "core4", "memif", "io16");
            c.Stats = DesignLab.Analyze(D, s, c);
            double typical = c.Stats.MaxClockMHz;
            c.Bins.Add(new BinDef { Name = "Fast", MinCores = 1, MinClockMHz = typical * 1.05 });
            c.Bins.Add(new BinDef { Name = "Slow", MinCores = 1, MinClockMHz = typical * 0.8 });
            var mix = Binning.Sample(D, new Rng(3), c, 2.5, 4000);
            Assert.True(mix["Fast"] > 0 && mix["Slow"] > mix["Fast"]);
            Assert.Equal(4000, mix.Values.Sum());
        }
    }

    public class WaferMapTests
    {
        static GameData D => TestData.Load();

        [Fact]
        public void WholeDiesFitRoughlyAsTheFormulaSays()
        {
            var n = D.Node("n5um");
            var grid = WaferMap.Grid(n.WaferMm, 5, 5);
            int formula = YieldModel.DiesPerWafer(n.WaferMm, YieldModel.WithScribe(25));
            Assert.InRange(grid.Count, formula * 0.85, formula * 1.15);
            double r = n.WaferMm / 2 - YieldModel.EdgeExclusionMm;
            Assert.All(grid, g => Assert.True(Math.Sqrt(Math.Pow(Math.Max(Math.Abs(g.x), Math.Abs(g.x + 5)), 2) + Math.Pow(Math.Max(Math.Abs(g.y), Math.Abs(g.y + 5)), 2)) <= r + 1e-9));
        }

        [Fact]
        public void TestWafersYieldAsTheFormulaSays()
        {
            var n = D.Node("n5um");
            double good = 0, dies = 0;
            for (ulong seed = 1; seed <= 20; seed++)
            {
                var m = WaferMap.Simulate(D, n, null, 5, seed);
                Assert.Equal(m.Dies.Count, m.Counts.Values.Sum());
                good += m.Counts[WaferMap.TestGood];
                dies += m.Dies.Count;
            }
            Assert.InRange(good / dies, YieldModel.Yield(5, 10) - 0.04, YieldModel.Yield(5, 10) + 0.04);
        }

        [Fact]
        public void ChipWafersBinAndRecordDefects()
        {
            var s = TestData.NewGame();
            var c = DesignLab.Starter(D, "T", "n10um", "dip16p", "core4", "ram64", "memif", "io16");
            c.Stats = DesignLab.Analyze(D, s, c);
            var m = WaferMap.Simulate(D, D.Node("n10um"), c, 8, 42);
            Assert.Equal(new[] { "Standard", Binning.Scrap }, m.BinOrder);
            Assert.True(m.Counts[Binning.Scrap] > 0 && m.Counts["Standard"] > 0);
            Assert.Contains(m.Dies, x => x.Defects.Any(f => f.Effect == Binning.DefectEffect.Fatal));
            Assert.All(m.Dies.Where(x => x.Defects.Any(f => f.Effect == Binning.DefectEffect.Fatal)), x => Assert.Equal(Binning.Scrap, x.Bin));
            var again = WaferMap.Simulate(D, D.Node("n10um"), c, 8, 42);
            Assert.Equal(m.Counts, again.Counts);
        }

        [Fact]
        public void LookingAtAWaferDoesntChangeTheGame()
        {
            var s = TestData.NewGame();
            var before = SaveGame.Serialize(s);
            WaferMap.Simulate(D, D.Node("n10um"), null, 5, WaferMap.Hash("f1|x"));
            Assert.Equal(before, SaveGame.Serialize(s));
            Assert.Equal(WaferMap.Hash("abc"), WaferMap.Hash("abc"));
            Assert.NotEqual(WaferMap.Hash("abc"), WaferMap.Hash("abd"));
        }
    }

    public class SurplusStockTests
    {
        static GameData D => TestData.Load();

        static ChipDesign Chip(GameState s)
        {
            var c = DesignLab.PlanForSegment(D, s, "n10um", Metric.Control, "S-1", maxPowerW: 1.0);
            c.Id = s.NewId("chip");
            c.Stats = DesignLab.Analyze(D, s, c);
            c.Stage = DesignStage.Released;
            c.ReleasedTick = s.Tick;
            s.Designs.Add(c);
            return c;
        }

        [Fact]
        public void StockCostsMoneyToKeep()
        {
            var s = TestData.NewGame();
            var c = Chip(s);
            Market.AddStock(s, c.Id, "Standard", 1_000_000);
            Assert.True(Market.CarryingCost(s, c.Id) > 0);
            Simulation.AdvanceMonths(D, s, 1);
            Assert.True(s.Ledger.LastMonth.Lines[Accounts.Warehousing] < 0);
        }

        [Fact]
        public void BrokersPayLittleAndLessForBigDumps()
        {
            var s = TestData.NewGame();
            var c = Chip(s);
            var seg = D.Segment("embedded");
            Market.AddStock(s, c.Id, "Standard", 1000);
            double small = Market.BrokerPrice(D, s, Market.StockOf(s, c.Id, "Standard"));
            Assert.True(small > 0 && small <= Market.BrokerShare * Market.ExpectedPrice(seg, s.YearF) + 1e-9);
            Market.AddStock(s, c.Id, "Standard", 50_000_000);
            double big = Market.BrokerPrice(D, s, Market.StockOf(s, c.Id, "Standard"));
            Assert.True(big < small * 0.5);
        }

        [Fact]
        public void SellingToABrokerEmptiesTheWarehouseForCash()
        {
            var s = TestData.NewGame();
            var c = Chip(s);
            Market.AddStock(s, c.Id, "Standard", 200_000);
            Market.SetListing(s, c.Id, "Standard", 5);
            double quote = Market.BrokerQuote(D, s, c.Id);
            double cash = s.Cash;
            Assert.Null(Market.SellToBroker(D, s, c.Id));
            Assert.Equal(0, Market.Stock(s, c.Id, "Standard"), 6);
            Assert.Empty(s.Listings);
            Assert.Equal(cash + quote, s.Cash, 3);
            Assert.NotNull(Market.SellToBroker(D, s, c.Id));
        }

        [Fact]
        public void ScrappingIsFreeButEarnsNothing()
        {
            var s = TestData.NewGame();
            var c = Chip(s);
            Market.AddStock(s, c.Id, "Standard", 200_000);
            double cash = s.Cash;
            Assert.Null(Market.ScrapStock(s, c.Id));
            Assert.Equal(0, Market.Stock(s, c.Id, "Standard"), 6);
            Assert.Equal(cash, s.Cash, 6);
            Assert.Equal(0, Market.CarryingCost(s, c.Id), 9);
        }
    }

    public class FabNamingTests
    {
        static GameData D => TestData.Load();

        static GameState WithNodes(params string[] nodes) =>
            Simulation.NewGame(D, new NewGameOptions { StartingCash = 50_000_000, NodesReady = new System.Collections.Generic.List<string>(nodes) });

        static Fab Running(GameState s, string name = null)
        {
            Assert.Null(FabOps.Build(D, s, name, "n10um", "us_ca", 1000));
            var f = s.Fabs.Last();
            while (!f.Producing) Simulation.Advance(D, s);
            return f;
        }

        [Fact]
        public void RetoolingRenamesDefaultNamedFabs()
        {
            var s = WithNodes("n10um", "n8um");
            var f = Running(s);
            Assert.Contains("10 µm", f.Name);
            Assert.Null(FabOps.Retool(D, s, f, "n8um"));
            Assert.Contains("8 µm", f.Name);
            Assert.DoesNotContain("10 µm", f.Name);
        }

        [Fact]
        public void RetoolingKeepsNamesYouChose()
        {
            var s = WithNodes("n10um", "n8um");
            var f = Running(s, "Fab One");
            Assert.Null(FabOps.Retool(D, s, f, "n8um"));
            Assert.Equal("Fab One", f.Name);
        }

        [Fact]
        public void FabsCanBeRenamed()
        {
            var s = WithNodes("n10um");
            var a = Running(s, "A");
            var b = Running(s, "B");
            Assert.Null(FabOps.Rename(s, a, "  Alpha "));
            Assert.Equal("Alpha", a.Name);
            Assert.NotNull(FabOps.Rename(s, b, "Alpha"));
            Assert.NotNull(FabOps.Rename(s, b, " "));
        }

        [Fact]
        public void WarnsWhenAFabHasNoChipForItsNode()
        {
            var s = WithNodes("n10um");
            Running(s);
            Assert.Contains(Advisor.Alerts(D, s), a => a.Text.Contains("Chips only run on the node"));
        }
    }
}
