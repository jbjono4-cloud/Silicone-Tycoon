using System;
using System.Collections.Generic;
using System.Linq;

namespace SiliconeTycoon.Sim
{
    /// <summary>
    /// The wafer business, both ways round. Fabless: rent capacity at a foundry (a supply agreement) and make your chips
    /// there without fabs or process research. Foundry: make wafers for other companies' designs, and design custom chips
    /// (ASICs) to clients' specs. Plus selling fabs to foundries, and a view of which business model you're running.
    /// </summary>
    public static class Foundry
    {
        public const int SupplyLeadMonths = 3;
        /// <summary>Share of the reserved wafers you pay for each month whether you use them or not.</summary>
        public const double MinTakeShare = 0.7;
        /// <summary>Breaking an agreement costs this share of the minimum payments left.</summary>
        public const double CancelShare = 0.3;
        public const int MinSupplyWafers = 100;
        /// <summary>Clients' trust in a foundry that also sells chips of its own.</summary>
        public const double ConflictPenalty = 0.75;
        /// <summary>Share of an ASIC's engineering fee paid when the deal is signed (the rest on delivery).</summary>
        public const double NreUpfront = 0.4;
        /// <summary>Least share of the wafer price a client pays for a lot, however poor its yield.</summary>
        public const double YieldFloor = 0.6;
        public const int BidWindowMonths = 2;
        public const int AsicDesignMonths = 18;
        public const int AsicOfferMonths = 3;
        public const double ShortfallPenalty = 0.15;

        // ------------------------------------------------------------------ world setup

        public static void Init(GameData d, GameState s)
        {
            int now = s.MonthIndex;
            foreach (var f in d.Foundries) State(s, f.Id);
            int i = 0;
            foreach (var c in d.Clients)
            {
                if (s.Clients.Any(x => x.Id == c.Id)) continue;
                int opens = Math.Max(now, MonthOf(c.From)) + (i++ * 3) % 12;
                s.Clients.Add(new ClientState { Id = c.Id, NextRfqMonth = opens, NextAsicMonth = opens + c.EveryMonths / 4 });
            }
        }

        static int MonthOf(int year) => (year - Calendar.StartYear) * 12;

        public static FoundryState State(GameState s, string id)
        {
            var st = s.Foundries.FirstOrDefault(x => x.Id == id);
            if (st == null)
            {
                st = new FoundryState { Id = id };
                s.Foundries.Add(st);
            }
            return st;
        }

        public static ClientState ClientOf(GameState s, string id) => s.Clients.FirstOrDefault(x => x.Id == id);

        static Rng LocalRng(GameState s, string key) => new Rng(WaferMap.Hash($"{s.Seed}|{key}|{s.MonthIndex}"));

        // ------------------------------------------------------------------ foundries you can buy from

        public static bool IsOpen(GameState s, FoundryDef f) => s.YearF >= f.From && s.Year <= f.To;

        public static IEnumerable<FoundryDef> OpenFoundries(GameData d, GameState s) => d.Foundries.Where(f => IsOpen(s, f));

        /// <summary>Wafers a month the foundry sells to outside customers this year.</summary>
        public static double Capacity(GameState s, FoundryDef f) =>
            f.Capacity * Math.Pow(1 + f.Growth, Math.Max(0, s.YearF - f.From)) + State(s, f.Id).BoughtCapacity;

        /// <summary>Wafers a month you have reserved at a foundry.</summary>
        public static double Booked(GameState s, string foundryId) =>
            s.Fabs.Where(x => x.External && x.FoundryId == foundryId && x.Stage != FabStage.Closed).Sum(x => (double)x.Capacity);

        /// <summary>When a node reaches a foundry's fabs (fractional year).</summary>
        public static double NodeYear(FoundryDef f, ProcessNodeDef n) => Math.Max(f.From, n.Year + f.NodeLagYears);

        public static bool Offers(GameState s, FoundryDef f, ProcessNodeDef n) =>
            IsOpen(s, f) && (s.YearF >= NodeYear(f, n) || State(s, f.Id).BoughtLearning.ContainsKey(n.Id));

        public static IEnumerable<ProcessNodeDef> NodesOffered(GameData d, GameState s, FoundryDef f) =>
            d.Nodes.Where(n => Offers(s, f, n)).OrderBy(n => n.FeatureNm);

        /// <summary>Months of learning a foundry's fabs have on a node.</summary>
        public static double Maturity(GameState s, FoundryDef f, ProcessNodeDef n)
        {
            double own = f.MaturityMonths + Math.Max(0, (s.YearF - NodeYear(f, n)) * 12);
            return State(s, f.Id).BoughtLearning.TryGetValue(n.Id, out var bought) ? Math.Max(own, bought) : own;
        }

        public static double D0(GameState s, FoundryDef f, ProcessNodeDef n) => LearningCurve.D0(n, Maturity(s, f, n));

        /// <summary>You can design chips for a node if you've researched it or any foundry you can reach runs it.</summary>
        public static bool CanDesignFor(GameData d, GameState s, string nodeId)
        {
            if (s.NodeReady(nodeId)) return true;
            var n = d.Node(nodeId);
            return n != null && d.Foundries.Any(f => Offers(s, f, n));
        }

