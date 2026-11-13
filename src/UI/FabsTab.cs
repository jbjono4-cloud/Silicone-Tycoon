using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SiliconeTycoon.Sim;

namespace SiliconeTycoon.UI
{
    /// <summary>Your fabs: construction, ramp, yield, wafer starts, engineering, retooling and closing; plus building new ones.</summary>
    public partial class FabsTab : GameTab
    {
        VBoxContainer list;
        // build form
        LineEdit nameEdit;
        OptionButton nodePick, sitePick;
        SpinBox capSpin;
        VBoxContainer quoteBox;
        List<ProcessNodeDef> formNodes = new List<ProcessNodeDef>();
        List<LocationDef> formSites = new List<LocationDef>();
        string formNode, formSite;
        int formCap = 1000;
        // per-fab choices that survive refreshes
        readonly Dictionary<string, string> retoolChoice = new Dictionary<string, string>();
        readonly Dictionary<string, int> expandChoice = new Dictionary<string, int>();
        string confirmClose;

        public FabsTab() { }
        public FabsTab(Main m) : base(m, "Fabs") { }

        protected override void Build()
        {
            list = Ui.V(10);
            var ls = Ui.Scroll(list);
            ls.SizeFlagsStretchRatio = 1.6f;
            AddChild(ls);

            var form = Ui.V(10);
            var fs = Ui.Scroll(form);
            AddChild(fs);
            var (card, body) = Ui.CardV("Build a fab");
            form.AddChild(card);
            body.AddChild(Ui.Wrap("A fab runs one process node. It takes a year or more to build, then ramps up over months while its defect density falls with every wafer it runs.", 13, Ui.Muted));
            body.AddChild(Ui.Heading("Name"));
            nameEdit = new LineEdit { PlaceholderText = "Fab name (optional)" };
            body.AddChild(nameEdit);
            body.AddChild(Ui.Heading("Process node"));
            nodePick = Ui.Options(new string[0], 0, i => { formNode = formNodes[i].Id; RefreshQuote(); });
            body.AddChild(nodePick);
            body.AddChild(Ui.Heading("Site"));
            sitePick = Ui.Options(new string[0], 0, i => { formSite = formSites[i].Id; RefreshQuote(); });
            body.AddChild(sitePick);
            body.AddChild(Ui.Heading("Capacity (wafer starts a month)"));
            capSpin = Ui.Spin(FabOps.MinCapacity, 20000, 100, formCap, v => { formCap = (int)v; RefreshQuote(); });
            body.AddChild(capSpin);
            quoteBox = Ui.V(3);
            body.AddChild(quoteBox);
            body.AddChild(Ui.Primary("Start construction", () =>
            {
                Act(FabOps.Build(D, S, nameEdit.Text, formNode, formSite, formCap));
                nameEdit.Text = "";
            }));
        }

        public override void Refresh()
        {
            if (S == null || list == null) return;
            RefreshForm();
            RefreshList();
        }

        // ---------------------------------------------------------------- build form

        void RefreshForm()
        {
            formNodes = D.Nodes.Where(n => S.NodeReady(n.Id)).OrderBy(n => n.FeatureNm).ToList();
            formSites = D.Locations.Where(l => S.Year >= l.From && S.Year <= l.To).ToList();
            if (formNode == null || !formNodes.Any(n => n.Id == formNode)) formNode = formNodes.FirstOrDefault()?.Id;
            if (formSite == null || !formSites.Any(l => l.Id == formSite)) formSite = formSites.FirstOrDefault()?.Id;
            Fill(nodePick, formNodes.Select(n => $"{n.Name} ({n.WaferMm:0} mm wafers)").ToArray(), formNodes.FindIndex(n => n.Id == formNode));
            Fill(sitePick, formSites.Select(l => SiteLabel(l)).ToArray(), formSites.FindIndex(l => l.Id == formSite));
            RefreshQuote();
        }

        static string SiteLabel(LocationDef l)
        {
            var bits = new List<string>();
            if (l.Subsidy > 0) bits.Add($"{l.Subsidy:P0} grants");
            bits.Add($"labour {l.LabourIndex:0.##}×");
            if (l.QuakeRisk > 0) bits.Add("earthquakes");
            if (l.PoliticalRisk > 0) bits.Add("political risk");
            return $"{l.Name} · {string.Join(", ", bits)}";
        }

        static void Fill(OptionButton o, string[] items, int selected)
        {
            o.Clear();
            for (int i = 0; i < items.Length; i++) o.AddItem(items[i], i);
            if (items.Length > 0) o.Selected = Math.Clamp(selected, 0, items.Length - 1);
        }

