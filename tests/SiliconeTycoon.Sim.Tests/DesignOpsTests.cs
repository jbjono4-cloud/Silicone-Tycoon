using System.Linq;
using Xunit;

namespace SiliconeTycoon.Sim.Tests
{
    public class DesignOpsTests
    {
        static GameData D => TestData.Load();

        static ChipDesign Chip(string name = "T-1") => DesignLab.Starter(D, name, "n10um", "dip16p", "core4", "memif", "io16");

        /// <summary>Runs the game until the chip reaches a stage (or gives up after five years).</summary>
        static void RunUntil(GameState s, ChipDesign c, DesignStage stage)
        {
            for (int i = 0; i < 5 * Calendar.WeeksPerYear && c.Stage != stage; i++) Simulation.Advance(D, s);
            Assert.Equal(stage, c.Stage);
        }

        [Fact]
        public void NewGamesHaveADesignTeam()
        {
            var s = TestData.NewGame();
            Assert.Single(s.DesignTeams);
            Assert.True(s.DesignTeams[0].Engineers > 0);
        }

        [Fact]
        public void TeamsWorkOnOneChipAtATime()
        {
            var s = TestData.NewGame();
            var team = s.DesignTeams[0];
            Assert.NotNull(DesignOps.StartDesign(D, s, Chip(), "nope"));
            Assert.Null(DesignOps.StartDesign(D, s, Chip("A"), team.Id));
            Assert.Contains("busy", DesignOps.StartDesign(D, s, Chip("B"), team.Id));
        }

        [Fact]
        public void InvalidDesignsDontStart()
        {
            var s = TestData.NewGame();
            var c = Chip();
            c.TargetClockMHz = 5;   // far too hot for a plastic package
            Assert.NotNull(DesignOps.StartDesign(D, s, c, s.DesignTeams[0].Id));
            Assert.Equal(DesignStage.Draft, c.Stage);
        }

        [Fact]
        public void DesignHouseIsPaidUpFront()
        {
            var s = TestData.NewGame();
            var c = Chip();
            double before = s.Cash;
            Assert.Null(DesignOps.StartDesign(D, s, c, s.DesignTeams[0].Id));
            Assert.True(c.Stats.DesignHouseFee > 0);
            Assert.Equal(before - c.Stats.DesignHouseFee, s.Cash, 6);
        }

        [Fact]
        public void BiggerTeamsFinishSooner()
        {
            var small = TestData.NewGame();
            var big = TestData.NewGame();
            DesignOps.SetEngineers(big, big.DesignTeams[0], 12);
            var a = Chip();
            var b = Chip();
            DesignOps.StartDesign(D, small, a, small.DesignTeams[0].Id);
            DesignOps.StartDesign(D, big, b, big.DesignTeams[0].Id);
            Assert.True(b.WeeksLeft < a.WeeksLeft / 2);
        }

        [Fact]
        public void TapeOutPaysForMasksThenSiliconArrives()
        {
            var s = TestData.NewGame();
            var c = Chip();
            DesignOps.StartDesign(D, s, c, s.DesignTeams[0].Id);
            Simulation.AdvanceWeeks(D, s, c.WeeksLeft - 1);
            Assert.Equal(DesignStage.Designing, c.Stage);
            Simulation.Advance(D, s);
            Assert.Equal(DesignStage.Silicon, c.Stage);
            double inflationAtTapeOut = Calendar.Inflation(Calendar.YearF(s.Tick - 1));   // the week that just ran
            Assert.Equal(D.Node("n10um").MaskSetCost * inflationAtTapeOut, c.MaskSpend, 2);
            RunUntil(s, c, DesignStage.Validated);
        }

        [Fact]
        public void VerificationMeansFewerBugs()
        {
            var s = TestData.NewGame();
            var c = Chip();
            var st = DesignLab.Analyze(D, s, c);
            c.Verification = 0.1;
            double sloppy = DesignOps.ExpectedBugs(c, st);
            c.Verification = 0.6;
            Assert.True(DesignOps.ExpectedBugs(c, st) < sloppy / 2);
        }

