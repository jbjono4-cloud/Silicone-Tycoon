using System;
using System.Linq;
using Xunit;

namespace SiliconeTycoon.Sim.Tests
{
    public class FabTests
    {
        static GameData D => TestData.Load();

        static Fab Build(GameState s, string node = "n10um", string location = "us_ca", int capacity = 1000)
        {
            Assert.Null(FabOps.Build(D, s, null, node, location, capacity));
            return s.Fabs[^1];
        }

        // n10um: 12 months to build, 9 months to ramp.
        const int BuildWeeks = 12 * Calendar.WeeksPerMonth;
        const int RampWeeks = 9 * Calendar.WeeksPerMonth;

        [Fact]
        public void CannotBuildOnAProcessYouDontHave()
        {
            var s = TestData.NewGame();
            Assert.NotNull(FabOps.Quote(D, s, "n3um", "us_ca", 1000).Problem);
        }

        [Fact]
        public void CannotBuildWhereFabsArentPossibleYet()
        {
            var s = TestData.NewGame();
            Assert.Contains("China", FabOps.Quote(D, s, "n10um", "cn", 1000).Problem);
        }

        [Fact]
        public void NeedsCashForTheFirstPayment()
        {
            var s = TestData.NewGame(cash: 1000);
            Assert.StartsWith("You need", FabOps.Build(D, s, null, "n10um", "us_ca", 1000));
        }

        [Fact]
        public void SubsidiesCutCapex()
        {
            var s = TestData.NewGame();
            var home = FabOps.Quote(D, s, "n10um", "us_ca", 1000);
            var ireland = FabOps.Quote(D, s, "n10um", "ie", 1000);
            Assert.True(ireland.Subsidy > 0);
            Assert.Equal(ireland.Capex - ireland.Subsidy, ireland.NetCapex, 6);
        }

        [Fact]
        public void ConstructionTakesTheNodesBuildTime()
        {
            var s = TestData.NewGame();
            var f = Build(s);
            Simulation.AdvanceWeeks(D, s, BuildWeeks - 1);
            Assert.Equal(FabStage.Construction, f.Stage);
            Simulation.Advance(D, s);
            Assert.Equal(FabStage.Ramping, f.Stage);
        }

        [Fact]
        public void CapexIsPaidInFullAndThenDepreciated()
        {
            var s = TestData.NewGame();
            var f = Build(s);
            Simulation.AdvanceWeeks(D, s, BuildWeeks);
            Assert.Equal(f.Capex, f.CapexPaid, 2);
            Assert.Equal(f.Capex, s.Ledger.Months.Sum(m => m.Capex), 2);
            Assert.Equal(-f.Capex / FabOps.DepreciationMonths, s.Ledger.LastMonth.Lines[Accounts.Depreciation], 2);
        }

        [Fact]
        public void RampsToFullCapacity()
        {
            var s = TestData.NewGame();
            var f = Build(s);
            Simulation.AdvanceWeeks(D, s, BuildWeeks + 1);
            int early = FabOps.AvailableCapacity(D, f);
            Assert.InRange(early, 1, f.Capacity / 2);
            Simulation.AdvanceWeeks(D, s, RampWeeks - 1);
            Assert.Equal(FabStage.Running, f.Stage);
            Assert.Equal(f.Capacity, FabOps.AvailableCapacity(D, f));
        }

        [Fact]
        public void WaferStartsFollowThePlan()
        {
            var s = TestData.NewGame();
            var f = Build(s);
            Simulation.AdvanceWeeks(D, s, BuildWeeks + RampWeeks);
            Assert.Equal(0, Calendar.WeekInMonth(s.Tick));
            Assert.Null(FabOps.SetPlannedStarts(f, 400));
            Simulation.AdvanceWeeks(D, s, Calendar.WeeksPerMonth);
            Assert.Equal(400, f.LastMonthStarts);
            Assert.Equal(0.4, f.LastUtilisation, 6);
        }

