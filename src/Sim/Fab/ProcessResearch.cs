using System;
using System.Linq;

namespace SiliconeTycoon.Sim
{
    /// <summary>
    /// A research programme for the next process node. Progress is measured in weeks of work at the normal budget; the
    /// budget can be changed at any time. Whether the programme slips (needs more work than planned) depends on a hidden
    /// roll made at the start and on the average budget spent so far, so money spent late can still rescue it.
    /// </summary>
    public class NodeProgram
    {
        public string NodeId;
        public int StartTick;
        /// <summary>Current budget multiplier, 0.5 to 2.</summary>
        public double SpendFactor = 1;
        public int PlannedWeeks;
        public int WeeksDone;
        /// <summary>Work done, in weeks at the normal budget.</summary>
        public double Progress;
        /// <summary>Sum of the budget multiplier over the weeks done (for the average).</summary>
        public double FactorWeeks;
        /// <summary>Hidden: the programme slips if this is below the slip chance at the average budget.</summary>
        public double SlipRoll = 1;
        /// <summary>Hidden: extra work if it slips, as a share of the planned work.</summary>
        public double SlipSize;
        /// <summary>Your fab experience on the previous node, in months of learning (updated weekly).</summary>
        public double PriorExperience;
        public double Spent;
        public bool Warned;
        public bool Done;
        /// <summary>Saves before version 2 only: the fixed length rolled at the start.</summary>
        public int ActualWeeks;
    }

    /// <summary>The node race: researching new processes, with the risk of slipping late.</summary>
    public static class ProcessResearch
    {
        public const double MinSpend = 0.5;
        public const double MaxSpend = 2.0;
        /// <summary>Chance that a slip is a major one (a year or more on a typical node).</summary>
        public const double MajorSlipShare = 0.25;
        /// <summary>How budget turns into speed: weekly progress = budget^this. 2× budget is about 23% faster.</summary>
        public const double BudgetSpeedExponent = 0.3;
        /// <summary>Months of experience on the previous node that halve the chance of a slip.</summary>
        public const double ExperienceHalfMonths = 18;

        /// <summary>Chance the programme slips. Doubling the budget cuts it by about 65%; halving it nearly triples it.</summary>
        public static double SlipChance(ProcessNodeDef n, double spendFactor, double experienceMonths = 0) =>
            Math.Clamp(n.SlipRisk / Math.Pow(Math.Clamp(spendFactor, MinSpend, MaxSpend), 1.5) / (1 + Math.Max(0, experienceMonths) / ExperienceHalfMonths), 0, 0.9);

        /// <summary>Your experience on the node before this one, which makes its research less likely to slip.</summary>
        public static double PriorExperience(GameData d, GameState s, ProcessNodeDef n) => FabOps.Experience(s, n.Requires);

        /// <summary>Weeks of normal-budget work done per week at a budget multiplier.</summary>
        public static double Speed(double spendFactor) => Math.Pow(Math.Clamp(spendFactor, MinSpend, MaxSpend), BudgetSpeedExponent);

        /// <summary>Average budget multiplier over the programme so far (the current one before it starts).</summary>
        public static double AverageFactor(NodeProgram p) => p.WeeksDone > 0 ? p.FactorWeeks / p.WeeksDone : p.SpendFactor;

        /// <summary>Hidden: whether the programme is heading for a slip at the average budget so far.</summary>
        public static bool WillSlip(ProcessNodeDef n, NodeProgram p) => p.SlipRoll < SlipChance(n, AverageFactor(p), p.PriorExperience);

        /// <summary>Hidden: total work the programme needs, in weeks at the normal budget.</summary>
        public static double RequiredWork(ProcessNodeDef n, NodeProgram p) => p.PlannedWeeks * (1 + (WillSlip(n, p) ? p.SlipSize : 0));

        /// <summary>Weeks to the planned finish at the current budget, if nothing slips.</summary>
        public static int WeeksToPlan(NodeProgram p) => (int)Math.Ceiling(Math.Max(0, p.PlannedWeeks - p.Progress) / Speed(p.SpendFactor));

        /// <summary>Earliest year a programme can start: a little ahead of history for a bold company.</summary>
        public static int EarliestStart(ProcessNodeDef n) => n.Year - (int)Math.Ceiling(n.ResearchMonths / 12.0) - 1;

        /// <summary>Cost of a week of research at a budget multiplier (current dollars).</summary>
        public static double WeeklyCost(GameState s, ProcessNodeDef n, double spendFactor) =>
            n.ResearchCost * spendFactor / Math.Max(1, n.ResearchMonths * Calendar.WeeksPerMonth) * s.Inflation;

