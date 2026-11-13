using System;
using System.Collections.Generic;
using System.Linq;

namespace SiliconeTycoon.Sim
{
    public class NewGameOptions
    {
        public string CompanyName = "Your Company";
        public string ScenarioId = "campaign";
        public ulong Seed = 1;
        public int StartYear = Calendar.StartYear;
        /// <summary>Starting cash in 1970 dollars.</summary>
        public double StartingCash = 6_000_000;
        /// <summary>Processes the company starts with. Null = every node in production by the start year.</summary>
        public List<string> NodesReady;
        /// <summary>Engineers in the starting design team.</summary>
        public int StartingEngineers = 6;
        /// <summary>How often random events happen: 0 none, 0.5 calm, 1 normal, 1.5 turbulent.</summary>
        public double EventRate = 1;
        /// <summary>What kind of company you found.</summary>
        public StartType StartType = StartType.Integrated;
    }

    /// <summary>
    /// Integrated: design chips and build fabs. Fabless: more engineers and no fab, buying wafers from foundries.
    /// Foundry: a running fab and a small design team, selling wafers and custom chips to other companies.
    /// </summary>
    public enum StartType { Integrated, Fabless, Foundry }

    /// <summary>The weekly simulation loop.</summary>
    public static class Simulation
    {
        /// <summary>Months a company can stay overdrawn before it goes under.</summary>
        public const int MaxOverdrawnMonths = 6;

        public static GameState NewGame(GameData d, NewGameOptions o)
        {
            var s = new GameState
            {
                CompanyName = string.IsNullOrWhiteSpace(o.CompanyName) ? "Your Company" : o.CompanyName.Trim(),
                ScenarioId = o.ScenarioId,
                Tick = Calendar.TickOf(o.StartYear),
                Seed = o.Seed,
                Rng = new Rng(o.Seed),
                EventRate = o.EventRate,
            };
            s.StartTick = s.Tick;
            s.Ledger.Cash = o.StartingCash * s.Inflation;
            s.NodesReady = o.NodesReady != null
                ? new List<string>(o.NodesReady)
                : d.Nodes.Where(n => n.Year <= o.StartYear).Select(n => n.Id).ToList();
            DesignOps.AddTeam(s, "Design team", o.StartingEngineers);
            Market.Init(d, s);
            Events.Init(d, s);
            Foundry.Init(d, s);
            ApplyStart(d, s, o);
            s.Log($"{s.CompanyName} opens its doors with {Format.Big(s.Cash)} in the bank.", 1);
            return s;
        }

        /// <summary>Runs one week.</summary>
        public static void Advance(GameData d, GameState s)
        {
            if (s.GameOver) return;
            Events.WeekTick(d, s);
            ProcessResearch.WeekTick(d, s);
            DesignOps.WeekTick(d, s);
            foreach (var f in s.Fabs) FabOps.WeekTick(d, s, f);
            Market.ReceiveLots(s);
            if (Calendar.IsMonthEnd(s.Tick)) MonthEnd(d, s);
            s.Tick++;
        }

        /// <summary>Shapes a new company for its start type.</summary>
        static void ApplyStart(GameData d, GameState s, NewGameOptions o)
        {
            s.StartType = o.StartType;
            switch (o.StartType)
            {
                case StartType.Fabless:
                    s.DesignTeams[0].Engineers = o.StartingEngineers + 4;
                    s.Ledger.Cash *= 0.75;
                    s.Log("A fabless start: no fab to build. Rent wafers from a foundry on the Foundry tab and put your chips into them.", 1);
                    break;
                case StartType.Foundry:
                {
                    s.DesignTeams[0].Engineers = Math.Max(2, o.StartingEngineers / 2);
                    var node = d.Nodes.Where(n => s.NodeReady(n.Id)).OrderBy(n => n.FeatureNm).FirstOrDefault();
                    var loc = d.Locations.FirstOrDefault(l => l.Id == "us_ca") ?? d.Locations.FirstOrDefault();
                    if (node == null || loc == null) break;
                    var f = FabOps.Existing(d, s, node.Id, loc.Id, 1500, learningMonths: 12, ageMonths: 18);
                    s.Ledger.Cash *= 0.5;
                    s.Log($"A foundry start: {f.Name} is running. Bid for other companies' wafers and custom chips on the Foundry tab.", 1);
                    break;
                }
            }
        }

        public static void AdvanceWeeks(GameData d, GameState s, int weeks)
        {
            for (int i = 0; i < weeks && !s.GameOver; i++) Advance(d, s);
        }

        public static void AdvanceMonths(GameData d, GameState s, int months) => AdvanceWeeks(d, s, months * Calendar.WeeksPerMonth);

        static void MonthEnd(GameData d, GameState s)
        {
            Events.MonthEnd(d, s);
            foreach (var f in s.Fabs) FabOps.MonthEnd(d, s, f);
            Market.MonthEnd(d, s);
            Foundry.MonthEnd(d, s);
            DesignOps.MonthEnd(s);
            Finance.MonthEnd(s);
            s.Ledger.Close(s.MonthIndex);

            if (s.Cash < 0)
            {
                s.OverdrawnMonths++;
                if (s.OverdrawnMonths == 1) s.Log($"You're overdrawn. Borrow or cut costs within {MaxOverdrawnMonths} months.", 3);
                if (s.OverdrawnMonths > MaxOverdrawnMonths)
                {
                    s.GameOver = true;
                    s.EndText = $"{s.CompanyName} ran out of money in {Calendar.DateLabel(s.Tick)}.";
                    s.Log(s.EndText, 3);
                }
            }
            else s.OverdrawnMonths = 0;

            if (Calendar.IsQuarterEnd(s.Tick))
            {
                var q = s.Ledger.Months.Skip(System.Math.Max(0, s.Ledger.Months.Count - 3)).ToList();
                double revenue = q.Sum(m => m.Revenue);
                double profit = q.Sum(m => m.Profit);
                s.Log($"{Calendar.QuarterLabel(s.Tick)} results: revenue {Format.Big(revenue)}, {(profit >= 0 ? "profit" : "loss")} {Format.Big(System.Math.Abs(profit))}.", profit >= 0 ? 1 : 2);
            }
        }
    }
}