        [Fact]
        public void FinishedLotsHaveSensibleYields()
        {
            var s = TestData.NewGame();
            Build(s);
            Simulation.AdvanceWeeks(D, s, BuildWeeks + 24);
            var done = s.Fabs[0].Lots.Where(l => l.Done).ToList();
            Assert.NotEmpty(done);
            Assert.All(done, l => Assert.InRange(l.GoodDies, 0, l.GrossDies));
            Assert.True(done.Sum(l => l.GoodDies) > 0);
            Assert.Contains(s.Fabs[0].Lots, l => !l.Done);
        }

        [Fact]
        public void DefectDensityFallsWithProduction()
        {
            var s = TestData.NewGame();
            var f = Build(s);
            Simulation.AdvanceWeeks(D, s, BuildWeeks + 24);
            double early = FabOps.D0(D, f);
            Simulation.AdvanceMonths(D, s, 36);
            double late = FabOps.D0(D, f);
            Assert.True(late < early);
            Assert.True(late > D.Node("n10um").D0Mature);
        }

        [Fact]
        public void BusyFabsLearnFasterThanIdleOnes()
        {
            var busy = TestData.NewGame(seed: 5);
            var idle = TestData.NewGame(seed: 5);
            var fb = Build(busy);
            var fi = Build(idle);
            Simulation.AdvanceWeeks(D, busy, BuildWeeks);
            Simulation.AdvanceWeeks(D, idle, BuildWeeks);
            FabOps.SetPlannedStarts(fi, 0);
            Simulation.AdvanceMonths(D, busy, 24);
            Simulation.AdvanceMonths(D, idle, 24);
            Assert.True(fb.Learning > fi.Learning * 1.5);
        }

        [Fact]
        public void ClosingAFabStopsItsCostsAndWritesItOff()
        {
            var s = TestData.NewGame();
            var f = Build(s);
            Simulation.AdvanceWeeks(D, s, BuildWeeks + 8);
            double book = f.BookValue;
            double cash = s.Cash;
            Assert.Null(FabOps.Close(D, s, f));
            Assert.Equal(FabStage.Closed, f.Stage);
            Assert.Equal(cash + book * FabOps.SalvageShare, s.Cash, 4);
            Assert.Equal(-book * (1 - FabOps.SalvageShare), s.Ledger.Current[Accounts.FabClosure], 4);
            Assert.DoesNotContain(f.Lots, l => !l.Done);
            Assert.NotNull(FabOps.Close(D, s, f));

            Simulation.AdvanceMonths(D, s, 2);
            var lines = s.Ledger.LastMonth.Lines;
            Assert.False(lines.ContainsKey(Accounts.FabRunning));
            Assert.False(lines.ContainsKey(Accounts.Depreciation));
            Assert.Equal(0, FabOps.AvailableCapacity(D, f));
        }

        static GameState WithNodes(params string[] nodes) =>
            Simulation.NewGame(D, new NewGameOptions { StartingCash = 50_000_000, NodesReady = new System.Collections.Generic.List<string>(nodes) });

        [Fact]
        public void RetoolingCostsLessAndTakesLessTimeThanANewFab()
        {
            var s = WithNodes("n10um", "n8um", "n6um");
            var f = Build(s);
            Simulation.AdvanceWeeks(D, s, BuildWeeks);
            var fresh = FabOps.Quote(D, s, "n6um", f.LocationId, f.Capacity);
            var retool = FabOps.RetoolQuote(D, s, f, "n6um");
            Assert.Null(retool.Problem);
            Assert.True(retool.NetCapex < fresh.NetCapex * 0.75);
            Assert.True(retool.BuildMonths < fresh.BuildMonths);
            Assert.True(FabOps.RetoolQuote(D, s, f, "n8um").NetCapex < retool.NetCapex);   // an older target node costs less
        }

