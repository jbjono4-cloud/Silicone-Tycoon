using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SiliconeTycoon.Sim;

// Headless run of the whole sim with a simple scripted company, printing one line per quarter.
// Usage: dotnet run --project tools/SimRunner -- [years=15] [seed=1] [dataFolder]
//
// The company: one fab per new node; a chip per segment on its newest node (planned like the rivals plan theirs);
// steps chips until they can ship; lists every chip with distributors at its cost plus a margin; bids on every request it
// can meet; and fills contracts before the open market. Rivals and customers are the real market.

int years = args.Length > 0 ? int.Parse(args[0]) : 15;
ulong seed = args.Length > 1 ? ulong.Parse(args[1]) : 1;
string dataFolder = args.Length > 2 ? args[2] : Path.Combine(AppContext.BaseDirectory, "data");

var d = GameData.FromFolder(dataFolder);
var s = Simulation.NewGame(d, new NewGameOptions { CompanyName = "Runner Semiconductor", Seed = seed, StartingCash = 6_000_000, StartingEngineers = 8 });
var team = s.DesignTeams[0];
var segmentOf = new Dictionary<string, string>();   // our chip id -> segment it was planned for

string Best() => d.Nodes.Where(n => s.NodeReady(n.Id)).OrderBy(n => n.FeatureNm).First().Id;
double LastQuarterProfit() => s.Ledger.Months.Skip(Math.Max(0, s.Ledger.Months.Count - 3)).Sum(m => m.Profit);
string CheapestSite() => d.Locations
    .Where(l => s.Year >= l.From && s.Year <= l.To)
    .OrderBy(l => 0.55 + 0.3 * l.LabourIndex + 0.15 * l.PowerIndex - l.Subsidy)
    .First().Id;
// cost per chip, from the estimate at design time carried forward by inflation
double CostOf(ChipDesign c) => c.Stats.CostPerGoodDie / Calendar.Inflation(Calendar.YearF(c.StartedTick)) * s.Inflation;
string Bin(ChipDesign c) => Binning.BinsFor(c)[0].Name;

// Chips a month the company can make of a design: its node's fab capacity (built or building) × dies per wafer × yield.
double CanMake(ChipDesign c) =>
    s.Fabs.Where(f => f.NodeId == c.NodeId && f.Stage != FabStage.Closed).Sum(f => f.Capacity)
    * c.Stats.DiesPerWafer * c.Stats.YieldEstimate;
// Chips a month already promised on a node, at each contract's cap.
double Committed(string nodeId) => s.Contracts
    .Where(k => k.SupplierId == Market.Player && k.Status == ContractStatus.Active && s.Design(k.DesignId)?.NodeId == nodeId)
    .Sum(k => k.MaxMonthly);

void Fail(string err) { if (err != null) Console.WriteLine($"  ! {Calendar.DateLabel(s.Tick)}: {err}"); }

bool StartChip(SegmentDef seg)
{
    var c = DesignLab.PlanForSegment(d, s, Best(), seg.Metric, $"RS-{s.Designs.Count + 1}", maxPowerW: seg.MaxPowerW);
    if (c == null) return false;
    string err = DesignOps.StartDesign(d, s, c, team.Id);
    Fail(err);
    if (err == null) segmentOf[c.Id] = seg.Id;
    return err == null;
}

Fail(FabOps.Build(d, s, "Fab 1", "n10um", "us_ca", 500));
StartChip(d.Segment("embedded"));

