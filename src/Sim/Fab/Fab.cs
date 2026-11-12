using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace SiliconeTycoon.Sim
{
    public enum FabStage { Construction, Ramping, Running, Closed }

    /// <summary>A batch of wafers started together. Its yield is sampled when it finishes.</summary>
    public class WaferLot
    {
        public string Id;
        public string FabId;
        public string NodeId;
        /// <summary>What the lot makes. Null for test wafers until chip designs exist (milestone 2).</summary>
        public string DesignId;
        public int StartTick;
        public int DoneTick;
        public int Wafers;
        public double DieAreaMm2;
        /// <summary>Defect density when the lot started.</summary>
        public double D0;
        public int DiesPerWafer;
        public int GrossDies;
        public int GoodDies;
        /// <summary>Dies per bin (including scrap) for lots of a real design; null for test wafers.</summary>
        public Dictionary<string, int> Bins;
        public bool Done;
        /// <summary>Set once the finished dies have been handed on (sold, binned, stocked).</summary>
        public bool Delivered;
    }

    public class Fab
    {
        public string Id;
        public string Name;
        public string LocationId;
        public string NodeId;
        public FabStage Stage = FabStage.Construction;
        /// <summary>Design capacity in wafer starts per month.</summary>
        public int Capacity;

        /// <summary>Capex after subsidy, in dollars of the year it was ordered.</summary>
        public double Capex;
        /// <summary>The same capex in 1970 dollars, used to inflate running costs over time.</summary>
        public double CapexReal;
        public double CapexPaid;
        public int BuildWeeksTotal;
        /// <summary>True while an existing fab is being re-equipped for a newer node.</summary>
        public bool Retooling;
        /// <summary>Times this fab has been retooled.</summary>
        public int Retools;
        public int BuildWeeksLeft;
        public int OrderedTick;

        public int RunningWeeks;
        /// <summary>Months of normal-volume learning accumulated (see <see cref="LearningCurve"/>).</summary>
        public double Learning;
        public double CumulativeWafers;

        /// <summary>Monthly process-engineering spend, in current dollars.</summary>
        public double EngineeringBudget;
        /// <summary>Wafer starts per month the player wants.</summary>
        public int PlannedStarts;
        /// <summary>Die size of what the fab is making. Test wafers use a nominal 10 mm² die.</summary>
        public double DieAreaMm2 = 10;
        /// <summary>Released design the fab makes; null runs test wafers that teach the process but aren't sold.</summary>
        public string DesignId;

        public int StartsThisMonth;
        public int LastMonthStarts;
        public double LastUtilisation;

        public double BookValue;
        public double MonthlyDepreciation;

        public List<WaferLot> Lots = new List<WaferLot>();

        [JsonIgnore] public bool Producing => Stage == FabStage.Ramping || Stage == FabStage.Running;
    }

    public class FabQuote
    {
        public double Capex;
        public double Subsidy;
        public double NetCapex;
        public double MonthlyRunning;
        public double WaferCost;
        public double RecommendedEngineering;
        public int BuildMonths;
        public int RampMonths;
        public string Problem;
    }

    /// <summary>Building and running fabs: construction, ramp, learning, wafer starts and lot yields.</summary>
    public static class FabOps
    {
        public const int DepreciationMonths = 60;
        /// <summary>Monthly running cost (staff, power, maintenance) as a share of capex, before location factors.</summary>
        public const double RunningShare = 0.009;
        /// <summary>Share of a closed fab's remaining value recovered by selling its equipment.</summary>
        public const double SalvageShare = 0.15;
        /// <summary>Retooling cost as a share of a new fab of the same size on the new node.</summary>
        public const double RetoolShare = 0.55;
        /// <summary>Extra share when the new node uses bigger wafers (all wafer-handling tools change).</summary>
        public const double RetoolWaferChangeShare = 0.15;
        /// <summary>Downtime as a share of the new node's normal build time.</summary>
        public const double RetoolTimeShare = 0.6;
        /// <summary>Share of the old equipment's remaining value written off when it's replaced.</summary>
        public const double RetoolWriteOff = 0.7;
        public const int MinCapacity = 100;
        public const int MaxCapacity = 200000;
        /// <summary>How many dies of a lot are simulated individually; the result is scaled to the lot.</summary>
        public const int SampleDies = 1000;
        /// <summary>Finished lots older than this are dropped from the fab's list.</summary>
        public const int KeepLotsWeeks = Calendar.WeeksPerYear * 2;

        static double RunningFactor(LocationDef l) => 0.55 + 0.3 * l.LabourIndex + 0.15 * l.PowerIndex;
        static double WaferFactor(LocationDef l) => 0.85 + 0.1 * l.LabourIndex + 0.05 * l.PowerIndex;

        public static FabQuote Quote(GameData d, GameState s, string nodeId, string locationId, int capacity)
        {
            var q = new FabQuote();
            var n = d.Node(nodeId);
            var l = d.Location(locationId);
            if (n == null) { q.Problem = "Unknown process."; return q; }
            if (l == null) { q.Problem = "Unknown location."; return q; }

            double infl = s.Inflation;
            double capexReal = n.FabCostPer1000 * Math.Pow(Math.Max(capacity, 1) / 1000.0, 0.85) * (0.85 + 0.15 * l.LabourIndex);
            q.Capex = capexReal * infl;
            q.Subsidy = q.Capex * l.Subsidy;
            q.NetCapex = q.Capex - q.Subsidy;
            q.MonthlyRunning = capexReal * infl * RunningShare * RunningFactor(l);
            q.WaferCost = n.WaferCost * infl * WaferFactor(l);
            q.RecommendedEngineering = capexReal * infl * 0.004;
            q.BuildMonths = n.BuildMonths;
            q.RampMonths = n.RampMonths;

            if (!s.NodeReady(nodeId)) q.Problem = $"The {n.Name} process isn't ready yet.";
            else if (s.Year < l.From || s.Year > l.To) q.Problem = $"You can't build a fab in {l.Name} in {s.Year}.";
            else if (capacity < MinCapacity || capacity > MaxCapacity) q.Problem = $"Capacity must be between {MinCapacity:N0} and {MaxCapacity:N0} wafer starts a month.";
            return q;
        }

        public static string Build(GameData d, GameState s, string name, string nodeId, string locationId, int capacity)
        {
            var q = Quote(d, s, nodeId, locationId, capacity);
            if (q.Problem != null) return q.Problem;
            double firstPayment = q.NetCapex / q.BuildMonths;
            if (s.Cash < firstPayment) return $"You need at least {Format.Money(firstPayment)} for the first month of construction.";

            var n = d.Node(nodeId);
            var l = d.Location(locationId);
            var f = new Fab
            {
                Id = s.NewId("fab"),
                Name = string.IsNullOrWhiteSpace(name) ? $"{l.Name} {n.Name} fab" : name.Trim(),
                LocationId = locationId,
                NodeId = nodeId,
                Capacity = capacity,
                Capex = q.NetCapex,
                CapexReal = q.NetCapex / s.Inflation,
                BuildWeeksTotal = q.BuildMonths * Calendar.WeeksPerMonth,
                BuildWeeksLeft = q.BuildMonths * Calendar.WeeksPerMonth,
                OrderedTick = s.Tick,
                EngineeringBudget = q.RecommendedEngineering,
                PlannedStarts = capacity,
            };
            s.Fabs.Add(f);
            string grant = q.Subsidy > 0 ? $" Government grants cover {Format.Big(q.Subsidy)}." : "";
            s.Log($"Construction starts on {f.Name}: {capacity:N0} wafers a month, {Format.Big(q.NetCapex)} over {q.BuildMonths} months.{grant}", 1);
            return null;
        }

        public static double RunningCost(GameData d, GameState s, Fab f) => f.CapexReal * s.Inflation * RunningShare * RunningFactor(d.Location(f.LocationId));
        public static double WaferCost(GameData d, GameState s, Fab f) => d.Node(f.NodeId).WaferCost * s.Inflation * WaferFactor(d.Location(f.LocationId));
        public static double RecommendedEngineering(GameState s, Fab f) => f.CapexReal * s.Inflation * 0.004;

        public static double D0(GameData d, Fab f) => LearningCurve.D0(d.Node(f.NodeId), f.Learning);

        /// <summary>Wafer starts per month the fab can take right now: none while building, climbing through the ramp.</summary>
        public static int AvailableCapacity(GameData d, Fab f)
        {
            if (f.Stage == FabStage.Running) return f.Capacity;
            if (f.Stage != FabStage.Ramping) return 0;
            double rampWeeks = d.Node(f.NodeId).RampMonths * Calendar.WeeksPerMonth;
            double share = Math.Clamp(0.1 + 0.9 * f.RunningWeeks / rampWeeks, 0.1, 1.0);
            return (int)Math.Floor(f.Capacity * share);
        }

        public static string SetPlannedStarts(Fab f, int starts)
        {
            if (starts < 0) return "Wafer starts can't be negative.";
            f.PlannedStarts = Math.Min(starts, f.Capacity);
            return null;
        }

        /// <summary>Points the fab at a released design on its node (or null for test wafers). Lots already started finish as they were.</summary>
        public static string AssignDesign(GameData d, GameState s, Fab f, string designId)
        {
            if (designId == null)
            {
                f.DesignId = null;
                f.DieAreaMm2 = 10;
                return null;
            }
            var c = s.Design(designId);
            if (c == null) return "Unknown chip.";
            if (c.Stage != DesignStage.Released) return $"Release the {c.Name} before putting it into production.";
            if (c.NodeId != f.NodeId) return $"The {c.Name} is laid out for {d.Node(c.NodeId).Name}, but {f.Name} runs {d.Node(f.NodeId).Name}.";
            f.DesignId = c.Id;
            f.DieAreaMm2 = c.DieAreaMm2;
            s.Log($"{f.Name} switches production to the {c.Name}.", 0);
            return null;
        }

        /// <summary>What re-equipping a fab for a newer node would cost and how long it would be down.</summary>
        public static FabQuote RetoolQuote(GameData d, GameState s, Fab f, string nodeId)
        {
            var n = d.Node(nodeId);
            var old = d.Node(f.NodeId);
            var fresh = Quote(d, s, nodeId, f.LocationId, f.Capacity);
            var q = new FabQuote
            {
                Capex = fresh.Capex,
                Subsidy = fresh.Subsidy,
                MonthlyRunning = fresh.MonthlyRunning,
                WaferCost = fresh.WaferCost,
                RecommendedEngineering = fresh.RecommendedEngineering,
                RampMonths = fresh.RampMonths,
                Problem = fresh.Problem,
            };
            if (n == null) { q.Problem = "Unknown process."; return q; }
            double share = RetoolShare + (n.WaferMm > old.WaferMm ? RetoolWaferChangeShare : 0);
            q.NetCapex = fresh.NetCapex * share;
            q.BuildMonths = Math.Max(1, (int)Math.Ceiling(n.BuildMonths * RetoolTimeShare));
            if (q.Problem != null) return q;
            if (f.Stage == FabStage.Closed) q.Problem = $"{f.Name} is closed.";
            else if (f.Stage == FabStage.Construction) q.Problem = $"{f.Name} is already being built or retooled.";
            else if (n.FeatureNm >= old.FeatureNm) q.Problem = $"{f.Name} already runs {old.Name}; retool it to a newer node.";
            return q;
        }

        /// <summary>
        /// Re-equips a fab for a newer node: cheaper and quicker than a new fab, but the fab makes nothing until it's back
        /// up, most of the old equipment's value is written off, wafers in progress are scrapped and the process learning
        /// starts again from the new node's launch defect density.
        /// </summary>
        public static string Retool(GameData d, GameState s, Fab f, string nodeId)
        {
            var q = RetoolQuote(d, s, f, nodeId);
            if (q.Problem != null) return q.Problem;
            double firstPayment = q.NetCapex / q.BuildMonths;
            if (s.Cash < firstPayment) return $"You need at least {Format.Money(firstPayment)} for the first month of retooling.";

            var old = d.Node(f.NodeId);
            var n = d.Node(nodeId);
            double writeOff = f.BookValue * RetoolWriteOff;
            if (writeOff > 0) s.Ledger.Accrue(Accounts.Retooling, -writeOff);
            f.BookValue -= writeOff;
            f.MonthlyDepreciation = 0;
            f.Lots.RemoveAll(l => !l.Done);
            f.NodeId = nodeId;
            f.DesignId = null;
            f.DieAreaMm2 = 10;
            f.Stage = FabStage.Construction;
            f.Retooling = true;
            f.Retools++;
            f.Capex = q.NetCapex;
            f.CapexPaid = 0;
            f.CapexReal = (q.Capex - q.Subsidy) / s.Inflation;   // running costs follow the new equipment, like a new fab's
            f.BuildWeeksTotal = q.BuildMonths * Calendar.WeeksPerMonth;
            f.BuildWeeksLeft = f.BuildWeeksTotal;
            f.OrderedTick = s.Tick;
            f.RunningWeeks = 0;
            f.Learning = 0;
            f.StartsThisMonth = 0;
            f.LastUtilisation = 0;
            f.EngineeringBudget = q.RecommendedEngineering;
            f.PlannedStarts = f.Capacity;
            s.Log($"{f.Name} shuts down to retool from {old.Name} to {n.Name}: {Format.Big(q.NetCapex)} over {q.BuildMonths} months. " +
                  $"{Format.Big(writeOff)} of old equipment is written off.", 1);
            return null;
        }

        /// <summary>
        /// Closes a fab for good: running and engineering costs stop, wafers in progress are scrapped, the equipment is sold
        /// for <see cref="SalvageShare"/> of its remaining value and the rest is written off. Works on a fab under construction too.
        /// </summary>
        public static string Close(GameData d, GameState s, Fab f)
        {
            if (f.Stage == FabStage.Closed) return $"{f.Name} is already closed.";
            double value = f.Stage == FabStage.Construction ? f.CapexPaid : f.BookValue;
            double salvage = value * SalvageShare;
            s.Ledger.Cash += salvage;
            if (value - salvage > 0) s.Ledger.Accrue(Accounts.FabClosure, -(value - salvage));
            f.Stage = FabStage.Closed;
            f.BookValue = 0;
            f.MonthlyDepreciation = 0;
            f.PlannedStarts = 0;
            f.DesignId = null;
            f.Lots.RemoveAll(l => !l.Done);
            s.Log($"{f.Name} closes. The equipment sells for {Format.Big(salvage)}; {Format.Big(value - salvage)} is written off.", 2);
            return null;
        }

        public static string SetEngineering(Fab f, double perMonth)
        {
            if (perMonth < 0) return "The engineering budget can't be negative.";
            f.EngineeringBudget = perMonth;
            return null;
        }

        public static void WeekTick(GameData d, GameState s, Fab f)
        {
            var n = d.Node(f.NodeId);
            if (f.Stage == FabStage.Construction)
            {
                f.BuildWeeksLeft--;
                if (f.BuildWeeksLeft > 0) return;
                double rest = f.Capex - f.CapexPaid;
                if (rest > 0.005)
                {
                    s.Ledger.Invest(rest);
                    f.CapexPaid += rest;
                }
                f.BookValue += f.CapexPaid;
                f.MonthlyDepreciation = f.BookValue / DepreciationMonths;
                f.Stage = FabStage.Ramping;
                f.RunningWeeks = 0;
                s.Log(f.Retooling
                    ? $"{f.Name} is back up on {n.Name}. First wafers are going in; yields will start poor and improve."
                    : $"{f.Name} is built. First wafers are going in; yields will start poor and improve.", 1);
                f.Retooling = false;
                return;
            }
            if (!f.Producing) return;

            f.RunningWeeks++;
            double rate = LearningCurve.Rate(f.LastUtilisation, LearningCurve.EngineerFactor(f.EngineeringBudget, RecommendedEngineering(s, f)));
            f.Learning += rate / Calendar.WeeksPerMonth;
            if (f.Stage == FabStage.Ramping && f.RunningWeeks >= n.RampMonths * Calendar.WeeksPerMonth)
            {
                f.Stage = FabStage.Running;
                s.Log($"{f.Name} has finished ramping and runs at full capacity.", 1);
            }

            StartWafers(d, s, f, n);
            FinishLots(d, s, f);
        }

        static void StartWafers(GameData d, GameState s, Fab f, ProcessNodeDef n)
        {
            int target = Math.Min(f.PlannedStarts, AvailableCapacity(d, f));
            int week = Calendar.WeekInMonth(s.Tick);
            int wafers = target / Calendar.WeeksPerMonth + (week < target % Calendar.WeeksPerMonth ? 1 : 0);
            if (wafers <= 0) return;

            double area = YieldModel.WithScribe(f.DieAreaMm2);
            int dpw = YieldModel.DiesPerWafer(n.WaferMm, area);
            var lot = new WaferLot
            {
                Id = s.NewId("lot"),
                FabId = f.Id,
                NodeId = f.NodeId,
                DesignId = f.DesignId,
                StartTick = s.Tick,
                DoneTick = s.Tick + n.CycleWeeks,
                Wafers = wafers,
                DieAreaMm2 = f.DieAreaMm2,
                D0 = D0(d, f),
                DiesPerWafer = dpw,
                GrossDies = dpw * wafers,
            };
            f.Lots.Add(lot);
            f.StartsThisMonth += wafers;
            f.CumulativeWafers += wafers;
            s.Ledger.Book(Accounts.Materials, -wafers * WaferCost(d, s, f));
        }

        static void FinishLots(GameData d, GameState s, Fab f)
        {
            foreach (var lot in f.Lots)
            {
                if (lot.Done || lot.DoneTick > s.Tick) continue;
                var design = s.Design(lot.DesignId);
                if (design != null)
                {
                    lot.Bins = Binning.Lot(d, s.Rng, design, lot.D0, lot.GrossDies);
                    lot.GoodDies = Binning.Good(lot.Bins);
                }
                else
                {
                    int sample = Math.Min(lot.GrossDies, SampleDies);
                    double y = YieldModel.SampleYield(s.Rng, sample, lot.D0, lot.DieAreaMm2);
                    lot.GoodDies = (int)Math.Round(lot.GrossDies * y);
                }
                lot.Done = true;
            }
            f.Lots.RemoveAll(l => l.Done && l.Delivered && l.DoneTick < s.Tick - KeepLotsWeeks);
        }

        public static void MonthEnd(GameData d, GameState s, Fab f)
        {
            if (f.Stage == FabStage.Construction)
            {
                double perMonth = f.Capex / (f.BuildWeeksTotal / (double)Calendar.WeeksPerMonth);
                double payment = Math.Min(f.Capex - f.CapexPaid, perMonth);
                if (payment > 0)
                {
                    s.Ledger.Invest(payment);
                    f.CapexPaid += payment;
                }
                return;
            }
            if (!f.Producing) return;

            s.Ledger.Book(Accounts.FabRunning, -RunningCost(d, s, f));
            s.Ledger.Book(Accounts.FabEngineering, -f.EngineeringBudget);
            if (f.BookValue > 0)
            {
                double dep = Math.Min(f.BookValue, f.MonthlyDepreciation);
                s.Ledger.Accrue(Accounts.Depreciation, -dep);
                f.BookValue -= dep;
            }

            int available = AvailableCapacity(d, f);
            f.LastUtilisation = available > 0 ? Math.Min(1.0, f.StartsThisMonth / (double)available) : 0;
            f.LastMonthStarts = f.StartsThisMonth;
            f.StartsThisMonth = 0;
        }

        /// <summary>Finished lots whose dies haven't been handed on yet.</summary>
        public static IEnumerable<WaferLot> Undelivered(Fab f) => f.Lots.Where(l => l.Done && !l.Delivered);
    }
}