        /// <summary>What a wafer on a node costs a foundry to make: materials, running costs and its fab's depreciation (falling as the fab is paid off).</summary>
        public static double CostToMake(GameState s, ProcessNodeDef n)
        {
            double age = Math.Max(0, s.YearF - n.Year);
            double capex = n.FabCostPer1000 / 1000.0 / FabOps.DepreciationMonths * Math.Max(0.25, 1 - 0.12 * age);
            double running = n.FabCostPer1000 / 1000.0 * FabOps.RunningShare;
            return (n.WaferCost * Events.MaterialsFactor(s) + running + capex) * s.Inflation;
        }

        /// <summary>The going price for a wafer on a node from a foundry (what clients compare bids with).</summary>
        public static double ReferencePrice(GameState s, ProcessNodeDef n) => CostToMake(s, n) * 1.3;

        /// <summary>A foundry's price for a supply agreement: its cost plus margin, a little less for longer terms.</summary>
        public static double SupplyPrice(GameState s, FoundryDef f, ProcessNodeDef n, int months) =>
            CostToMake(s, n) * (1 + f.Margin) * (months >= 36 ? 0.92 : months >= 24 ? 0.96 : 1);

        public class SupplyQuote
        {
            public FoundryDef Foundry;
            public ProcessNodeDef Node;
            public int Wafers;
            public int Months;
            public double Price;
            public double Available;
            public double D0;
            public double MonthlyMinimum;
            public string Problem;
        }

        public static SupplyQuote QuoteSupply(GameData d, GameState s, string foundryId, string nodeId, int wafers, int months)
        {
            var q = new SupplyQuote { Foundry = d.Foundry(foundryId), Node = d.Node(nodeId), Wafers = wafers, Months = months };
            if (q.Foundry == null || q.Node == null) { q.Problem = "Choose a foundry and a process."; return q; }
            if (!IsOpen(s, q.Foundry)) { q.Problem = $"{q.Foundry.Name} isn't taking customers in {s.Year}."; return q; }
            if (!Offers(s, q.Foundry, q.Node)) { q.Problem = $"{q.Foundry.Name} doesn't run {q.Node.Name} yet (expected around {NodeYear(q.Foundry, q.Node):0})."; return q; }
            q.Price = SupplyPrice(s, q.Foundry, q.Node, months);
            q.Available = Math.Max(0, Capacity(s, q.Foundry) - Booked(s, foundryId));
            q.D0 = D0(s, q.Foundry, q.Node);
            q.MonthlyMinimum = Math.Floor(wafers * MinTakeShare) * q.Price;
            if (months != 12 && months != 24 && months != 36) q.Problem = "Agreements run 12, 24 or 36 months.";
            else if (wafers < MinSupplyWafers) q.Problem = $"Foundries won't reserve fewer than {MinSupplyWafers} wafers a month.";
            else if (wafers > q.Available) q.Problem = $"{q.Foundry.Name} has only {q.Available:N0} wafers a month free on its lines.";
            return q;
        }

        /// <summary>Reserves wafers at a foundry: it appears with your fabs once the line is qualified, a few months on.</summary>
        public static string SignSupply(GameData d, GameState s, string foundryId, string nodeId, int wafers, int months)
        {
            var q = QuoteSupply(d, s, foundryId, nodeId, wafers, months);
            if (q.Problem != null) return q.Problem;
            var f = new Fab
            {
                Id = s.NewId("fab"),
                Name = $"{q.Foundry.Name} ({q.Node.Name})",
                LocationId = q.Foundry.LocationId,
                NodeId = nodeId,
                Capacity = wafers,
                External = true,
                FoundryId = foundryId,
                WaferPrice = q.Price,
                MinTake = MinTakeShare,
                SupplyEndMonth = s.MonthIndex + SupplyLeadMonths + months - 1,
                BuildWeeksTotal = SupplyLeadMonths * Calendar.WeeksPerMonth,
                BuildWeeksLeft = SupplyLeadMonths * Calendar.WeeksPerMonth,
                OrderedTick = s.Tick,
                Learning = Maturity(s, q.Foundry, q.Node) + SupplyLeadMonths,
            };
            s.Fabs.Add(f);
            s.Log($"You reserve {wafers:N0} wafers a month of {q.Node.Name} at {q.Foundry.Name} for {months} months at {Format.Money(q.Price)} a wafer. " +
                  $"Wafers start in about {SupplyLeadMonths} months; you pay for at least {MinTakeShare:P0} of them every month.", 1);
            return null;
        }

        public static double CancelCost(GameState s, Fab f)
        {
            int left = Math.Max(0, f.SupplyEndMonth - s.MonthIndex);
            return left * Math.Floor(f.Capacity * f.MinTake) * f.WaferPrice * CancelShare;
        }

        public static string CancelSupply(GameData d, GameState s, Fab f)
        {
            if (!f.External || f.Stage == FabStage.Closed) return "That isn't an open supply agreement.";
            double cost = CancelCost(s, f);
            if (cost > 0) s.Ledger.Book(Accounts.SupplyPenalty, -cost);
            EndSupply(s, f);
            s.Log($"You end the agreement with {d.Foundry(f.FoundryId)?.Name} early and pay {Format.Big(cost)} to get out of it.", 2);
            return null;
        }

        /// <summary>Extends an agreement by more months, at the foundry's price today.</summary>
        public static string RenewSupply(GameData d, GameState s, Fab f, int months)
        {
            if (!f.External || f.Stage == FabStage.Closed) return "That isn't an open supply agreement.";
            var fd = d.Foundry(f.FoundryId);
            var n = d.Node(f.NodeId);
            if (fd == null || !IsOpen(s, fd)) return "The foundry isn't taking customers.";
            if (months != 12 && months != 24 && months != 36) return "Agreements run 12, 24 or 36 months.";
            f.WaferPrice = SupplyPrice(s, fd, n, months);
            f.SupplyEndMonth += months;
            s.Log($"{f.Name}: renewed for {months} more months at {Format.Money(f.WaferPrice)} a wafer.", 0);
            return null;
        }

