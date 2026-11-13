using System;
using System.Collections.Generic;
using System.Linq;

namespace SiliconeTycoon.Sim
{
    /// <summary>
    /// Selling chips. Each segment's monthly demand is split three ways: customers under design-win contracts, an open
    /// distribution market where listed chips compete on performance, price and reputation, and the rest of the industry
    /// (<see cref="Others"/>). Customers run requests for quotes (RFQs) every product cycle; the best-scoring bid wins a
    /// contract to supply the customer's share of the segment for the length of the product.
    /// </summary>
    public static class Market
    {
        public const string Player = "player";
        public const string Others = "others";

        /// <summary>Reputation factor of the rest of the industry.</summary>
        public const double OthersRep = 0.8;
        /// <summary>The rest of the industry bids a just-adequate chip at a little over the going price.</summary>
        public const double OthersPriceFactor = 1.05;
        /// <summary>Score kept by a bid with a chip that isn't released yet.</summary>
        public const double DevelopmentRisk = 0.85;
        /// <summary>Score kept by a bid with a chip expected after deliveries start.</summary>
        public const double LateRisk = 0.7;
        /// <summary>Penalty per missing chip, as a share of the contract price.</summary>
        public const double PenaltyShare = 0.25;
        /// <summary>Yearly price reduction written into contracts.</summary>
        public const double AnnualPriceDown = 0.08;
        public const int BidWindowMonths = 2;
        /// <summary>RFQs ask for at least this share of the performance a competitive chip will have at launch.</summary>
        public const double MinMetricShare = 0.7;
        /// <summary>Distributors won't stock a chip below this share of the competitive performance.</summary>
        public const double OpenMarketMinShare = 0.5;
        /// <summary>RFQs cap prices at this multiple of the going price.</summary>
        public const double MaxPriceFactor = 1.5;
        /// <summary>How sharply open-market share follows attractiveness.</summary>
        public const double ShareExponent = 2;
        /// <summary>Reputation gained each month you ship chips through distributors.</summary>
        public const double RepPerSellingMonth = 0.002;
        /// <summary>Smallest partial bid a customer will consider, as a share of its expected volume.</summary>
        public const double MinBidShare = 0.1;
        public const int MaxHistory = 1500;
        /// <summary>Contracts cap monthly orders at the customer's forecast times this; the rest goes to second sources.</summary>
        public const double ContractUpside = 1.3;

        public static double YearOfMonth(int month) => Calendar.YearF(month * Calendar.WeeksPerMonth);

        // ------------------------------------------------------------------ chip numbers

        public static double MetricOf(DesignStats st, Metric m) => m switch
        {
            Metric.Control => st.Control,
            Metric.Compute => st.Compute,
            Metric.Graphics => st.Graphics,
            _ => 0,
        };

        public static bool HasBin(ChipDesign c, string bin) => c != null && Binning.BinsFor(c).Any(b => b.Name == bin);

        /// <summary>Clock a bin is sold at: its speed requirement, or the chip's rated clock.</summary>
        public static double BinClock(ChipDesign c, string bin)
        {
            var b = Binning.BinsFor(c).FirstOrDefault(x => x.Name == bin);
            return b != null && b.MinClockMHz > 0 ? b.MinClockMHz : c.Stats.ClockMHz;
        }

        public static double Perf(ChipDesign c, string bin, Metric m)
        {
            if (c?.Stats == null || c.Stats.ClockMHz <= 0) return 0;
            return MetricOf(c.Stats, m) * BinClock(c, bin) / c.Stats.ClockMHz;
        }

        public static double Power(ChipDesign c, string bin) =>
            c?.Stats == null || c.Stats.ClockMHz <= 0 ? 0 : c.Stats.PowerW * BinClock(c, bin) / c.Stats.ClockMHz;

        /// <summary>Going price per chip in a segment, in that year's dollars.</summary>
        public static double ExpectedPrice(SegmentDef seg, double year) => seg.PriceAt(year) * Calendar.Inflation(year);

