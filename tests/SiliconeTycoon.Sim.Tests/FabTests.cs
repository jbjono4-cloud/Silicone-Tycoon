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
    }
}