        static void EndSupply(GameState s, Fab f)
        {
            f.Stage = FabStage.Closed;
            f.Allocations.Clear();
            f.Lots.RemoveAll(l => !l.Done);
        }

        /// <summary>A foundry squeezes its smaller customers when chips are scarce everywhere.</summary>
        static double Squeeze(GameData d, GameState s, Fab f)
        {
            var fd = d.Foundry(f.FoundryId);
            var segs = d.Segments.Where(x => x.Active(s.YearF)).ToList();
            double shortage = segs.Count == 0 ? 0 : segs.Average(x => Market.LastShortage(s, x.Id));
            double factor = shortage > 0.3 ? 1 - 0.6 * (shortage - 0.3) : 1;
            if (fd?.RivalId != null)
            {
                var r = Rivals.Get(s, fd.RivalId);
                if (r != null && r.LastUtilisation > 0.9) factor *= 0.8;   // the rival keeps its own fabs for itself first
            }
            if (fd?.PurePlay == true) factor = 1 - (1 - factor) * 0.5;      // a pure-play foundry looks after its customers
            return Math.Clamp(factor, 0.5, 1);
        }

        // ------------------------------------------------------------------ the monthly loop

        public static void MonthEnd(GameData d, GameState s)
        {
            if (d.Foundries.Count > 0 || d.Clients.Count > 0) Init(d, s);   // also catches old saves up
            int month = s.MonthIndex;

            foreach (var f in s.Fabs.Where(x => x.External && x.Stage != FabStage.Closed).ToList())
            {
                if (month >= f.SupplyEndMonth)
                {
                    EndSupply(s, f);
                    s.Log($"Your agreement with {d.Foundry(f.FoundryId)?.Name} for {d.Node(f.NodeId)?.Name} wafers has ended.", 2);
                    continue;
                }
                if (month == f.SupplyEndMonth - 3) s.Log($"{f.Name}: the supply agreement ends in three months. Renew it on the Fabs tab or move your chips.", 2);
                double old = f.SupplyFactor;
                f.SupplyFactor = Squeeze(d, s, f);
                if (f.SupplyFactor < 0.95 && old >= 0.95)
                    s.Log($"Chips are short everywhere and {d.Foundry(f.FoundryId)?.Name} is rationing: you'll get {f.SupplyFactor:P0} of your wafers.", 2);
                else if (f.SupplyFactor >= 0.95 && old < 0.95)
                    s.Log($"{d.Foundry(f.FoundryId)?.Name} is delivering all your wafers again.", 1);
            }

            WaferContractsMonth(d, s, month);
            CloseWaferRfqs(d, s, month);
            OpenWaferRfqs(d, s, month);
            AsicMonth(d, s, month);
        }

        // ------------------------------------------------------------------ you as a foundry

        /// <summary>Whether clients see you as a competitor: you sell chips of your own.</summary>
        public static bool SellsOwnChips(GameState s) =>
            s.Listings.Any(l => s.Design(l.DesignId)?.CustomFor == null)
            || s.Contracts.Any(k => k.SupplierId == Market.Player && k.Status == ContractStatus.Active && k.FixedMonthly <= 0);

        public static double TrustFactor(GameState s) => SellsOwnChips(s) ? ConflictPenalty : 1;

        /// <summary>Price the rest of the foundry industry offers, as a multiple of the reference (competition grows over time).</summary>
        public static double OthersMarkup(double year) => Math.Clamp(1.35 - 0.02 * (year - 1975), 1.05, 1.35);

        /// <summary>The coarsest node a client accepts this year.</summary>
        public static ProcessNodeDef CoarsestAllowed(GameData d, ClientDef c, double year)
        {
            var newest = d.Nodes.Where(n => n.Year <= year).OrderBy(n => n.FeatureNm).FirstOrDefault();
            if (newest == null) return null;
            return d.Nodes.Where(n => n.Year <= year && n.Year >= newest.Year - c.MaxNodeAgeYears - 1e-9).OrderByDescending(n => n.FeatureNm).FirstOrDefault();
        }

        static void OpenWaferRfqs(GameData d, GameState s, int month)
        {
            foreach (var cs in s.Clients)
            {
                var c = d.Client(cs.Id);
                if (c == null || c.Kind != ClientKind.Wafers || !c.Active(s.Year) || month < cs.NextRfqMonth) continue;
                if (s.WaferRfqs.Any(r => r.ClientId == c.Id)) continue;
                var node = CoarsestAllowed(d, c, s.YearF);
                if (node == null) continue;
                var rng = LocalRng(s, c.Id);
                double wafers = Math.Max(MinSupplyWafers, Math.Round(Curves.At(c.Wafers, s.YearF) * (0.85 + 0.3 * rng.NextDouble()) / 50) * 50);
                double reference = ReferencePrice(s, node);
                int close = month + BidWindowMonths;
                var r = new WaferRfq
                {
                    Id = s.NewId("wrfq"),
                    ClientId = c.Id,
                    OpenMonth = month,
                    CloseMonth = close,
                    StartMonth = close + c.LeadMonths,
                    LengthMonths = c.CycleMonths,
                    MaxFeatureNm = node.FeatureNm,
                    Wafers = wafers,
                    ReferencePrice = reference,
                    MaxPrice = reference * c.PriceFactor,
                    DieMm2 = c.DieMm2,
                    MaxD0 = node.D0Mature * c.QualityFactor,
                };
                s.WaferRfqs.Add(r);
                cs.NextRfqMonth = int.MaxValue;
                s.Log($"{c.Name} wants a foundry: {wafers:N0} wafers a month on {node.Name} or finer for {r.LengthMonths} months, up to {Format.Money(r.MaxPrice)} a wafer. Bids close end of {Calendar.DateLabel(close * Calendar.WeeksPerMonth)}.", 0, NewsKinds.Rfq);
            }
        }

