using System;
using System.Linq;
using Xunit;

namespace SiliconeTycoon.Sim.Tests
{
    public class EventsTests
    {
        static GameData D => TestData.Load();

        static GameState Game(double rate = 1, int year = 1970, ulong seed = 1) =>
            Simulation.NewGame(D, new NewGameOptions { EventRate = rate, StartYear = year, Seed = seed, StartingCash = 50_000_000 });

        static EventDef Def(string id) => D.Events.First(e => e.Id == id);

        static Fab RunningFab(GameState s, string location = "us_ca")
        {
            Assert.Null(FabOps.Build(D, s, null, "n10um", location, 1000));
            var f = s.Fabs.Last();
            while (f.Stage != FabStage.Running) Simulation.Advance(D, s);
            return f;
        }

        [Fact]
        public void TheEventDataLoads()
        {
            Assert.NotEmpty(D.Events);
            Assert.Contains(D.Events, e => e.Scripted);
            Assert.Contains(D.Events, e => !e.Scripted);
            Assert.Empty(D.Validate());
        }

        /// <summary>The real data with only the scripted (historical) events, so random ones can't muddy a test.</summary>
        static GameData ScriptedOnly() => new GameData
        {
            Nodes = D.Nodes, Locations = D.Locations, Blocks = D.Blocks, Packages = D.Packages, Segments = D.Segments,
            Customers = D.Customers, Rivals = D.Rivals, Events = D.Events.Where(e => e.Scripted).ToList(),
        };

        [Fact]
        public void TheOilCrisisHitsDemandOnSchedule()
        {
            var d = ScriptedOnly();
            var s = Simulation.NewGame(d, new NewGameOptions { EventRate = 1, StartYear = 1973, StartingCash = 50_000_000 });
            var crisis = Def("oil_crisis");
            int when = (crisis.Year - Calendar.StartYear) * 12 + crisis.Month - 1;
            while (s.MonthIndex <= when) Simulation.AdvanceMonths(d, s, 1);
            Assert.Contains("oil_crisis", s.FiredEvents);
            Assert.Equal(crisis.Factor, Events.DemandFactor(s, "embedded"), 9);
            var rec = s.MarketHistory.Last(r => r.SegmentId == "embedded");
            Assert.Equal(D.Segment("embedded").DemandAt(Calendar.YearF(rec.Month * Calendar.WeeksPerMonth)) * crisis.Factor, rec.Demand, 0);
            Assert.True(Events.MaterialsFactor(s) > 1);
            Simulation.AdvanceMonths(d, s, crisis.Months + 1);
            Assert.DoesNotContain(s.Effects, e => e.EventId == "oil_crisis");
        }

        [Fact]
        public void HistoryBeforeTheStartDoesntFire()
        {
            var s = Game(year: 1976);
            Assert.Contains("oil_crisis", s.FiredEvents);
            Simulation.AdvanceMonths(D, s, 2);
            Assert.DoesNotContain(s.Effects, e => e.EventId == "oil_crisis");
        }

        [Fact]
        public void NoEventsWhenSwitchedOff()
        {
            var s = Game(rate: 0, year: 1973);
            Simulation.AdvanceMonths(D, s, 36);
            Assert.Empty(s.Effects);
            Assert.Empty(s.Decisions);
            Assert.DoesNotContain(s.News, n => n.Kind == NewsKinds.Event || n.Kind == NewsKinds.Decision);
        }

        [Fact]
        public void RandomEventsHappenOverTheYears()
        {
            var s = Game(rate: 1.6, seed: 7);
            RunningFab(s, "jp");
            Simulation.AdvanceMonths(D, s, 12 * 10);
            Assert.True(s.News.Count(n => n.Kind == NewsKinds.Event || n.Kind == NewsKinds.Decision) >= 5);
        }

