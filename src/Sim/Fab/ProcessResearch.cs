using System;
using System.Linq;

namespace SiliconeTycoon.Sim
{
    /// <summary>A research programme for the next process node. Its real length is rolled (hidden) when it starts.</summary>
    public class NodeProgram
    {
        public string NodeId;
        public int StartTick;
        /// <summary>Budget multiplier, 0.5 to 2. More money cuts the chance of a slip.</summary>
        public double SpendFactor = 1;
        public int PlannedWeeks;
        /// <summary>Hidden: how long the programme will really take.</summary>
        public int ActualWeeks;
        public int WeeksDone;
        public double Spent;
        public bool Warned;
        public bool Done;
    }

    /// <summary>The node race: researching new processes, with the risk of slipping late.</summary>
    public static class ProcessResearch
    {
        public const double MinSpend = 0.5;
        public const double MaxSpend = 2.0;
        /// <summary>Chance that a slip is a major one (a year or more on a typical node).</summary>
        public const double MajorSlipShare = 0.25;

        /// <summary>Chance the programme slips. Doubling the budget cuts it by about 65%; halving it nearly triples it.</summary>
        public static double SlipChance(ProcessNodeDef n, double spendFactor) =>
            Math.Clamp(n.SlipRisk / Math.Pow(Math.Clamp(spendFactor, MinSpend, MaxSpend), 1.5), 0, 0.9);

        /// <summary>Earliest year a programme can start: a little ahead of history for a bold company.</summary>
        public static int EarliestStart(ProcessNodeDef n) => n.Year - (int)Math.Ceiling(n.ResearchMonths / 12.0) - 1;

        public static double WeeklyCost(GameState s, ProcessNodeDef n, NodeProgram p) =>
            n.ResearchCost * p.SpendFactor / Math.Max(1, p.PlannedWeeks) * s.Inflation;

        public static NodeProgram Active(GameState s, string nodeId) => s.NodePrograms.FirstOrDefault(p => p.NodeId == nodeId && !p.Done);

        public static string CanStart(GameData d, GameState s, string nodeId, double spendFactor)
        {
            var n = d.Node(nodeId);
            if (n == null) return "Unknown process.";
            if (s.NodeReady(nodeId)) return $"The {n.Name} process is already ready.";
            if (n.Requires != null && !s.NodeReady(n.Requires)) return $"Research the {d.Node(n.Requires).Name} process first.";
            if (Active(s, nodeId) != null) return $"You're already researching {n.Name}.";
            if (s.Year < EarliestStart(n)) return $"Nobody can attempt {n.Name} before {EarliestStart(n)}.";
            if (spendFactor < MinSpend || spendFactor > MaxSpend) return $"The budget must be between {MinSpend:0.0}× and {MaxSpend:0.0}× the normal level.";
            return null;
        }

        public static string Start(GameData d, GameState s, string nodeId, double spendFactor = 1.0)
        {
            string why = CanStart(d, s, nodeId, spendFactor);
            if (why != null) return why;
            var n = d.Node(nodeId);
            int planned = n.ResearchMonths * Calendar.WeeksPerMonth;
            int delay = 0;
            if (s.Rng.Chance(SlipChance(n, spendFactor)))
            {
                delay = (int)Math.Round(planned * s.Rng.Range(0.15, 0.6));
                if (s.Rng.Chance(MajorSlipShare)) delay += (int)Math.Round(planned * s.Rng.Range(0.5, 1.5));
            }
            s.NodePrograms.Add(new NodeProgram
            {
                NodeId = nodeId,
                StartTick = s.Tick,
                SpendFactor = spendFactor,
                PlannedWeeks = planned,
                ActualWeeks = planned + delay,
            });
            double total = n.ResearchCost * spendFactor * s.Inflation;
            s.Log($"Process research starts on {n.Name}: planned {n.ResearchMonths} months, about {Format.Big(total)}.", 1);
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

                if (!p.Warned && p.ActualWeeks > p.PlannedWeeks && p.WeeksDone >= p.PlannedWeeks * 3 / 4)
                {
                    p.Warned = true;
                    s.Log($"Test wafers on {n.Name} keep failing. The programme is running late.", 3);
                }
                if (p.WeeksDone >= p.ActualWeeks)
                {
                    p.Done = true;
                    s.NodesReady.Add(n.Id);
                    int lateMonths = (p.ActualWeeks - p.PlannedWeeks) / Calendar.WeeksPerMonth;
                    string when = lateMonths > 0 ? $", {lateMonths} months late" : ", on schedule";
                    s.Log($"The {n.Name} process is ready for production{when}. Total cost {Format.Big(p.Spent)}.", 1);
                }
            }
        }
    }
}