        /// <summary>Why a bid wouldn't qualify, or null.</summary>
        public static string CheckWaferBid(GameData d, GameState s, WaferRfq r, Fab f, double price)
        {
            if (f == null || f.External || f.Stage == FabStage.Closed) return "Bid with one of your own fabs.";
            var n = d.Node(f.NodeId);
            if (n.FeatureNm > r.MaxFeatureNm + 1e-6) return $"The client needs {r.MaxFeatureNm / 1000:0.#} µm or finer; {f.Name} runs {n.Name}.";
            if (price <= 0) return "Enter a price.";
            if (price > r.MaxPrice) return $"The client won't pay more than {Format.Money(r.MaxPrice)} a wafer.";
            double d0 = f.Producing ? FabOps.D0(d, f) : LearningCurve.D0(n, FabOps.StartingLearning(d, s, f.NodeId));
            if (d0 > r.MaxD0 * 1.5) return $"{f.Name}'s defect density ({d0:0.0}/cm²) is far worse than the client accepts ({r.MaxD0:0.0}).";
            return null;
        }

        public static double WaferScore(GameData d, GameState s, WaferRfq r, Fab f, double price)
        {
            if (CheckWaferBid(d, s, r, f, price) != null) return 0;
            var n = d.Node(f.NodeId);
            double d0 = f.Producing ? FabOps.D0(d, f) : LearningCurve.D0(n, FabOps.StartingLearning(d, s, f.NodeId));
            double quality = Math.Sqrt(Math.Clamp(r.MaxD0 / Math.Max(0.01, d0), 0.6, 1.3));
            double nodeBonus = Math.Pow(r.MaxFeatureNm / n.FeatureNm, 0.2);
            var cs = ClientOf(s, r.ClientId);
            double loyalty = cs?.SupplierId == Market.Player ? 1 + (d.Client(r.ClientId)?.Loyalty ?? 0) : 1;
            bool late = !f.Producing && f.BuildWeeksLeft / Calendar.WeeksPerMonth > r.StartMonth - s.MonthIndex;
            return Math.Pow(r.ReferencePrice / price, 1.5) * quality * nodeBonus * s.RepFactor * TrustFactor(s) * loyalty * (late ? 0.6 : 1);
        }

        public static double WaferOthersScore(WaferRfq r) => Math.Pow(1 / OthersMarkup(Market.YearOfMonth(r.OpenMonth)), 1.5) * Market.OthersRep;

        public static string BidWafers(GameData d, GameState s, string rfqId, string fabId, double price)
        {
            var r = s.WaferRfqs.FirstOrDefault(x => x.Id == rfqId);
            if (r == null) return "That request has closed.";
            var f = s.Fabs.FirstOrDefault(x => x.Id == fabId);
            string err = CheckWaferBid(d, s, r, f, price);
            if (err != null) return err;
            r.BidFabId = fabId;
            r.BidPrice = price;
            return null;
        }

        public static string WithdrawWaferBid(GameState s, string rfqId)
        {
            var r = s.WaferRfqs.FirstOrDefault(x => x.Id == rfqId);
            if (r == null || r.BidFabId == null) return "You haven't bid on that.";
            r.BidFabId = null;
            return null;
        }

        static void CloseWaferRfqs(GameData d, GameState s, int month)
        {
            foreach (var r in s.WaferRfqs.Where(x => month >= x.CloseMonth).ToList())
            {
                s.WaferRfqs.Remove(r);
                var c = d.Client(r.ClientId);
                var cs = ClientOf(s, r.ClientId);
                var fab = s.Fabs.FirstOrDefault(x => x.Id == r.BidFabId);
                double score = r.BidFabId == null ? 0 : WaferScore(d, s, r, fab, r.BidPrice);
                int end = r.StartMonth + r.LengthMonths - 1;
                if (cs != null) cs.NextRfqMonth = end - (c?.LeadMonths ?? 3) - BidWindowMonths + 1;
                if (score > WaferOthersScore(r))
                {
                    var k = new WaferContract
                    {
                        Id = s.NewId("wcontract"),
                        ClientId = r.ClientId,
                        FabId = fab.Id,
                        NodeId = fab.NodeId,
                        Wafers = r.Wafers,
                        Price = r.BidPrice,
                        MaxD0 = r.MaxD0,
                        DieMm2 = r.DieMm2,
                        StartMonth = r.StartMonth,
                        EndMonth = end,
                    };
                    s.WaferContracts.Add(k);
                    if (cs != null) cs.SupplierId = Market.Player;
                    Market.AdjustRep(s, 0.01);
                    s.Log($"You win {c?.Name}'s wafers: {r.Wafers:N0} a month at {Format.Money(r.BidPrice)} from {fab.Name} for {r.LengthMonths} months from {Calendar.DateLabel(r.StartMonth * Calendar.WeeksPerMonth)}.", 1);
                }
                else
                {
                    if (cs != null) cs.SupplierId = Market.Others;
                    if (r.BidFabId != null)
                        s.Log($"{c?.Name} gives its wafers to another foundry{(SellsOwnChips(s) ? " (it worries about handing its designs to a chip maker that competes with it)" : "")}. Independent foundries were offering about {Format.Money(r.ReferencePrice * OthersMarkup(s.YearF))}.", 2);
                }
            }
        }

