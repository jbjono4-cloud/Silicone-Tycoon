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
        string confirmSurplus;
        readonly HashSet<string> hideChart = new HashSet<string>();
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
                double shortage = Market.LastShortage(S, seg.Id);
                if (shortage > 0.05)
                    b.AddChild(Ui.L($"Chips are scarce: distributors couldn't get {shortage:P0} of what they wanted last month, so buyers are paying up to {Format.Money(Market.MarketPrice(S, seg, S.YearF))}.", 13, Ui.Ok));
                b.AddChild(Ui.L($"{Ui.Units(seg.DemandAt(S.YearF))} chips a month ({seg.OpenShare:P0} through distributors) · going price {Format.Money(Market.ExpectedPrice(seg, S.YearF))} · " +
                                $"competitive {seg.Metric.ToString().ToLower()} {seg.NeedAt(S.YearF):0.00} · under {seg.MaxPowerW:0.#} W", 13, Ui.Muted));
                b.AddChild(Ui.L($"You sold {Ui.Units(rec?.PlayerUnits ?? 0)} last month ({share:P1} of the market){(rec != null && rec.PlayerRevenue > 0 ? $" for {Format.Big(rec.PlayerRevenue)}" : "")}", 13, share > 0 ? Ui.Ok : Ui.Muted));
                foreach (var e in Events.Active(S, "demand").Where(e => e.Target == null || e.Target == seg.Id))
                    b.AddChild(Ui.L($"{e.Text}: demand {e.Factor - 1:+0%;-0%} until {Calendar.DateLabel(e.EndMonth * Calendar.WeeksPerMonth)}", 12, e.Factor >= 1 ? Ui.Ok : Ui.Warn));
                var hist = S.MarketHistory.Where(r => r.SegmentId == seg.Id).TakeLast(36).ToList();
                if (hist.Count >= 3)
                {
                    bool open = !hideChart.Contains(seg.Id);
                    string id = seg.Id;
                    b.AddChild(Ui.Btn(open ? "Hide chart" : "Chart", () => { if (!hideChart.Remove(id)) hideChart.Add(id); Refresh(); }));
                    if (open)
                    {
                        var labels = hist.Select(r => Calendar.DateLabel(r.Month * Calendar.WeeksPerMonth)).ToList();
                        var units = new LineChart { ValueFormat = v => Ui.Units(v) };
                        units.CustomMinimumSize = new Vector2(300, 170);
                        units.SetData(labels,
                            ("Market", Ui.Muted, hist.Select(r => r.Demand).ToList()),
                            ("You", Ui.Accent, hist.Select(r => r.PlayerUnits).ToList()),
                            ("Rivals", Ui.Rival1, hist.Select(r => r.RivalUnits).ToList()));
                        b.AddChild(units);
                        if (hist.Any(r => r.Price > 0))
                        {
                            var price = new LineChart { ValueFormat = v => Format.Money(v) };
                            price.CustomMinimumSize = new Vector2(300, 120);
                            price.SetData(labels, ("Price buyers paid", Ui.Ok, hist.Select(r => r.Price).ToList()));
                            b.AddChild(price);
                        }
                    }
                }
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
            var spec = ChipSpec.ForRfq(D, r);
            var reqs = new List<string>();
            if (r.MinClockMHz > 0) reqs.Add($"runs at {r.MinClockMHz:0.##} MHz or faster");
            if (r.MaxPins > 0 && r.MaxPins < 999) reqs.Add($"fits a board taking {r.MaxPins} pins at most");
            if (r.Features.Count > 0) reqs.Add($"has {Features.List(r.Features)}");
            if (reqs.Count > 0) box.AddChild(Ui.L("The chip must: " + string.Join(" · ", reqs), 13, Ui.Fg));
            var nice = spec.Wanted.Except(r.Features).ToList();
            if (nice.Count > 0) box.AddChild(Ui.L("Scores better with: " + Features.List(nice), 12, Ui.Muted));

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
                double spareNow = Market.EstimatedOutput(D, S, best) - Market.Committed(S, best.Id, r.StartMonth);
                double volumeNow = r.EstimatedUnits * Market.ContractUpside;
                double startUnits = spareNow >= volumeNow ? 0 : Math.Max(0, Math.Floor(spareNow / 500) * 500);
                d = new BidDraft { ChipId = best.Id, Price = Math.Round(r.ExpectedPrice * 0.95, 2), Units = startUnits };
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

            // can you make that many?
            double output = Market.EstimatedOutput(D, S, chip);
            double promised = Market.Committed(S, chip.Id, r.StartMonth);
            double spare = Math.Max(0, output - promised);
            double offered = d.Units > 0 ? d.Units : r.EstimatedUnits * Market.ContractUpside;
            string capText = output <= 0
                ? $"You have no {D.Node(chip.NodeId).Name} fab to make the {chip.Name} in."
                : $"Your {D.Node(chip.NodeId).Name} fabs could make about {Ui.Units(output)} {chip.Name} chips a month flat out" +
                  (promised > 0 ? $", {Ui.Units(promised)} already promised" : "") + $" · this bid offers up to {Ui.Units(offered)} a month";
            box.AddChild(Ui.L(capText, 12, offered > spare * 1.05 ? Ui.Warn : Ui.Muted));
            if (offered > spare * 1.05)
            {
                var fix = Ui.H(8);
                fix.AddChild(Ui.L("Short deliveries cost penalties, reputation and, after three bad months, the contract.", 12, Ui.Warn));
                if (spare >= r.EstimatedUnits * Market.MinBidShare)
                    fix.AddChild(Ui.Btn($"Offer {Ui.Units(Math.Floor(spare / 500) * 500)} a month", () => { d.Units = Math.Floor(spare / 500) * 500; Refresh(); }));
                box.AddChild(fix);
            }

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
                var fits = D.Segments.Where(seg => seg.Active(S.YearF) && Market.Qualifies(D, seg, c, Binning.BinsFor(c)[0].Name, S.YearF)).ToList();
                b.AddChild(Ui.L(fits.Count == 0 ? "Distributors won't stock it: it's too far behind, too hot, or missing a feature buyers now insist on." : "Distributors stock it for: " + string.Join(", ", fits.Select(x => x.Name.ToLower())), 12, fits.Count == 0 ? Ui.Warn : Ui.Muted));
                var missingWants = fits.SelectMany(seg => seg.WantsAt(S.YearF).Where(w => !Features.OfBin(D, c, Binning.BinsFor(c)[0].Name).Contains(w.Feature)).Select(w => (seg, w))).ToList();
                if (missingWants.Count > 0)
                    b.AddChild(Ui.L("Buyers mark it down for lacking: " + string.Join(", ", missingWants.Select(x => $"{Features.Name(x.w.Feature)} (−{x.w.Weight:P0} in {x.seg.Name.ToLower()})")), 12, Ui.Warn));
                double held = S.Inventory.Where(x => x.DesignId == c.Id).Sum(x => x.Units);
                if (held >= 1)
                {
                    var surplus = Ui.H(8);
                    double quote = Market.BrokerQuote(D, S, c.Id);
                    surplus.AddChild(Ui.L($"{Ui.Units(held)} in stock cost {Format.Money(Market.CarryingCost(S, c.Id))} a month to keep · a broker offers {Format.Big(quote)} (${quote / held:0.00} each)", 12, Ui.Muted));
                    surplus.AddChild(Ui.Spacer());
                    string key = c.Id;
                    if (confirmSurplus == key + "|sell")
                    {
                        surplus.AddChild(Ui.Btn("Confirm sale", () => { confirmSurplus = null; Act(Market.SellToBroker(D, S, key)); }));
                        surplus.AddChild(Ui.Btn("Keep them", () => { confirmSurplus = null; Refresh(); }));
                    }
                    else if (confirmSurplus == key + "|scrap")
                    {
                        surplus.AddChild(Ui.Btn("Confirm scrap", () => { confirmSurplus = null; Act(Market.ScrapStock(S, key)); }));
                        surplus.AddChild(Ui.Btn("Keep them", () => { confirmSurplus = null; Refresh(); }));
                    }
                    else
                    {
                        surplus.AddChild(Ui.Btn("Sell all to a broker", () => { confirmSurplus = key + "|sell"; Refresh(); }, "Cash now, at a fraction of the market price. Bigger dumps fetch less per chip."));
                        surplus.AddChild(Ui.Btn("Scrap", () => { confirmSurplus = key + "|scrap"; Refresh(); }, "Destroy the stock: no money, but no more warehouse costs."));
                    }
                    b.AddChild(surplus);
                }
                foreach (var bin in Binning.BinsFor(c))
                {
                    string key = c.Id + "|" + bin.Name;
                    var listing = S.Listings.FirstOrDefault(l => l.DesignId == c.Id && l.Bin == bin.Name);
                    if (!listPrice.TryGetValue(key, out var price))
                        price = listing?.Price ?? Math.Round(fits.Select(x => Market.MarketPrice(S, x, S.YearF)).DefaultIfEmpty(c.Stats.CostPerGoodDie * 1.5).Max(), 2);
                    listPrice[key] = price;
                    var item = Market.StockOf(S, c.Id, bin.Name);
                    double stock = item?.Units ?? 0, sold = item?.SoldLastMonth ?? 0, made = item?.MadeLastMonth ?? 0;
                    var row = Ui.H(8);
                    row.AddChild(Ui.L($"{bin.Name}: {Ui.Units(stock)} in stock", 13));
                    row.AddChild(Ui.Spacer());
                    double goingNow = Market.GoingPrice(D, S, c, bin.Name);
                    string binName = bin.Name;
                    bool tracking = listing != null && listing.Track > 0;
                    if (tracking) price = listing.Price;
                    var spin = Ui.Spin(0.01, 100000, 0.05, price, v => listPrice[key] = v, "$");
                    spin.Editable = !tracking;
                    row.AddChild(spin);
                    if (!tracking)
                    {
                        row.AddChild(Ui.Btn(listing == null ? "List" : "Reprice", () => Act(Market.SetListing(S, c.Id, binName, listPrice[key]))));
                        if (goingNow > 0)
                            row.AddChild(Ui.Btn("Price at market", () => { listPrice[key] = Math.Round(goingNow, 2); Act(Market.SetListing(S, c.Id, binName, listPrice[key])); }, $"List at what buyers are paying now ({Format.Money(goingNow)})"));
                    }
                    if (listing != null)
                    {
                        var follow = new CheckBox { Text = "Track the market", ButtonPressed = tracking, TooltipText = "Reprice every month to what buyers are paying (it rises while chips are scarce and falls as rivals catch up)" };
                        follow.Toggled += on => Act(Market.TrackMarket(D, S, c.Id, binName, on ? 1.0 : 0));
                        row.AddChild(follow);
                        row.AddChild(Ui.Btn("Unlist", () => { listPrice.Remove(key); Act(Market.SetListing(S, c.Id, binName, 0)); }));
                    }
                    b.AddChild(row);
                    b.AddChild(Ui.L(listing == null ? "   Not on sale to distributors." : $"   Listed at {Format.Money(listing.Price)} · costs about {Format.Money(c.Stats.CostPerGoodDie)} to make", 12, listing == null ? Ui.Warn : Ui.Muted));
                    if (fits.Count > 0)
                    {
                        double going = fits.Max(x => Market.MarketPrice(S, x, S.YearF));
                        bool under = listing != null && listing.Price < going * 0.85 && sold > 0 && stock < sold * 0.25;
                        string note = listing != null && listing.Price > going * 1.15 ? " · you're well above it, so it will sell slowly"
                                    : under ? $" · you're selling out {1 - listing.Price / going:P0} under it: you could charge more" : "";
                        b.AddChild(Ui.L($"   Buyers are paying about {Format.Money(going)}{note}", 12, note.Length > 0 ? Ui.Warn : Ui.Muted));
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
                string conv = r.ConvertingNodeId != null ? $" (converting to {D.Node(r.ConvertingNodeId).Name})" : "";
                b.AddChild(Ui.L($"{best?.Name ?? "—"}{conv} · makes {Ui.Units(r.Capacity)} chips a month ({r.LastUtilisation:P0} used) · {Format.Big(r.LastMonthRevenue)} revenue last month · reputation {r.Rep * 100:+0;-0;0}" +
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
