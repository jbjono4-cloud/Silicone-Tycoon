using System;
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
            var s = Simulation.NewGame(D, new NewGameOptions { EventRate = 0, NodesReady = new List<string> { "n10um", "n8um", "n6um" } });
            Assert.Contains("before 1973", ProcessResearch.CanStart(D, s, "n5um", 1.0));
        }

        [Fact]
        public void RejectsOutOfRangeBudgets()
        {
            var s = TestData.NewGame();
            Assert.NotNull(ProcessResearch.CanStart(D, s, "n8um", 0.1));
            Assert.NotNull(ProcessResearch.CanStart(D, s, "n8um", 5));
        }

        static ProcessNodeDef N8 => D.Node("n8um");

        /// <summary>Weeks a programme needs at a constant budget.</summary>
        static int WeeksNeeded(NodeProgram p) => (int)Math.Ceiling(ProcessResearch.RequiredWork(N8, p) / ProcessResearch.Speed(p.SpendFactor) - 1e-9);

        [Fact]
        public void FinishesWhenTheWorkIsDone()
        {
            var s = TestData.NewGame(seed: 3);
            Assert.Null(ProcessResearch.Start(D, s, "n8um"));
            var p = ProcessResearch.Active(s, "n8um");
            int weeks = WeeksNeeded(p);
            Assert.True(weeks >= p.PlannedWeeks);
            Simulation.AdvanceWeeks(D, s, weeks - 1);
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
            Assert.Equal(ProcessResearch.RequiredWork(N8, ProcessResearch.Active(a, "n8um")), ProcessResearch.RequiredWork(N8, ProcessResearch.Active(b, "n8um")));
        }

        static int Slips(double spend)
        {
            int slips = 0;
            for (ulong seed = 1; seed <= 300; seed++)
            {
                var s = TestData.NewGame(seed: seed);
                ProcessResearch.Start(D, s, "n8um", spend);
                if (ProcessResearch.WillSlip(N8, ProcessResearch.Active(s, "n8um"))) slips++;
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

        static GameState SlippedGame(out NodeProgram p, double spend = 0.5)
        {
            for (ulong seed = 1; ; seed++)
            {
                var s = TestData.NewGame(seed: seed);
                ProcessResearch.Start(D, s, "n8um", spend);
                p = ProcessResearch.Active(s, "n8um");
                if (ProcessResearch.WillSlip(N8, p)) return s;
            }
        }

        [Fact]
        public void SlipsCostMoreAndWarnYou()
        {
            var s = SlippedGame(out var p);
            double work = ProcessResearch.RequiredWork(N8, p);
            Simulation.AdvanceWeeks(D, s, WeeksNeeded(p));
            Assert.True(p.Done);
            Assert.True(p.WeeksDone > p.PlannedWeeks);
            Assert.True(p.Spent >= N8.ResearchCost * 0.5 * work / p.PlannedWeeks * 0.999);
            Assert.Contains(s.News, x => x.Severity == 3 && x.Text.Contains("running late"));
        }

        [Fact]
        public void BudgetCanChangeMidProgramme()
        {
            var s = TestData.NewGame();
            Assert.Null(ProcessResearch.Start(D, s, "n8um"));
            Assert.NotNull(ProcessResearch.SetBudget(D, s, "n8um", 3));
            Assert.NotNull(ProcessResearch.SetBudget(D, s, "n6um", 1.5));
            Simulation.AdvanceMonths(D, s, 1);
            double slowCost = s.Ledger.LastMonth.Lines[Accounts.ProcessResearch];
            Assert.Null(ProcessResearch.SetBudget(D, s, "n8um", 2.0));
            Simulation.AdvanceMonths(D, s, 1);
            Assert.InRange(s.Ledger.LastMonth.Lines[Accounts.ProcessResearch] / slowCost, 1.95, 2.05);   // twice the money (give or take a month's inflation)
            var p = ProcessResearch.Active(s, "n8um");
            double expected = 4 * ProcessResearch.Speed(1.0) + 4 * ProcessResearch.Speed(2.0);
            Assert.Equal(expected, p.Progress, 9);
            Assert.Equal(1.5, ProcessResearch.AverageFactor(p), 9);
        }

        [Fact]
        public void MoreBudgetFinishesSooner()
        {
            int Weeks(double spend)
            {
                var s = TestData.NewGame(seed: 4);
                ProcessResearch.Start(D, s, "n8um", spend);
                var p = ProcessResearch.Active(s, "n8um");
                p.SlipRoll = 1;   // no slip, to compare like with like
                int w = 0;
                while (!p.Done && w < 500) { Simulation.Advance(D, s); w++; }
                return w;
            }
            Assert.True(Weeks(2.0) < Weeks(1.0));
            Assert.True(Weeks(0.5) > Weeks(1.0));
        }

        [Fact]
        public void RaisingTheBudgetCanRescueASlippingProgramme()
        {
            // find a programme that slips at 0.5× but not once the average budget reaches 1.6×
            for (ulong seed = 1; seed < 2000; seed++)
            {
                var s = TestData.NewGame(seed: seed);
                ProcessResearch.Start(D, s, "n8um", 0.5);
                var p = ProcessResearch.Active(s, "n8um");
                if (!ProcessResearch.WillSlip(N8, p) || p.SlipRoll < ProcessResearch.SlipChance(N8, 1.6)) continue;
                Simulation.AdvanceMonths(D, s, 2);
                Assert.True(ProcessResearch.WillSlip(N8, p));
                Assert.Null(ProcessResearch.SetBudget(D, s, "n8um", 2.0));
                for (int i = 0; i < 400 && ProcessResearch.WillSlip(N8, p); i++) Simulation.Advance(D, s);
                Assert.False(ProcessResearch.WillSlip(N8, p));
                return;
            }
            Assert.Fail("No seed slipped at a low budget but not a high one");
        }

        [Fact]
        public void OldSavesKeepTheirResearchOutcome()
        {
            var s = TestData.NewGame();
            ProcessResearch.Start(D, s, "n8um");
            var p = ProcessResearch.Active(s, "n8um");
            p.WeeksDone = 10;
            p.ActualWeeks = p.PlannedWeeks + 20;
            s.SaveVersion = 1;
            var loaded = SaveGame.Deserialize(SaveGame.Serialize(s));
            var q = ProcessResearch.Active(loaded, "n8um");
            Assert.Equal(10, q.Progress, 9);
            Assert.True(ProcessResearch.WillSlip(N8, q));
            Assert.Equal(q.PlannedWeeks + 20, ProcessResearch.RequiredWork(N8, q), 6);
        }

        [Fact]
        public void ExperienceOnThePreviousNodeCutsSlipRisk()
        {
            Assert.Equal(ProcessResearch.SlipChance(N8, 1.0) / 2, ProcessResearch.SlipChance(N8, 1.0, ProcessResearch.ExperienceHalfMonths), 9);
            var s = TestData.NewGame();
            FabOps.Build(D, s, null, "n10um", "us_ca", 1000);
            Simulation.AdvanceMonths(D, s, 30);
            ProcessResearch.Start(D, s, "n8um");
            Simulation.Advance(D, s);
            Assert.True(ProcessResearch.Active(s, "n8um").PriorExperience > 5);
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
