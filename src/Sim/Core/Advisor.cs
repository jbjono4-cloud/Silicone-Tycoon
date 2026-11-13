using System;
using System.Collections.Generic;
using System.Linq;

namespace SiliconeTycoon.Sim
{
    public class Alert
    {
        /// <summary>0 info, 2 warning, 3 serious.</summary>
        public int Severity;
        public string Text;
        /// <summary>Tab where the player can act on it (Company, Design, Fabs, Research, Market).</summary>
        public string Tab;
    }

    /// <summary>
    /// Spots problems the player should know about: stock piling up, chips with nowhere to be made, unlisted or overpriced
    /// chips, unanswered requests, short deliveries and running out of cash. Read-only.
    /// </summary>
    public static class Advisor
    {
        /// <summary>Months of sales in the warehouse before it counts as piling up.</summary>
        public const double StockMonthsWarning = 3;

        /// <summary>Average monthly change in cash over the last few months, ignoring borrowing and repayments.</summary>
        public static double MonthlyCashFlow(GameState s, int months = 3)
        {
            var recent = s.Ledger.Months.Skip(Math.Max(0, s.Ledger.Months.Count - months)).ToList();
            if (recent.Count == 0) return 0;
            return recent.Average(m => m.Profit - m.Capex
                                       - m.Lines.Where(l => l.Key == Accounts.Depreciation || l.Key == Accounts.FabClosure || l.Key == Accounts.Retooling).Sum(l => l.Value));
        }

        static bool HasFab(GameState s, string nodeId) => s.Fabs.Any(f => f.NodeId == nodeId && f.Stage != FabStage.Closed);

        public static List<Alert> Alerts(GameData d, GameState s)
        {
            var a = new List<Alert>();
            void Add(int sev, string tab, string text) => a.Add(new Alert { Severity = sev, Tab = tab, Text = text });

            // ---- money
            double flow = MonthlyCashFlow(s);
            if (s.Cash < 0)
                Add(3, "Company", $"You're overdrawn ({s.OverdrawnMonths} of {Simulation.MaxOverdrawnMonths} months allowed). Borrow, or cut costs now.");
            else if (flow < 0)
            {
                double months = (s.Cash + Finance.Headroom(s)) / -flow;
                if (months < 12) Add(months < 6 ? 3 : 2, "Company",
                    $"Cash is falling by about {Format.Big(-flow)} a month: about {months:0} months left, counting what the bank would still lend.");
            }

            // ---- chips and fabs
            foreach (var c in s.Designs)
            {
                var n = d.Node(c.NodeId);
                if (c.Stage == DesignStage.Released)
                {
                    bool making = s.Fabs.Any(f => f.Makes(c.Id) && f.Producing);
                    double stock = s.Inventory.Where(x => x.DesignId == c.Id).Sum(x => x.Units);
                    double sold = s.Inventory.Where(x => x.DesignId == c.Id).Sum(x => x.SoldLastMonth);
                    double made = s.Inventory.Where(x => x.DesignId == c.Id).Sum(x => x.MadeLastMonth);
                    bool newest = !s.Designs.Any(o => o.Stage == DesignStage.Released && o.NodeId == c.NodeId && o.ReleasedTick > c.ReleasedTick);
                    if (!HasFab(s, c.NodeId) && newest)
                        Add(2, "Fabs", $"The {c.Name} is released but you have no {n.Name} fab to make it. Build or retool one.");
                    else if (!making && newest && stock < 1000)
                        Add(2, "Fabs", $"No fab is making the {c.Name}. Point a {n.Name} fab at it.");

                    if (stock >= 1000)
                    {
                        if (sold <= 0) Add(2, "Market", $"{Format.Units(stock)} {c.Name} chips are in the warehouse and none sold last month.");
                        else if (stock > sold * StockMonthsWarning && made > sold)
                            Add(2, "Fabs", $"{stock / sold:0} months of {c.Name} stock in the warehouse: you made {Format.Units(made)} last month but sold {Format.Units(sold)}. Cut wafer starts or lower the price.");
                    }

                    var bin = Binning.BinsFor(c)[0].Name;
                    var fits = d.Segments.Where(seg => seg.Active(s.YearF)
                                                        && Market.Perf(c, bin, seg.Metric) >= seg.NeedAt(s.YearF) * Market.OpenMarketMinShare
                                                        && Market.Power(c, bin) <= seg.MaxPowerW).ToList();
                    var listing = s.Listings.FirstOrDefault(l => l.DesignId == c.Id);
                    if (stock >= 1000 && fits.Count == 0 && !s.Contracts.Any(k => k.DesignId == c.Id && k.Status == ContractStatus.Active))
                        Add(2, "Market", $"Distributors won't take the {c.Name} any more: it's too far behind. Stop making it.");
                    else if (stock >= 1000 && listing == null)
                        Add(2, "Market", $"The {c.Name} isn't listed with distributors, so {Format.Units(stock)} chips are sitting unsold.");
                    else if (listing != null && fits.Count > 0)
                    {
                        double going = fits.Max(seg => Market.MarketPrice(s, seg, s.YearF));
                        if (listing.Price > going * 1.15)
                            Add(0, "Market", $"The {c.Name} is listed at {Format.Money(listing.Price)}, above the going price of {Format.Money(going)}. It will sell slowly.");
                    }
                }
                else if (c.Stage == DesignStage.Designing || c.Stage == DesignStage.Silicon || c.Stage == DesignStage.Validated)
                {
                    if (!HasFab(s, c.NodeId))
                        Add(2, "Fabs", $"The {c.Name} is a {n.Name} chip, but you have no {n.Name} fab. Build or retool one before it's ready.");
                    if (c.Stage == DesignStage.Validated)
                        Add(0, "Design", $"The {c.Name}'s silicon is back: release it or start a new stepping.");
                }
            }

            foreach (var f in s.Fabs.Where(f => f.Producing))
            {
                if (f.DesignId == null && s.Designs.Any(c => c.Stage == DesignStage.Released && c.NodeId == f.NodeId))
                    Add(2, "Fabs", $"{f.Name} is running only test wafers while a released chip could be made there.");
                else if (f.Stage == FabStage.Running && f.PlannedStarts < f.Capacity * 0.9 && f.DesignId != null && !f.Expanding
                         && s.Designs.Any(c => c.Stage == DesignStage.Released && c.NodeId == f.NodeId))
                    Add(0, "Fabs", $"{f.Name} has {f.Capacity - f.PlannedStarts:N0} wafer starts a month unplanned.");
                else if (f.Stage == FabStage.Running && f.LastUtilisation < 0.5 && f.DesignId != null && !f.Expanding)
                    Add(0, "Fabs", $"{f.Name} ran at {f.LastUtilisation:P0} last month. An idle fab still costs its running costs and depreciation.");
            }

            // ---- customers
            int unbid = s.Rfqs.Count(r => !r.Bids.Any(b => b.BidderId == Market.Player));
            if (unbid > 0) Add(0, "Market", $"{unbid} customer{(unbid == 1 ? " is" : "s are")} asking for bids you haven't answered.");
            foreach (var k in s.Contracts.Where(k => k.SupplierId == Market.Player && k.Status == ContractStatus.Active && k.ShortMonths > 0))
                Add(3, "Fabs", $"You're short on {d.Customer(k.CustomerId).Name}'s order ({k.ShortMonths} month{(k.ShortMonths == 1 ? "" : "s")} running; three and they cancel).");

            return a.OrderByDescending(x => x.Severity).ToList();
        }
    }
}