        [Fact]
        public void EarthquakesStopTheFabAndRushRepairsCostMore()
        {
            var s = Game(rate: 0);
            var f = RunningFab(s, "jp");
            Assert.True(Events.Fire(D, s, Def("quake"), f.Id));
            Assert.True(f.OutageWeeks > 0);
            Assert.Equal(0, FabOps.AvailableCapacity(D, f));
            var x = Assert.Single(s.Decisions);
            Assert.Equal(2, x.Options.Count);
            int weeks = f.OutageWeeks;
            double cash = s.Cash;
            Assert.Null(Events.Choose(D, s, x.Id, 1));
            Assert.Equal(cash - x.Cost * 2, s.Cash, 3);
            Assert.Equal(Math.Max(1, (weeks + 1) / 2), f.OutageWeeks);
            Assert.Empty(s.Decisions);
            Simulation.AdvanceWeeks(D, s, f.OutageWeeks + 1);
            Assert.Equal(f.Capacity, FabOps.AvailableCapacity(D, f));
        }

        [Fact]
        public void FiresLoseEveryLotInTheLine()
        {
            var s = Game(rate: 0);
            var f = RunningFab(s);
            Assert.Contains(f.Lots, l => !l.Done);
            double cash = s.Cash;
            Assert.True(Events.Fire(D, s, Def("fire"), f.Id));
            Assert.DoesNotContain(f.Lots, l => !l.Done);
            Assert.True(s.Cash < cash);
            Assert.True(f.OutageWeeks >= Def("fire").MinWeeks);
        }

        [Fact]
        public void ContaminationRaisesDefectsUntilCleaned()
        {
            var s = Game(rate: 0);
            var f = RunningFab(s);
            double before = FabOps.D0(D, f);
            Events.Fire(D, s, Def("contamination"), f.Id);
            Assert.True(FabOps.D0(D, f) > before * 1.5);
            var x = Assert.Single(s.Decisions);
            Assert.Null(Events.Choose(D, s, x.Id, 0));   // shut down and clean
            Assert.Equal(0, FabOps.AvailableCapacity(D, f));
            Simulation.AdvanceWeeks(D, s, Events.CleanWeeks + 1);
            Assert.True(FabOps.D0(D, f) <= before * 1.01);
            Assert.Equal(f.Capacity, FabOps.AvailableCapacity(D, f));
        }

        [Fact]
        public void UndecidedEventsTakeTheDefault()
        {
            var s = Game(rate: 0);
            var f = RunningFab(s);
            Events.Fire(D, s, Def("strike"), f.Id);
            var x = Assert.Single(s.Decisions);
            Assert.True(FabOps.AvailableCapacity(D, f) < f.Capacity);
            double rep = s.Rep;
            Simulation.AdvanceWeeks(D, s, Events.DecisionWeeks + 1);
            Assert.Empty(s.Decisions);
            Assert.True(s.Rep < rep + 0.05);   // held out (the default): a small reputation cost, not a settlement
            Assert.Contains(s.News, n => n.Text.Contains("Nobody decided in time"));
        }

        [Fact]
        public void SettlingAStrikeGetsTheFabBack()
        {
            var s = Game(rate: 0);
            var f = RunningFab(s);
            Events.Fire(D, s, Def("strike"), f.Id);
            var x = s.Decisions.Single();
            Assert.Null(Events.Choose(D, s, x.Id, 0));
            Simulation.AdvanceWeeks(D, s, 2);
            Assert.Equal(f.Capacity, FabOps.AvailableCapacity(D, f));
        }

        static ChipDesign ReleasedWithBugs(GameState s, int bugs)
        {
            var c = DesignLab.PlanForSegment(D, s, "n10um", Metric.Control, "E-1", maxPowerW: 1);
            c.Id = s.NewId("chip");
            c.Stats = DesignLab.Analyze(D, s, c);
            c.Stage = DesignStage.Released;
            c.EscapedBugs = bugs;
            s.Designs.Add(c);
            return c;
        }

        [Fact]
        public void ErrataSurfaceEscapedBugs()
        {
            var s = Game(rate: 0);
            var c = ReleasedWithBugs(s, 40);
            int decisions = 0, minor = 0;
            for (int i = 0; i < 40; i++)
            {
                int before = s.Decisions.Count;
                Assert.True(Events.Fire(D, s, Def("errata"), c.Id));
                if (s.Decisions.Count > before) { decisions++; Events.Choose(D, s, s.Decisions.Last().Id, 1); }
                else minor++;
            }
            Assert.Equal(0, c.EscapedBugs);
            Assert.True(decisions > 0 && minor > 0);
            Assert.False(Events.Fire(D, s, Def("errata"), c.Id));
        }