        static void WaferContractsMonth(GameData d, GameState s, int month)
        {
            foreach (var k in s.WaferContracts.Where(x => x.Status == WaferContractStatus.Active).ToList())
            {
                var fab = s.Fabs.FirstOrDefault(x => x.Id == k.FabId);
                var c = d.Client(k.ClientId);
                var cs = ClientOf(s, k.ClientId);
                if (month >= k.StartMonth && k.Allocated)
                {
                    // last month's deliveries
                    int started = k.StartedThisMonth;
                    k.LastMonthStarted = started;
                    k.StartedThisMonth = 0;
                    double missing = k.Wafers - started;
                    if (missing > k.Wafers * 0.1)
                    {
                        s.Ledger.Book(Accounts.Penalties, -missing * k.Price * ShortfallPenalty);
                        if (cs != null) cs.Satisfaction = Math.Max(0, cs.Satisfaction - 0.05);
                    }
                    k.ShortMonths = started < k.Wafers * 0.5 ? k.ShortMonths + 1 : 0;
                    if (k.ShortMonths == 1) s.Log($"You started only {started:N0} of the {k.Wafers:N0} wafers {c?.Name} is owed this month. They're charging penalties.", 3);
                    if (k.ShortMonths >= 3)
                    {
                        EndContract(s, k, WaferContractStatus.Cancelled);
                        Market.AdjustRep(s, -0.05);
                        if (cs != null) { cs.SupplierId = Market.Others; cs.NextRfqMonth = Math.Min(cs.NextRfqMonth, month + 2); }
                        s.Log($"{c?.Name} takes its wafers elsewhere after three months of missed starts.", 3);
                        continue;
                    }
                }
                if (month >= k.EndMonth)
                {
                    EndContract(s, k, WaferContractStatus.Completed);
                    if (cs != null) cs.Satisfaction = Math.Min(1, cs.Satisfaction + 0.1);
                    s.Log($"Your wafer contract with {c?.Name} ends: {k.Delivered:N0} wafers, {Format.Big(k.Revenue)} in all.", 0);
                    continue;
                }
                if (!k.Allocated && month + 1 >= k.StartMonth)
                {
                    if (fab == null || fab.Stage == FabStage.Closed)
                    {
                        EndContract(s, k, WaferContractStatus.Cancelled);
                        Market.AdjustRep(s, -0.05);
                        s.Log($"You have no fab for {c?.Name}'s wafers: the contract is cancelled.", 3);
                        continue;
                    }
                    MakeRoom(s, fab, (int)k.Wafers);
                    fab.Allocations.Add(new FabAllocation { ContractId = k.Id, Starts = (int)k.Wafers });
                    k.Allocated = true;
                    s.Log($"{fab.Name} sets aside {k.Wafers:N0} wafer starts a month for {c?.Name}.", 0);
                }
            }
        }

        /// <summary>Frees wafer starts for a contract, taking them from test wafers first and then your own products.</summary>
        static void MakeRoom(GameState s, Fab f, int wafers)
        {
            int over = f.PlannedStarts + wafers - f.Capacity;
            if (over <= 0) return;
            foreach (var a in f.Allocations.Where(a => a.ContractId == null).OrderBy(a => a.DesignId != null).ToList())
            {
                int take = Math.Min(a.Starts, over);
                a.Starts -= take;
                over -= take;
                if (over <= 0) break;
            }
            f.Allocations.RemoveAll(a => a.ContractId == null && a.Starts <= 0);
            if (over > 0) s.Log($"{f.Name} is over-committed to foundry clients by {over:N0} wafers a month.", 3);
            else s.Log($"{f.Name}'s own products give up wafer starts to make room for a foundry client.", 2);
        }

        static void EndContract(GameState s, WaferContract k, WaferContractStatus status)
        {
            k.Status = status;
            foreach (var f in s.Fabs) f.Allocations.RemoveAll(a => a.ContractId == k.Id);
        }

        /// <summary>A client's lot comes off the line: they pay per wafer, less if its yield misses what they were promised.</summary>
        public static void LotFinished(GameState s, WaferLot lot, double yield)
        {
            var k = s.WaferContracts.FirstOrDefault(x => x.Id == lot.ContractId);
            if (k == null) return;
            double expected = YieldModel.Yield(k.MaxD0, k.DieMm2);
            double factor = expected > 0 ? Math.Clamp(yield / expected, YieldFloor, 1) : 1;
            double revenue = lot.Wafers * k.Price * factor;
            s.Ledger.Book(Accounts.FoundryRevenue, revenue);
            k.QualityPaid = (k.QualityPaid * k.Delivered + factor * lot.Wafers) / Math.Max(1, k.Delivered + lot.Wafers);
            k.Delivered += lot.Wafers;
            k.Revenue += revenue;
            lot.Delivered = true;
        }

        // ------------------------------------------------------------------ custom chips (ASICs)