        public static double ContractPrice(Contract k, int month) =>
            k.BasePrice * Math.Pow(1 - AnnualPriceDown, Math.Max(0, month - k.StartMonth) / 12.0);

        // ------------------------------------------------------------------ inventory

        public static StockItem StockOf(GameState s, string designId, string bin) =>
            s.Inventory.FirstOrDefault(x => x.DesignId == designId && x.Bin == bin);

        public static double Stock(GameState s, string designId, string bin) => StockOf(s, designId, bin)?.Units ?? 0;

        public static void AddStock(GameState s, string designId, string bin, double units)
        {
            if (units <= 0) return;
            var it = StockOf(s, designId, bin);
            if (it == null)
            {
                it = new StockItem { DesignId = designId, Bin = bin };
                s.Inventory.Add(it);
            }
            it.Units += units;
            it.MadeThisMonth += units;
        }

        static double Take(GameState s, string designId, string bin, double units)
        {
            var it = StockOf(s, designId, bin);
            if (it == null || units <= 0) return 0;
            double t = Math.Min(units, it.Units);
            it.Units -= t;
            it.SoldThisMonth += t;
            return t;
        }

        /// <summary>Takes chips of a bin, topping up from better bins of the same design (down-binning) when short.</summary>
        public static double TakeWithDownBin(GameState s, ChipDesign c, string bin, double units)
        {
            double got = Take(s, c.Id, bin, units);
            var bins = Binning.BinsFor(c);
            int idx = bins.FindIndex(b => b.Name == bin);
            for (int i = idx - 1; i >= 0 && got < units; i--) got += Take(s, c.Id, bins[i].Name, units - got);
            return got;
        }

        /// <summary>Moves finished lots into the warehouse: good dies by bin. Test wafers are scrapped.</summary>
        public static void ReceiveLots(GameState s)
        {
            foreach (var f in s.Fabs)
                foreach (var lot in f.Lots)
                {
                    if (!lot.Done || lot.Delivered) continue;
                    if (lot.Bins != null && lot.DesignId != null)
                        foreach (var kv in lot.Bins)
                            if (kv.Key != Binning.Scrap) AddStock(s, lot.DesignId, kv.Key, kv.Value);
                    lot.Delivered = true;
                }
        }

        // ------------------------------------------------------------------ open market

        /// <summary>Offers a chip bin to distributors at a list price. A price of 0 takes it off the list.</summary>
        public static string SetListing(GameState s, string designId, string bin, double price)
        {
            var c = s.Design(designId);
            if (c == null) return "Unknown chip.";
            if (!HasBin(c, bin)) return $"The {c.Name} has no {bin} bin.";
            if (price < 0) return "The price can't be negative.";
            s.Listings.RemoveAll(l => l.DesignId == designId && l.Bin == bin);
            if (price > 0) s.Listings.Add(new Listing { DesignId = designId, Bin = bin, Price = price });
            return null;
        }

        static double Attract(double perfRatio, double priceRatio, SegmentDef seg, double rep) =>
            Math.Pow(Math.Min(3, perfRatio), seg.PerfWeight) * Math.Pow(priceRatio, seg.PriceSensitivity) * rep;

