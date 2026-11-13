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
        readonly Dictionary<string, int> addChoice = new Dictionary<string, int>();
        // wafer map shown per fab: product id ("" = test wafers) and a resample counter
        readonly Dictionary<string, string> mapFor = new Dictionary<string, string>();
        readonly Dictionary<string, int> mapDraw = new Dictionary<string, int>();
        string confirmClose, renaming;
        readonly HashSet<string> showHistory = new HashSet<string>();

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
            double head = FabOps.StartingLearning(D, S, formNode);
            quoteBox.AddChild(Ui.Row("Defect density", $"{LearningCurve.D0(n, head):0.0} at start → {n.D0Mature:0.0} per cm²"));
            if (head > 0.05) quoteBox.AddChild(Ui.Wrap($"Your experience gives it a {head:0.0}-month head start on learning (so far; it grows while your other fabs run).", 12, Ui.Ok));
            else if (n.Requires != null) quoteBox.AddChild(Ui.Wrap($"No experience to draw on: running a {D.Node(n.Requires).Name} fab first would give it a head start.", 12, Ui.Muted));
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
            if (renaming == f.Id)
            {
                var edit = new LineEdit { Text = f.Name, CustomMinimumSize = new Vector2(320, 0) };
                edit.TextSubmitted += t => { renaming = null; Act(FabOps.Rename(S, f, t)); };
                head.AddChild(edit);
                head.AddChild(Ui.Btn("Save", () => { renaming = null; Act(FabOps.Rename(S, f, edit.Text)); }));
                head.AddChild(Ui.Btn("Cancel", () => { renaming = null; Refresh(); }));
                Callable.From(() => { if (IsInstanceValid(edit)) edit.GrabFocus(); }).CallDeferred();
            }
            else
            {
                head.AddChild(Ui.Title(f.Name, 17));
                head.AddChild(Ui.Btn("Rename", () => { renaming = f.Id; Refresh(); }));
            }
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
            if (f.OutageWeeks > 0)
                b.AddChild(Ui.L(f.OutageFactor <= 0
                    ? $"Down for {f.OutageReason}: about {f.OutageWeeks} week{(f.OutageWeeks == 1 ? "" : "s")} until wafers start again."
                    : $"Running at {f.OutageFactor:P0} because of {f.OutageReason}: about {f.OutageWeeks} week{(f.OutageWeeks == 1 ? "" : "s")} to go.", 13, Ui.Bad));
            if (f.D0PenaltyWeeks > 0 && f.D0Penalty > 1)
                b.AddChild(Ui.L($"Contaminated: defect density {f.D0Penalty:0.#}× normal for about {f.D0PenaltyWeeks} more weeks.", 13, Ui.Warn));
            var pending = S.Decisions.FirstOrDefault(x => x.TargetId == f.Id);
            if (pending != null) b.AddChild(DecisionRow(pending));

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
            var grid = new GridContainer { Columns = 4 };
            grid.AddThemeConstantOverride("h_separation", 18);
            void Cell(string k, string v, Color? c = null) { grid.AddChild(Ui.L(k, 13, Ui.Muted)); grid.AddChild(Ui.L(v, 13, c ?? Ui.Fg)); }
            Cell("Capacity", f.Stage == FabStage.Ramping ? $"{avail:N0} of {f.Capacity:N0}" : $"{f.Capacity:N0}");
            Cell("Last month", $"{f.LastMonthStarts:N0} wafers ({f.LastUtilisation:P0})", f.LastUtilisation < 0.6 ? Ui.Warn : null);
            Cell("Defect density", $"{d0:0.00}/cm² (floor {n.D0Mature:0.0})");
            Cell("Learning", $"{f.Learning:0.0} months");
            Cell("Running cost", $"{Format.Money(FabOps.RunningCost(D, S, f))}/mo");
            Cell("Wafer materials", $"{Format.Money(FabOps.WaferCost(D, S, f))}");
            Cell("Book value", Format.Big(f.BookValue));
            Cell("Depreciation", $"{Format.Money(Math.Min(f.BookValue, f.MonthlyDepreciation))}/mo");
            b.AddChild(grid);
            b.AddChild(ProductsBox(f, n, d0));
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
                    b.AddChild(Ui.L($"{Calendar.DateLabel(lot.DoneTick)}: {S.Design(lot.DesignId)?.Name ?? "test"}, {lot.Wafers} wafers, {lot.GrossDies:N0} dies at D0 {lot.D0:0.0}, {lot.GoodDies / (double)Math.Max(1, lot.GrossDies):P0} good → {bins}", 12, Ui.Muted));
                }
            }

            if (f.History.Count >= 2)
            {
                bool open = showHistory.Contains(f.Id);
                b.AddChild(Ui.Btn(open ? "Hide history" : "History", () => { if (!showHistory.Remove(f.Id)) showHistory.Add(f.Id); Refresh(); }, "Defect density, output and utilisation month by month"));
                if (open) b.AddChild(HistoryBox(f));
            }
            b.AddChild(ExpandRow(f));
            b.AddChild(RetoolRow(f));
            b.AddChild(CloseRow(f));
            return card;
        }

        Control DecisionRow(Decision x)
        {
            var box = Ui.V(4);
            box.AddChild(Ui.Wrap($"{x.Title}: decide by {Calendar.WeekLabel(x.DeadlineTick)} or \"{x.Options[x.Default].Label}\" happens.", 13, Ui.Warn));
            var row = Ui.H(8);
            for (int i = 0; i < x.Options.Count; i++)
            {
                int pick = i;
                row.AddChild(Ui.Btn(x.Options[i].Label, () => Act(Events.Choose(D, S, x.Id, pick)), x.Options[i].Detail));
            }
            box.AddChild(row);
            return box;
        }

        Control HistoryBox(Fab f)
        {
            var box = Ui.V(6);
            var h = f.History.TakeLast(60).ToList();
            var labels = h.Select(x => Calendar.DateLabel(x.Month * Calendar.WeeksPerMonth)).ToList();
            var n = D.Node(f.NodeId);
            var d0 = new LineChart { ValueFormat = v => v.ToString("0.0") };
            d0.CustomMinimumSize = new Vector2(300, 160);
            d0.SetData(labels, ("Defects per cm²", Ui.Warn, h.Select(x => x.D0).ToList()), ($"{n.Name} floor", Ui.Muted, h.Select(_ => n.D0Mature).ToList()));
            box.AddChild(Ui.Heading("Defect density"));
            box.AddChild(d0);
            var output = new LineChart { ValueFormat = v => Ui.Units(v) };
            output.CustomMinimumSize = new Vector2(300, 160);
            output.SetData(labels, ("Good dies", Ui.Ok, h.Select(x => x.Good).ToList()));
            box.AddChild(Ui.Heading("Good dies a month"));
            box.AddChild(output);
            var util = new LineChart { ValueFormat = v => $"{v:0}%" };
            util.CustomMinimumSize = new Vector2(300, 140);
            util.SetData(labels, ("Wafer starts as % of capacity", Ui.Accent, h.Select(x => x.Utilisation * 100).ToList()));
            box.AddChild(Ui.Heading("Utilisation"));
            box.AddChild(util);
            return box;
        }

        Control WaferMapBox(Fab f, ProcessNodeDef n, double d0, string key)
        {
            var chip = key == "" ? null : S.Design(key);
            if (key != "" && chip?.Stats == null) { mapFor.Remove(f.Id); return Ui.L("", 12); }
            int draw = mapDraw.TryGetValue(f.Id, out var k) ? k : 0;
            var map = WaferMap.Simulate(D, n, chip, d0, WaferMap.Hash($"{f.Id}|{key}|{S.MonthIndex}|{draw}"));
            var row = Ui.H(14);
            var view = new WaferMapView { CustomMinimumSize = new Vector2(380, 380) };
            view.SetMap(map);
            row.AddChild(view);
            var side = Ui.V(4);
            side.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            side.AddChild(Ui.Title($"{chip?.Name ?? "Test wafer"} on a {n.WaferMm:0} mm wafer", 15));
            side.AddChild(Ui.L($"{map.Dies.Count:N0} whole dies fit ({map.FormulaDies:N0} by the costing formula) · {map.DieW:0.00} × {map.DieH:0.00} mm · D0 {d0:0.00}/cm²", 12, Ui.Muted));
            foreach (var bin in map.BinOrder)
            {
                int c = map.Counts[bin];
                var line = Ui.H(6);
                line.AddChild(new ColorRect { Color = WaferMapView.BinColor(map.BinOrder, bin), CustomMinimumSize = new Vector2(12, 12), SizeFlagsVertical = SizeFlags.ShrinkCenter });
                line.AddChild(Ui.L($"{bin}: {c:N0} ({c / (double)Math.Max(1, map.Dies.Count):P0})", 13));
                side.AddChild(line);
            }
            side.AddChild(Ui.L($"Formula yield at this D0: {YieldModel.Yield(d0, map.DieW * map.DieH):P0} good of every die (before binning for speed and harvesting).", 12, Ui.Muted));
            side.AddChild(Ui.Wrap("White dots killed a die. Yellow ones switched off a spare core or graphics unit; blue ones were fixed by spare RAM rows; faint ones fell in empty silicon. Hover a die for details.", 12, Ui.Muted));
            side.AddChild(Ui.Btn("Another wafer", () => { mapDraw[f.Id] = draw + 1; Refresh(); }));
            row.AddChild(side);
            return row;
        }

        /// <summary>The chips the fab makes, each with its own wafer starts, plus a row to add another.</summary>
        Control ProductsBox(Fab f, ProcessNodeDef n, double d0)
        {
            var box = Ui.V(6);
            int planned = f.PlannedStarts;
            int avail = FabOps.AvailableCapacity(D, f);
            var head = Ui.H(10);
            head.AddChild(Ui.Heading("Products"));
            head.AddChild(Ui.Spacer());
            int free = f.Capacity - planned;
            head.AddChild(Ui.L($"{planned:N0} of {f.Capacity:N0} wafer starts planned · {free:N0} free", 12, free > 0 ? Ui.Muted : Ui.Fg));
            box.AddChild(head);
            if (planned > avail && f.Stage == FabStage.Ramping)
                box.AddChild(Ui.L($"Still ramping: only {avail:N0} starts a month are possible, so every product gets {avail / (double)planned:P0} of its plan.", 12, Ui.Warn));

            var effective = FabOps.EffectiveStarts(D, f).ToDictionary(x => x.designId ?? "", x => x.starts);
            var grid = new GridContainer { Columns = 8 };
            grid.AddThemeConstantOverride("h_separation", 14);
            foreach (var h in new[] { "Chip", "Wafer starts", "Die", "Yield", "Good dies/mo", "Made / sold / stock", "", "" })
                grid.AddChild(Ui.L(h, 12, Ui.Muted));
            foreach (var a in f.Allocations.OrderBy(a => a.DesignId == null).ThenByDescending(a => a.Starts).ToList())
            {
                var chip = S.Design(a.DesignId);
                string id = a.DesignId;
                double die = FabOps.DieArea(S, id);
                int dpw = YieldModel.DiesPerWafer(n.WaferMm, YieldModel.WithScribe(die));
                double y = YieldModel.Yield(d0, die);
                int eff = effective.TryGetValue(id ?? "", out var e) ? e : 0;
                grid.AddChild(Ui.L(chip?.Name ?? "Test wafers", 13, chip == null ? Ui.Muted : Ui.Fg));
                int others = planned - a.Starts;
                grid.AddChild(Ui.Spin(0, Math.Max(a.Starts, f.Capacity - others), 25, a.Starts, v => Act(FabOps.SetAllocation(D, S, f, id, (int)v)), "", " /mo"));
                grid.AddChild(Ui.L($"{die:0.0} mm² · {dpw:N0}/wafer", 12, Ui.Muted));
                grid.AddChild(Ui.L($"{y:P0}", 13));
                grid.AddChild(Ui.L(chip == null ? "learning only" : Ui.Units(eff * dpw * y), 13, chip == null ? Ui.Muted : Ui.Fg));
                if (chip != null)
                {
                    double made = S.Inventory.Where(x => x.DesignId == chip.Id).Sum(x => x.MadeLastMonth);
                    double sold = S.Inventory.Where(x => x.DesignId == chip.Id).Sum(x => x.SoldLastMonth);
                    double stock = S.Inventory.Where(x => x.DesignId == chip.Id).Sum(x => x.Units);
                    bool piling = stock >= 1000 && (sold <= 0 || stock > sold * Advisor.StockMonthsWarning);
                    grid.AddChild(Ui.L($"{Ui.Units(made)} / {Ui.Units(sold)} / {Ui.Units(stock)}", 13, piling ? Ui.Warn : null));
                }
                else grid.AddChild(Ui.L("—", 13, Ui.Muted));
                string key = id ?? "";
                bool showing = mapFor.TryGetValue(f.Id, out var shown) && shown == key;
                grid.AddChild(Ui.Btn(showing ? "Hide wafer" : "Wafer map", () => { if (showing) mapFor.Remove(f.Id); else mapFor[f.Id] = key; Refresh(); }, "Look at one simulated wafer at today's defect density"));
                grid.AddChild(Ui.Btn("Stop", () => Act(FabOps.SetAllocation(D, S, f, id, 0)), "Stop starting wafers for this product (lots already started finish)"));
            }
            if (f.Allocations.Count > 0) box.AddChild(grid);
            else box.AddChild(Ui.L("Idle: nothing planned.", 13, Ui.Warn));
            if (mapFor.TryGetValue(f.Id, out var mapKey)) box.AddChild(WaferMapBox(f, n, d0, mapKey));
            if (f.Allocations.Any(a => a.DesignId != null))
                box.AddChild(Ui.L("Made / sold / stock are last month's figures across all your fabs.", 11, Ui.Muted));

            // add another product
            var released = S.Designs.Where(c => c.Stage == DesignStage.Released && c.NodeId == f.NodeId && !f.Makes(c.Id))
                                    .OrderByDescending(c => c.ReleasedTick).ToList();
            var options = released.Select(c => (id: c.Id, name: c.Name)).ToList();
            if (!f.Makes(null)) options.Add((null, "Test wafers (learning only)"));
            if (options.Count > 0 && free > 0)
            {
                int pick = addChoice.TryGetValue(f.Id, out var p) ? Math.Clamp(p, 0, options.Count - 1) : 0;
                var add = Ui.H(10);
                add.AddChild(Ui.L("Add", 14, Ui.Muted));
                add.AddChild(Ui.Options(options.Select(o => o.name).ToArray(), pick, i => { addChoice[f.Id] = i; }));
                var spin = Ui.Spin(0, free, 5, free, _ => { }, "", " /mo");
                add.AddChild(spin);
                add.AddChild(Ui.Btn("Add to production", () =>
                {
                    int i = addChoice.TryGetValue(f.Id, out var q) ? Math.Clamp(q, 0, options.Count - 1) : 0;
                    Act(FabOps.SetAllocation(D, S, f, options[i].id, (int)spin.Value));
                }));
                box.AddChild(add);
            }
            else if (released.Count == 0 && !f.Allocations.Any(a => a.DesignId != null))
                box.AddChild(Ui.Wrap($"No released chip for {n.Name} yet. Test wafers still teach the process, so yields will be better when your first chip arrives.", 12, Ui.Muted));
            else if (released.Count > 0 && free <= 0)
                box.AddChild(Ui.L($"To add {released[0].Name}{(released.Count > 1 ? " or another chip" : "")}, free up wafer starts from a product above.", 12, Ui.Muted));
            box.AddChild(Ui.Wrap("Each product gets its own wafer starts. They share the fab's capacity, defect density and learning; a bigger die yields less per wafer.", 12, Ui.Muted));
            return box;
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
            row.AddChild(Ui.L($"{Format.Big(q.NetCapex)}, down {q.BuildMonths} months, {Format.Big(f.BookValue * FabOps.RetoolWriteOff)} written off · starts at D0 {LearningCurve.D0(D.Node(target), FabOps.StartingLearning(D, S, target)):0.0}", 13, Ui.Muted));
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
