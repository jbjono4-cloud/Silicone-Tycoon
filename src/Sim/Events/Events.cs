using System;
using System.Collections.Generic;
using System.Linq;

namespace SiliconeTycoon.Sim
{
    /// <summary>
    /// World events: scripted history (oil shocks, recessions) and random trouble and luck (earthquakes, fires,
    /// contamination, strikes, errata, poached engineers, rival setbacks, grants). Some leave an effect on the world for a
    /// few months; some need a decision, which defaults if nobody makes it within <see cref="DecisionWeeks"/>.
    /// </summary>
    public static class Events
    {
        public const int DecisionWeeks = 4;
        /// <summary>Weeks a fab is shut to clean out contamination.</summary>
        public const int CleanWeeks = 2;
        /// <summary>Months of sales a recall has to replace.</summary>
        public const int RecallMonths = 6;
        /// <summary>Share of shipped chips customers send back in a recall.</summary>
        public const double RecallShare = 0.3;
        /// <summary>Share of surfacing bugs bad enough to need a decision (the rest are a footnote in the errata sheet).</summary>
        public const double SeriousErrataShare = 0.4;
        public const int MinRaidTeam = 8;
        /// <summary>Months of salary it costs to keep engineers a rival is courting.</summary>
        public const double RetentionMonths = 6;

        // ------------------------------------------------------------------ effects on the world

        public static IEnumerable<ActiveEffect> Active(GameState s, string kind)
        {
            int m = s.MonthIndex;
            return s.Effects.Where(e => e.Kind == kind && e.StartMonth <= m && e.EndMonth >= m);
        }

        /// <summary>Demand multiplier for a segment this month from booms, busts and recessions.</summary>
        public static double DemandFactor(GameState s, string segmentId) =>
            Active(s, "demand").Where(e => e.Target == null || e.Target == segmentId).Aggregate(1.0, (f, e) => f * e.Factor);

        /// <summary>Wafer materials price multiplier this month.</summary>
        public static double MaterialsFactor(GameState s) => Active(s, "materials").Aggregate(1.0, (f, e) => f * e.Factor);

        /// <summary>Extra subsidy (share of capex) a government is offering for fabs started at a location.</summary>
        public static double ExtraSubsidy(GameState s, string locationId) =>
            Active(s, "grant").Where(e => e.Target == locationId).Sum(e => e.Factor);

        // ------------------------------------------------------------------ the loop

        /// <summary>Scripted events dated before the game starts count as history, not news.</summary>
        public static void Init(GameData d, GameState s)
        {
            foreach (var e in d.Events.Where(x => x.Scripted && ScriptedMonth(x) < s.MonthIndex))
                if (!s.FiredEvents.Contains(e.Id)) s.FiredEvents.Add(e.Id);
        }

        static int ScriptedMonth(EventDef e) => (e.Year - Calendar.StartYear) * 12 + Math.Clamp(e.Month, 1, 12) - 1;

        /// <summary>Decisions nobody made go to their default.</summary>
        public static void WeekTick(GameData d, GameState s)
        {
            foreach (var x in s.Decisions.Where(x => s.Tick >= x.DeadlineTick).ToList())
                Choose(d, s, x.Id, x.Default, timedOut: true);
        }

        public static void MonthEnd(GameData d, GameState s)
        {
            int m = s.MonthIndex;
            s.Effects.RemoveAll(e => e.EndMonth < m);
            if (s.EventRate <= 0) return;

            foreach (var e in d.Events.Where(x => x.Scripted && !s.FiredEvents.Contains(x.Id)).ToList())
            {
                int when = ScriptedMonth(e);
                if (m < when) continue;
                s.FiredEvents.Add(e.Id);
                if (m - when <= 2) Fire(d, s, e, null);   // a save from before the date catches up; older ones are history
            }

            double year = s.YearF;
            foreach (var e in d.Events.Where(x => !x.Scripted && x.Chance > 0 && year >= x.From && year < x.To + 1).ToList())
                Roll(d, s, e);
        }

        static bool Chance(GameState s, double p) => p > 0 && s.Rng.NextDouble() < Math.Min(1, p * s.EventRate);

        static int Weeks(GameState s, EventDef e) => e.MinWeeks + (int)Math.Floor(s.Rng.NextDouble() * (Math.Max(e.MinWeeks, e.MaxWeeks) - e.MinWeeks + 1));

