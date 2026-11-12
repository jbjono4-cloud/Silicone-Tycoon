using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SiliconeTycoon.Sim;

// Headless run of the sim with a cautious scripted company, printing one line per quarter.
// Usage: dotnet run --project tools/SimRunner -- [years=15] [seed=1] [dataFolder]
// The company designs a real chip for each new node (best core, memory interface, smallest I/O ring and cheapest package
// that pass analysis), tapes it out, steps it until it can ship, then makes and bins it in its fabs.
// There is no market yet (milestone 3), so sales are faked: a placeholder price that rises with performance, and a
// monthly demand ceiling that grows over time. Unsold good dies wait in stock.

int years = args.Length > 0 ? int.Parse(args[0]) : 15;
ulong seed = args.Length > 1 ? ulong.Parse(args[1]) : 1;
string dataFolder = args.Length > 2 ? args[2] : Path.Combine(AppContext.BaseDirectory, "data");

var d = GameData.FromFolder(dataFolder);
var s = Simulation.NewGame(d, new NewGameOptions { CompanyName = "Runner Semiconductor", Seed = seed, StartingCash = 6_000_000 });
var team = s.DesignTeams[0];

const double BasePrice = 30;                  // 1970 dollars per die for 1970-class performance, falling 8% a year
const double StartDemand = 12_000;            // dies a month in 1970, growing 30% a year
double baseCompute = 0;

double DiePrice(ChipDesign c) => BasePrice * Math.Sqrt(Math.Max(1, c.Stats.Compute / baseCompute)) * Math.Pow(0.92, s.YearF - 1970) * s.Inflation;
double WeeklyDemand() => StartDemand * Math.Pow(1.3, s.YearF - 1970) / Calendar.WeeksPerMonth;
string Best() => d.Nodes.Where(n => s.NodeReady(n.Id)).OrderBy(n => n.FeatureNm).First().Id;
double LastQuarterProfit() => s.Ledger.Months.Skip(Math.Max(0, s.Ledger.Months.Count - 3)).Sum(m => m.Profit);

string CheapestSite() => d.Locations
    .Where(l => s.Year >= l.From && s.Year <= l.To)
    .OrderBy(l => 0.55 + 0.3 * l.LabourIndex + 0.15 * l.PowerIndex - l.Subsidy)
    .First().Id;

BlockDef BestOf(BlockKind kind, Func<BlockDef, double> score) =>
    d.Blocks.Where(b => b.Kind == kind && b.Year <= s.Year).OrderByDescending(score).FirstOrDefault();

// The best simple microprocessor the company can make on a node: first valid combination of I/O ring and package.
ChipDesign PlanChip(string nodeId)
{
    var core = BestOf(BlockKind.Core, b => b.Ipc);
    var mem = BestOf(BlockKind.MemoryInterface, b => b.BytesPerClock);
    foreach (var io in d.Blocks.Where(b => b.Kind == BlockKind.Io && b.Year <= s.Year).OrderBy(b => b.Pins))
        foreach (var pkg in d.Packages.Where(p => p.Year <= s.Year).OrderBy(p => p.Cost))
        {
            var c = DesignLab.Starter(d, $"RS-{s.Designs.Count + 1}", nodeId, pkg.Id, core.Id, mem.Id, io.Id);
            if (DesignLab.Analyze(d, s, c).Valid) return c;
        }
    return null;
}

void Fail(string err) { if (err != null) Console.WriteLine($"  ! {Calendar.DateLabel(s.Tick)}: {err}"); }

Fail(FabOps.Build(d, s, "Fab 1", "n10um", "us_ca", 500));
var first = PlanChip("n10um");
Fail(DesignOps.StartDesign(d, s, first, team.Id));
baseCompute = first.Stats.Compute;

