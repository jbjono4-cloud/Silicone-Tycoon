using System.Collections.Generic;

namespace SiliconeTycoon.Sim
{
    /// <summary>What an event does. Each kind has its own rules in <see cref="Events"/>.</summary>
    public enum EventKind
    {
        /// <summary>A headline only.</summary>
        News,
        /// <summary>Demand in some segments (all if none listed) changes by Factor for Months.</summary>
        DemandShift,
        /// <summary>Wafer materials cost Factor times as much for Months.</summary>
        MaterialShortage,
        /// <summary>An earthquake hits a fab (chance from the location's QuakeRisk): it stops, loses lots and needs repairs.</summary>
        Quake,
        /// <summary>A fire in a fab: it stops for a while and every lot in the line is lost.</summary>
        FabFire,
        /// <summary>Contamination in a fab's clean room: defect density jumps until it is cleaned.</summary>
        Contamination,
        /// <summary>A strike at a fab: output drops until it is settled.</summary>
        Strike,
        /// <summary>Political trouble near a fab (chance from the location's PoliticalRisk): it stops for weeks.</summary>
        Unrest,
        /// <summary>A bug that escaped validation is found by customers in a shipping chip.</summary>
        Errata,
        /// <summary>A rival tries to hire away part of your design team.</summary>
        TalentRaid,
        /// <summary>A rival loses part of its capacity for a while (Factor of it is left).</summary>
        RivalSetback,
        /// <summary>A rival (or every rival, scripted) cuts prices to Factor of normal.</summary>
        RivalPriceWar,
        /// <summary>A government offers an extra fab subsidy (Factor, a share of capex) for fabs started there within Months.</summary>
        Grant,
    }

    /// <summary>
    /// An event from events.json. Scripted events have a Year (and Month) and fire once; random ones have a monthly
    /// Chance (per fab, per escaped bug and so on for the kinds that work on one thing) between From and To.
    /// </summary>
    public class EventDef
    {
        public string Id;
        public string Name;
        public EventKind Kind;
        /// <summary>Scripted: the year and month (1-12) it happens. 0 = random.</summary>
        public int Year;
        public int Month = 1;
        /// <summary>Random: chance each month (per target for per-fab, per-bug and per-rival kinds).</summary>
        public double Chance;
        public int From = 1970;
        public int To = 9999;
        public double Factor = 1;
        public int Months;
        public int MinWeeks;
        public int MaxWeeks;
        /// <summary>A share drawn between these (repair cost as a share of the fab's equipment, engineers poached...).</summary>
        public double MinShare;
        public double MaxShare;
        /// <summary>Segments a demand shift or price war applies to; empty = all (scripted) or one at random (random).</summary>
        public List<string> Segments = new List<string>();
        /// <summary>News severity: 0 info, 1 good, 2 warning, 3 bad.</summary>
        public int Severity = 2;
        /// <summary>Headline. Placeholders: {segment} {fab} {location} {rival} {chip} {team} {weeks} {months} {cost} {engineers} {share}.</summary>
        public string Text;

        public bool Scripted => Year > 0;
    }

    /// <summary>A world condition an event left behind, until EndMonth.</summary>
    public class ActiveEffect
    {
        public string EventId;
        /// <summary>"demand", "materials" or "grant".</summary>
        public string Kind;
        /// <summary>Segment id, location id, or null for everything.</summary>
        public string Target;
        public double Factor = 1;
        public int StartMonth;
        /// <summary>Last month it applies.</summary>
        public int EndMonth;
        public string Text;
    }

    public class DecisionOption
    {
        public string Label;
        public string Detail;
    }

    /// <summary>An event that needs the player's call. If nobody decides by the deadline, the default option happens.</summary>
    public class Decision
    {
        public string Id;
        public string EventId;
        public EventKind Kind;
        public string Title;
        public string Text;
        /// <summary>Fab, chip or team the decision is about.</summary>
        public string TargetId;
        /// <summary>Kind-specific numbers: costs, weeks, engineers.</summary>
        public double Cost;
        public double Value;
        public int Weeks;
        public List<DecisionOption> Options = new List<DecisionOption>();
        public int Default;
        public int DeadlineTick;
        public int RaisedTick;
    }
}