        static void AsicMonth(GameData d, GameState s, int month)
        {
            foreach (var deal in s.AsicDeals.ToList())
            {
                var c = d.Client(deal.ClientId);
                var cs = ClientOf(s, deal.ClientId);
                switch (deal.Status)
                {
                    case AsicStatus.Offered:
                        if (month > deal.ExpiresMonth)
                        {
                            deal.Status = AsicStatus.Declined;
                            if (cs != null) cs.NextAsicMonth = month + (c?.EveryMonths ?? 30);
                        }
                        break;
                    case AsicStatus.Accepted:
                    {
                        var chip = s.Design(deal.DesignId);
                        if (chip != null && chip.Stage == DesignStage.Released)
                        {
                            string bin = Binning.BinsFor(chip).Select(b => b.Name).FirstOrDefault(b => Specs.Check(d, deal.Spec, chip, b).Count == 0);
                            if (bin != null)
                            {
                                Deliver(d, s, deal, chip, bin, month);
                                break;
                            }
                            if (!deal.WarnedSpec)
                            {
                                deal.WarnedSpec = true;
                                var gaps = Specs.Check(d, deal.Spec, chip, Binning.BinsFor(chip)[0].Name);
                                s.Log($"The {chip.Name} doesn't meet {c?.Name}'s spec: it {string.Join("; ", gaps)}. A new stepping or another chip is needed before the deadline.", 3);
                            }
                        }
                        if (month > deal.DeadlineMonth)
                        {
                            deal.Status = AsicStatus.Failed;
                            Market.AdjustRep(s, -0.06);
                            if (chip != null) chip.CustomFor = null;   // the client walks away; the chip is yours to sell
                            if (cs != null) { cs.Satisfaction = 0.2; cs.NextAsicMonth = month + (int)((c?.EveryMonths ?? 30) * 1.5); }
                            s.Log($"You missed {c?.Name}'s deadline for its custom chip. They cancel the deal and keep the fees they paid.", 3);
                        }
                        break;
                    }
                    case AsicStatus.Supplying:
                    {
                        var k = s.Contracts.FirstOrDefault(x => x.Id == deal.ContractId);
                        if (k == null || k.Status == ContractStatus.Completed)
                        {
                            deal.Status = AsicStatus.Done;
                            if (cs != null) { cs.Satisfaction = Math.Min(1, cs.Satisfaction + 0.15); cs.NextAsicMonth = Math.Min(cs.NextAsicMonth, month + 3); }
                        }
                        else if (k.Status == ContractStatus.Cancelled)
                        {
                            deal.Status = AsicStatus.Failed;
                            if (cs != null) { cs.Satisfaction = 0.2; cs.NextAsicMonth = month + (c?.EveryMonths ?? 30) * 2; }
                        }
                        break;
                    }
                }
            }
            s.AsicDeals.RemoveAll(x => (x.Status == AsicStatus.Declined || x.Status == AsicStatus.Done || x.Status == AsicStatus.Failed) && month - x.OfferedMonth > 60);

            foreach (var cs in s.Clients)
            {
                var c = d.Client(cs.Id);
                if (c == null || c.Kind != ClientKind.Asic || !c.Active(s.Year) || month < cs.NextAsicMonth) continue;
                if (s.AsicDeals.Any(x => x.ClientId == c.Id && (x.Status == AsicStatus.Offered || x.Status == AsicStatus.Accepted || x.Status == AsicStatus.Supplying))) continue;
                var deal = MakeOffer(d, s, c, month);
                if (deal == null) { cs.NextAsicMonth = month + 6; continue; }
                s.AsicDeals.Add(deal);
                cs.NextAsicMonth = int.MaxValue;
                s.Log($"{c.Name} offers a custom chip contract: {Format.Big(deal.Nre)} in engineering fees, then {Format.Units(deal.MonthlyUnits)} chips a month at {Format.Money(deal.UnitPrice)} for {deal.LengthMonths} months. Answer by {Calendar.DateLabel(deal.ExpiresMonth * Calendar.WeeksPerMonth)} (Foundry tab).", 0, NewsKinds.Rfq);
            }
        }

        /// <summary>The newest node you could design for right now (your own or a foundry's).</summary>
        public static ProcessNodeDef BestDesignNode(GameData d, GameState s) =>
            d.Nodes.Where(n => CanDesignFor(d, s, n.Id)).OrderBy(n => n.FeatureNm).FirstOrDefault();