        static double Share(GameState s, EventDef e) => e.MinShare + s.Rng.NextDouble() * (Math.Max(e.MinShare, e.MaxShare) - e.MinShare);

        static T Pick<T>(GameState s, IList<T> items) => items.Count == 0 ? default : items[Math.Min(items.Count - 1, (int)(s.Rng.NextDouble() * items.Count))];

        static bool Busy(GameState s, string targetId) => s.Decisions.Any(x => x.TargetId == targetId);

        static IEnumerable<Fab> Working(GameState s) => s.Fabs.Where(f => f.Producing && f.OutageWeeks <= 0 && !Busy(s, f.Id));

        /// <summary>Rolls a random event's chance against each thing it could happen to.</summary>
        static void Roll(GameData d, GameState s, EventDef e)
        {
            switch (e.Kind)
            {
                case EventKind.Quake:
                    foreach (var f in Working(s).ToList())
                        if (Chance(s, (d.Location(f.LocationId)?.QuakeRisk ?? 0) / 12 * e.Chance)) Fire(d, s, e, f.Id);
                    break;
                case EventKind.Unrest:
                    foreach (var f in Working(s).ToList())
                        if (Chance(s, (d.Location(f.LocationId)?.PoliticalRisk ?? 0) / 12 * e.Chance)) Fire(d, s, e, f.Id);
                    break;
                case EventKind.Strike:
                    foreach (var f in Working(s).ToList())
                        if (Chance(s, e.Chance * (1 + 10 * (d.Location(f.LocationId)?.PoliticalRisk ?? 0)))) Fire(d, s, e, f.Id);
                    break;
                case EventKind.FabFire:
                case EventKind.Contamination:
                    foreach (var f in Working(s).ToList())
                        if (Chance(s, e.Chance)) Fire(d, s, e, f.Id);
                    break;
                case EventKind.Errata:
                    foreach (var c in s.Designs.Where(c => c.Stage == DesignStage.Released && c.EscapedBugs > 0 && !Busy(s, c.Id)).ToList())
                        if (Chance(s, e.Chance * c.EscapedBugs)) Fire(d, s, e, c.Id);
                    break;
                case EventKind.TalentRaid:
                    foreach (var t in s.DesignTeams.Where(t => t.Engineers >= MinRaidTeam && !Busy(s, t.Id)).ToList())
                        if (Chance(s, e.Chance)) Fire(d, s, e, t.Id);
                    break;
                case EventKind.RivalSetback:
                case EventKind.RivalPriceWar:
                    foreach (var r in s.Rivals.ToList())
                        if (Chance(s, e.Chance)) Fire(d, s, e, r.Id);
                    break;
                default:
                    if (Chance(s, e.Chance)) Fire(d, s, e, null);
                    break;
            }
        }

        // ------------------------------------------------------------------ what each event does