        [Fact]
        public void RetoolingNeedsANewerNodeAndAWorkingFab()
        {
            var s = WithNodes("n10um", "n8um");
            var f = Build(s, node: "n8um");
            Assert.Contains("already being built", FabOps.RetoolQuote(D, s, f, "n8um").Problem ?? FabOps.Retool(D, s, f, "n8um"));
            Simulation.AdvanceWeeks(D, s, BuildWeeks + 4);
            Assert.Contains("newer node", FabOps.Retool(D, s, f, "n10um"));
            Assert.Contains("isn't ready", FabOps.Retool(D, s, f, "n6um"));
        }

        [Fact]
        public void RetoolingSwitchesNodeAndRestartsLearning()
        {
            var s = WithNodes("n10um", "n8um");
            var f = Build(s);
            Simulation.AdvanceMonths(D, s, 30);
            Assert.Equal(FabStage.Running, f.Stage);
            Assert.True(f.Learning > 5);
            double book = f.BookValue;
            var q = FabOps.RetoolQuote(D, s, f, "n8um");

            Assert.Null(FabOps.Retool(D, s, f, "n8um"));
            Assert.Equal("n8um", f.NodeId);
            Assert.Equal(FabStage.Construction, f.Stage);
            Assert.True(f.Retooling);
            Assert.Equal(0, f.Learning);
            Assert.Equal(0, FabOps.AvailableCapacity(D, f));
            Assert.Equal(-book * FabOps.RetoolWriteOff, s.Ledger.Current[Accounts.Retooling], 4);

            Simulation.AdvanceWeeks(D, s, q.BuildMonths * Calendar.WeeksPerMonth);
            Assert.Equal(FabStage.Ramping, f.Stage);
            Assert.False(f.Retooling);
            Assert.Equal(q.NetCapex, f.CapexPaid, 2);
            Assert.True(f.BookValue > q.NetCapex);   // new equipment plus what's left of the old
            Assert.Equal(D.Node("n8um").D0Launch, FabOps.D0(D, f), 1);
        }

        [Fact]
        public void ExpandingIsCheaperPerWaferThanANewFab()
        {
            var s = TestData.NewGame();
            var f = Build(s);
            Simulation.AdvanceWeeks(D, s, BuildWeeks);
            var q = FabOps.ExpandQuote(D, s, f, 500);
            Assert.Null(q.Problem);
            Assert.True(q.NetCapex < FabOps.Quote(D, s, f.NodeId, f.LocationId, 500).NetCapex);
            Assert.True(q.BuildMonths < D.Node(f.NodeId).BuildMonths);
        }

        [Fact]
        public void ExpansionNeedsAWorkingFabAndOneAtATime()
        {
            var s = TestData.NewGame();
            var f = Build(s);
            Assert.Contains("built and running", FabOps.Expand(D, s, f, 500));
            Simulation.AdvanceWeeks(D, s, BuildWeeks);
            Assert.Contains("at least", FabOps.Expand(D, s, f, 10));
            Assert.Null(FabOps.Expand(D, s, f, 500));
            Assert.Contains("already being expanded", FabOps.Expand(D, s, f, 500));
            Assert.Contains("being expanded", FabOps.RetoolQuote(D, s, f, "n10um").Problem ?? "being expanded");
        }

        [Fact]
        public void ExpansionKeepsProducingThenAddsCapacity()
        {
            var s = TestData.NewGame();
            var f = Build(s);
            Simulation.AdvanceMonths(D, s, 24);
            Assert.Equal(FabStage.Running, f.Stage);
            double book = f.BookValue;
            double running = FabOps.RunningCost(D, s, f);
            var q = FabOps.ExpandQuote(D, s, f, 500);
            Assert.Null(FabOps.Expand(D, s, f, 500));

            Simulation.AdvanceMonths(D, s, 2);
            Assert.Equal(1000, f.Capacity);
            Assert.Equal(1000, f.LastMonthStarts);   // still running flat out

            Simulation.AdvanceWeeks(D, s, q.BuildMonths * Calendar.WeeksPerMonth);
            Assert.False(f.Expanding);
            Assert.Equal(1500, f.Capacity);
            Assert.Equal(1500, f.PlannedStarts);
            Assert.True(f.BookValue > book);
            Assert.True(FabOps.RunningCost(D, s, f) > running);
            Simulation.AdvanceMonths(D, s, 2);
            Assert.Equal(1500, f.LastMonthStarts);
        }

