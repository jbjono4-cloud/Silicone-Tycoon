using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SiliconeTycoon.Sim;

namespace SiliconeTycoon.UI
{
    /// <summary>Selling chips: segments, customers' requests for quotes and your bids, contracts, the warehouse and price list, rivals.</summary>
    public partial class MarketTab : GameTab
    {
        VBoxContainer left, right;

        class BidDraft
        {
            public string ChipId;
            public double Price;
            public double Units;
        }

        readonly Dictionary<string, BidDraft> drafts = new Dictionary<string, BidDraft>();
        readonly Dictionary<string, double> listPrice = new Dictionary<string, double>();

        public MarketTab() { }
        public MarketTab(Main m) : base(m, "Market") { }

        protected override void Build()
        {
            left = Ui.V(10);
            var ls = Ui.Scroll(left);
            ls.SizeFlagsStretchRatio = 1.4f;
            AddChild(ls);
            right = Ui.V(10);
            AddChild(Ui.Scroll(right));
        }

        public override void Refresh()
        {
            if (S == null || left == null) return;
            Ui.Clear(left);
            Ui.Clear(right);
            left.AddChild(SegmentsCard());
            left.AddChild(RfqsCard());
            left.AddChild(ContractsCard());
            right.AddChild(WarehouseCard());
            right.AddChild(RivalsCard());
            right.AddChild(CustomersCard());
        }

        // ---------------------------------------------------------------- segments

        Control SegmentsCard()
        {
            var (card, b) = Ui.CardV("Markets this month");
            int last = S.MonthIndex - 1;
            foreach (var seg in D.Segments)
            {
                if (!seg.Active(S.YearF))
                {
                    b.AddChild(Ui.L($"{seg.Name}: opens in {seg.From}", 13, Ui.Muted));
                    continue;
                }
                var rec = S.MarketHistory.LastOrDefault(r => r.SegmentId == seg.Id && r.Month == last);
                double share = rec == null || rec.Demand <= 0 ? 0 : rec.PlayerUnits / rec.Demand;
                b.AddChild(Ui.Title(seg.Name, 16));
                b.AddChild(Ui.L($"{Ui.Units(seg.DemandAt(S.YearF))} chips a month ({seg.OpenShare:P0} through distributors) · going price {Format.Money(Market.ExpectedPrice(seg, S.YearF))} · " +
                                $"competitive {seg.Metric.ToString().ToLower()} {seg.NeedAt(S.YearF):0.00} · under {seg.MaxPowerW:0.#} W", 13, Ui.Muted));
                b.AddChild(Ui.L($"You sold {Ui.Units(rec?.PlayerUnits ?? 0)} last month ({share:P1} of the market){(rec != null && rec.PlayerRevenue > 0 ? $" for {Format.Big(rec.PlayerRevenue)}" : "")}", 13, share > 0 ? Ui.Ok : Ui.Muted));
            }
            return card;
        }

        // ---------------------------------------------------------------- RFQs

        Control RfqsCard()
        {
            var (card, b) = Ui.CardV("Customers asking for bids");
            if (S.Rfqs.Count == 0) b.AddChild(Ui.Wrap("No open requests. Customers ask for bids when they start a new product; the game pauses when one does.", 13, Ui.Muted));
            var chips = S.Designs.Where(c => c.Stats != null && c.Stage != DesignStage.Draft && c.Stage != DesignStage.Cancelled).ToList();
            foreach (var r in S.Rfqs.OrderBy(x => x.CloseMonth)) b.AddChild(RfqView(r, chips));
            return card;
        }