        [Fact]
        public void ShowstoppersNeedASteppingBeforeRelease()
        {
            for (ulong seed = 1; seed <= 200; seed++)
            {
                var s = TestData.NewGame(seed: seed);
                var c = Chip();
                c.Verification = 0.1;
                DesignOps.StartDesign(D, s, c, s.DesignTeams[0].Id);
                RunUntil(s, c, DesignStage.Validated);
                if (c.Showstoppers == 0) continue;

                Assert.NotNull(DesignOps.Release(D, s, c));
                for (int i = 0; i < 10 && c.Stage == DesignStage.Validated && c.Showstoppers > 0; i++)
                {
                    Assert.Null(DesignOps.Step(D, s, c));
                    Assert.Equal(DesignStage.Designing, c.Stage);
                    RunUntil(s, c, DesignStage.Validated);
                }
                Assert.True(c.Stepping >= 1);
                Assert.Null(DesignOps.Release(D, s, c));
                Assert.Equal(DesignStage.Released, c.Stage);
                Assert.Null(s.DesignTeams[0].ProjectId);
                return;
            }
            Assert.Fail("No seed produced a showstopper");
        }

        [Fact]
        public void FabsMakeAndBinReleasedChips()
        {
            var s = TestData.NewGame();
            var c = Chip();
            Assert.Null(FabOps.Build(D, s, null, "n10um", "us_ca", 500));
            var f = s.Fabs[0];
            DesignOps.StartDesign(D, s, c, s.DesignTeams[0].Id);
            RunUntil(s, c, DesignStage.Validated);
            if (c.Showstoppers > 0)
            {
                while (c.Showstoppers > 0)
                {
                    DesignOps.Step(D, s, c);
                    RunUntil(s, c, DesignStage.Validated);
                }
            }
            Assert.NotNull(FabOps.AssignDesign(D, s, f, c.Id));   // not released yet
            Assert.Null(DesignOps.Release(D, s, c));
            while (!f.Producing) Simulation.Advance(D, s);
            Assert.Null(FabOps.AssignDesign(D, s, f, c.Id));
            Assert.Equal(c.DieAreaMm2, f.DieAreaMm2, 9);

            Simulation.AdvanceMonths(D, s, 4);
            var lots = f.Lots.Where(l => l.Done && l.DesignId == c.Id).ToList();
            Assert.NotEmpty(lots);
            Assert.All(lots, l =>
            {
                Assert.NotNull(l.Bins);
                Assert.Equal(l.GrossDies, l.Bins.Values.Sum());
                Assert.Equal(l.GrossDies - l.Bins[Binning.Scrap], l.GoodDies);
            });
        }

        [Fact]
        public void ChipsMustMatchTheFabsNode()
        {
            var s = Simulation.NewGame(D, new NewGameOptions { StartingCash = 20_000_000, NodesReady = new System.Collections.Generic.List<string> { "n10um", "n8um" } });
            Assert.Null(FabOps.Build(D, s, null, "n8um", "us_ca", 500));
            var c = Chip();
            DesignOps.StartDesign(D, s, c, s.DesignTeams[0].Id);
            RunUntil(s, c, DesignStage.Validated);
            while (c.Showstoppers > 0)
            {
                DesignOps.Step(D, s, c);
                RunUntil(s, c, DesignStage.Validated);
            }
            DesignOps.Release(D, s, c);
            Assert.Contains("laid out for", FabOps.AssignDesign(D, s, s.Fabs[0], c.Id));
        }

        [Fact]
        public void SavesKeepChipProjectsDeterministic()
        {
            var a = TestData.NewGame(seed: 21);
            DesignOps.StartDesign(D, a, Chip(), a.DesignTeams[0].Id);
            FabOps.Build(D, a, null, "n10um", "us_ca", 500);
            Simulation.AdvanceMonths(D, a, 6);
            var b = SaveGame.Deserialize(SaveGame.Serialize(a));
            Simulation.AdvanceMonths(D, a, 18);
            Simulation.AdvanceMonths(D, b, 18);
            Assert.Equal(SaveGame.Serialize(a), SaveGame.Serialize(b));
        }
    }
}
