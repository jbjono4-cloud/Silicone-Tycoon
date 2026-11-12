using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SiliconeTycoon.Sim.Tests
{
    public class ProcessResearchTests
    {
        static GameData D => TestData.Load();

        [Fact]
        public void NeedsThePreviousNode()
        {
            var s = TestData.NewGame();
            Assert.Contains("5 µm", ProcessResearch.CanStart(D, s, "n3um", 1.0));
        }

        [Fact]
        public void CannotJumpTooFarAheadOfHistory()
        {
            var s = Simulation.NewGame(D, new NewGameOptions { NodesReady = new List<string> { "n10um", "n8um", "n6um" } });
            Assert.Contains("before 1973", ProcessResearch.CanStart(D, s, "n5um", 1.0));
        }

        [Fact]
        public void RejectsOutOfRangeBudgets()
        {
            var s = TestData.NewGame();
            Assert.NotNull(ProcessResearch.CanStart(D, s, "n8um", 0.1));
            Assert.NotNull(ProcessResearch.CanStart(D, s, "n8um", 5));
        }

        [Fact]
        public void FinishesAfterItsRealLength()
        {
            var s = TestData.NewGame(seed: 3);
            Assert.Null(ProcessResearch.Start(D, s, "n8um"));
            var p = ProcessResearch.Active(s, "n8um");
            Assert.True(p.ActualWeeks >= p.PlannedWeeks);
            Simulation.AdvanceWeeks(D, s, p.ActualWeeks - 1);
            Assert.False(s.NodeReady("n8um"));
            Simulation.Advance(D, s);
            Assert.True(s.NodeReady("n8um"));
            Assert.True(p.Done);
        }

        [Fact]
        public void SameSeedSameOutcome()
        {
            var a = TestData.NewGame(seed: 9);
            var b = TestData.NewGame(seed: 9);
            ProcessResearch.Start(D, a, "n8um", 0.6);
            ProcessResearch.Start(D, b, "n8um", 0.6);
            Assert.Equal(ProcessResearch.Active(a, "n8um").ActualWeeks, ProcessResearch.Active(b, "n8um").ActualWeeks);
        }

        static int Slips(double spend)
        {
            int slips = 0;
            for (ulong seed = 1; seed <= 300; seed++)
            {
                var s = TestData.NewGame(seed: seed);
                ProcessResearch.Start(D, s, "n8um", spend);
                var p = ProcessResearch.Active(s, "n8um");
                if (p.ActualWeeks > p.PlannedWeeks) slips++;
            }
            return slips;
        }

        [Fact]
        public void MoneyBuysSchedule()
        {
            int cheap = Slips(0.5);
            int rich = Slips(2.0);
            Assert.True(cheap > rich * 3, $"cheap {cheap}, rich {rich}");
            Assert.True(rich < 40);
        }

        static GameState SlippedGame(out NodeProgram p)
        {
            for (ulong seed = 1; ; seed++)
            {
                var s = TestData.NewGame(seed: seed);
                ProcessResearch.Start(D, s, "n8um", 0.5);
                p = ProcessResearch.Active(s, "n8um");
                if (p.ActualWeeks > p.PlannedWeeks) return s;
            }
        }

        [Fact]
        public void SlipsCostMoreAndWarnYou()
        {
            var s = SlippedGame(out var p);
            var n = D.Node("n8um");
            Simulation.AdvanceWeeks(D, s, p.ActualWeeks);
            Assert.True(p.Done);
            Assert.True(p.Spent >= n.ResearchCost * 0.5 * p.ActualWeeks / p.PlannedWeeks * 0.999);
            Assert.Contains(s.News, x => x.Severity == 3 && x.Text.Contains("running late"));
        }

        [Fact]
        public void CancellingStopsTheProgramme()
        {
            var s = TestData.NewGame();
            ProcessResearch.Start(D, s, "n8um");
            Simulation.AdvanceMonths(D, s, 3);
            Assert.Null(ProcessResearch.Cancel(D, s, "n8um"));
            Assert.Null(ProcessResearch.Active(s, "n8um"));
            Simulation.AdvanceMonths(D, s, 40);
            Assert.False(s.NodeReady("n8um"));
            Assert.DoesNotContain(s.NodePrograms, x => x.NodeId == "n8um");
        }
    }
}