        Control RfqView(Rfq r, List<ChipDesign> chips)
        {
            var cust = D.Customer(r.CustomerId);
            var seg = D.Segment(r.SegmentId);
            var box = Ui.V(5);
            box.AddChild(Ui.Rule());
            var head = Ui.H(10);
            head.AddChild(Ui.Title($"{cust.Name}", 16));
            head.AddChild(Ui.L($"{cust.Country} · {seg.Name}", 13, Ui.Muted));
            head.AddChild(Ui.Spacer());
            head.AddChild(Ui.L($"Bids close end of {Calendar.DateLabel(r.CloseMonth * Calendar.WeeksPerMonth)}", 13, Ui.Warn));
            box.AddChild(head);
            box.AddChild(Ui.L($"Needs {seg.Metric.ToString().ToLower()} {r.MinMetric:0.00}+, under {r.MaxPowerW:0.#} W, at most {Format.Money(r.MaxPrice)} a chip (going rate {Format.Money(r.ExpectedPrice)}). " +
                              $"About {Ui.Units(r.EstimatedUnits)} a month for {r.LengthMonths} months from {Calendar.DateLabel(r.StartMonth * Calendar.WeeksPerMonth)}.", 13, Ui.Muted));

            var mine = r.Bids.FirstOrDefault(x => x.BidderId == Market.Player);
            if (mine != null)
            {
                var c = S.Design(mine.DesignId);
                double score = Market.Score(D, S, r, mine), bar = Market.OthersScore(D, S, r);
                box.AddChild(Ui.L($"Your bid: {c?.Name} at {Format.Money(mine.Price)}{(mine.MaxUnits > 0 ? $", up to {Ui.Units(mine.MaxUnits)} a month" : ", the whole volume")} · " +
                                  $"score {score:0.00} vs the industry's {bar:0.00}", 13, score > bar ? Ui.Ok : Ui.Bad));
                box.AddChild(Ui.L("Rivals bid too; their bids are sealed.", 12, Ui.Muted));
            }

            if (chips.Count == 0)
            {
                box.AddChild(Ui.L("You have no chip to bid with. Start one on the Design tab: you can bid with a chip still in development.", 13, Ui.Muted));
                return box;
            }
            if (!drafts.TryGetValue(r.Id, out var d))
            {
                var best = chips.OrderByDescending(c => Market.Perf(c, Binning.BinsFor(c)[0].Name, seg.Metric)).First();
                d = new BidDraft { ChipId = best.Id, Price = Math.Round(r.ExpectedPrice * 0.95, 2), Units = 0 };
                drafts[r.Id] = d;
            }
            if (!chips.Any(c => c.Id == d.ChipId)) d.ChipId = chips[0].Id;
            var chip = chips.First(c => c.Id == d.ChipId);
            string bin = Binning.BinsFor(chip)[0].Name;

            var row = Ui.H(8);
            row.AddChild(Ui.Options(chips.Select(c => $"{c.Name} ({c.Stage.ToString().ToLower()})").ToArray(), chips.FindIndex(c => c.Id == d.ChipId), i => { d.ChipId = chips[i].Id; Refresh(); }));
            row.AddChild(Ui.Spin(0.01, Math.Max(1, r.MaxPrice), 0.05, d.Price, v => { d.Price = v; Refresh(); }, "$", " each"));
            var units = Ui.Spin(0, Math.Ceiling(r.EstimatedUnits * Market.ContractUpside), 500, d.Units, v => { d.Units = v; Refresh(); }, "", " /mo max");
            units.TooltipText = "0 = offer the customer's whole volume. A partial bid wins only that much; the rest goes to the next-best bidder.";
            row.AddChild(units);
            row.AddChild(Ui.Btn(mine == null ? "Submit bid" : "Change bid", () => Act(Market.SubmitBid(D, S, r.Id, d.ChipId, bin, d.Price, d.Units), "Bid submitted.")));
            if (mine != null) row.AddChild(Ui.Btn("Withdraw", () => Act(Market.WithdrawBid(S, r.Id))));
            box.AddChild(row);

            // live check of the draft
            string problem = Market.CheckBid(D, r, chip, bin, d.Price);
            if (problem == null && d.Units > 0 && d.Units < r.EstimatedUnits * Market.MinBidShare) problem = $"The customer won't split its order for fewer than {Ui.Units(r.EstimatedUnits * Market.MinBidShare)} a month.";
            if (problem != null) box.AddChild(Ui.L(problem, 12, Ui.Bad));
            else
            {
                var trial = new Bid { BidderId = Market.Player, DesignId = chip.Id, Bin = bin, Price = d.Price, MaxUnits = d.Units };
                double score = Market.Score(D, S, r, trial), bar = Market.OthersScore(D, S, r);
                double cost = chip.Stats.CostPerGoodDie;
                string risk = chip.Stage == DesignStage.Released ? "" : Market.EstimatedReleaseMonth(D, S, chip) > r.StartMonth
                    ? " · it won't be ready when deliveries start (big score penalty, and penalties if you can't deliver)"
                    : " · not released yet (small score penalty)";
                box.AddChild(Ui.L($"This bid scores {score:0.00} against the industry's {bar:0.00}{risk} · margin {(d.Price - cost) / Math.Max(0.01, d.Price):P0} over about {Format.Money(cost)} a chip", 12, score > bar ? Ui.Ok : Ui.Warn));
            }
            return box;
        }