        /// <summary>Makes an event happen now (to a target, for the kinds that have one). Returns false if it couldn't apply.</summary>
        public static bool Fire(GameData d, GameState s, EventDef e, string targetId)
        {
            int m = s.MonthIndex;
            var words = new Dictionary<string, string>();
            string Say(string fallback)
            {
                string t = string.IsNullOrWhiteSpace(e.Text) ? fallback : e.Text;
                foreach (var kv in words) t = t.Replace("{" + kv.Key + "}", kv.Value);
                return t;
            }

            switch (e.Kind)
            {
                case EventKind.News:
                    s.Log(Say(e.Name), e.Severity, NewsKinds.Event);
                    return true;

                case EventKind.DemandShift:
                {
                    var segs = e.Segments.Count > 0
                        ? e.Segments.Select(d.Segment).Where(x => x != null).ToList()
                        : e.Scripted ? new List<SegmentDef> { null }
                        : new List<SegmentDef> { Pick(s, d.Segments.Where(x => x.Active(s.YearF)).ToList()) };
                    if (segs.Count == 0 || (!e.Scripted && segs[0] == null)) return false;
                    foreach (var seg in segs)
                    {
                        if (s.Effects.Any(x => x.EventId == e.Id && x.Target == seg?.Id)) continue;
                        s.Effects.Add(new ActiveEffect { EventId = e.Id, Kind = "demand", Target = seg?.Id, Factor = e.Factor, StartMonth = m, EndMonth = m + Math.Max(1, e.Months) - 1, Text = e.Name });
                    }
                    words["segment"] = segs[0] == null ? "chip" : string.Join(" and ", segs.Select(x => x.Name.ToLower()));
                    words["months"] = e.Months.ToString();
                    words["share"] = $"{Math.Abs(e.Factor - 1):P0}";
                    s.Log(Say($"{e.Name}: demand for {words["segment"]}s changes by {e.Factor - 1:+0%;-0%} for about {e.Months} months."), e.Severity, NewsKinds.Event);
                    return true;
                }

                case EventKind.MaterialShortage:
                    if (Active(s, "materials").Any(x => x.EventId == e.Id)) return false;
                    s.Effects.Add(new ActiveEffect { EventId = e.Id, Kind = "materials", Factor = e.Factor, StartMonth = m, EndMonth = m + Math.Max(1, e.Months) - 1, Text = e.Name });
                    words["share"] = $"{e.Factor - 1:P0}";
                    words["months"] = e.Months.ToString();
                    s.Log(Say($"{e.Name}: wafer materials cost {e.Factor - 1:P0} more for about {e.Months} months."), e.Severity, NewsKinds.Event);
                    return true;

                case EventKind.Grant:
                {
                    var loc = Pick(s, d.Locations.Where(l => s.Year >= l.From && s.Year <= l.To && ExtraSubsidy(s, l.Id) <= 0).ToList());
                    if (loc == null) return false;
                    int months = Math.Max(1, e.Months);
                    s.Effects.Add(new ActiveEffect { EventId = e.Id, Kind = "grant", Target = loc.Id, Factor = e.Factor, StartMonth = m, EndMonth = m + months - 1, Text = $"{loc.Name} grant" });
                    words["location"] = loc.Name;
                    words["share"] = $"{e.Factor:P0}";
                    words["months"] = months.ToString();
                    s.Log(Say($"{loc.Name} offers an extra {e.Factor:P0} of the cost of any fab you start there in the next {months} months."), e.Severity, NewsKinds.Event);
                    return true;
                }

                case EventKind.RivalSetback:
                case EventKind.RivalPriceWar:
                {
                    var rivals = (targetId != null ? s.Rivals.Where(r => r.Id == targetId) : s.Rivals)
                        .Where(r => e.Segments.Count == 0 || (d.Rival(r.Id)?.Segments.Any(e.Segments.Contains) ?? false)).ToList();
                    if (rivals.Count == 0) return false;
                    foreach (var r in rivals)
                    {
                        words["rival"] = r.Name;
                        if (e.Kind == EventKind.RivalSetback)
                        {
                            r.Capacity = Math.Max(Rivals.MinCapacity, r.Capacity * e.Factor);
                            words["share"] = $"{1 - e.Factor:P0}";
                            s.Log(Say($"{r.Name} loses {1 - e.Factor:P0} of its capacity to a fab problem. It will take months to rebuild."), e.Severity, NewsKinds.Event);
                        }
                        else
                        {
                            r.PriceFactor = Math.Max(r.BasePriceFactor * Rivals.PriceWarFloor, r.PriceFactor * e.Factor);
                            r.PriceWar = true;
                            s.Log(Say($"{r.Name} cuts its prices to win share."), e.Severity, NewsKinds.Event);
                        }
                    }
                    return true;
                }

                case EventKind.Quake:
                case EventKind.FabFire:
                case EventKind.Unrest:
                case EventKind.Contamination:
                case EventKind.Strike:
                    return FabEvent(d, s, e, s.Fabs.FirstOrDefault(f => f.Id == targetId), words, Say);

                case EventKind.Errata:
                    return ErrataEvent(d, s, e, s.Design(targetId), words, Say);

                case EventKind.TalentRaid:
                    return RaidEvent(d, s, e, DesignOps.Team(s, targetId), words, Say);
            }
            return false;
        }