var stock = new Dictionary<string, double>();   // design id -> good dies waiting
Console.WriteLine($"{"Quarter",-8} {"Cash",9} {"Debt",9} {"Revenue",9} {"Profit",9} {"Stock",7}  Fabs (node, stage, chip, util, D0, sellable)");
int endTick = s.Tick + years * Calendar.WeeksPerYear;
int newsSeen = 0;
while (s.Tick < endTick && !s.GameOver)
{
    Simulation.Advance(d, s);

    // finished lots: real chips go to stock, test wafers are scrapped
    foreach (var f in s.Fabs)
        foreach (var lot in FabOps.Undelivered(f).ToList())
        {
            if (lot.Bins != null)
            {
                stock.TryGetValue(lot.DesignId, out var have);
                stock[lot.DesignId] = have + lot.GoodDies;
            }
            lot.Delivered = true;
        }

    // sell newest chips first, up to this week's demand
    double demand = WeeklyDemand();
    foreach (var id in stock.Keys.OrderByDescending(id => s.Design(id).ReleasedTick).ToList())
    {
        double sold = Math.Min(stock[id], demand);
        if (sold <= 0) continue;
        stock[id] -= sold;
        demand -= sold;
        s.Ledger.Book(Accounts.Sales, sold * DiePrice(s.Design(id)));
    }
    double totalStock = stock.Values.Sum();

    // chip projects: step until shippable, then release
    foreach (var v in s.Designs.Where(x => x.Stage == DesignStage.Validated).ToList())
        Fail(v.Showstoppers > 0 || v.FoundBugs > 2 ? DesignOps.Step(d, s, v) : DesignOps.Release(d, s, v));

    // each producing fab makes the newest released chip for its node, else test wafers at a quarter of capacity
    foreach (var f in s.Fabs.Where(f => f.Producing))
    {
        var chip = s.Designs.Where(x => x.Stage == DesignStage.Released && x.NodeId == f.NodeId).OrderByDescending(x => x.ReleasedTick).FirstOrDefault();
        if (chip != null && f.DesignId != chip.Id) Fail(FabOps.AssignDesign(d, s, f, chip.Id));
        int plan = f.DesignId == null ? f.Capacity / 4 : totalStock > WeeklyDemand() * 8 ? f.Capacity / 2 : f.Capacity;
        FabOps.SetPlannedStarts(f, plan);
    }

    bool healthy = LastQuarterProfit() > 0 && s.OverdrawnMonths == 0;
    string best = Best();

    // a new chip for the newest node when the team is free
    if (team.ProjectId == null && healthy && !s.Designs.Any(x => x.NodeId == best && x.Stage != DesignStage.Cancelled))
    {
        var plan = PlanChip(best);
        if (plan != null) Fail(DesignOps.StartDesign(d, s, plan, team.Id));
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

    // stay solvent: borrow when cash runs low, repay when flush
    if (s.Cash < 300_000 * s.Inflation && Finance.Headroom(s) > 0) Finance.Borrow(s, Math.Min(Finance.Headroom(s), 1_000_000 * s.Inflation));
    if (s.Cash > 8_000_000 * s.Inflation && s.Ledger.Debt > 0) Finance.Repay(s, Math.Min(s.Ledger.Debt, s.Cash - 6_000_000 * s.Inflation));

    for (; newsSeen < s.News.Count; newsSeen++)
    {
        var n = s.News[newsSeen];
        if (n.Text.Contains("results:") || n.Text.StartsWith("You borrow") || n.Text.StartsWith("You repay")) continue;
        if (n.Severity >= 1) Console.WriteLine($"  * {Calendar.DateLabel(n.Tick)}: {n.Text}");
    }

    if (Calendar.IsQuarterEnd(s.Tick - 1))
    {
        var q3 = s.Ledger.Months.Skip(Math.Max(0, s.Ledger.Months.Count - 3)).ToList();
        string fabs = string.Join(" | ", s.Fabs.Select(f =>
        {
            var n = d.Node(f.NodeId);
            double d0 = FabOps.D0(d, f);
            var recent = f.Lots.Where(l => l.Done && l.Bins != null).TakeLast(8).ToList();
            string sellable = recent.Count == 0 ? "  -" : $"{recent.Sum(l => l.GoodDies) / (double)Math.Max(1, recent.Sum(l => l.GrossDies)),4:P0}";
            string chip = s.Design(f.DesignId)?.Name ?? "test";
            return $"{n.Name} {f.Stage.ToString()[..4]} {chip} {f.LastUtilisation,4:P0} D0 {d0,4:0.0} {sellable}";
        }));
        string stockText = totalStock >= 1e6 ? $"{totalStock / 1e6:0.0}M" : $"{totalStock / 1e3:0}k";
        Console.WriteLine($"{Calendar.QuarterLabel(s.Tick - 1),-8} {Format.Big(s.Cash),9} {Format.Big(s.Ledger.Debt),9} {Format.Big(q3.Sum(m => m.Revenue)),9} {Format.Big(q3.Sum(m => m.Profit)),9} {stockText,7}  {fabs}");
    }
}

Console.WriteLine();
Console.WriteLine("Chips:");
foreach (var c in s.Designs)
{
    var st = c.Stats;
    Console.WriteLine($"  {c.Name,-6} {d.Node(c.NodeId).Name,-7} {c.Stage,-10} {c.SteppingName,-3} {st.Transistors,8:N0} tr  {c.DieAreaMm2,6:0.0} mm²  {st.ClockMHz,6:0.00} MHz  {st.PowerW,5:0.00} W  compute {st.Compute,6:0.00}  {d.Package(c.PackageId).Name}");
}
Console.WriteLine();
Console.WriteLine(s.GameOver ? s.EndText : $"Finished at {Calendar.DateLabel(s.Tick)} with {Format.Big(s.Cash)} cash and {Format.Big(s.Ledger.Debt)} debt.");
