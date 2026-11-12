using System;
using System.Linq;

namespace SiliconeTycoon.Sim
{
    public class DesignTeam
    {
        public string Id;
        public string Name;
        public int Engineers;
        /// <summary>Design the team is working on, or null when free.</summary>
        public string ProjectId;
    }

    /// <summary>
    /// Running chip projects: design time from transistor count and team size, tape-out and mask sets, first silicon,
    /// validation that finds (most) bugs, steppings to fix them, and release.
    /// </summary>
    public static class DesignOps
    {
        /// <summary>Monthly cost of one design engineer, 1970 dollars.</summary>
        public const double Salary = 1500;
        public const double MinVerification = 0.1;
        public const double MaxVerification = 0.6;
        /// <summary>Share of the work an outside design house takes on.</summary>
        public const double DesignHouseShare = 0.2;
        /// <summary>What the design house charges over doing it in-house.</summary>
        public const double DesignHouseMarkup = 1.75;
        /// <summary>Weeks from wafers arriving to validation results ("bring-up").</summary>
        public const int BringUpWeeks = 4;
        /// <summary>Weeks of redesign for a stepping, before the new masks.</summary>
        public const int SteppingWeeks = 6;
        /// <summary>A stepping only replaces some mask layers.</summary>
        public const double SteppingMaskShare = 0.35;
        /// <summary>Share of found bugs bad enough to stop a release.</summary>
        public const double ShowstopperShare = 0.35;
        public const int MaxEngineers = 500;

        /// <summary>Transistors one engineer can design and verify in a month; design tools improve it every year.</summary>
        public static double Productivity(double year) => 60 * Math.Pow(1.22, Math.Max(0, year - 1970));

        public static double BaseEngineerMonths(ChipDesign c, DesignStats st, double year) =>
            st.Transistors * (1 + c.Verification) / Productivity(year);

        public static double EngineerMonths(ChipDesign c, DesignStats st, double year) =>
            BaseEngineerMonths(c, st, year) * (c.UseDesignHouse ? 1 - DesignHouseShare : 1);

        /// <summary>Average bugs in first silicon: grows with complexity, falls with verification effort.</summary>
        public static double ExpectedBugs(ChipDesign c, DesignStats st) =>
            Math.Pow(st.Transistors / 1500.0, 0.6) * Math.Max(0.2, 1.3 - 1.5 * c.Verification);

        /// <summary>Chance validation finds any given bug.</summary>
        public static double FindChance(ChipDesign c) => Math.Clamp(0.45 + c.Verification, 0.5, 0.95);

        public static DesignTeam Team(GameState s, string id) => s.DesignTeams.FirstOrDefault(t => t.Id == id);

        public static DesignTeam AddTeam(GameState s, string name, int engineers)
        {
            var t = new DesignTeam { Id = s.NewId("team"), Name = name, Engineers = engineers };
            s.DesignTeams.Add(t);
            return t;
        }

        public static string SetEngineers(GameState s, DesignTeam t, int engineers)
        {
            if (engineers < 0 || engineers > MaxEngineers) return $"A team can have 0 to {MaxEngineers} engineers.";
            t.Engineers = engineers;
            return null;
        }

        public static string StartDesign(GameData d, GameState s, ChipDesign c, string teamId)
        {
            var team = Team(s, teamId);
            if (team == null) return "Choose a design team.";
            if (team.ProjectId != null) return $"{team.Name} is busy on {s.Design(team.ProjectId)?.Name ?? "another chip"}.";
            if (team.Engineers <= 0) return $"{team.Name} has no engineers.";
            if (c.Stage != DesignStage.Draft) return "That chip is already under way.";
            if (string.IsNullOrWhiteSpace(c.Name)) return "Name the chip.";
            if (s.Designs.Any(x => x != c && x.Name == c.Name.Trim() && x.Stage != DesignStage.Cancelled)) return "You already have a chip with that name.";

            var st = DesignLab.Analyze(d, s, c);
            if (!st.Valid) return st.FirstError;
            if (s.Cash < st.DesignHouseFee) return $"The design house wants {Format.Money(st.DesignHouseFee)} up front.";

            c.Name = c.Name.Trim();
            if (c.Id == null) c.Id = s.NewId("chip");
            if (!s.Designs.Contains(c)) s.Designs.Add(c);
            c.Stats = st;
            c.Stage = DesignStage.Designing;
            c.TeamId = team.Id;
            c.StartedTick = s.Tick;
            c.WeeksLeft = Math.Max(1, (int)Math.Ceiling(st.EngineerMonths / team.Engineers * Calendar.WeeksPerMonth));
            team.ProjectId = c.Id;
            if (st.DesignHouseFee > 0) s.Ledger.Book(Accounts.DesignHouse, -st.DesignHouseFee);
            s.Log($"{team.Name} starts the {c.Name}: {st.Transistors:N0} transistors on {d.Node(c.NodeId).Name}, about {c.WeeksLeft / Calendar.WeeksPerMonth} months to tape-out.", 1);
            return null;
        }