        // ---------------------------------------------------------------- contracts

        Control ContractsCard()
        {
            var (card, b) = Ui.CardV("Your contracts");
            var mine = S.Contracts.Where(k => k.SupplierId == Market.Player).OrderBy(k => k.Status).ThenBy(k => k.StartMonth).ToList();
            if (mine.Count == 0) b.AddChild(Ui.L("None yet. Win a customer's bid to get one.", 13, Ui.Muted));
            foreach (var k in mine)
            {
                var cust = D.Customer(k.CustomerId);
                var c = S.Design(k.DesignId);
                int now = S.MonthIndex;
                string when = now < k.StartMonth ? $"starts {Calendar.DateLabel(k.StartMonth * Calendar.WeeksPerMonth)}"
                    : k.Status == ContractStatus.Active ? $"{k.EndMonth - now + 1} months left" : k.Status.ToString().ToLower();
                var col = k.Status == ContractStatus.Cancelled ? Ui.Bad : k.ShortMonths > 0 ? Ui.Warn : k.Status == ContractStatus.Active ? Ui.Fg : Ui.Muted;
                b.AddChild(Ui.L($"{cust.Name}: {c?.Name} {k.Bin} at {Format.Money(Market.ContractPrice(k, Math.Max(now, k.StartMonth)))} · up to {Ui.Units(k.MaxMonthly)} a month · {when}", 14, col));
                if (k.Status == ContractStatus.Active && now > k.StartMonth)
                    b.AddChild(Ui.L($"   Last month: ordered {Ui.Units(k.LastMonthOrdered)}, delivered {Ui.Units(k.LastMonthDelivered)}{(k.ShortMonths > 0 ? $" · short {k.ShortMonths} month(s) running: three and they cancel" : "")}", 12, k.ShortMonths > 0 ? Ui.Warn : Ui.Muted));
            }
            b.AddChild(Ui.Wrap($"Contracts take chips first, before distributors. Short deliveries cost {Market.PenaltyShare:P0} of the price per missing chip and reputation. Prices fall {Market.AnnualPriceDown:P0} a year.", 12, Ui.Muted));
            return card;
        }

        // ---------------------------------------------------------------- warehouse and price list