        void RefreshQuote()
        {
            if (quoteBox == null || S == null) return;
            Ui.Clear(quoteBox);
            if (formNode == null || formSite == null) return;
            var q = FabOps.Quote(D, S, formNode, formSite, formCap);
            var n = D.Node(formNode);
            quoteBox.AddChild(Ui.Rule());
            quoteBox.AddChild(Ui.Row("Construction cost", Format.Money(q.Capex)));
            if (q.Subsidy > 0) quoteBox.AddChild(Ui.Row("Government grants", "−" + Format.Money(q.Subsidy), Ui.Ok));
            quoteBox.AddChild(Ui.Row("You pay", Format.Money(q.NetCapex), null, 15));
            quoteBox.AddChild(Ui.Row("Build time", $"{q.BuildMonths} months, then a {q.RampMonths}-month ramp"));
            quoteBox.AddChild(Ui.Row("Running cost", $"{Format.Money(q.MonthlyRunning)} a month"));
            quoteBox.AddChild(Ui.Row("Process engineering (recommended)", $"{Format.Money(q.RecommendedEngineering)} a month"));
            quoteBox.AddChild(Ui.Row("Wafer materials", $"{Format.Money(q.WaferCost)} a wafer"));
            quoteBox.AddChild(Ui.Row("Depreciation once built", $"{Format.Money(q.NetCapex / FabOps.DepreciationMonths)} a month"));
            quoteBox.AddChild(Ui.Row("Defect density", $"{n.D0Launch:0.0} → {n.D0Mature:0.0} per cm²"));
            if (q.Problem != null) quoteBox.AddChild(Ui.Wrap(q.Problem, 13, Ui.Bad));
            else if (S.Cash < q.NetCapex / q.BuildMonths) quoteBox.AddChild(Ui.Wrap($"You need {Format.Money(q.NetCapex / q.BuildMonths)} for the first month.", 13, Ui.Bad));
        }

        // ---------------------------------------------------------------- fab list

        void RefreshList()
        {
            Ui.Clear(list);
            var fabs = S.Fabs.Where(f => f.Stage != FabStage.Closed).ToList();
            if (fabs.Count == 0)
                list.AddChild(Ui.Card(Ui.Wrap("You have no fabs. Build one on the right: without a fab there is nothing to make your chips on.", 14, Ui.Muted)));
            foreach (var f in fabs) list.AddChild(FabCard(f));
            int closed = S.Fabs.Count(f => f.Stage == FabStage.Closed);
            if (closed > 0) list.AddChild(Ui.L($"{closed} closed fab{(closed == 1 ? "" : "s")} not shown.", 12, Ui.Muted));
        }