        public static void WeekTick(GameData d, GameState s)
        {
            foreach (var c in s.Designs)
            {
                if (c.Stage != DesignStage.Designing && c.Stage != DesignStage.Silicon) continue;
                c.WeeksLeft--;
                if (c.WeeksLeft > 0) continue;
                var n = d.Node(c.NodeId);
                if (c.Stage == DesignStage.Designing) TapeOut(s, c, n);
                else Validate(s, c);
            }
        }

        static void TapeOut(GameState s, ChipDesign c, ProcessNodeDef n)
        {
            double masks = n.MaskSetCost * s.Inflation * (c.Stepping == 0 ? 1 : SteppingMaskShare);
            s.Ledger.Book(Accounts.MaskSets, -masks);
            c.MaskSpend += masks;
            if (c.Stepping == 0) c.HiddenBugs = s.Rng.Poisson(ExpectedBugs(c, c.Stats));
            c.Stage = DesignStage.Silicon;
            c.WeeksLeft = n.CycleWeeks + BringUpWeeks;
            s.Log($"The {c.Name} ({c.SteppingName}) tapes out. Masks cost {Format.Big(masks)}; first silicon in about {c.WeeksLeft / Calendar.WeeksPerMonth} months.", 0);
        }

        static void Validate(GameState s, ChipDesign c)
        {
            int found = 0, stoppers = 0;
            double p = FindChance(c);
            for (int i = 0; i < c.HiddenBugs; i++)
            {
                if (!s.Rng.Chance(p)) continue;
                found++;
                if (s.Rng.Chance(ShowstopperShare)) stoppers++;
            }
            c.HiddenBugs -= found;
            c.FoundBugs += found;
            c.Showstoppers += stoppers;
            c.Stage = DesignStage.Validated;
            if (found == 0) s.Log($"First silicon of the {c.Name} ({c.SteppingName}) passes validation. Ready to release.", 1);
            else if (stoppers > 0) s.Log($"The {c.Name} ({c.SteppingName}) has {found} bug{(found == 1 ? "" : "s")}, {stoppers} too serious to ship. It needs a new stepping.", 3);
            else s.Log($"The {c.Name} ({c.SteppingName}) has {found} minor bug{(found == 1 ? "" : "s")}. Ship with errata, or fix them in a new stepping.", 2);
        }

        /// <summary>Starts a new stepping to fix the bugs validation found. Fixes can introduce new bugs.</summary>
        public static string Step(GameData d, GameState s, ChipDesign c)
        {
            if (c.Stage != DesignStage.Validated) return "A stepping needs validated silicon.";
            if (c.FoundBugs == 0) return "There are no known bugs to fix.";
            c.Stepping++;
            c.FoundBugs = 0;
            c.Showstoppers = 0;
            c.HiddenBugs += s.Rng.Poisson(0.3);
            c.Stage = DesignStage.Designing;
            c.WeeksLeft = SteppingWeeks;
            s.Log($"Work starts on the {c.Name} {c.SteppingName} stepping.", 0);
            return null;
        }

        /// <summary>Releases validated silicon for production. Known minor bugs ship as documented errata; unknown ones escape.</summary>
        public static string Release(GameData d, GameState s, ChipDesign c)
        {
            if (c.Stage != DesignStage.Validated) return "Only validated silicon can be released.";
            if (c.Showstoppers > 0) return $"The {c.Name} has {c.Showstoppers} showstopper bug{(c.Showstoppers == 1 ? "" : "s")}. Fix them in a new stepping first.";
            c.EscapedBugs = c.HiddenBugs;
            c.Stage = DesignStage.Released;
            c.ReleasedTick = s.Tick;
            FreeTeam(s, c);
            string errata = c.FoundBugs > 0 ? $" with {c.FoundBugs} documented errata" : "";
            s.Log($"The {c.Name} ({c.SteppingName}) is released for production{errata}.", 1);
            return null;
        }

        public static string Cancel(GameState s, ChipDesign c)
        {
            if (c.Stage == DesignStage.Released || c.Stage == DesignStage.Cancelled) return "That chip can't be cancelled.";
            c.Stage = DesignStage.Cancelled;
            FreeTeam(s, c);
            s.Log($"You cancel the {c.Name}.", 2);
            return null;
        }

        static void FreeTeam(GameState s, ChipDesign c)
        {
            var team = Team(s, c.TeamId);
            if (team != null && team.ProjectId == c.Id) team.ProjectId = null;
        }

        public static void MonthEnd(GameState s)
        {
            int engineers = s.DesignTeams.Sum(t => t.Engineers);
            if (engineers > 0) s.Ledger.Book(Accounts.DesignStaff, -engineers * Salary * s.Inflation);
        }
    }
}