        Control WarehouseCard()
        {
            var (card, b) = Ui.CardV("Warehouse and price list");
            var chips = S.Designs.Where(c => c.Stage == DesignStage.Released).OrderByDescending(c => c.ReleasedTick).ToList();
            if (chips.Count == 0) b.AddChild(Ui.Wrap("Release a chip and make it in a fab; finished chips arrive here to sell through distributors.", 13, Ui.Muted));
            foreach (var c in chips)
            {
                b.AddChild(Ui.Title(c.Name, 15));
                var fits = D.Segments.Where(seg => seg.Active(S.YearF)
                                                   && Market.Perf(c, Binning.BinsFor(c)[0].Name, seg.Metric) >= seg.NeedAt(S.YearF) * Market.OpenMarketMinShare
                                                   && Market.Power(c, Binning.BinsFor(c)[0].Name) <= seg.MaxPowerW).ToList();
                b.AddChild(Ui.L(fits.Count == 0 ? "Distributors won't stock it any more: it's too far behind." : "Distributors stock it for: " + string.Join(", ", fits.Select(x => x.Name.ToLower())), 12, fits.Count == 0 ? Ui.Warn : Ui.Muted));
                foreach (var bin in Binning.BinsFor(c))
                {
                    string key = c.Id + "|" + bin.Name;
                    var listing = S.Listings.FirstOrDefault(l => l.DesignId == c.Id && l.Bin == bin.Name);
                    if (!listPrice.TryGetValue(key, out var price))
                        price = listing?.Price ?? Math.Round(fits.Select(x => Market.ExpectedPrice(x, S.YearF)).DefaultIfEmpty(c.Stats.CostPerGoodDie * 1.5).Max(), 2);
                    listPrice[key] = price;
                    var item = Market.StockOf(S, c.Id, bin.Name);
                    double stock = item?.Units ?? 0, sold = item?.SoldLastMonth ?? 0, made = item?.MadeLastMonth ?? 0;
                    var row = Ui.H(8);
                    row.AddChild(Ui.L($"{bin.Name}: {Ui.Units(stock)} in stock", 13));
                    row.AddChild(Ui.Spacer());
                    row.AddChild(Ui.Spin(0.01, 100000, 0.05, price, v => listPrice[key] = v, "$"));
                    row.AddChild(Ui.Btn(listing == null ? "List" : "Reprice", () => Act(Market.SetListing(S, c.Id, bin.Name, listPrice[key]))));
                    if (listing != null) row.AddChild(Ui.Btn("Unlist", () => { listPrice.Remove(key); Act(Market.SetListing(S, c.Id, bin.Name, 0)); }));
                    b.AddChild(row);
                    b.AddChild(Ui.L(listing == null ? "   Not on sale to distributors." : $"   Listed at {Format.Money(listing.Price)} · costs about {Format.Money(c.Stats.CostPerGoodDie)} to make", 12, listing == null ? Ui.Warn : Ui.Muted));
                    if (fits.Count > 0)
                    {
                        double going = fits.Max(x => Market.ExpectedPrice(x, S.YearF));
                        b.AddChild(Ui.L($"   Going price {Format.Money(going)}{(listing != null && listing.Price > going * 1.15 ? " · you're well above it, so it will sell slowly" : "")}", 12, listing != null && listing.Price > going * 1.15 ? Ui.Warn : Ui.Muted));
                    }
                    bool piling = stock >= 1000 && (sold <= 0 || stock > sold * Advisor.StockMonthsWarning);
                    b.AddChild(Ui.L($"   Last month: made {Ui.Units(made)}, sold {Ui.Units(sold)}" + (sold > 0 ? $" · {stock / sold:0.0} months of stock" : ""), 12, piling ? Ui.Warn : Ui.Muted));
                }
            }
            return card;
        }

        // ---------------------------------------------------------------- rivals and customers

        Control RivalsCard()
        {
            var (card, b) = Ui.CardV("Rivals");
            foreach (var r in S.Rivals)
            {
                var best = Rivals.BestNode(D, r);
                b.AddChild(Ui.Title(r.Name, 15));
                b.AddChild(Ui.L($"{best?.Name ?? "—"} · makes {Ui.Units(r.Capacity)} chips a month ({r.LastUtilisation:P0} used) · {Format.Big(r.LastMonthRevenue)} revenue last month · reputation {r.Rep * 100:+0;-0;0}" +
                                (r.PriceWar ? " · PRICE WAR" : ""), 12, r.PriceWar ? Ui.Warn : Ui.Muted));
                foreach (var p in r.Products)
                {
                    var seg = D.Segment(p.SegmentId);
                    b.AddChild(Ui.L($"   {p.Design.Name} for {seg.Name.ToLower()}: {D.Node(p.Design.NodeId).Name}, {p.Design.Stats.ClockMHz:0.00} MHz, " +
                                    $"{seg.Metric.ToString().ToLower()} {Market.MetricOf(p.Design.Stats, seg.Metric):0.00}, about {Format.Money(Rivals.Price(S, r, p, seg))}", 12, Ui.Muted));
                }
                if (r.Project != null) b.AddChild(Ui.L($"   Working on a {D.Segment(r.Project.SegmentId).Name.ToLower()} chip.", 12, Ui.Muted));
            }
            return card;
        }

        Control CustomersCard()
        {
            var (card, b) = Ui.CardV("Customers");
            foreach (var cs in S.Customers)
            {
                var cust = D.Customer(cs.Id);
                if (!cust.Active(S.Year)) continue;
                string next = cs.NextRfqMonth == int.MaxValue ? "asking for bids now" : $"next bids around {Calendar.DateLabel(cs.NextRfqMonth * Calendar.WeeksPerMonth)}";
                b.AddChild(Ui.L($"{cust.Name} ({cust.Country}, {D.Segment(cust.SegmentId).Name.ToLower()}, {cust.Share:P0} of it): supplier {Market.SupplierName(S, cs.SupplierId)} · {next}",
                                12, cs.SupplierId == Market.Player ? Ui.Ok : Ui.Muted));
            }
            return card;
        }
    }
}