        static bool FabEvent(GameData d, GameState s, EventDef e, Fab f, Dictionary<string, string> words, Func<string, string> say)
        {
            if (f == null || !f.Producing) return false;
            var loc = d.Location(f.LocationId);
            words["fab"] = f.Name;
            words["location"] = loc?.Name ?? "";
            int weeks = Weeks(s, e);
            words["weeks"] = weeks.ToString();
            double equipment = f.CapexReal * s.Inflation;
            switch (e.Kind)
            {
                case EventKind.Quake:
                {
                    int lost = ScrapLots(s, f, 0.5);
                    double repair = equipment * Share(s, e);
                    Outage(f, 0, weeks, "earthquake damage");
                    words["cost"] = Format.Big(repair);
                    var x = Raise(s, e, f.Id, $"Earthquake at {f.Name}", say($"An earthquake shakes {loc?.Name}. {f.Name} is down for about {weeks} weeks and {lost} lots in the line are lost. Repairs will cost {Format.Big(repair)}."));
                    x.Cost = repair;
                    x.Weeks = weeks;
                    x.Options.Add(new DecisionOption { Label = "Standard repairs", Detail = $"{Format.Big(repair)}, back in about {weeks} weeks." });
                    x.Options.Add(new DecisionOption { Label = "Rush the repairs", Detail = $"{Format.Big(repair * 2)} for crews working round the clock: back in about {(weeks + 1) / 2} weeks." });
                    return true;
                }
                case EventKind.FabFire:
                {
                    int lost = ScrapLots(s, f, 1.0);
                    double repair = equipment * Share(s, e);
                    Outage(f, 0, weeks, "fire damage");
                    s.Ledger.Book(Accounts.FabDamage, -repair);
                    words["cost"] = Format.Big(repair);
                    s.Log(say($"Fire at {f.Name}: every lot in the line ({lost}) is lost and the fab is down for about {weeks} weeks. Repairs cost {Format.Big(repair)}."), 3, NewsKinds.Event);
                    return true;
                }
                case EventKind.Unrest:
                {
                    Outage(f, 0, weeks, "unrest");
                    s.Log(say($"Unrest in {loc?.Name} shuts {f.Name} for about {weeks} weeks."), 3, NewsKinds.Event);
                    return true;
                }
                case EventKind.Contamination:
                {
                    f.D0Penalty = Math.Max(f.D0Penalty, e.Factor);
                    f.D0PenaltyWeeks = Math.Max(f.D0PenaltyWeeks, weeks);
                    var x = Raise(s, e, f.Id, $"Contamination at {f.Name}", say($"Particles in {f.Name}'s clean room: defect density is {e.Factor:0.#}× normal and will stay up for about {weeks} weeks unless the line is cleaned."));
                    x.Weeks = weeks;
                    x.Default = 1;
                    x.Options.Add(new DecisionOption { Label = "Shut down and clean", Detail = $"No wafer starts for {CleanWeeks} weeks, then yields are back to normal." });
                    x.Options.Add(new DecisionOption { Label = "Keep running", Detail = $"Production continues at {e.Factor:0.#}× the defects for about {weeks} weeks." });
                    return true;
                }
                case EventKind.Strike:
                {
                    double factor = Math.Clamp(e.Factor, 0, 1);
                    Outage(f, factor, weeks, "a strike");
                    double settle = FabOps.RunningCost(d, s, f) * 1.5;
                    words["cost"] = Format.Big(settle);
                    var x = Raise(s, e, f.Id, $"Strike at {f.Name}", say($"Workers at {f.Name} walk out over pay. The fab is running at {factor:P0} and the union says it will stay out for about {weeks} weeks."));
                    x.Cost = settle;
                    x.Weeks = weeks;
                    x.Default = 1;
                    x.Options.Add(new DecisionOption { Label = "Settle", Detail = $"A {Format.Big(settle)} pay deal: everyone is back next week." });
                    x.Options.Add(new DecisionOption { Label = "Hold out", Detail = $"Run at {factor:P0} for about {weeks} weeks; the press won't be kind." });
                    return true;
                }
            }
            return false;
        }

        static void Outage(Fab f, double factor, int weeks, string reason)
        {
            f.OutageFactor = f.OutageWeeks > 0 ? Math.Min(f.OutageFactor, factor) : factor;
            f.OutageWeeks = Math.Max(f.OutageWeeks, weeks);
            f.OutageReason = reason;
        }

        /// <summary>Throws away a share of the lots still in the line. Returns how many were lost.</summary>
        static int ScrapLots(GameState s, Fab f, double share)
        {
            var inLine = f.Lots.Where(l => !l.Done).ToList();
            int lost = 0;
            foreach (var lot in inLine)
                if (share >= 1 || s.Rng.NextDouble() < share)
                {
                    f.Lots.Remove(lot);
                    lost++;
                }
            return lost;
        }

