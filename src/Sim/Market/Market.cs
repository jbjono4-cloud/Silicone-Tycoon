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
        /// <summary>How much more buyers pay when chips are scarce: up to this much extra when nothing is available.</summary>
        public const double ShortagePremium = 1.0;
        /// <summary>Storage, handling and insurance per chip per month, 1970 dollars.</summary>
        public const double StoragePerChip = 0.004;
        /// <summary>Monthly cost of the money tied up in stock, as a share of what the chips cost to make.</summary>
        public const double CapitalShare = 0.01;
        /// <summary>Share of a chip's market value a surplus broker pays before volume discounts.</summary>
        public const double BrokerShare = 0.25;

        /// <summary>The segment's shortage last month (0 = plenty of chips, 1 = none to be had).</summary>
        public static double LastShortage(GameState s, string segmentId) =>
            s.MarketHistory.LastOrDefault(r => r.SegmentId == segmentId)?.Shortage ?? 0;

        /// <summary>What buyers will pay this month: the going price, raised while chips are scarce.</summary>
        public static double MarketPrice(GameState s, SegmentDef seg, double year) =>
            ExpectedPrice(seg, year) * (1 + ShortagePremium * LastShortage(s, seg.Id));

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

        /// <summary>
        /// Performance of chips sold in a bin: the chip's, scaled to the bin's clock and to the cores, graphics units and
        /// features the bin guarantees (a die with a core or the FPU switched off does less).
        /// </summary>
        public static double Perf(ChipDesign c, string bin, Metric m)
        {
            var st = c?.Stats;
            if (st == null || st.ClockMHz <= 0) return 0;
            double v = MetricOf(st, m) * BinClock(c, bin) / st.ClockMHz;
            var b = Binning.BinsFor(c).FirstOrDefault(x => x.Name == bin);
            if (b == null) return v;
            if (m != Metric.Graphics && st.Cores > 0 && b.MinCores < st.Cores) v *= Math.Max(0, b.MinCores) / (double)st.Cores;
            if (m == Metric.Graphics && st.GraphicsUnits > 0 && b.MinGraphics < st.GraphicsUnits) v *= Math.Max(0, b.MinGraphics) / (double)st.GraphicsUnits;
            if (m == Metric.Compute && b.Features != null && st.FeatureComputeBoost.Count > 0)
            {
                double lost = st.FeatureComputeBoost.Where(kv => !b.Features.Contains(kv.Key)).Sum(kv => kv.Value);
                v *= (1 + st.ComputeBoost - lost) / (1 + st.ComputeBoost);
            }
            return v;
        }

        /// <summary>Whether distributors in a market will stock chips from this bin: close enough to competitive, cool enough, and with the features the market insists on.</summary>
        public static bool Qualifies(GameData d, SegmentDef seg, ChipDesign c, string bin, double year)
        {
            if (c?.Stats == null) return false;
            if (Perf(c, bin, seg.Metric) < seg.NeedAt(year) * OpenMarketMinShare || Power(c, bin) > seg.MaxPowerW) return false;
            var have = Features.OfBin(d, c, bin);
            return seg.RequiredAt(year).All(have.Contains);
        }

        /// <summary>How a market rates a bin's features (1 = everything buyers want).</summary>
        public static double FeatureFactor(GameData d, SegmentDef seg, ChipDesign c, string bin, double year) =>
            c == null ? 1 : Features.Factor(seg, Features.OfBin(d, c, bin), year);

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
            if (c.CustomFor != null && price > 0) return $"The {c.Name} was made for another customer.";
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

                var offers = new List<(string supplier, Listing listing, RivalState rival, double attract, double price)>();
                foreach (var l in s.Listings)
                {
                    var c = s.Design(l.DesignId);
                    if (c == null || c.Stage != DesignStage.Released || l.Price <= 0) continue;
                    if (c.CustomFor != null || !Qualifies(d, seg, c, l.Bin, year)) continue;
                    if (Stock(s, l.DesignId, l.Bin) <= 0) continue;
                    offers.Add((Player, l, null, 0, l.Price));
                }
                foreach (var r in s.Rivals)
                {
                    var p = Rivals.OpenMarketOffer(d, s, r, seg);
                    if (p == null) continue;
                    offers.Add((r.Id, null, r, 0, Rivals.Price(s, r, p, seg)));
                }

                // scarcity: when the chips on offer can't cover demand, buyers pay more
                double supply = offers.Sum(o => o.supplier == Player ? Stock(s, o.listing.DesignId, o.listing.Bin) : Math.Max(0, o.rival.Capacity - o.rival.UsedThisMonth));
                rec.Shortage = Math.Clamp((demand - supply) / demand, 0, 1);
                double expected = ExpectedPrice(seg, year) * (1 + ShortagePremium * rec.Shortage);
                for (int i = 0; i < offers.Count; i++)
                {
                    var o = offers[i];
                    ChipDesign chip;
                    string bin;
                    if (o.supplier == Player) { chip = s.Design(o.listing.DesignId); bin = o.listing.Bin; }
                    else { var rp = Rivals.OpenMarketOffer(d, s, o.rival, seg); chip = rp.Design; bin = Rivals.BinOf(rp); }
                    double perf = Perf(chip, bin, seg.Metric);
                    double rep = o.supplier == Player ? s.RepFactor : GameState.RepFactorOf(o.rival.Rep);
                    offers[i] = (o.supplier, o.listing, o.rival, Attract(perf / need, expected / o.price, seg, rep) * FeatureFactor(d, seg, chip, bin, year), o.price);
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
            if (c?.Stats == null) return "Choose a chip that's in development or released.";
            if (!HasBin(c, bin)) return $"The {c.Name} has no {bin} bin.";
            if (c.CustomFor != null) return $"The {c.Name} was made for another customer.";
            if (price <= 0) return "Enter a price.";
            if (price > r.MaxPrice) return $"The customer won't pay more than {Format.Money(r.MaxPrice)} a chip.";
            var gaps = Specs.Check(d, ChipSpec.ForRfq(d, r), c, bin);
            return gaps.Count > 0 ? "The customer " + gaps[0] + "." : null;
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
            return BaseScore(seg, cust, CustomerOf(s, r.CustomerId), b.BidderId, perfRatio, r.ExpectedPrice / b.Price, RepFactor(s, b.BidderId)) * risk
                   * FeatureFactor(d, seg, c, b.Bin, YearOfMonth(r.OpenMonth));
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
                double expected = ExpectedPrice(seg, startYear) * (1 + ShortagePremium * LastShortage(s, seg.Id));
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
                    MaxPins = cust.MaxPins,
                    MinClockMHz = cust.MinClock == null ? 0 : Math.Round(Curves.At(cust.MinClock, s.YearF), 2),
                    Features = seg.RequiredAt(s.YearF).Concat(cust.Needs.Where(x => s.YearF >= x.From).Select(x => x.Feature)).Distinct().ToList(),
                };
                s.Rfqs.Add(rfq);
                cs.NextRfqMonth = int.MaxValue;
                string extras = (rfq.MinClockMHz > 0 ? $", {rfq.MinClockMHz:0.##} MHz+" : "") + (rfq.Features.Count > 0 ? $", with {Features.List(rfq.Features)}" : "");
                s.Log($"{cust.Name} asks for bids on a {seg.Name.ToLower()} chip: {seg.Metric.ToString().ToLower()} {rfq.MinMetric:0.00}+{extras}, under {rfq.MaxPowerW:0.#} W, " +
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
                Demand = x.DemandAt(year) * Events.DemandFactor(s, x.Id),
                OpenDemand = x.DemandAt(year) * x.OpenShare * Events.DemandFactor(s, x.Id),
                Price = MarketPrice(s, x, year),
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
            double carrying = s.Inventory.Sum(it => CarryingCost(s, it));
            if (carrying > 0) s.Ledger.Book(Accounts.Warehousing, -carrying);
        }

        // ------------------------------------------------------------------ surplus stock

        /// <summary>Monthly cost of keeping a stock item: storage plus the cost of the money tied up in it.</summary>
        public static double CarryingCost(GameState s, StockItem it)
        {
            if (it.Units <= 0) return 0;
            double made = s.Design(it.DesignId)?.Stats?.CostPerGoodDie ?? 0;
            if (double.IsInfinity(made) || double.IsNaN(made)) made = 0;
            return it.Units * (StoragePerChip * s.Inflation + CapitalShare * made);
        }

        public static double CarryingCost(GameState s, string designId) =>
            s.Inventory.Where(x => x.DesignId == designId).Sum(x => CarryingCost(s, x));

        /// <summary>
        /// What a surplus broker pays per chip for a stock item: a quarter of what the chip is worth in the market it suits
        /// best (less if it's behind or too hot for that market), discounted further the more you dump compared with the
        /// market's monthly demand.
        /// </summary>
        public static double BrokerPrice(GameData d, GameState s, StockItem it)
        {
            var c = s.Design(it.DesignId);
            if (c?.Stats == null || it.Units <= 0) return 0;
            double year = s.YearF, best = 0;
            foreach (var seg in d.Segments.Where(x => x.Active(year)))
            {
                double need = seg.NeedAt(year);
                double fit = need > 0 ? Math.Min(1, Perf(c, it.Bin, seg.Metric) / need) : 0;
                if (Power(c, it.Bin) > seg.MaxPowerW) fit *= 0.5;
                fit *= FeatureFactor(d, seg, c, it.Bin, year);
                if (!seg.RequiredAt(year).All(Features.OfBin(d, c, it.Bin).Contains)) fit *= 0.5;
                double demand = Math.Max(1, seg.DemandAt(year));
                double value = ExpectedPrice(seg, year) * fit / (1 + it.Units / demand);
                best = Math.Max(best, value);
            }
            return BrokerShare * best;
        }

        public static double BrokerQuote(GameData d, GameState s, string designId) =>
            s.Inventory.Where(x => x.DesignId == designId).Sum(x => x.Units * BrokerPrice(d, s, x));

        /// <summary>Sells every chip of a design in the warehouse to a surplus broker, all at once, for cash now.</summary>
        public static string SellToBroker(GameData d, GameState s, string designId)
        {
            var c = s.Design(designId);
            if (c == null) return "Unknown chip.";
            var items = s.Inventory.Where(x => x.DesignId == designId && x.Units >= 1).ToList();
            if (items.Count == 0) return $"You have no {c.Name} chips in stock.";
            double units = 0, cash = 0;
            foreach (var it in items)
            {
                cash += it.Units * BrokerPrice(d, s, it);
                units += it.Units;
                it.Units = 0;
            }
            s.Ledger.Book(Accounts.BrokerSales, cash);
            s.Listings.RemoveAll(l => l.DesignId == designId);
            s.Log($"A surplus broker takes {Format.Units(units)} {c.Name} chips for {Format.Big(cash)} (${cash / units:0.00} each).", 0);
            return null;
        }

        /// <summary>Destroys a design's stock: no money, but no more warehouse costs.</summary>
        public static string ScrapStock(GameState s, string designId)
        {
            var c = s.Design(designId);
            if (c == null) return "Unknown chip.";
            double units = s.Inventory.Where(x => x.DesignId == designId).Sum(x => x.Units);
            if (units < 1) return $"You have no {c.Name} chips in stock.";
            foreach (var it in s.Inventory.Where(x => x.DesignId == designId)) it.Units = 0;
            s.Listings.RemoveAll(l => l.DesignId == designId);
            s.Log($"{Format.Units(units)} {c.Name} chips are scrapped.", 0);
            return null;
        }
    }
}