        static void OpenMarket(GameData d, GameState s, double year, Dictionary<string, MarketRecord> records)
        {
            bool soldOpen = false;
            foreach (var seg in d.Segments)
            {
                var rec = records[seg.Id];
                double demand = rec.OpenDemand;
                if (demand <= 0) continue;
                double need = seg.NeedAt(year);
                double expected = ExpectedPrice(seg, year);

                var offers = new List<(string supplier, Listing listing, RivalState rival, double attract, double price)>();
                foreach (var l in s.Listings)
                {
                    var c = s.Design(l.DesignId);
                    if (c == null || c.Stage != DesignStage.Released || l.Price <= 0) continue;
                    double perf = Perf(c, l.Bin, seg.Metric);
                    if (perf < need * OpenMarketMinShare || Power(c, l.Bin) > seg.MaxPowerW) continue;
                    if (Stock(s, l.DesignId, l.Bin) <= 0) continue;
                    offers.Add((Player, l, null, Attract(perf / need, expected / l.Price, seg, s.RepFactor), l.Price));
                }
                foreach (var r in s.Rivals)
                {
                    var p = Rivals.OpenMarketOffer(d, s, r, seg);
                    if (p == null) continue;
                    double price = Rivals.Price(s, r, p, seg);
                    double perf = Perf(p.Design, Rivals.BinOf(p), seg.Metric);
                    offers.Add((r.Id, null, r, Attract(perf / need, expected / price, seg, GameState.RepFactorOf(r.Rep)), price));
                }
                if (offers.Count == 0) continue;

                double outside = Math.Pow(OthersRep, ShareExponent);
                double total = outside + offers.Sum(o => Math.Pow(o.attract, ShareExponent));
                foreach (var o in offers)
                {
                    double want = demand * Math.Pow(o.attract, ShareExponent) / total;
                    if (o.supplier == Player)
                    {
                        double got = Take(s, o.listing.DesignId, o.listing.Bin, want);
                        if (got <= 0) continue;
                        soldOpen = true;
                        s.Ledger.Book(Accounts.Sales, got * o.price);
                        rec.PlayerUnits += got;
                        rec.PlayerRevenue += got * o.price;
                    }
                    else
                    {
                        double got = Math.Min(want, Math.Max(0, o.rival.Capacity - o.rival.UsedThisMonth));
                        o.rival.UsedThisMonth += got;
                        o.rival.RevenueThisMonth += got * o.price;
                        rec.RivalUnits += got;
                    }
                }
            }
            if (soldOpen) AdjustRep(s, RepPerSellingMonth);
        }

        // ------------------------------------------------------------------ RFQs and bids

        public static Rfq FindRfq(GameState s, string id) => s.Rfqs.FirstOrDefault(r => r.Id == id);
        public static CustomerState CustomerOf(GameState s, string id) => s.Customers.FirstOrDefault(c => c.Id == id);

        public static string SupplierName(GameState s, string id) =>
            id == Player ? s.CompanyName : id == Others ? "another supplier" : Rivals.Get(s, id)?.Name ?? id;

        public static double RepFactor(GameState s, string bidderId) =>
            bidderId == Player ? s.RepFactor : bidderId == Others ? OthersRep : GameState.RepFactorOf(Rivals.Get(s, bidderId)?.Rep ?? 0);

        /// <summary>Month a chip is expected to be released, judging by where it is in development.</summary>
        public static int EstimatedReleaseMonth(GameData d, GameState s, ChipDesign c)
        {
            int now = s.MonthIndex;
            var n = d.Node(c.NodeId);
            int silicon = n == null ? 3 : (n.CycleWeeks + DesignOps.BringUpWeeks) / Calendar.WeeksPerMonth + 1;
            switch (c.Stage)
            {
                case DesignStage.Released: return Calendar.MonthIndex(c.ReleasedTick);
                case DesignStage.Designing: return now + c.WeeksLeft / Calendar.WeeksPerMonth + silicon + 1;
                case DesignStage.Silicon: return now + c.WeeksLeft / Calendar.WeeksPerMonth + 1;
                case DesignStage.Validated: return now + 1;
                default: return int.MaxValue;
            }
        }