        Control FabCard(Fab f)
        {
            var n = D.Node(f.NodeId);
            var site = D.Location(f.LocationId);
            var (card, b) = Ui.CardV(null, 6);
            var head = Ui.H(10);
            head.AddChild(Ui.Title(f.Name, 17));
            head.AddChild(Ui.L($"{n.Name} · {site.Name}", 14, Ui.Muted));
            head.AddChild(Ui.Spacer());
            string stage = f.Stage switch
            {
                FabStage.Construction => f.Retooling ? "Retooling" : "Under construction",
                FabStage.Ramping => "Ramping up",
                FabStage.Running => "Running",
                _ => "Closed",
            };
            head.AddChild(Ui.L(stage, 14, f.Stage == FabStage.Running ? Ui.Ok : Ui.Warn));
            b.AddChild(head);

            if (f.Stage == FabStage.Construction)
            {
                double done = 1 - f.BuildWeeksLeft / (double)Math.Max(1, f.BuildWeeksTotal);
                var row = Ui.H(10);
                row.AddChild(Ui.Bar(done, 1, null, 260));
                row.AddChild(Ui.L($"{f.BuildWeeksLeft / Calendar.WeeksPerMonth} months left · {Format.Big(f.CapexPaid)} of {Format.Big(f.Capex)} paid", 13, Ui.Muted));
                b.AddChild(row);
                b.AddChild(Ui.L($"{f.Capacity:N0} wafer starts a month when finished.", 13, Ui.Muted));
                b.AddChild(CloseRow(f));
                return card;
            }

            int avail = FabOps.AvailableCapacity(D, f);
            double d0 = FabOps.D0(D, f);
            var chip = S.Design(f.DesignId);
            var grid = new GridContainer { Columns = 4 };
            grid.AddThemeConstantOverride("h_separation", 18);
            void Cell(string k, string v, Color? c = null) { grid.AddChild(Ui.L(k, 13, Ui.Muted)); grid.AddChild(Ui.L(v, 13, c ?? Ui.Fg)); }
            Cell("Capacity", f.Stage == FabStage.Ramping ? $"{avail:N0} of {f.Capacity:N0}" : $"{f.Capacity:N0}");
            Cell("Last month", $"{f.LastMonthStarts:N0} wafers ({f.LastUtilisation:P0})", f.LastUtilisation < 0.6 ? Ui.Warn : null);
            Cell("Defect density", $"{d0:0.00}/cm² (floor {n.D0Mature:0.0})");
            Cell("Learning", $"{f.Learning:0.0} months");
            if (chip != null)
            {
                Cell("Chip", $"{chip.Name} ({chip.DieAreaMm2:0.0} mm²)");
                Cell("Expected yield", $"{YieldModel.Yield(d0, chip.DieAreaMm2):P0}");
                Cell("Dies per wafer", $"{YieldModel.DiesPerWafer(n.WaferMm, YieldModel.WithScribe(chip.DieAreaMm2)):N0}");
                Cell("Good dies a month", Ui.Units(FabOps.AvailableCapacity(D, f) * YieldModel.DiesPerWafer(n.WaferMm, YieldModel.WithScribe(chip.DieAreaMm2)) * YieldModel.Yield(d0, chip.DieAreaMm2)));
            }
            Cell("Running cost", $"{Format.Money(FabOps.RunningCost(D, S, f))}/mo");
            Cell("Wafer materials", $"{Format.Money(FabOps.WaferCost(D, S, f))}");
            Cell("Book value", Format.Big(f.BookValue));
            Cell("Depreciation", $"{Format.Money(Math.Min(f.BookValue, f.MonthlyDepreciation))}/mo");
            b.AddChild(grid);

            // what it makes
            var released = S.Designs.Where(c => c.Stage == DesignStage.Released && c.NodeId == f.NodeId).OrderByDescending(c => c.ReleasedTick).ToList();
            var make = Ui.H(10);
            make.AddChild(Ui.L("Making", 14, Ui.Muted));
            var names = new[] { "Test wafers (learning only)" }.Concat(released.Select(c => c.Name)).ToArray();
            int sel = chip == null ? 0 : 1 + released.FindIndex(c => c.Id == chip.Id);
            make.AddChild(Ui.Options(names, sel, i => Act(FabOps.AssignDesign(D, S, f, i == 0 ? null : released[i - 1].Id))));
            make.AddChild(Ui.L("Wafer starts", 14, Ui.Muted));
            make.AddChild(Ui.Spin(0, f.Capacity, 25, f.PlannedStarts, v => FabOps.SetPlannedStarts(f, (int)v), "", " /mo"));
            b.AddChild(make);
            if (released.Count == 0)
                b.AddChild(Ui.Wrap($"No released chip for {n.Name} yet. Test wafers still teach the process, so yields will be better when your first chip arrives.", 12, Ui.Muted));

            var eng = Ui.H(10);
            double rec = FabOps.RecommendedEngineering(S, f);
            eng.AddChild(Ui.L("Process engineering", 14, Ui.Muted));
            eng.AddChild(Ui.Spin(0, 200, 10, rec > 0 ? Math.Round(f.EngineeringBudget / rec * 100) : 100, v => FabOps.SetEngineering(f, rec * v / 100), "", "% of recommended"));
            eng.AddChild(Ui.L($"{Format.Money(f.EngineeringBudget)}/mo · learning ×{LearningCurve.EngineerFactor(f.EngineeringBudget, rec):0.00}", 13, Ui.Muted));
            b.AddChild(eng);

            // recent lots
            var lots = f.Lots.Where(l => l.Done && l.Bins != null).OrderByDescending(l => l.DoneTick).Take(3).ToList();
            if (lots.Count > 0)
            {
                b.AddChild(Ui.Heading("Recent lots"));
                foreach (var lot in lots)
                {
                    string bins = string.Join(" · ", lot.Bins.Where(kv => kv.Value > 0).Select(kv => $"{kv.Key} {kv.Value:N0}"));
                    b.AddChild(Ui.L($"{Calendar.DateLabel(lot.DoneTick)}: {lot.Wafers} wafers, {lot.GrossDies:N0} dies at D0 {lot.D0:0.0}, {lot.GoodDies / (double)Math.Max(1, lot.GrossDies):P0} good → {bins}", 12, Ui.Muted));
                }
            }

            b.AddChild(ExpandRow(f));
            b.AddChild(RetoolRow(f));
            b.AddChild(CloseRow(f));
            return card;
        }