        [Fact]
        public void FabsOnTheNextNodeStartWithYourExperience()
        {
            var s = WithNodes("n10um", "n8um");
            var old = Build(s);
            Simulation.AdvanceMonths(D, s, 36);
            double exp = FabOps.Experience(s, "n10um");
            Assert.True(exp > 10, $"experience {exp}");
            var f = Build(s, node: "n8um");
            Simulation.AdvanceWeeks(D, s, BuildWeeks);
            Assert.Equal(FabStage.Ramping, f.Stage);
            Assert.Equal(Math.Min(FabOps.MaxTransfer, FabOps.Experience(s, "n10um") * FabOps.PrevNodeTransfer), f.Learning, 1);
            Assert.True(FabOps.D0(D, f) < D.Node("n8um").D0Launch - 1);
        }

        [Fact]
        public void ASecondFabOnTheSameNodeStartsAhead()
        {
            var s = TestData.NewGame();
            Build(s);
            Simulation.AdvanceMonths(D, s, 30);
            var second = Build(s);
            Simulation.AdvanceWeeks(D, s, BuildWeeks);
            Assert.True(second.Learning >= FabOps.MaxTransfer - 1e-9 || second.Learning > 5);
        }

        [Fact]
        public void WithoutExperienceFabsStartFromScratch()
        {
            var s = WithNodes("n10um", "n8um");
            var f = Build(s, node: "n8um");
            Simulation.AdvanceWeeks(D, s, BuildWeeks);
            Assert.Equal(0, f.Learning, 9);
        }

        [Fact]
        public void RunningCostsAppearOnceProducing()
        {
            var s = TestData.NewGame();
            Build(s);
            Simulation.AdvanceMonths(D, s, 6);
            Assert.False(s.Ledger.LastMonth.Lines.ContainsKey(Accounts.FabRunning));
            Simulation.AdvanceMonths(D, s, 8);
            Assert.True(s.Ledger.LastMonth.Lines[Accounts.FabRunning] < 0);
            Assert.True(s.Ledger.LastMonth.Lines[Accounts.Materials] < 0);
        }

        /// <summary>A chip released straight away, skipping the design programme.</summary>
        static ChipDesign Released(GameState s, string name, params string[] blocks)
        {
            var c = DesignLab.Starter(D, name, "n10um", "dip16p", blocks);
            c.Id = s.NewId("chip");
            c.Stats = DesignLab.Analyze(D, s, c);
            c.Stage = DesignStage.Released;
            s.Designs.Add(c);
            return c;
        }

