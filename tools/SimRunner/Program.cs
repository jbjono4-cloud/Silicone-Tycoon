using System;
using System.IO;
using System.Linq;
using SiliconeTycoon.Sim;

// Headless run of the sim core with a cautious scripted company, printing one line per quarter.
// Usage: dotnet run --project tools/SimRunner -- [years=15] [seed=1] [dataFolder]
// There is no market yet (milestone 3), so the runner fakes one: a placeholder price per good die and a monthly
// demand ceiling that grows over time. Unsold dies wait in stock.

int years = args.Length > 0 ? int.Parse(args[0]) : 15;
ulong seed = args.Length > 1 ? ulong.Parse(args[1]) : 1;
string dataFolder = args.Length > 2 ? args[2] : Path.Combine(AppContext.BaseDirectory, "data");

var d = GameData.FromFolder(dataFolder);
var s = Simulation.NewGame(d, new NewGameOptions { CompanyName = "Runner Semiconductor", Seed = seed, StartingCash = 6_000_000 });

const double TestChipTransistors = 2300;      // about a first-generation microprocessor, shrunk onto each new node
const double PlaceholderDiePrice = 40;        // 1970 dollars per die, falling 8% a year in real terms
const double StartDemand = 12_000;            // dies a month in 1970, growing 30% a year

double DieArea(ProcessNodeDef n) => TestChipTransistors / n.LogicDensity + 2.0;   // + bond pads
double DiePrice() => PlaceholderDiePrice * Math.Pow(0.92, s.YearF - 1970) * s.Inflation;
double WeeklyDemand() => StartDemand * Math.Pow(1.3, s.YearF - 1970) / Calendar.WeeksPerMonth;
string Best() => d.Nodes.Where(n => s.NodeReady(n.Id)).OrderBy(n => n.FeatureNm).First().Id;
double LastQuarterProfit() => s.Ledger.Months.Skip(Math.Max(0, s.Ledger.Months.Count - 3)).Sum(m => m.Profit);

// Cheapest place to run a fab that's open this year.
string CheapestSite() => d.Locations
    .Where(l => s.Year >= l.From && s.Year <= l.To)
    .OrderBy(l => 0.55 + 0.3 * l.LabourIndex + 0.15 * l.PowerIndex - l.Subsidy)
    .First().Id;

void Fail(string err) { if (err != null) Console.WriteLine($"  ! {Calendar.DateLabel(s.Tick)}: {err}"); }

Fail(FabOps.Build(d, s, "Fab 1", "n10um", "us_ca", 500));
s.Fabs[0].DieAreaMm2 = DieArea(d.Node("n10um"));

double stock = 0;
Console.WriteLine($"{"Quarter",-8} {"Cash",9} {"Debt",9} {"Revenue",9} {"Profit",9} {"Stock",8}  Fabs (node, stage, util, D0, yield)");
int endTick = s.Tick + years * Calendar.WeeksPerYear;
int newsSeen = 0;
while (s.Tick < endTick && !s.GameOver)
{
    Simulation.Advance(d, s);

    // finished dies go to stock; sell up to this week's demand
    foreach (var f in s.Fabs)
        foreach (var lot in FabOps.Undelivered(f).ToList())
        {
            stock += lot.GoodDies;
            lot.Delivered = true;
        }
    double sold = Math.Min(stock, WeeklyDemand());
    stock -= sold;
    if (sold > 0) s.Ledger.Book(Accounts.Sales, sold * DiePrice());

    // don't make more than the market takes: trim wafer starts when stock passes two months of demand
    foreach (var f in s.Fabs.Where(f => f.Producing))
        FabOps.SetPlannedStarts(f, stock > WeeklyDemand() * 8 ? f.Capacity / 2 : f.Capacity);

    bool healthy = LastQuarterProfit() > 0 && s.OverdrawnMonths == 0;

    // the node race: only when the business can carry it
    var next = d.Nodes.FirstOrDefault(n => n.Requires == Best() && ProcessResearch.Active(s, n.Id) == null && !s.NodeReady(n.Id));
    bool researching = s.NodePrograms.Any(p => !p.Done);
    if (next != null && !researching && healthy && ProcessResearch.CanStart(d, s, next.Id, 1.0) == null
        && s.Cash > next.ResearchCost * s.Inflation * 0.5)
        Fail(ProcessResearch.Start(d, s, next.Id));

    // one fab per new node, once profitable and able to fund most of it
    string best = Best();
    bool building = s.Fabs.Any(f => f.Stage == FabStage.Construction);
    if (!building && healthy && !s.Fabs.Any(f => f.NodeId == best))
    {
        var q = FabOps.Quote(d, s, best, CheapestSite(), 1000);
        if (q.Problem == null && s.Cash + Finance.Headroom(s) > q.NetCapex * 0.8)
        {
            if (s.Cash < q.NetCapex * 0.5) Finance.Borrow(s, Math.Min(Finance.Headroom(s), q.NetCapex * 0.5 - s.Cash));
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
            double y = YieldModel.Yield(d0, f.DieAreaMm2);
            return $"{n.Name} {f.Stage.ToString()[..4]} {f.LastUtilisation,4:P0} D0 {d0,4:0.0} Y {y,4:P0}";
        }));
        string stockText = stock >= 1e6 ? $"{stock / 1e6:0.0}M" : $"{stock / 1e3:0}k";
        Console.WriteLine($"{Calendar.QuarterLabel(s.Tick - 1),-8} {Format.Big(s.Cash),9} {Format.Big(s.Ledger.Debt),9} {Format.Big(q3.Sum(m => m.Revenue)),9} {Format.Big(q3.Sum(m => m.Profit)),9} {stockText,8}  {fabs}");
    }
}

Console.WriteLine();
Console.WriteLine(s.GameOver ? s.EndText : $"Finished at {Calendar.DateLabel(s.Tick)} with {Format.Big(s.Cash)} cash and {Format.Big(s.Ledger.Debt)} debt.");