        Control ExpandRow(Fab f)
        {
            var box = Ui.V(4);
            if (f.Expanding)
            {
                double done = 1 - f.ExpandWeeksLeft / (double)Math.Max(1, f.ExpandWeeksTotal);
                var prog = Ui.H(10);
                prog.AddChild(Ui.L($"Expanding +{f.ExpandCapacity:N0}", 14, Ui.Accent));
                prog.AddChild(Ui.Bar(done, 1, null, 200));
                prog.AddChild(Ui.L($"{Math.Max(1, f.ExpandWeeksLeft / Calendar.WeeksPerMonth)} month(s) left · {Format.Big(f.ExpandPaid)} of {Format.Big(f.ExpandCapex)} paid · still running at {f.Capacity:N0}", 13, Ui.Muted));
                box.AddChild(prog);
                return box;
            }
            if (!expandChoice.TryGetValue(f.Id, out var extra)) extra = Math.Max(FabOps.MinExpansion, (int)Math.Round(f.Capacity / 2.0 / 100) * 100);
            extra = Math.Clamp(extra, FabOps.MinExpansion, Math.Max(FabOps.MinExpansion, FabOps.MaxCapacity - f.Capacity));
            expandChoice[f.Id] = extra;
            var q = FabOps.ExpandQuote(D, S, f, extra);
            var row = Ui.H(10);
            row.AddChild(Ui.L("Expand by", 14, Ui.Muted));
            row.AddChild(Ui.Spin(FabOps.MinExpansion, Math.Max(FabOps.MinExpansion, FabOps.MaxCapacity - f.Capacity), 100, extra, v => { expandChoice[f.Id] = (int)v; Refresh(); }, "+", " /mo"));
            var perWafer = q.NetCapex / Math.Max(1, extra);
            var newFab = FabOps.Quote(D, S, f.NodeId, f.LocationId, extra);
            row.AddChild(Ui.L($"{Format.Big(q.NetCapex)} over {q.BuildMonths} months ({Format.Money(perWafer)} per wafer of capacity vs {Format.Money(newFab.NetCapex / Math.Max(1, extra))} in a new fab) · +{Format.Money(q.MonthlyRunning)}/mo running", 13, Ui.Muted));
            var go = Ui.Btn("Expand", () => Act(FabOps.Expand(D, S, f, expandChoice[f.Id])));
            go.Disabled = q.Problem != null;
            row.AddChild(go);
            box.AddChild(row);
            if (q.Problem != null) box.AddChild(Ui.L(q.Problem, 12, Ui.Muted));
            return box;
        }

        Control RetoolRow(Fab f)
        {
            var current = D.Node(f.NodeId);
            var newer = D.Nodes.Where(n => S.NodeReady(n.Id) && n.FeatureNm < current.FeatureNm).OrderBy(n => n.FeatureNm).ToList();
            var box = Ui.V(4);
            if (newer.Count == 0) return box;
            if (!retoolChoice.TryGetValue(f.Id, out var target) || !newer.Any(n => n.Id == target)) target = newer[0].Id;
            retoolChoice[f.Id] = target;
            var q = FabOps.RetoolQuote(D, S, f, target);
            var row = Ui.H(10);
            row.AddChild(Ui.L("Retool to", 14, Ui.Muted));
            row.AddChild(Ui.Options(newer.Select(n => n.Name).ToArray(), newer.FindIndex(n => n.Id == target), i => { retoolChoice[f.Id] = newer[i].Id; Refresh(); }));
            row.AddChild(Ui.L($"{Format.Big(q.NetCapex)}, down {q.BuildMonths} months, {Format.Big(f.BookValue * FabOps.RetoolWriteOff)} written off", 13, Ui.Muted));
            row.AddChild(Ui.Btn("Retool", () => Act(FabOps.Retool(D, S, f, retoolChoice[f.Id]))));
            box.AddChild(row);
            if (q.Problem != null) box.AddChild(Ui.L(q.Problem, 12, Ui.Bad));
            return box;
        }

        Control CloseRow(Fab f)
        {
            var row = Ui.H(10);
            row.AddChild(Ui.Spacer());
            double value = f.Stage == FabStage.Construction ? f.CapexPaid + f.BookValue : f.BookValue;
            if (confirmClose == f.Id)
            {
                row.AddChild(Ui.L($"Close {f.Name}? You'd get {Format.Big(value * FabOps.SalvageShare)} and write off {Format.Big(value * (1 - FabOps.SalvageShare))}.", 13, Ui.Warn));
                row.AddChild(Ui.Btn("Close it", () => { confirmClose = null; Act(FabOps.Close(D, S, f)); }));
                row.AddChild(Ui.Btn("Keep it", () => { confirmClose = null; Refresh(); }));
            }
            else row.AddChild(Ui.Btn("Close fab…", () => { confirmClose = f.Id; Refresh(); }));
            return row;
        }
    }
}