        static AsicDeal MakeOffer(GameData d, GameState s, ClientDef c, int month)
        {
            var seg = d.Segment(c.SegmentId);
            var node = BestDesignNode(d, s);
            if (seg == null || node == null || !seg.Active(s.YearF)) return null;
            var rng = LocalRng(s, c.Id + "|asic");
            var spec = ChipSpec.ForSegment(seg, s.YearF);
            spec.Features = spec.Features.Union(spec.Wanted).Distinct().ToList();
            spec.Wanted = new List<string>();
            // one thing their product needs that the market at large doesn't
            var extras = d.Blocks.Where(b => b.Kind == BlockKind.Peripheral && b.Year <= s.Year).SelectMany(b => b.Features).Distinct().Except(spec.Features).ToList();
            if (extras.Count > 0) spec.Features.Add(extras[(int)(rng.NextDouble() * extras.Count) % extras.Count]);
            spec.MinMetric = Math.Round(seg.NeedAt(s.YearF) * (0.6 + 0.3 * rng.NextDouble()), 2);

            var plan = DesignLab.PlanForSpec(d, s, node.Id, spec, "ASIC", out _, ignoreNodeReady: true);
            if (plan == null) return null;
            var st = DesignLab.Analyze(d, s, plan, ignoreNodeReady: true);
            plan.Stats = st;
            if (Market.Perf(plan, Binning.BinsFor(plan)[0].Name, spec.Metric) < spec.MinMetric)
                spec.MinMetric = Math.Round(Market.Perf(plan, Binning.BinsFor(plan)[0].Name, spec.Metric) * 0.9, 2);
            double fullMonths = DesignOps.BaseEngineerMonths(plan, st, s.YearF);
            double nre = fullMonths * DesignOps.Salary * s.Inflation * 1.8 + node.MaskSetCost * s.Inflation * 2;
            double startYear = s.YearF + AsicDesignMonths / 12.0;
            double unitPrice = Math.Max(st.CostPerGoodDie * 2.5, Market.ExpectedPrice(seg, startYear) * c.PriceFactor);
            return new AsicDeal
            {
                Id = s.NewId("asic"),
                ClientId = c.Id,
                SegmentId = seg.Id,
                Spec = spec,
                Nre = Math.Round(nre, -2),
                UnitPrice = Math.Round(unitPrice, 2),
                MonthlyUnits = Math.Round(Curves.At(c.Units, s.YearF) / 500) * 500,
                LengthMonths = c.CycleMonths,
                OfferedMonth = month,
                ExpiresMonth = month + AsicOfferMonths,
                DeadlineMonth = month + AsicDesignMonths,
                Status = AsicStatus.Offered,
            };
        }

        public static AsicDeal Deal(GameState s, string id) => s.AsicDeals.FirstOrDefault(x => x.Id == id);

        public static string AcceptAsic(GameData d, GameState s, string id)
        {
            var deal = Deal(s, id);
            if (deal == null || deal.Status != AsicStatus.Offered) return "That offer isn't open.";
            deal.Status = AsicStatus.Accepted;
            deal.DeadlineMonth = Math.Max(deal.DeadlineMonth, s.MonthIndex + AsicDesignMonths - 2);
            double upfront = deal.Nre * NreUpfront;
            deal.NrePaid = upfront;
            s.Ledger.Book(Accounts.Nre, upfront);
            s.Log($"You sign {d.Client(deal.ClientId)?.Name}'s custom chip: {Format.Big(upfront)} now, the rest when the chip is released to spec by {Calendar.DateLabel(deal.DeadlineMonth * Calendar.WeeksPerMonth)}. Start it on the Design tab.", 1);
            return null;
        }

        public static string DeclineAsic(GameData d, GameState s, string id)
        {
            var deal = Deal(s, id);
            if (deal == null || deal.Status != AsicStatus.Offered) return "That offer isn't open.";
            deal.Status = AsicStatus.Declined;
            var cs = ClientOf(s, deal.ClientId);
            if (cs != null) cs.NextAsicMonth = s.MonthIndex + (d.Client(deal.ClientId)?.EveryMonths ?? 30);
            return null;
        }

        /// <summary>Makes a chip the one for a deal. It can then only be sold to that client.</summary>
        public static string AssignChip(GameState s, string dealId, ChipDesign c)
        {
            var deal = Deal(s, dealId);
            if (deal == null || deal.Status != AsicStatus.Accepted) return "That deal isn't waiting for a chip.";
            if (c == null || c.Stage == DesignStage.Cancelled) return "Choose a chip.";
            if (c.CustomFor != null && c.CustomFor != dealId) return $"The {c.Name} is already another client's.";
            var old = s.Design(deal.DesignId);
            if (old != null && old != c) old.CustomFor = null;
            c.CustomFor = dealId;
            deal.DesignId = c.Id;
            deal.WarnedSpec = false;
            s.Listings.RemoveAll(l => l.DesignId == c.Id);
            return null;
        }

        static void Deliver(GameData d, GameState s, AsicDeal deal, ChipDesign chip, string bin, int month)
        {
            double rest = deal.Nre - deal.NrePaid;
            if (rest > 0) s.Ledger.Book(Accounts.Nre, rest);
            deal.NrePaid = deal.Nre;
            var k = new Contract
            {
                Id = s.NewId("contract"),
                CustomerId = deal.ClientId,
                SupplierId = Market.Player,
                DesignId = chip.Id,
                Bin = bin,
                BasePrice = deal.UnitPrice,
                StartMonth = month + 1,
                EndMonth = month + deal.LengthMonths,
                MaxMonthly = deal.MonthlyUnits,
                FixedMonthly = deal.MonthlyUnits,
            };
            s.Contracts.Add(k);
            deal.ContractId = k.Id;
            deal.Status = AsicStatus.Supplying;
            Market.AdjustRep(s, 0.02);
            s.Log($"The {chip.Name} meets {d.Client(deal.ClientId)?.Name}'s spec: {Format.Big(rest)} of fees, and {Format.Units(deal.MonthlyUnits)} chips a month from next month.", 1);
        }

        // ------------------------------------------------------------------ selling fabs

        public class SaleQuote
        {
            public FoundryDef Buyer;
            public double Price;
            public double BookValue;
            public double LeasePrice;
            public string Problem;
        }