        /// <summary>Why a chip and price don't meet a request, or null if they do.</summary>
        public static string CheckBid(GameData d, Rfq r, ChipDesign c, string bin, double price)
        {
            var seg = d.Segment(r.SegmentId);
            if (c?.Stats == null) return "Choose a chip that's in development or released.";
            if (!HasBin(c, bin)) return $"The {c.Name} has no {bin} bin.";
            if (price <= 0) return "Enter a price.";
            if (price > r.MaxPrice) return $"The customer won't pay more than {Format.Money(r.MaxPrice)} a chip.";
            double perf = Perf(c, bin, seg.Metric);
            if (perf < r.MinMetric) return $"The customer needs {seg.Metric.ToString().ToLower()} of {r.MinMetric:0.00}; the {c.Name} {bin} manages {perf:0.00}.";
            double watts = Power(c, bin);
            if (watts > r.MaxPowerW) return $"The customer's design can shed {r.MaxPowerW:0.#} W; the {c.Name} {bin} draws {watts:0.00} W.";
            return null;
        }

        /// <param name="maxUnits">Most chips a month you'll supply; 0 offers the customer's whole volume. A partial bid
        /// wins only part of the order; the customer gives the rest to the next-best bidder.</param>
        public static string SubmitBid(GameData d, GameState s, string rfqId, string designId, string bin, double price, double maxUnits = 0)
        {
            var r = FindRfq(s, rfqId);
            if (r == null) return "That request has closed.";
            var c = s.Design(designId);
            if (c == null) return "Unknown chip.";
            if (c.Stage == DesignStage.Draft || c.Stage == DesignStage.Cancelled) return "Start the chip's design before bidding with it.";
            string err = CheckBid(d, r, c, bin, price);
            if (err != null) return err;
            if (maxUnits < 0) return "The volume can't be negative.";
            if (maxUnits > 0 && maxUnits < r.EstimatedUnits * MinBidShare) return $"The customer won't split its order for fewer than {r.EstimatedUnits * MinBidShare:N0} chips a month.";
            r.Bids.RemoveAll(b => b.BidderId == Player);
            r.Bids.Add(new Bid { BidderId = Player, DesignId = designId, Bin = bin, Price = price, MaxUnits = maxUnits, SubmittedTick = s.Tick });
            return null;
        }

        public static string WithdrawBid(GameState s, string rfqId)
        {
            var r = FindRfq(s, rfqId);
            if (r == null) return "That request has closed.";
            return r.Bids.RemoveAll(b => b.BidderId == Player) == 0 ? "You haven't bid on that." : null;
        }

        public static ChipDesign BidChip(GameState s, Bid b) =>
            b.BidderId == Player ? s.Design(b.DesignId) : Rivals.Product(s, b.BidderId, b.DesignId)?.Design;

        static double BaseScore(SegmentDef seg, CustomerDef cust, CustomerState cs, string bidder, double perfRatio, double priceRatio, double rep) =>
            Math.Pow(Math.Min(3, perfRatio), seg.PerfWeight)
            * Math.Pow(priceRatio, seg.PriceSensitivity * cust.PriceSensitivity)
            * rep
            * (cs != null && cs.SupplierId == bidder ? 1 + cust.Loyalty : 1);

        /// <summary>How the customer rates a bid: performance against need, price against the going rate, reputation, loyalty and delivery risk. 0 if the bid doesn't qualify.</summary>
        public static double Score(GameData d, GameState s, Rfq r, Bid b)
        {
            var seg = d.Segment(r.SegmentId);
            var cust = d.Customer(r.CustomerId);
            var c = BidChip(s, b);
            if (CheckBid(d, r, c, b.Bin, b.Price) != null) return 0;
            double need = seg.NeedAt(YearOfMonth(r.StartMonth));
            double perfRatio = Perf(c, b.Bin, seg.Metric) / need;
            double risk = 1;
            if (b.BidderId == Player && c.Stage != DesignStage.Released)
                risk = EstimatedReleaseMonth(d, s, c) > r.StartMonth ? LateRisk : DevelopmentRisk;
            return BaseScore(seg, cust, CustomerOf(s, r.CustomerId), b.BidderId, perfRatio, r.ExpectedPrice / b.Price, RepFactor(s, b.BidderId)) * risk;
        }