Console.WriteLine($"{"Quarter",-8} {"Cash",9} {"Debt",8} {"Revenue",9} {"Profit",9} {"Rep",4} {"Deals",5}  Share: embedded / home / consoles   Rivals' revenue");
int endTick = s.Tick + years * Calendar.WeeksPerYear;
int newsSeen = 0;
while (s.Tick < endTick && !s.GameOver)
{
    Simulation.Advance(d, s);

    // chip projects: step until shippable, then release and list
    foreach (var v in s.Designs.Where(x => x.Stage == DesignStage.Validated).ToList())
    {
        if (v.Showstoppers > 0 || v.FoundBugs > 2) { Fail(DesignOps.Step(d, s, v)); continue; }
        Fail(DesignOps.Release(d, s, v));
    }

    if (Calendar.WeekInMonth(s.Tick) == 0)
    {
        var released = s.Designs.Where(x => x.Stage == DesignStage.Released).ToList();

        // list everything at cost plus 40%, but no lower than 60% of the going price in its segment
        foreach (var c in released)
        {
            var seg = d.Segment(segmentOf.GetValueOrDefault(c.Id, "embedded"));
            double price = Math.Max(CostOf(c) * 1.4, Market.ExpectedPrice(seg, s.YearF) * 0.6);
            Market.SetListing(s, c.Id, Bin(c), Math.Round(price, 2));
        }

        // bid on every open request we can meet, with the strongest chip, at cost plus 30% (or 85% of the going price)
        foreach (var rfq in s.Rfqs.Where(r => !r.Bids.Any(b => b.BidderId == Market.Player)))
        {
            var seg = d.Segment(rfq.SegmentId);
            var options = s.Designs
                .Where(c => c.Stats != null && c.Stage != DesignStage.Draft && c.Stage != DesignStage.Cancelled)
                .Select(c => (chip: c, price: Math.Min(rfq.MaxPrice, Math.Max(CostOf(c) * 1.3, rfq.ExpectedPrice * 0.85))))
                .Where(o => Market.CheckBid(d, rfq, o.chip, Bin(o.chip), o.price) == null && o.price >= CostOf(o.chip))
                .Where(o => Committed(o.chip.NodeId) + rfq.EstimatedUnits * Market.ContractUpside <= CanMake(o.chip))   // only promise what we can make
                .OrderByDescending(o => Market.Perf(o.chip, Bin(o.chip), seg.Metric))
                .ToList();
            if (options.Count > 0) Market.SubmitBid(d, s, rfq.Id, options[0].chip.Id, Bin(options[0].chip), options[0].price);
        }

        // each producing fab makes the chip on its node with the biggest contract orders, else the newest; idle chips get test wafers
        double monthlyOrders = s.Contracts.Where(k => k.SupplierId == Market.Player && k.Status == ContractStatus.Active).Sum(k => k.LastMonthOrdered);
        double lastSales = s.MarketHistory.Where(r => r.Month == s.MonthIndex - 1).Sum(r => r.PlayerUnits);
        double stock = s.Inventory.Sum(x => x.Units);
        foreach (var f in s.Fabs.Where(f => f.Producing))
        {
            var chip = released.Where(c => c.NodeId == f.NodeId)
                .OrderByDescending(c => s.Contracts.Where(k => k.DesignId == c.Id && k.Status == ContractStatus.Active).Sum(k => k.LastMonthOrdered))
                .ThenByDescending(c => c.ReleasedTick)
                .FirstOrDefault();
            if (chip != null && f.DesignId != chip.Id) Fail(FabOps.AssignDesign(d, s, f, chip.Id));
            bool overstocked = stock > 3 * Math.Max(1, Math.Max(monthlyOrders, lastSales));
            FabOps.SetPlannedStarts(f, f.DesignId == null ? f.Capacity / 4 : overstocked ? f.Capacity / 3 : f.Capacity);
        }

        bool healthy = LastQuarterProfit() > 0 && s.OverdrawnMonths == 0;
        string best = Best();

        // a chip for each active segment on the newest node, one project at a time
        if (team.ProjectId == null && (healthy || s.Designs.Count(x => x.Stage == DesignStage.Released) == 0))
            foreach (var seg in d.Segments.Where(x => x.Active(s.YearF)))
            {
                bool have = s.Designs.Any(c => c.NodeId == best && c.Stage != DesignStage.Cancelled && segmentOf.GetValueOrDefault(c.Id) == seg.Id);
                if (!have && StartChip(seg)) break;
            }

        // the node race, one programme at a time
        var next = d.Nodes.FirstOrDefault(n => n.Requires == best && !s.NodeReady(n.Id));
        if (next != null && !s.NodePrograms.Any(p => !p.Done) && healthy
            && ProcessResearch.CanStart(d, s, next.Id, 1.0) == null && s.Cash > next.ResearchCost * s.Inflation * 0.5)
            Fail(ProcessResearch.Start(d, s, next.Id));

        // one fab per new node, once profitable and able to fund most of it
        if (!s.Fabs.Any(f => f.Stage == FabStage.Construction) && healthy && !s.Fabs.Any(f => f.NodeId == best))
        {
            var q = FabOps.Quote(d, s, best, CheapestSite(), 1000);
            if (q.Problem == null && s.Cash + Finance.Headroom(s) > q.NetCapex * 0.8)
            {
                if (s.Cash < q.NetCapex * 0.5) Finance.Borrow(s, Math.Min(Finance.Headroom(s), q.NetCapex * 0.5 - s.Cash));
                Fail(FabOps.Build(d, s, $"Fab {s.Fabs.Count + 1}", best, CheapestSite(), 1000));
            }
        }

        // sold out on the newest node: add another fab there
        var bestFabs = s.Fabs.Where(f => f.NodeId == best && f.Producing).ToList();
        if (!s.Fabs.Any(f => f.Stage == FabStage.Construction) && healthy && bestFabs.Count > 0
            && bestFabs.All(f => f.LastUtilisation > 0.9) && stock < Math.Max(monthlyOrders, lastSales) * 0.5)
        {
            var q = FabOps.Quote(d, s, best, CheapestSite(), 1000);
            if (q.Problem == null && s.Cash > q.NetCapex * 0.6)
                Fail(FabOps.Build(d, s, $"Fab {s.Fabs.Count + 1}", best, CheapestSite(), 1000));
        }

        // stay solvent: borrow when cash runs low, repay when flush
        if (s.Cash < 300_000 * s.Inflation && Finance.Headroom(s) > 0) Finance.Borrow(s, Math.Min(Finance.Headroom(s), 1_000_000 * s.Inflation));
        if (s.Cash > 8_000_000 * s.Inflation && s.Ledger.Debt > 0) Finance.Repay(s, Math.Min(s.Ledger.Debt, s.Cash - 6_000_000 * s.Inflation));
    }

    for (; newsSeen < s.News.Count; newsSeen++)
    {
        var n = s.News[newsSeen];
        if (n.Text.Contains("results:") || n.Text.StartsWith("You borrow") || n.Text.StartsWith("You repay")) continue;
        if (n.Severity >= 1) Console.WriteLine($"  * {Calendar.DateLabel(n.Tick)}: {n.Text}");
    }

    if (Calendar.IsQuarterEnd(s.Tick - 1))
    {
        int lastMonth = s.MonthIndex - 1;
        var q3 = s.Ledger.Months.Skip(Math.Max(0, s.Ledger.Months.Count - 3)).ToList();
        string Share(string seg)
        {
            var recs = s.MarketHistory.Where(r => r.SegmentId == seg && r.Month > lastMonth - 3).ToList();
            double demand = recs.Sum(r => r.Demand);
            return demand <= 0 ? "  - " : $"{recs.Sum(r => r.PlayerUnits) / demand,4:P0}";
        }
        int deals = s.Contracts.Count(k => k.SupplierId == Market.Player && k.Status == ContractStatus.Active);
        string rivals = string.Join(", ", s.Rivals.Select(r => $"{r.Name.Split(' ')[0]} {Format.Big(r.LastMonthRevenue * 3)}"));
        Console.WriteLine($"{Calendar.QuarterLabel(s.Tick - 1),-8} {Format.Big(s.Cash),9} {Format.Big(s.Ledger.Debt),8} {Format.Big(q3.Sum(m => m.Revenue)),9} " +
                          $"{Format.Big(q3.Sum(m => m.Profit)),9} {s.Rep * 100,4:0} {deals,5}  {Share("embedded")} / {Share("home")} / {Share("consoles")}   {rivals}");
    }
}