        static bool ErrataEvent(GameData d, GameState s, EventDef e, ChipDesign c, Dictionary<string, string> words, Func<string, string> say)
        {
            if (c == null || c.EscapedBugs <= 0) return false;
            c.EscapedBugs--;
            c.FoundBugs++;
            words["chip"] = c.Name;
            if (s.Rng.NextDouble() >= SeriousErrataShare)
            {
                Market.AdjustRep(s, -0.005);
                s.Log(say($"Customers find a minor bug in the {c.Name}. It goes into the errata sheet."), 0, NewsKinds.Event);
                return true;
            }
            double sold = s.Inventory.Where(x => x.DesignId == c.Id).Sum(x => x.SoldLastMonth);
            double units = Math.Max(sold, 1000) * RecallMonths * RecallShare;
            double unitCost = (c.Stats?.CostPerGoodDie ?? 1) * 1.5;
            if (double.IsInfinity(unitCost) || double.IsNaN(unitCost)) unitCost = 1;
            double cost = units * unitCost;
            words["cost"] = Format.Big(cost);
            var x = Raise(s, e, c.Id, $"Bug in the {c.Name}", say($"A customer has found a serious bug in the {c.Name} that validation missed. Word is spreading."));
            x.Cost = cost;
            x.Value = units;
            x.Default = 1;
            x.Options.Add(new DecisionOption { Label = "Recall and replace", Detail = $"About {Format.Units(units)} chips replaced for {Format.Big(cost)}. Customers will remember you made it right." });
            x.Options.Add(new DecisionOption { Label = "Publish an erratum", Detail = "Costs nothing now, but reputation takes a real hit and contract customers are unhappy." });
            return true;
        }

        static bool RaidEvent(GameData d, GameState s, EventDef e, DesignTeam t, Dictionary<string, string> words, Func<string, string> say)
        {
            if (t == null || t.Engineers < MinRaidTeam) return false;
            int lost = Math.Max(1, (int)Math.Round(t.Engineers * Share(s, e)));
            var rival = Pick(s, s.Rivals);
            double cost = lost * DesignOps.Salary * s.Inflation * RetentionMonths;
            words["team"] = t.Name;
            words["engineers"] = lost.ToString();
            words["rival"] = rival?.Name ?? "a start-up";
            words["cost"] = Format.Big(cost);
            var x = Raise(s, e, t.Id, $"{rival?.Name ?? "A start-up"} is hiring", say($"{rival?.Name ?? "A start-up"} is offering {lost} of your {t.Name.ToLower()}'s engineers better pay."));
            x.Cost = cost;
            x.Value = lost;
            x.Default = 1;
            x.Options.Add(new DecisionOption { Label = "Match the offer", Detail = $"Retention bonuses of {Format.Big(cost)} keep everyone." });
            var c = s.Design(t.ProjectId);
            x.Options.Add(new DecisionOption { Label = "Let them go", Detail = $"The team drops to {t.Engineers - lost} engineers{(c != null ? $" and the {c.Name} takes longer" : "")}." });
            return true;
        }

        static Decision Raise(GameState s, EventDef e, string targetId, string title, string text)
        {
            var x = new Decision
            {
                Id = s.NewId("decision"),
                EventId = e.Id,
                Kind = e.Kind,
                Title = title,
                Text = text,
                TargetId = targetId,
                RaisedTick = s.Tick,
                DeadlineTick = s.Tick + DecisionWeeks,
            };
            s.Decisions.Add(x);
            s.Log(text, Math.Max(2, e.Severity), NewsKinds.Decision);
            return x;
        }

        // ------------------------------------------------------------------ decisions

        public static Decision Find(GameState s, string id) => s.Decisions.FirstOrDefault(x => x.Id == id);