        public static double WeeklyCost(GameState s, ProcessNodeDef n, NodeProgram p) => WeeklyCost(s, n, p.SpendFactor);

        public static NodeProgram Active(GameState s, string nodeId) => s.NodePrograms.FirstOrDefault(p => p.NodeId == nodeId && !p.Done);

        static string CheckFactor(double spendFactor) =>
            spendFactor < MinSpend - 1e-9 || spendFactor > MaxSpend + 1e-9 ? $"The budget must be between {MinSpend:0.0}× and {MaxSpend:0.0}× the normal level." : null;

        public static string CanStart(GameData d, GameState s, string nodeId, double spendFactor)
        {
            var n = d.Node(nodeId);
            if (n == null) return "Unknown process.";
            if (s.NodeReady(nodeId)) return $"The {n.Name} process is already ready.";
            if (n.Requires != null && !s.NodeReady(n.Requires)) return $"Research the {d.Node(n.Requires).Name} process first.";
            if (Active(s, nodeId) != null) return $"You're already researching {n.Name}.";
            if (s.Year < EarliestStart(n)) return $"Nobody can attempt {n.Name} before {EarliestStart(n)}.";
            return CheckFactor(spendFactor);
        }

        public static string Start(GameData d, GameState s, string nodeId, double spendFactor = 1.0)
        {
            string why = CanStart(d, s, nodeId, spendFactor);
            if (why != null) return why;
            var n = d.Node(nodeId);
            double size = s.Rng.Range(0.15, 0.6);
            if (s.Rng.Chance(MajorSlipShare)) size += s.Rng.Range(0.5, 1.5);
            s.NodePrograms.Add(new NodeProgram
            {
                NodeId = nodeId,
                StartTick = s.Tick,
                SpendFactor = spendFactor,
                PlannedWeeks = n.ResearchMonths * Calendar.WeeksPerMonth,
                SlipRoll = s.Rng.NextDouble(),
                SlipSize = size,
            });
            s.Log($"Process research starts on {n.Name}: planned {n.ResearchMonths} months at the normal budget, about {Format.Big(WeeklyCost(s, n, spendFactor) * Calendar.WeeksPerMonth)} a month.", 1);
            return null;
        }

        /// <summary>Changes a running programme's budget. Takes effect from the next week.</summary>
        public static string SetBudget(GameData d, GameState s, string nodeId, double spendFactor)
        {
            var p = Active(s, nodeId);
            if (p == null) return "That programme isn't running.";
            string err = CheckFactor(spendFactor);
            if (err != null) return err;
            p.SpendFactor = Math.Clamp(spendFactor, MinSpend, MaxSpend);
            return null;
        }

        /// <summary>Stops a programme. Money already spent is lost.</summary>
        public static string Cancel(GameData d, GameState s, string nodeId)
        {
            var p = Active(s, nodeId);
            if (p == null) return "That programme isn't running.";
            s.NodePrograms.Remove(p);
            s.Log($"You cancel the {d.Node(nodeId).Name} programme after spending {Format.Big(p.Spent)}.", 2);
            return null;
        }

        public static void WeekTick(GameData d, GameState s)
        {
            foreach (var p in s.NodePrograms)
            {
                if (p.Done) continue;
                var n = d.Node(p.NodeId);
                double cost = WeeklyCost(s, n, p);
                s.Ledger.Book(Accounts.ProcessResearch, -cost);
                p.Spent += cost;
                p.WeeksDone++;
                p.FactorWeeks += p.SpendFactor;
                p.PriorExperience = PriorExperience(d, s, n);
                p.Progress += Speed(p.SpendFactor);

                double need = RequiredWork(n, p);
                if (!p.Warned && need > p.PlannedWeeks && p.Progress >= p.PlannedWeeks * 0.75)
                {
                    p.Warned = true;
                    s.Log($"Test wafers on {n.Name} keep failing. The programme is running late; more budget may still pull it back.", 3);
                }
                if (p.Progress >= need - 1e-9)
                {
                    p.Done = true;
                    s.NodesReady.Add(n.Id);
                    int lateMonths = (int)Math.Round((p.WeeksDone - p.PlannedWeeks) / (double)Calendar.WeeksPerMonth);
                    string when = lateMonths > 0 ? $", {lateMonths} months late" : lateMonths < 0 ? $", {-lateMonths} months early" : ", on schedule";
                    s.Log($"The {n.Name} process is ready for production{when}. Total cost {Format.Big(p.Spent)}.", 1, NewsKinds.NodeReady);
                }
            }
        }
    }
}