        /// <summary>The score every bid has to beat: the rest of the industry offering a just-adequate chip.</summary>
        public static double OthersScore(GameData d, GameState s, Rfq r) =>
            BaseScore(d.Segment(r.SegmentId), d.Customer(r.CustomerId), CustomerOf(s, r.CustomerId), Others, 1.0, 1 / OthersPriceFactor, OthersRep);

        static void OpenRfqs(GameData d, GameState s, int month)
        {
            foreach (var cs in s.Customers)
            {
                var cust = d.Customer(cs.Id);
                if (cust == null || !cust.Active(s.Year)) continue;
                var seg = d.Segment(cust.SegmentId);
                if (seg == null || !seg.Active(s.YearF)) continue;
                if (month < cs.NextRfqMonth || s.Rfqs.Any(r => r.CustomerId == cs.Id)) continue;

                int close = month + BidWindowMonths;
                int start = close + cust.LeadMonths;
                double startYear = YearOfMonth(start);
                double expected = ExpectedPrice(seg, startYear);
                var rfq = new Rfq
                {
                    Id = s.NewId("rfq"),
                    CustomerId = cs.Id,
                    SegmentId = seg.Id,
                    OpenMonth = month,
                    CloseMonth = close,
                    StartMonth = start,
                    LengthMonths = cust.CycleMonths,
                    MinMetric = seg.NeedAt(startYear) * MinMetricShare,
                    MaxPowerW = seg.MaxPowerW,
                    ExpectedPrice = expected,
                    MaxPrice = expected * MaxPriceFactor,
                    EstimatedUnits = seg.DemandAt(startYear) * cust.Share,
                };
                s.Rfqs.Add(rfq);
                cs.NextRfqMonth = int.MaxValue;
                s.Log($"{cust.Name} asks for bids on a {seg.Name.ToLower()} chip: {seg.Metric.ToString().ToLower()} {rfq.MinMetric:0.00}+, under {rfq.MaxPowerW:0.#} W, " +
                      $"up to {Format.Money(rfq.MaxPrice)} each, about {rfq.EstimatedUnits:N0} a month. Bids close end of {Calendar.DateLabel(close * Calendar.WeeksPerMonth)}.", 0, NewsKinds.Rfq);
            }
        }