        /// <summary>Carries out an option of a pending decision.</summary>
        public static string Choose(GameData d, GameState s, string id, int option, bool timedOut = false)
        {
            var x = Find(s, id);
            if (x == null) return "That's already been decided.";
            if (option < 0 || option >= x.Options.Count) return "Pick one of the options.";
            if (!timedOut && x.Cost > 0 && IsSpend(x, option) && s.Cash < SpendOf(x, option) && Finance.LoanLimit(s) - s.Ledger.Debt < SpendOf(x, option))
                return $"You can't find {Format.Big(SpendOf(x, option))} for that.";
            s.Decisions.Remove(x);
            string late = timedOut ? "Nobody decided in time: " : "";
            var f = s.Fabs.FirstOrDefault(z => z.Id == x.TargetId);
            switch (x.Kind)
            {
                case EventKind.Quake:
                    if (option == 1)
                    {
                        s.Ledger.Book(Accounts.FabDamage, -x.Cost * 2);
                        if (f != null) f.OutageWeeks = Math.Max(1, (f.OutageWeeks + 1) / 2);
                        s.Log($"{late}rush repairs at {f?.Name} for {Format.Big(x.Cost * 2)}.", 0);
                    }
                    else
                    {
                        s.Ledger.Book(Accounts.FabDamage, -x.Cost);
                        s.Log($"{late}standard repairs at {f?.Name} for {Format.Big(x.Cost)}.", 0);
                    }
                    break;
                case EventKind.Contamination:
                    if (option == 0 && f != null)
                    {
                        Outage(f, 0, CleanWeeks, "cleaning");
                        f.D0PenaltyWeeks = CleanWeeks;
                        s.Log($"{f.Name} stops for {CleanWeeks} weeks to clean the line.", 0);
                    }
                    else s.Log($"{late}{f?.Name} keeps running through the contamination.", 0);
                    break;
                case EventKind.Strike:
                    if (option == 0)
                    {
                        s.Ledger.Book(Accounts.FabRunning, -x.Cost);
                        if (f != null) f.OutageWeeks = Math.Min(f.OutageWeeks, 1);
                        s.Log($"The strike at {f?.Name} is settled for {Format.Big(x.Cost)}.", 1);
                    }
                    else
                    {
                        Market.AdjustRep(s, -0.01);
                        s.Log($"{late}you hold out against the strike at {f?.Name}.", 0);
                    }
                    break;
                case EventKind.Errata:
                {
                    var c = s.Design(x.TargetId);
                    if (option == 0)
                    {
                        s.Ledger.Book(Accounts.Recalls, -x.Cost);
                        Market.AdjustRep(s, -0.01);
                        s.Log($"You recall and replace {Format.Units(x.Value)} {c?.Name} chips for {Format.Big(x.Cost)}.", 0);
                    }
                    else
                    {
                        Market.AdjustRep(s, -0.05);
                        foreach (var k in s.Contracts.Where(k => k.SupplierId == Market.Player && k.DesignId == x.TargetId && k.Status == ContractStatus.Active))
                        {
                            var cs = Market.CustomerOf(s, k.CustomerId);
                            if (cs != null) cs.Satisfaction = Math.Max(0, cs.Satisfaction - 0.2);
                        }
                        s.Log($"{late}an erratum for the {c?.Name} goes out. Customers grumble.", 2);
                    }
                    break;
                }
                case EventKind.TalentRaid:
                {
                    var t = DesignOps.Team(s, x.TargetId);
                    if (t == null) break;
                    if (option == 0)
                    {
                        s.Ledger.Book(Accounts.DesignStaff, -x.Cost);
                        s.Log($"Retention bonuses of {Format.Big(x.Cost)} keep {t.Name.ToLower()} together.", 0);
                    }
                    else
                    {
                        int before = t.Engineers;
                        t.Engineers = Math.Max(0, t.Engineers - (int)x.Value);
                        var c = s.Design(t.ProjectId);
                        if (c != null && c.Stage == DesignStage.Designing && t.Engineers > 0)
                            c.WeeksLeft = (int)Math.Ceiling(c.WeeksLeft * before / (double)t.Engineers);
                        s.Log($"{late}{(int)x.Value} engineers leave {t.Name.ToLower()}{(c != null && c.Stage == DesignStage.Designing ? $"; the {c.Name} slips" : "")}.", 2);
                    }
                    break;
                }
            }
            return null;
        }

        /// <summary>Whether an option costs money now, and how much.</summary>
        static bool IsSpend(Decision x, int option) => SpendOf(x, option) > 0;

        public static double SpendOf(Decision x, int option) => x.Kind switch
        {
            EventKind.Quake => option == 1 ? x.Cost * 2 : x.Cost,
            EventKind.Strike or EventKind.Errata or EventKind.TalentRaid => option == 0 ? x.Cost : 0,
            _ => 0,
        };
    }
}
