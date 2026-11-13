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
        /// <summary>Wafers made for a foundry client's contract (no design of yours).</summary>
        public string ContractId;
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

    /// <summary>Wafer starts a month a fab gives one product. DesignId null = test wafers (learning only).</summary>
    /// <summary>One month of a fab's record, for its charts.</summary>
    public class FabMonth
    {
        public int Month;
        public int Starts;
        /// <summary>Starts as a share of the fab's full capacity.</summary>
        public double Utilisation;
        public double D0;
        public double Good;
    }

    public class FabAllocation
    {
        public string DesignId;
        public int Starts;
        /// <summary>Wafers for a foundry client's contract (DesignId is null then).</summary>
        public string ContractId;
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

        /// <summary>Wafer starts a month being added by an expansion in progress (0 = none).</summary>
        public int ExpandCapacity;
        public double ExpandCapex;
        public double ExpandPaid;
        public int ExpandWeeksTotal;
        public int ExpandWeeksLeft;
        public int BuildWeeksLeft;
        public int OrderedTick;

        public int RunningWeeks;
        /// <summary>Months of normal-volume learning accumulated (see <see cref="LearningCurve"/>).</summary>
        public double Learning;
        public double CumulativeWafers;

        /// <summary>Monthly process-engineering spend, in current dollars.</summary>
        public double EngineeringBudget;
        /// <summary>What the fab makes: wafer starts a month per product. A fab can run several chips at once.</summary>
        public List<FabAllocation> Allocations = new List<FabAllocation>();
        /// <summary>Saves before version 4 only (one product per fab).</summary>
        [JsonPropertyName("DesignId"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public string LegacyDesignId;
        [JsonPropertyName("PlannedStarts"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int LegacyPlannedStarts;

        /// <summary>Total wafer starts a month planned across all products. Setting it scales every product's share.</summary>
        [JsonIgnore]
        public int PlannedStarts
        {
            get => Allocations.Sum(a => a.Starts);
            set => FabOps.SetTotalStarts(this, value);
        }

        /// <summary>The main chip the fab makes (most wafer starts), or null if it only runs test wafers.</summary>
        [JsonIgnore]
        public string DesignId => Allocations.Where(a => a.ContractId == null && a.DesignId != null && a.Starts > 0).OrderByDescending(a => a.Starts).FirstOrDefault()?.DesignId;

        public int StartsFor(string designId) => Allocations.Where(a => a.ContractId == null && a.DesignId == designId).Sum(a => a.Starts);
        /// <summary>Wafer starts a month reserved for foundry clients.</summary>
        [JsonIgnore] public int ContractStarts => Allocations.Where(a => a.ContractId != null).Sum(a => a.Starts);
        /// <summary>Share of an external supply the foundry actually delivers this month (it squeezes customers in shortages).</summary>
        public double SupplyFactor = 1;
        public bool Makes(string designId) => StartsFor(designId) > 0;

        public int StartsThisMonth;
        public int LastMonthStarts;
        public double LastUtilisation;

        public double BookValue;
        public double MonthlyDepreciation;

        public List<WaferLot> Lots = new List<WaferLot>();

        /// <summary>Weeks left of a disruption (earthquake, fire, strike...) and the share of capacity it leaves.</summary>
        public int OutageWeeks;
        public double OutageFactor = 1;
        public string OutageReason;
        /// <summary>Weeks left of raised defect density (contamination) and its multiplier.</summary>
        public int D0PenaltyWeeks;
        public double D0Penalty = 1;
        /// <summary>Good dies from lots that finished this month (all products and test wafers).</summary>
        public double GoodThisMonth;
        public List<FabMonth> History = new List<FabMonth>();

        /// <summary>
        /// A wafer supply agreement with a foundry rather than a fab you own: no capex, running costs or learning of your
        /// own; you pay WaferPrice a wafer and at least MinTake of the reserved wafers each month until SupplyEndMonth.
        /// </summary>
        public bool External;
        public string FoundryId;
        public double WaferPrice;
        public int SupplyEndMonth;
        public double MinTake;
        /// <summary>Sold to a foundry (shown as gone, like a closed fab).</summary>
        public bool Sold;

        [JsonIgnore] public bool Producing => Stage == FabStage.Ramping || Stage == FabStage.Running;
        [JsonIgnore] public bool Expanding => ExpandCapacity > 0;
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
        /// <summary>Expansion build time as a share of the node's normal build time.</summary>
        public const double ExpandTimeShare = 0.5;
        /// <summary>Share of your learning on the previous node a new fab on the next node starts with.</summary>
        public const double PrevNodeTransfer = 0.4;
        /// <summary>Share of your learning on the same node a second fab starts with.</summary>
        public const double SameNodeTransfer = 0.6;
        /// <summary>Most months of learning a new fab can start with.</summary>
        public const double MaxTransfer = 12;
        public const int MinExpansion = 100;
        public const int MinCapacity = 100;
        public const int MaxCapacity = 200000;
        /// <summary>How many dies of a lot are simulated individually; the result is scaled to the lot.</summary>
        public const int SampleDies = 1000;
        public const int HistoryMonths = 120;
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
            q.Subsidy = q.Capex * Math.Min(0.6, l.Subsidy + Events.ExtraSubsidy(s, l.Id));
            q.NetCapex = q.Capex - q.Subsidy;
            q.MonthlyRunning = capexReal * infl * RunningShare * RunningFactor(l);
            q.WaferCost = n.WaferCost * infl * WaferFactor(l) * Events.MaterialsFactor(s);
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
                Name = string.IsNullOrWhiteSpace(name) ? DefaultName(l, n) : name.Trim(),
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

        /// <summary>A fab that is already running (for company starts): built some months ago, paid for, part-depreciated.</summary>
        public static Fab Existing(GameData d, GameState s, string nodeId, string locationId, int capacity, double learningMonths, int ageMonths)
        {
            var q = Quote(d, s, nodeId, locationId, capacity);
            var n = d.Node(nodeId);
            var l = d.Location(locationId);
            double book = q.NetCapex * Math.Max(0, 1 - ageMonths / (double)DepreciationMonths);
            var f = new Fab
            {
                Id = s.NewId("fab"),
                Name = DefaultName(l, n),
                LocationId = locationId,
                NodeId = nodeId,
                Capacity = capacity,
                Capex = q.NetCapex,
                CapexReal = q.NetCapex / s.Inflation,
                CapexPaid = q.NetCapex,
                BookValue = book,
                MonthlyDepreciation = q.NetCapex / DepreciationMonths,
                Stage = FabStage.Running,
                Learning = learningMonths,
                EngineeringBudget = q.RecommendedEngineering,
                OrderedTick = s.Tick,
            };
            f.Allocations.Add(new FabAllocation { DesignId = null, Starts = capacity / 4 });
            s.Fabs.Add(f);
            s.NodeExperience[nodeId] = Math.Max(FabOps.Experience(s, nodeId), learningMonths);
            return f;
        }

        /// <summary>The name a fab gets when the player doesn't choose one. Retooling renames fabs that still have it.</summary>
        public static string DefaultName(LocationDef l, ProcessNodeDef n) => $"{l.Name} {n.Name} fab";

        /// <summary>Renames a fab.</summary>
        public static string Rename(GameState s, Fab f, string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "The fab needs a name.";
            name = name.Trim();
            if (s.Fabs.Any(x => x != f && x.Name == name)) return "You already have a fab with that name.";
            f.Name = name;
            return null;
        }

        public static double RunningCost(GameData d, GameState s, Fab f) => f.External ? 0 : f.CapexReal * s.Inflation * RunningShare * RunningFactor(d.Location(f.LocationId));
        public static double WaferCost(GameData d, GameState s, Fab f) => f.External ? f.WaferPrice : d.Node(f.NodeId).WaferCost * s.Inflation * WaferFactor(d.Location(f.LocationId)) * Events.MaterialsFactor(s);
        public static double RecommendedEngineering(GameState s, Fab f) => f.CapexReal * s.Inflation * 0.004;

        public static double D0(GameData d, Fab f) => LearningCurve.D0(d.Node(f.NodeId), f.Learning) * (f.D0PenaltyWeeks > 0 ? Math.Max(1, f.D0Penalty) : 1);

        /// <summary>Wafer starts per month the fab can take right now: none while building, climbing through the ramp.</summary>
        public static int AvailableCapacity(GameData d, Fab f)
        {
            double outage = (f.OutageWeeks > 0 ? Math.Clamp(f.OutageFactor, 0, 1) : 1) * (f.External ? Math.Clamp(f.SupplyFactor, 0, 1) : 1);
            if (f.Stage == FabStage.Running) return (int)Math.Floor(f.Capacity * outage);
            if (f.Stage != FabStage.Ramping) return 0;
            double rampWeeks = d.Node(f.NodeId).RampMonths * Calendar.WeeksPerMonth;
            double share = Math.Clamp(0.1 + 0.9 * f.RunningWeeks / rampWeeks, 0.1, 1.0);
            return (int)Math.Floor(f.Capacity * share * outage);
        }

        /// <summary>Die area wafers for a product are cut into. Test wafers use a nominal 10 mm² die.</summary>
        public static double DieArea(GameState s, string designId) => s.Design(designId)?.DieAreaMm2 ?? 10;

        static string CheckProduct(GameData d, GameState s, Fab f, string designId)
        {
            if (designId == null) return null;
            var c = s.Design(designId);
            if (c == null) return "Unknown chip.";
            if (c.Stage != DesignStage.Released) return $"Release the {c.Name} before putting it into production.";
            if (c.NodeId != f.NodeId) return $"The {c.Name} is laid out for {d.Node(c.NodeId).Name}, but {f.Name} runs {d.Node(f.NodeId).Name}.";
            return null;
        }

        /// <summary>
        /// Sets the wafer starts a month for one product (null = test wafers). 0 stops making it. The fab's products together
        /// can't plan more than its capacity; while ramping they share what's available.
        /// </summary>
        public static string SetAllocation(GameData d, GameState s, Fab f, string designId, int starts)
        {
            if (f.Stage == FabStage.Closed) return $"{f.Name} is closed.";
            if (starts < 0) return "Wafer starts can't be negative.";
            string err = CheckProduct(d, s, f, designId);
            if (err != null && starts > 0) return err;
            int others = f.Allocations.Where(a => !(a.ContractId == null && a.DesignId == designId)).Sum(a => a.Starts);
            if (others + starts > f.Capacity) return $"{f.Name} can start {f.Capacity:N0} wafers a month; the other products{(f.ContractStarts > 0 ? " and foundry clients" : "")} already take {others:N0}.";
            bool isNew = f.StartsFor(designId) == 0 && starts > 0;
            f.Allocations.RemoveAll(a => a.ContractId == null && a.DesignId == designId);
            if (starts > 0) f.Allocations.Add(new FabAllocation { DesignId = designId, Starts = starts });
            if (isNew && designId != null) s.Log($"{f.Name} starts making the {s.Design(designId).Name}.", 0);
            return null;
        }

        /// <summary>Sets the fab's total wafer starts, scaling each product's share to fit (test wafers if it has none).</summary>
        public static string SetPlannedStarts(Fab f, int starts)
        {
            if (starts < 0) return "Wafer starts can't be negative.";
            SetTotalStarts(f, starts);
            return null;
        }

        /// <summary>Sets the total for your own products (foundry clients' wafers stay as contracted).</summary>
        internal static void SetTotalStarts(Fab f, int total)
        {
            int contracts = f.ContractStarts;
            total = Math.Clamp(total - contracts, 0, Math.Max(0, f.Capacity - contracts));
            var own = f.Allocations.Where(a => a.ContractId == null).ToList();
            int sum = own.Sum(a => a.Starts);
            if (own.Count == 0)
            {
                if (total > 0) f.Allocations.Add(new FabAllocation { DesignId = null, Starts = total });
                return;
            }
            if (sum <= 0)
            {
                own[0].Starts = total;
                for (int i = 1; i < own.Count; i++) own[i].Starts = 0;
                return;
            }
            int assigned = 0;
            for (int i = 0; i < own.Count; i++)
            {
                var a = own[i];
                a.Starts = i == own.Count - 1 ? total - assigned : (int)Math.Round(a.Starts * (double)total / sum);
                a.Starts = Math.Max(0, a.Starts);
                assigned += a.Starts;
            }
        }

        /// <summary>Makes the fab run one product only (null = test wafers), keeping its total wafer starts. Lots already started finish as they were.</summary>
        public static string AssignDesign(GameData d, GameState s, Fab f, string designId)
        {
            string err = CheckProduct(d, s, f, designId);
            if (err != null) return err;
            int total = (f.PlannedStarts > 0 ? f.PlannedStarts : f.Capacity) - f.ContractStarts;
            f.Allocations.RemoveAll(a => a.ContractId == null);
            if (total > 0) f.Allocations.Add(new FabAllocation { DesignId = designId, Starts = total });
            if (designId != null) s.Log($"{f.Name} switches production to the {s.Design(designId).Name}.", 0);
            return null;
        }

        /// <summary>What re-equipping a fab for a newer node would cost and how long it would be down.</summary>
        public static FabQuote RetoolQuote(GameData d, GameState s, Fab f, string nodeId)
        {
            if (f.External) return new FabQuote { Problem = "That's a foundry's line: you can't retool it." };
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
            else if (f.Expanding) q.Problem = $"{f.Name} is being expanded. Retool it once the expansion is finished.";
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
            var site = d.Location(f.LocationId);
            if (site != null && f.Name == DefaultName(site, d.Node(f.NodeId))) f.Name = DefaultName(site, d.Node(nodeId));
            f.NodeId = nodeId;
            f.Allocations.Clear();
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
            f.Allocations.Add(new FabAllocation { DesignId = null, Starts = f.Capacity });
            s.Log($"{f.Name} shuts down to retool from {old.Name} to {n.Name}: {Format.Big(q.NetCapex)} over {q.BuildMonths} months. " +
                  $"{Format.Big(writeOff)} of old equipment is written off.", 1);
            return null;
        }

        /// <summary>
        /// What adding capacity to a working fab would cost: the difference between a fab of the new size and one of the
        /// current size, at today's prices on this site. Bigger fabs cost less per wafer, so expanding is cheaper than
        /// building a second fab of the same size, and the fab keeps running while the new tools go in.
        /// </summary>
        public static FabQuote ExpandQuote(GameData d, GameState s, Fab f, int extra)
        {
            if (f.External) return new FabQuote { Problem = "That's a foundry's line: renew or sign a bigger agreement instead." };
            var now = Quote(d, s, f.NodeId, f.LocationId, f.Capacity);
            var bigger = Quote(d, s, f.NodeId, f.LocationId, f.Capacity + extra);
            var n = d.Node(f.NodeId);
            var q = new FabQuote
            {
                Capex = bigger.Capex - now.Capex,
                Subsidy = bigger.Subsidy - now.Subsidy,
                NetCapex = bigger.NetCapex - now.NetCapex,
                MonthlyRunning = bigger.MonthlyRunning - now.MonthlyRunning,
                WaferCost = bigger.WaferCost,
                RecommendedEngineering = bigger.RecommendedEngineering,
                BuildMonths = Math.Max(1, (int)Math.Ceiling(n.BuildMonths * ExpandTimeShare)),
                RampMonths = 0,
            };
            if (f.Stage == FabStage.Closed) q.Problem = $"{f.Name} is closed.";
            else if (!f.Producing) q.Problem = $"{f.Name} has to be built and running before you can expand it.";
            else if (f.Expanding) q.Problem = $"{f.Name} is already being expanded.";
            else if (extra < MinExpansion) q.Problem = $"Add at least {MinExpansion} wafer starts a month.";
            else if (f.Capacity + extra > MaxCapacity) q.Problem = $"A fab can't run more than {MaxCapacity:N0} wafer starts a month.";
            return q;
        }

        /// <summary>Starts adding capacity to a working fab. It keeps producing at its current capacity until the new tools are in.</summary>
        public static string Expand(GameData d, GameState s, Fab f, int extra)
        {
            var q = ExpandQuote(d, s, f, extra);
            if (q.Problem != null) return q.Problem;
            double firstPayment = q.NetCapex / q.BuildMonths;
            if (s.Cash < firstPayment) return $"You need at least {Format.Money(firstPayment)} for the first month of the expansion.";
            f.ExpandCapacity = extra;
            f.ExpandCapex = q.NetCapex;
            f.ExpandPaid = 0;
            f.ExpandWeeksTotal = q.BuildMonths * Calendar.WeeksPerMonth;
            f.ExpandWeeksLeft = f.ExpandWeeksTotal;
            s.Log($"{f.Name} starts an expansion: +{extra:N0} wafer starts a month for {Format.Big(q.NetCapex)} over {q.BuildMonths} months.", 1);
            return null;
        }

        static void FinishExpansion(GameState s, Fab f)
        {
            double rest = f.ExpandCapex - f.ExpandPaid;
            if (rest > 0.005)
            {
                s.Ledger.Invest(rest);
                f.ExpandPaid += rest;
            }
            int extra = f.ExpandCapacity;
            bool full = f.PlannedStarts >= f.Capacity;
            f.Capacity += extra;
            if (full) f.PlannedStarts = f.Capacity;             // a fab that was running flat out keeps doing so
            f.BookValue += f.ExpandPaid;
            f.MonthlyDepreciation = f.BookValue / DepreciationMonths;
            f.CapexReal += f.ExpandPaid / s.Inflation;           // running costs grow with the new equipment
            f.ExpandCapacity = 0;
            f.ExpandCapex = 0;
            f.ExpandPaid = 0;
            s.Log($"{f.Name}'s expansion is finished: {f.Capacity:N0} wafer starts a month.", 1);
        }

        /// <summary>
        /// Closes a fab for good: running and engineering costs stop, wafers in progress are scrapped, the equipment is sold
        /// for <see cref="SalvageShare"/> of its remaining value and the rest is written off. Works on a fab under construction too.
        /// </summary>
        public static string Close(GameData d, GameState s, Fab f)
        {
            if (f.Stage == FabStage.Closed) return $"{f.Name} is already closed.";
            if (f.External) return Foundry.CancelSupply(d, s, f);
            double value = (f.Stage == FabStage.Construction ? f.CapexPaid + f.BookValue : f.BookValue)   // a retooling fab still carries its old equipment
                           + f.ExpandPaid;                                                                  // and an unfinished expansion is lost too
            double salvage = value * SalvageShare;
            s.Ledger.Cash += salvage;
            if (value - salvage > 0) s.Ledger.Accrue(Accounts.FabClosure, -(value - salvage));
            f.Stage = FabStage.Closed;
            f.ExpandCapacity = 0;
            f.ExpandPaid = 0;
            f.BookValue = 0;
            f.MonthlyDepreciation = 0;
            f.Allocations.Clear();
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
                if (f.External)
                {
                    f.Stage = FabStage.Running;
                    f.RunningWeeks = 0;
                    s.Log($"{f.Name}: your first wafers are going into the line.", 1);
                    return;
                }
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
                f.Learning = Math.Max(f.Learning, StartingLearning(d, s, f.NodeId));
                s.Log(f.Retooling
                    ? $"{f.Name} is back up on {n.Name}. First wafers are going in; yields will start poor and improve."
                    : $"{f.Name} is built. First wafers are going in; yields will start poor and improve.", 1);
                f.Retooling = false;
                return;
            }
            if (!f.Producing) return;

            if (f.Expanding && --f.ExpandWeeksLeft <= 0) FinishExpansion(s, f);
            if (f.D0PenaltyWeeks > 0 && --f.D0PenaltyWeeks == 0) s.Log($"{f.Name}'s yields are back to normal.", 1);
            if (f.OutageWeeks > 0 && --f.OutageWeeks == 0)
            {
                s.Log($"{f.Name} is back to full production after {f.OutageReason ?? "the disruption"}.", 1);
                f.OutageFactor = 1;
                f.OutageReason = null;
            }
            f.RunningWeeks++;
            // a foundry keeps learning on its own volume; your fabs learn from yours
            double rate = f.External ? 1.0 : LearningCurve.Rate(f.LastUtilisation, LearningCurve.EngineerFactor(f.EngineeringBudget, RecommendedEngineering(s, f)));
            f.Learning += rate / Calendar.WeeksPerMonth;
            if (f.Stage == FabStage.Ramping && f.RunningWeeks >= n.RampMonths * Calendar.WeeksPerMonth)
            {
                f.Stage = FabStage.Running;
                s.Log($"{f.Name} has finished ramping and runs at full capacity.", 1);
            }

            StartWafers(d, s, f, n);
            FinishLots(d, s, f);
        }

        /// <summary>Wafer starts a month each product actually gets: its plan, scaled down if the fab can't run them all (while ramping).</summary>
        public static List<(string designId, int starts)> EffectiveStarts(GameData d, Fab f) =>
            EffectiveAllocations(d, f).Where(x => x.a.ContractId == null).Select(x => (x.a.DesignId, x.starts)).ToList();

        /// <summary>Every allocation (your products and foundry clients) with the starts it gets this month.</summary>
        public static List<(FabAllocation a, int starts)> EffectiveAllocations(GameData d, Fab f)
        {
            int planned = f.PlannedStarts;
            int available = AvailableCapacity(d, f);
            double scale = planned > available && planned > 0 ? available / (double)planned : 1;
            return f.Allocations.Where(a => a.Starts > 0).Select(a => (a, (int)Math.Floor(a.Starts * scale))).ToList();
        }

        static void StartWafers(GameData d, GameState s, Fab f, ProcessNodeDef n)
        {
            int week = Calendar.WeekInMonth(s.Tick);
            foreach (var (alloc, target) in EffectiveAllocations(d, f))
            {
                int wafers = target / Calendar.WeeksPerMonth + (week < target % Calendar.WeeksPerMonth ? 1 : 0);
                if (wafers <= 0) continue;
                var contract = alloc.ContractId == null ? null : s.WaferContracts.FirstOrDefault(k => k.Id == alloc.ContractId);
                if (alloc.ContractId != null && contract == null) continue;
                // a product no longer released (cancelled) runs as test wafers
                string id = contract == null && s.Design(alloc.DesignId)?.Stage == DesignStage.Released ? alloc.DesignId : null;
                double die = contract?.DieMm2 ?? DieArea(s, id);
                if (contract != null) contract.StartedThisMonth += wafers;
                int dpw = YieldModel.DiesPerWafer(n.WaferMm, YieldModel.WithScribe(die));
                f.Lots.Add(new WaferLot
                {
                    Id = s.NewId("lot"),
                    FabId = f.Id,
                    NodeId = f.NodeId,
                    DesignId = id,
                    ContractId = contract?.Id,
                    StartTick = s.Tick,
                    DoneTick = s.Tick + n.CycleWeeks,
                    Wafers = wafers,
                    DieAreaMm2 = die,
                    D0 = D0(d, f),
                    DiesPerWafer = dpw,
                    GrossDies = dpw * wafers,
                });
                f.StartsThisMonth += wafers;
                f.CumulativeWafers += wafers;
                s.Ledger.Book(f.External ? Accounts.FoundryWafers : Accounts.Materials, -wafers * WaferCost(d, s, f));
            }
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
                    f.GoodThisMonth += lot.GoodDies;
                }
                else
                {
                    int sample = Math.Min(lot.GrossDies, SampleDies);
                    double y = YieldModel.SampleYield(s.Rng, sample, lot.D0, lot.DieAreaMm2);
                    lot.GoodDies = (int)Math.Round(lot.GrossDies * y);
                    f.GoodThisMonth += lot.GoodDies;
                    if (lot.ContractId != null) Foundry.LotFinished(s, lot, y);
                }
                lot.Done = true;
            }
            f.Lots.RemoveAll(l => l.Done && l.Delivered && l.DoneTick < s.Tick - KeepLotsWeeks);
        }

        /// <summary>Experience on a node: the best learning any of your fabs has reached on it.</summary>
        public static double Experience(GameState s, string nodeId) =>
            nodeId != null && s.NodeExperience.TryGetValue(nodeId, out var v) ? v : 0;

        /// <summary>
        /// Months of learning a fab starts with on a node when it comes up: part of your experience on the same node (a
        /// second fab) or the node before it, whichever is more, up to <see cref="MaxTransfer"/>.
        /// </summary>
        public static double StartingLearning(GameData d, GameState s, string nodeId)
        {
            var n = d.Node(nodeId);
            double same = Experience(s, nodeId) * SameNodeTransfer;
            double prev = n?.Requires == null ? 0 : Experience(s, n.Requires) * PrevNodeTransfer;
            return Math.Min(MaxTransfer, Math.Max(same, prev));
        }

        public static void MonthEnd(GameData d, GameState s, Fab f)
        {
            if (f.External)
            {
                ExternalMonthEnd(d, s, f);
                return;
            }
            if (f.Producing && f.Learning > Experience(s, f.NodeId)) s.NodeExperience[f.NodeId] = f.Learning;
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

            if (f.Expanding)
            {
                double payment = Math.Min(f.ExpandCapex - f.ExpandPaid, f.ExpandCapex / (f.ExpandWeeksTotal / (double)Calendar.WeeksPerMonth));
                if (payment > 0)
                {
                    s.Ledger.Invest(payment);
                    f.ExpandPaid += payment;
                }
            }
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
            f.History.Add(new FabMonth { Month = s.MonthIndex, Starts = f.StartsThisMonth, Utilisation = f.Capacity > 0 ? f.StartsThisMonth / (double)f.Capacity : 0, D0 = D0(d, f), Good = f.GoodThisMonth });
            if (f.History.Count > HistoryMonths) f.History.RemoveRange(0, f.History.Count - HistoryMonths);
            f.StartsThisMonth = 0;
            f.GoodThisMonth = 0;
        }

        /// <summary>A supply agreement's month: pay for reserved wafers you didn't use (take-or-pay) and keep the record.</summary>
        static void ExternalMonthEnd(GameData d, GameState s, Fab f)
        {
            if (!f.Producing) return;
            int available = AvailableCapacity(d, f);
            double minimum = Math.Floor(f.Capacity * f.MinTake * Math.Clamp(f.SupplyFactor, 0, 1));
            if (f.StartsThisMonth < minimum)
                s.Ledger.Book(Accounts.UnusedWafers, -(minimum - f.StartsThisMonth) * f.WaferPrice);
            f.LastUtilisation = available > 0 ? Math.Min(1.0, f.StartsThisMonth / (double)available) : 0;
            f.LastMonthStarts = f.StartsThisMonth;
            f.History.Add(new FabMonth { Month = s.MonthIndex, Starts = f.StartsThisMonth, Utilisation = f.Capacity > 0 ? f.StartsThisMonth / (double)f.Capacity : 0, D0 = D0(d, f), Good = f.GoodThisMonth });
            if (f.History.Count > HistoryMonths) f.History.RemoveRange(0, f.History.Count - HistoryMonths);
            f.StartsThisMonth = 0;
            f.GoodThisMonth = 0;
        }

        /// <summary>Finished lots whose dies haven't been handed on yet.</summary>
        public static IEnumerable<WaferLot> Undelivered(Fab f) => f.Lots.Where(l => l.Done && !l.Delivered);
    }
}