        [Fact]
        public void RecallsCostMoneyErrataCostReputation()
        {
            var s = Game(rate: 0);
            var c = ReleasedWithBugs(s, 100);
            Decision Next()
            {
                while (s.Decisions.Count == 0) Events.Fire(D, s, Def("errata"), c.Id);
                return s.Decisions[0];
            }
            var a = Next();
            double cash = s.Cash, rep = s.Rep;
            Events.Choose(D, s, a.Id, 0);
            Assert.Equal(cash - a.Cost, s.Cash, 3);
            Assert.True(rep - s.Rep < 0.02);
            var b = Next();
            cash = s.Cash; rep = s.Rep;
            Events.Choose(D, s, b.Id, 1);
            Assert.Equal(cash, s.Cash, 6);
            Assert.True(rep - s.Rep >= 0.04);
        }

        [Fact]
        public void PoachedEngineersSlowTheChip()
        {
            var s = Game(rate: 0);
            var t = s.DesignTeams[0];
            DesignOps.SetEngineers(s, t, 20);
            var c = DesignLab.Starter(D, "P-1", "n10um", "dip16p", "core4", "memif", "io16");
            Assert.Null(DesignOps.StartDesign(D, s, c, t.Id));
            int weeks = c.WeeksLeft;
            Assert.True(Events.Fire(D, s, Def("poaching"), t.Id));
            var x = s.Decisions.Single();
            Events.Choose(D, s, x.Id, 1);
            Assert.Equal(20 - (int)x.Value, t.Engineers);
            Assert.True(c.WeeksLeft > weeks);
        }

        [Fact]
        public void GrantsRaiseTheSubsidy()
        {
            var s = Game(rate: 0);
            Assert.True(Events.Fire(D, s, Def("grant"), null));
            var e = s.Effects.Single(x => x.Kind == "grant");
            var loc = D.Location(e.Target);
            var q = FabOps.Quote(D, s, "n10um", loc.Id, 1000);
            Assert.Equal(Math.Min(0.6, loc.Subsidy + e.Factor), q.Subsidy / q.Capex, 6);
        }

        [Fact]
        public void MaterialShortagesRaiseWaferCosts()
        {
            var s = Game(rate: 0);
            var f = RunningFab(s);
            double before = FabOps.WaferCost(D, s, f);
            Assert.True(Events.Fire(D, s, Def("materials"), null));
            Assert.Equal(before * Def("materials").Factor, FabOps.WaferCost(D, s, f), 6);
        }

        [Fact]
        public void RivalSetbacksCutTheirCapacity()
        {
            var s = Game(rate: 0, year: 1975);
            var r = s.Rivals[0];
            double cap = r.Capacity;
            Assert.True(Events.Fire(D, s, Def("rival_setback"), r.Id));
            Assert.True(r.Capacity < cap);
            Assert.True(Events.Fire(D, s, Def("rival_price_war"), r.Id));
            Assert.True(r.PriceWar);
        }

        [Fact]
        public void EventsSurviveASave()
        {
            var s = Game(rate: 1, year: 1973);
            var f = RunningFab(s, "jp");
            Events.Fire(D, s, Def("quake"), f.Id);
            Simulation.AdvanceMonths(D, s, 1);
            var b = SaveGame.Deserialize(SaveGame.Serialize(s));
            Assert.Equal(s.Decisions.Count, b.Decisions.Count);
            Assert.Equal(s.Effects.Count, b.Effects.Count);
            Assert.Equal(s.Fabs[0].OutageWeeks, b.Fabs[0].OutageWeeks);
            Simulation.AdvanceMonths(D, s, 12);
            Simulation.AdvanceMonths(D, b, 12);
            Assert.Equal(SaveGame.Serialize(s), SaveGame.Serialize(b));
        }

        [Fact]
        public void FabsKeepAMonthlyHistory()
        {
            var s = Game(rate: 0);
            var f = RunningFab(s);
            Simulation.AdvanceMonths(D, s, 6);
            Assert.True(f.History.Count >= 6);
            Assert.True(f.History.Last().Good > 0);
            Assert.True(f.History.Last().D0 > 0);
        }
    }
}