        public static SaleQuote QuoteSale(GameData d, GameState s, Fab f)
        {
            var q = new SaleQuote();
            if (f == null || f.External || f.Stage == FabStage.Closed) { q.Problem = "Only a fab you own can be sold."; return q; }
            if (!f.Producing || f.Expanding) { q.Problem = "Foundries only buy fabs that are up and running (not being built, retooled or expanded)."; return q; }
            var n = d.Node(f.NodeId);
            q.Buyer = OpenFoundries(d, s).OrderByDescending(x => x.PurePlay).ThenBy(x => x.RivalId != null).ThenByDescending(x => Capacity(s, x)).FirstOrDefault();
            if (q.Buyer == null) { q.Problem = "No foundry is buying fabs yet."; return q; }
            double freshness = Math.Clamp(1 - 0.08 * (s.YearF - n.Year), 0.4, 1);
            double know = Math.Clamp(0.7 + 0.2 * f.Learning / 24, 0.7, 0.9);
            q.BookValue = f.BookValue;
            q.Price = (f.BookValue + 0.25 * f.CapexReal * s.Inflation * freshness) * know;
            q.LeasePrice = CostToMake(s, n) * (1 + q.Buyer.Margin) * 0.9;
            return q;
        }

        /// <summary>
        /// Sells a fab to a foundry. With <paramref name="leaseWafers"/> above 0 you keep buying that many of its wafers a
        /// month (on its current learning) for <paramref name="months"/>; lots in the line move with them, otherwise they go
        /// with the fab.
        /// </summary>
        public static string SellFab(GameData d, GameState s, Fab f, int leaseWafers = 0, int months = 24)
        {
            var q = QuoteSale(d, s, f);
            if (q.Problem != null) return q.Problem;
            if (leaseWafers < 0 || leaseWafers > f.Capacity) return $"You can keep up to {f.Capacity:N0} wafers a month.";
            if (leaseWafers > 0 && leaseWafers < MinSupplyWafers) return $"Keep at least {MinSupplyWafers} wafers a month, or none.";
            if (leaseWafers > 0 && months != 12 && months != 24 && months != 36) return "Agreements run 12, 24 or 36 months.";

            s.Ledger.Cash += q.Price;
            s.Ledger.Accrue(Accounts.FabSale, q.Price - f.BookValue);
            var st = State(s, q.Buyer.Id);
            st.BoughtCapacity += f.Capacity;
            st.BoughtLearning[f.NodeId] = Math.Max(st.BoughtLearning.TryGetValue(f.NodeId, out var l) ? l : 0, f.Learning);

            foreach (var k in s.WaferContracts.Where(k => k.FabId == f.Id && k.Status == WaferContractStatus.Active).ToList())
            {
                EndContract(s, k, WaferContractStatus.Completed);
                s.Log($"{d.Client(k.ClientId)?.Name}'s wafers move to {q.Buyer.Name} with the fab.", 0);
            }

            Fab lease = null;
            if (leaseWafers > 0)
            {
                lease = new Fab
                {
                    Id = s.NewId("fab"),
                    Name = $"{q.Buyer.Name} ({d.Node(f.NodeId).Name}, ex-{f.Name})",
                    LocationId = f.LocationId,
                    NodeId = f.NodeId,
                    Capacity = leaseWafers,
                    External = true,
                    FoundryId = q.Buyer.Id,
                    WaferPrice = q.LeasePrice * (months >= 36 ? 0.92 : months >= 24 ? 0.96 : 1),
                    MinTake = MinTakeShare,
                    SupplyEndMonth = s.MonthIndex + months,
                    Stage = FabStage.Running,
                    Learning = f.Learning,
                    OrderedTick = s.Tick,
                };
                double scale = leaseWafers / (double)Math.Max(1, f.Allocations.Where(a => a.ContractId == null).Sum(a => a.Starts));
                foreach (var a in f.Allocations.Where(a => a.ContractId == null && a.Starts > 0))
                    lease.Allocations.Add(new FabAllocation { DesignId = a.DesignId, Starts = (int)Math.Floor(a.Starts * Math.Min(1, scale)) });
                lease.Lots.AddRange(f.Lots.Where(x => !x.Done && x.ContractId == null));
                foreach (var lot in lease.Lots) lot.FabId = lease.Id;
                s.Fabs.Add(lease);
            }

            f.Sold = true;
            f.Stage = FabStage.Closed;
            f.BookValue = 0;
            f.MonthlyDepreciation = 0;
            f.Allocations.Clear();
            f.Lots.RemoveAll(x => !x.Done);
            s.Log($"You sell {f.Name} to {q.Buyer.Name} for {Format.Big(q.Price)} ({(q.Price >= q.BookValue ? "a gain" : "a loss")} of {Format.Big(Math.Abs(q.Price - q.BookValue))} on its book value)." +
                  (lease != null ? $" You keep {leaseWafers:N0} wafers a month at {Format.Money(lease.WaferPrice)} for {months} months." : ""), 1);
            return null;
        }

        // ------------------------------------------------------------------ business model

        public static string Model(GameState s)
        {
            bool ownFabs = s.Fabs.Any(f => !f.External && f.Stage != FabStage.Closed);
            bool rented = s.Fabs.Any(f => f.External && f.Stage != FabStage.Closed);
            bool chips = s.Designs.Any(c => c.Stage == DesignStage.Released && c.CustomFor == null) || SellsOwnChips(s);
            bool services = s.WaferContracts.Any(k => k.Status == WaferContractStatus.Active)
                            || s.AsicDeals.Any(x => x.Status == AsicStatus.Accepted || x.Status == AsicStatus.Supplying);
            if (ownFabs && chips) return services ? "Integrated maker with foundry services" : "Integrated device maker";
            if (ownFabs) return services ? "Pure-play foundry" : "Fab owner without products yet";
            if (chips || rented) return services ? "Fabless, with design services" : "Fabless";
            return services ? "Design services" : "Start-up";
        }
    }
}
