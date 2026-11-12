using System;
using System.IO;
using System.Linq;
using SiliconeTycoon.Sim;

// Headless run of the sim core with a simple scripted company, printing one line per quarter.
// Usage: dotnet run --project tools/SimRunner -- [years=15] [seed=1] [dataFolder]
// There is no market yet (milestone 3), so finished dies are sold at a flat placeholder price.

int years = args.Length > 0 ? int.Parse(args[0]) : 15;
ulong seed = args.Length > 1 ? ulong.Parse(args[1]) : 1;
string dataFolder = args.Length > 2 ? args[2] : Path.Combine(AppContext.BaseDirectory, "data");

var d = GameData.FromFolder(dataFolder);
var s = Simulation.NewGame(d, new NewGameOptions { CompanyName = "Runner Semiconductor", Seed = seed, StartingCash = 6_000_000 });

const double TestChipTransistors = 5000;      // a small logic chip, shrunk onto each new node
const double PlaceholderDiePrice = 45;        // 1970 dollars per good die, falling 6% a year in real terms

double DieArea(ProcessNodeDef n) => TestChipTransistors / n.LogicDensity + 2.0;   // + pads
double DiePrice() => PlaceholderDiePrice * Math.Pow(0.94, s.YearF - 1970) * s.Inflation;
string Best() => d.Nodes.Where(n => s.NodeReady(n.Id)).OrderBy(n => n.FeatureNm).First().Id;

// Pick the cheapest place to run a fab that's open this year.
string CheapestSite() => d.Locations
    .Where(l => s.Year >= l.From && s.Year <= l.To)
    .OrderBy(l => 0.55 + 0.3 * l.LabourIndex + 0.15 * l.PowerIndex - l.Subsidy)
    .First().Id;

void Fail(string err) { if (err != null) Console.WriteLine($"  ! {Calendar.DateLabel(s.Tick)}: {err}"); }

Fail(FabOps.Build(d, s, "Fab 1", "n10um", "us_ca", 800));
s.Fabs[0].DieAreaMm2 = DieArea(d.Node("n10um"));
Fail(ProcessResearch.Start(d, s, "n8um"));

Console.WriteLine($"{"Quarter",-8} {"Cash",9} {"Debt",9} {"Revenue",9} {"Profit",9}  Fabs (node, stage, util, D0, yield)");
int endTick = s.Tick + years * Calendar.WeeksPerYear;
int newsSeen = s.News.Count;
while (s.Tick < endTick && !s.GameOver)
{
    Simulation.Advance(d, s);

    // sell finished dies at the placeholder price
    foreach (var f in s.Fabs)
        foreach (var lot in FabOps.Undelivered(f).ToList())
        {
            s.Ledger.Book(Accounts.Sales, lot.GoodDies * DiePrice());
            lot.Delivered = true;
        }

    // keep the node race going
    var next = d.Nodes.FirstOrDefault(n => n.Requires == Best() && ProcessResearch.Active(s, n.Id) == null && !s.NodeReady(n.Id));
    if (next != null && ProcessResearch.CanStart(d, s, next.Id, 1.0) == null && s.Cash > next.ResearchCost * s.Inflation * 0.3)
        Fail(ProcessResearch.Start(d, s, next.Id));

    // build on a newly ready node once, borrowing if needed
    string best = Best();
    if (!s.Fabs.Any(f => f.NodeId == best))
    {
        var q = FabOps.Quote(d, s, best, CheapestSite(), 1000);
        if (q.Problem == null && s.Cash < q.NetCapex * 0.5) Finance.Borrow(s, Math.Min(Finance.Headroom(s), q.NetCapex * 0.5));
        if (q.Problem == null && s.Cash > q.NetCapex * 0.4)
        {
            Fail(FabOps.Build(d, s, $"Fab {s.Fabs.Count + 1}", best, CheapestSite(), 1000));
            s.Fabs[^1].DieAreaMm2 = DieArea(d.Node(best));
        }
    }

    // stay solvent: borrow when cash runs low, repay when flush
    if (s.Cash < 300_000 * s.Inflation && Finance.Headroom(s) > 0) Finance.Borrow(s, Math.Min(Finance.Headroom(s), 1_000_000 * s.Inflation));
    if (s.Cash > 8_000_000 * s.Inflation && s.Ledger.Debt > 0) Finance.Repay(s, Math.Min(s.Ledger.Debt, s.Cash - 6_000_000 * s.Inflation));

    // print notable news as it happens
    for (; newsSeen < s.News.Count; newsSeen++)
    {
        var n = s.News[newsSeen];
        if (n.Text.Contains("results:")) continue;
        if (n.Severity >= 1) Console.WriteLine($"  * {Calendar.DateLabel(n.Tick)}: {n.Text}");
    }

    if (Calendar.IsQuarterEnd(s.Tick - 1))
    {
        var q3 = s.Ledger.Months.Skip(Math.Max(0, s.Ledger.Months.Count - 3)).ToList();
        string fabs = string.Join(" | ", s.Fabs.Select(f =>
        {
            var n = d.Node(f.NodeId);
            double d0 = FabOps.D0(d, f);
            double y = YieldModel.Yield(d0, f.DieAreaMm2);
            return $"{n.Name} {f.Stage.ToString()[..4]} {f.LastUtilisation,4:P0} D0 {d0,4:0.0} Y {y,4:P0}";
        }));
        Console.WriteLine($"{Calendar.QuarterLabel(s.Tick - 1),-8} {Format.Big(s.Cash),9} {Format.Big(s.Ledger.Debt),9} {Format.Big(q3.Sum(m => m.Revenue)),9} {Format.Big(q3.Sum(m => m.Profit)),9}  {fabs}");
    }
}

Console.WriteLine();
Console.WriteLine(s.GameOver ? s.EndText : $"Finished at {Calendar.DateLabel(s.Tick)} with {Format.Big(s.Cash)} cash and {Format.Big(s.Ledger.Debt)} debt.");