        static void CloseRfqs(GameData d, GameState s, int month)
        {
            foreach (var r in s.Rfqs.Where(x => month >= x.CloseMonth).ToList())
            {
                var cust = d.Customer(r.CustomerId);
                var cs = CustomerOf(s, r.CustomerId);
                s.Rfqs.Remove(r);

                // bids that beat the rest of the industry, best first; each takes what it offered until the volume is gone
                double othersScore = OthersScore(d, s, r);
                var ranked = r.Bids
                    .Select(b => (bid: b, score: Score(d, s, r, b)))
                    .Where(x => x.score > othersScore)
                    .OrderByDescending(x => x.score)
                    .ToList();
                double volume = r.EstimatedUnits * ContractUpside;
                double left = volume;
                var awards = new List<(Bid bid, double units)>();
                foreach (var (bid, _) in ranked)
                {
                    if (left <= volume * 0.001) break;
                    double units = bid.MaxUnits > 0 ? Math.Min(bid.MaxUnits, left) : left;
                    awards.Add((bid, units));
                    left -= units;
                }

                cs.SupplierId = awards.Count > 0 ? awards[0].bid.BidderId : Others;
                cs.ContractId = null;
                cs.ContractEndMonth = r.StartMonth + r.LengthMonths - 1;
                cs.NextRfqMonth = cs.ContractEndMonth - cust.LeadMonths - BidWindowMonths + 1;
                foreach (var (bid, units) in awards)
                {
                    var k = new Contract
                    {
                        Id = s.NewId("contract"),
                        CustomerId = r.CustomerId,
                        SupplierId = bid.BidderId,
                        DesignId = bid.DesignId,
                        Bin = bid.Bin,
                        BasePrice = bid.Price,
                        StartMonth = r.StartMonth,
                        EndMonth = cs.ContractEndMonth,
                        MaxMonthly = units,
                        VolumeShare = volume > 0 ? units / volume : 1,
                    };
                    s.Contracts.Add(k);
                    if (cs.ContractId == null) cs.ContractId = k.Id;
                }

                var mine = awards.FirstOrDefault(a => a.bid.BidderId == Player);
                bool playerBid = r.Bids.Any(b => b.BidderId == Player);
                string primary = SupplierName(s, cs.SupplierId);
                if (mine.bid != null)
                {
                    AdjustRep(s, 0.01);
                    string part = mine.units < volume * 0.999 ? $" ({mine.units / volume:P0} of their volume)" : "";
                    s.Log($"You win {cust.Name}'s design{part}: up to {mine.units:N0} chips a month for {r.LengthMonths} months from " +
                          $"{Calendar.DateLabel(r.StartMonth * Calendar.WeeksPerMonth)}, at {Format.Money(mine.bid.Price)} each.", 1);
                }
                else if (playerBid)
                {
                    var won = awards.Count > 0 ? awards[0].bid : null;
                    var ours = r.Bids.First(b => b.BidderId == Player);
                    string why = won != null
                        ? $"{primary} won at {Format.Money(won.Price)} a chip; you bid {Format.Money(ours.Price)}."
                        : $"No bid beat the industry's offer of a just-adequate chip at about {Format.Money(r.ExpectedPrice * OthersPriceFactor)}; you bid {Format.Money(ours.Price)}.";
                    s.Log($"{cust.Name} picks {primary} over your bid. {why}", 2);
                }
                else if (cs.SupplierId != Others) s.Log($"{cust.Name} picks {primary} for its next product.", 0);
            }
            s.Contracts.RemoveAll(k => k.Status != ContractStatus.Active && k.EndMonth < month - 24);
        }

        // ------------------------------------------------------------------ contracts

        static void DeliverContracts(GameData d, GameState s, int month, Dictionary<string, MarketRecord> records)
        {
            foreach (var k in s.Contracts.Where(x => x.Status == ContractStatus.Active).ToList())
            {
                var cust = d.Customer(k.CustomerId);
                var rec = records[cust.SegmentId];
                if (month > k.EndMonth)
                {
                    k.Status = ContractStatus.Completed;
                    if (k.SupplierId == Player) s.Log($"Your contract with {cust.Name} ends after {k.Delivered:N0} chips.", 0);
                    continue;
                }
                if (month < k.StartMonth) continue;

                double ordered = cust.Active(s.Year) ? rec.Demand * cust.Share * k.VolumeShare : 0;
                if (k.MaxMonthly > 0) ordered = Math.Min(ordered, k.MaxMonthly);
                double price = ContractPrice(k, month);
                k.LastMonthOrdered = ordered;

                if (k.SupplierId != Player)
                {
                    var rival = Rivals.Get(s, k.SupplierId);
                    double made = rival == null ? 0 : Math.Min(ordered, Math.Max(0, rival.Capacity - rival.UsedThisMonth));
                    if (rival != null)
                    {
                        rival.UsedThisMonth += made;
                        rival.RevenueThisMonth += made * price;
                    }
                    k.Delivered += made;
                    k.LastMonthDelivered = made;
                    rec.RivalUnits += made;
                    continue;
                }

                var c = s.Design(k.DesignId);
                var cs = CustomerOf(s, k.CustomerId);
                double got = c != null && c.Stage == DesignStage.Released ? TakeWithDownBin(s, c, k.Bin, ordered) : 0;
                k.Delivered += got;
                k.LastMonthDelivered = got;
                if (got > 0) s.Ledger.Book(Accounts.Sales, got * price);
                rec.PlayerUnits += got;
                rec.PlayerRevenue += got * price;

                double missing = ordered - got;
                double shortShare = ordered > 0 ? missing / ordered : 0;
                if (shortShare <= 0.001)
                {
                    k.ShortMonths = 0;
                    AdjustRep(s, 0.002);
                    cs.Satisfaction = Math.Min(1, cs.Satisfaction + 0.01);
                    continue;
                }

                k.Shortfall += missing;
                s.Ledger.Book(Accounts.Penalties, -missing * price * PenaltyShare);
                AdjustRep(s, -0.03 * shortShare);
                cs.Satisfaction = Math.Max(0, cs.Satisfaction - 0.1 * shortShare);
                k.ShortMonths = shortShare > 0.5 ? k.ShortMonths + 1 : 0;
                if (k.ShortMonths == 1)
                    s.Log($"You delivered only {got:N0} of the {ordered:N0} chips {cust.Name} ordered. They're charging penalties.", 3);
                if (k.ShortMonths >= 3)
                {
                    k.Status = ContractStatus.Cancelled;
                    AdjustRep(s, -0.08);
                    if (cs.ContractId == k.Id)
                    {
                        cs.SupplierId = Others;
                        cs.ContractId = null;
                    }
                    s.Log($"{cust.Name} cancels your contract after three months of missed deliveries.", 3);
                }
            }
        }