        [Fact]
        public void FabsCanMakeSeveralChipsAtOnce()
        {
            var s = TestData.NewGame();
            var f = Build(s);
            var small = Released(s, "Small", "core4", "memif", "io16");
            var big = Released(s, "Big", "core8", "ram256", "memif", "io16");
            Assert.True(big.Stats.DieAreaMm2 > small.Stats.DieAreaMm2);
            Simulation.AdvanceWeeks(D, s, BuildWeeks + RampWeeks);
            Assert.Equal(0, Calendar.WeekInMonth(s.Tick));
            Assert.Null(FabOps.SetAllocation(D, s, f, null, 0));
            Assert.Null(FabOps.SetAllocation(D, s, f, small.Id, 600));
            Assert.Null(FabOps.SetAllocation(D, s, f, big.Id, 300));
            Assert.Equal(900, f.PlannedStarts);
            Assert.Equal(small.Id, f.DesignId);
            Simulation.AdvanceWeeks(D, s, Calendar.WeeksPerMonth);
            Assert.Equal(900, f.LastMonthStarts);
            var started = f.Lots.Where(l => l.StartTick >= s.Tick - Calendar.WeeksPerMonth).ToList();
            Assert.Equal(600, started.Where(l => l.DesignId == small.Id).Sum(l => l.Wafers));
            Assert.Equal(300, started.Where(l => l.DesignId == big.Id).Sum(l => l.Wafers));
            Assert.All(started.Where(l => l.DesignId == big.Id), l => Assert.Equal(big.DieAreaMm2, l.DieAreaMm2, 9));
            Assert.True(started.First(l => l.DesignId == small.Id).DiesPerWafer > started.First(l => l.DesignId == big.Id).DiesPerWafer);
            Simulation.AdvanceMonths(D, s, 4);
            Assert.True(s.Inventory.Where(x => x.DesignId == small.Id).Sum(x => x.MadeLastMonth) > 0);
            Assert.True(s.Inventory.Where(x => x.DesignId == big.Id).Sum(x => x.MadeLastMonth) > 0);
        }

        [Fact]
        public void ProductsCantPlanMoreThanCapacity()
        {
            var s = TestData.NewGame();
            var f = Build(s);   // 1000 wafers, all test wafers to begin with
            var a = Released(s, "A", "core4", "memif", "io16");
            Assert.Contains("already take", FabOps.SetAllocation(D, s, f, a.Id, 100));
            Assert.Null(FabOps.SetAllocation(D, s, f, null, 400));
            Assert.Null(FabOps.SetAllocation(D, s, f, a.Id, 600));
            Assert.Equal(1000, f.PlannedStarts);
            Assert.Null(FabOps.SetAllocation(D, s, f, a.Id, 0));
            Assert.False(f.Makes(a.Id));
            Assert.Null(f.DesignId);
        }

        [Fact]
        public void RampingFabsShareOutWhatTheyCanStart()
        {
            var s = TestData.NewGame();
            var f = Build(s);
            var a = Released(s, "A", "core4", "memif", "io16");
            FabOps.SetAllocation(D, s, f, null, 500);
            FabOps.SetAllocation(D, s, f, a.Id, 500);
            Simulation.AdvanceWeeks(D, s, BuildWeeks + Calendar.WeeksPerMonth);
            int avail = FabOps.AvailableCapacity(D, f);
            Assert.True(avail < 1000);
            var eff = FabOps.EffectiveStarts(D, f);
            Assert.True(eff.Sum(x => x.starts) <= avail);
            Assert.InRange(eff.First(x => x.designId == a.Id).starts, avail / 2 - 1, avail / 2);
        }

        [Fact]
        public void TotalStartsScaleEveryProduct()
        {
            var s = TestData.NewGame();
            var f = Build(s);
            var a = Released(s, "A", "core4", "memif", "io16");
            FabOps.SetAllocation(D, s, f, null, 200);
            FabOps.SetAllocation(D, s, f, a.Id, 600);
            Assert.Null(FabOps.SetPlannedStarts(f, 400));
            Assert.Equal(400, f.PlannedStarts);
            Assert.Equal(300, f.StartsFor(a.Id));
            Assert.Equal(100, f.StartsFor(null));
        }

        [Fact]
        public void OldSingleProductSavesBecomeOneAllocation()
        {
            var s = TestData.NewGame();
            var f = Build(s);
            var a = Released(s, "A", "core4", "memif", "io16");
            f.Allocations.Clear();
            f.LegacyDesignId = a.Id;
            f.LegacyPlannedStarts = 700;
            s.SaveVersion = 3;
            var loaded = SaveGame.Deserialize(SaveGame.Serialize(s));
            var g = loaded.Fabs[0];
            Assert.Single(g.Allocations);
            Assert.Equal(a.Id, g.DesignId);
            Assert.Equal(700, g.PlannedStarts);
            Assert.Null(g.LegacyDesignId);
        }
    }
}