Console.WriteLine();
Console.WriteLine("Our chips:");
foreach (var c in s.Designs)
{
    var st = c.Stats;
    string seg = segmentOf.GetValueOrDefault(c.Id, "?");
    Console.WriteLine($"  {c.Name,-6} {seg,-9} {d.Node(c.NodeId).Name,-7} {c.Stage,-10} {c.SteppingName,-3} {st.Transistors,8:N0} tr {c.DieAreaMm2,6:0.0} mm² {st.ClockMHz,6:0.00} MHz {st.PowerW,5:0.00} W  {d.Package(c.PackageId).Name}");
}
Console.WriteLine();
Console.WriteLine("Rivals:");
foreach (var r in s.Rivals)
{
    string node = Rivals.BestNode(d, r)?.Name ?? "?";
    string chips = string.Join(", ", r.Products.Select(p => $"{p.Design.Name} ({p.SegmentId}, {d.Node(p.Design.NodeId).Name})"));
    Console.WriteLine($"  {r.Name}: {node}, capacity {r.Capacity:N0}/month, lifetime revenue {Format.Big(r.TotalRevenue)}. Selling: {chips}");
}
Console.WriteLine();
Console.WriteLine(s.GameOver ? s.EndText : $"Finished at {Calendar.DateLabel(s.Tick)} with {Format.Big(s.Cash)} cash, {Format.Big(s.Ledger.Debt)} debt, reputation {s.Rep * 100:0} ({GameState.RepWord(s.Rep)}).");