        public static void AdjustRep(GameState s, double delta) => s.Rep = Math.Clamp(s.Rep + delta, -1, 1);

        // ------------------------------------------------------------------ setup and monthly loop

        /// <summary>Sets up customer cycles and rivals for a new game.</summary>
        public static void Init(GameData d, GameState s)
        {
            int now = s.MonthIndex;
            foreach (var cust in d.Customers)
            {
                var cs = new CustomerState { Id = cust.Id };
                if (cust.From <= s.Year) cs.NextRfqMonth = now + s.Rng.Next(Math.Max(1, cust.CycleMonths));
                else cs.NextRfqMonth = Calendar.MonthIndex(Calendar.TickOf(cust.From)) + s.Rng.Next(6);
                cs.ContractEndMonth = cs.NextRfqMonth + BidWindowMonths + cust.LeadMonths - 1;
                s.Customers.Add(cs);
            }
            Rivals.Init(d, s);
        }

        public static void MonthEnd(GameData d, GameState s)
        {
            if (d.Segments.Count == 0) return;
            int month = s.MonthIndex;
            double year = s.YearF;
            foreach (var r in s.Rivals)
            {
                r.UsedThisMonth = 0;
                r.RevenueThisMonth = 0;
            }
            var records = d.Segments.ToDictionary(x => x.Id, x => new MarketRecord
            {
                Month = month,
                SegmentId = x.Id,
                Demand = x.DemandAt(year),
                OpenDemand = x.DemandAt(year) * x.OpenShare,
            });

            DeliverContracts(d, s, month, records);
            OpenMarket(d, s, year, records);
            CloseRfqs(d, s, month);
            Rivals.MonthEnd(d, s);
            OpenRfqs(d, s, month);
            Rivals.BidOnOpenRfqs(d, s);

            foreach (var seg in d.Segments)
                if (seg.Active(year)) s.MarketHistory.Add(records[seg.Id]);
            if (s.MarketHistory.Count > MaxHistory) s.MarketHistory.RemoveRange(0, s.MarketHistory.Count - MaxHistory);
            foreach (var it in s.Inventory)
            {
                it.MadeLastMonth = it.MadeThisMonth;
                it.SoldLastMonth = it.SoldThisMonth;
                it.MadeThisMonth = 0;
                it.SoldThisMonth = 0;
            }
            s.Inventory.RemoveAll(x => x.Units <= 0.5 && x.MadeLastMonth <= 0 && x.SoldLastMonth <= 0);
        }
    }
}
