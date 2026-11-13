using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SiliconeTycoon.Sim;

namespace SiliconeTycoon.UI
{
    /// <summary>
    /// Chip projects: the design team, chips through tape-out, silicon, steppings and release, and starting new chips.
    /// A new chip starts from the design house's plan for a market (or a blank die, or an existing chip's floorplan) and
    /// can be laid out by hand in the floorplan editor.
    /// </summary>
    public partial class DesignTab : GameTab
    {
        VBoxContainer list, teamBox;
        // new chip form
        LineEdit nameEdit;
        OptionButton segPick, nodePick;
        SpinBox clockSpin, verifSpin;
        VBoxContainer planBox;
        List<ProcessNodeDef> formNodes = new List<ProcessNodeDef>();
        List<SegmentDef> formSegs = new List<SegmentDef>();
        string formSeg, formNode;
        ChipDesign draft;
        // floorplan editor
        bool editing;
        PanelContainer editorCard;
        VBoxContainer editTools, binBox;
        FloorplanCanvas canvas;
        int addBlockIdx;
        readonly HashSet<string> showPlan = new HashSet<string>();

        public DesignTab() { }
        public DesignTab(Main m) : base(m, "Design") { }

        protected override void Build()
        {
            var left = Ui.V(10);
            var ls = Ui.Scroll(left);
            ls.SizeFlagsStretchRatio = 1.4f;
            AddChild(ls);
            var (teamCard, tb) = Ui.CardV("Design team");
            teamBox = Ui.V(6);
            tb.AddChild(teamBox);
            left.AddChild(teamCard);
            BuildEditor(left);
            list = Ui.V(10);
            left.AddChild(list);

            var form = Ui.V(10);
            AddChild(Ui.Scroll(form));
            var (card, b) = Ui.CardV("New chip");
            form.AddChild(card);
            b.AddChild(Ui.Wrap("Give the design house a brief and it lays out a chip for that market from this year's blocks: valid, but a little loose and 5% slower than a hand layout, and it charges a fee. Or edit the floorplan yourself: tighter dies, short wires and full speed, but your own team does all the work.", 13, Ui.Muted));
            b.AddChild(Ui.Heading("Name"));
            nameEdit = new LineEdit { PlaceholderText = "Chip name" };
            b.AddChild(nameEdit);
            b.AddChild(Ui.Heading("Market"));
            segPick = Ui.Options(new string[0], 0, i => { formSeg = formSegs[i].Id; if (draft == null || draft.UseDesignHouse) Replan(); else Reanalyze(); });
            b.AddChild(segPick);
            b.AddChild(Ui.Heading("Process node"));
            nodePick = Ui.Options(new string[0], 0, i => { formNode = formNodes[i].Id; NodeChanged(); });
            b.AddChild(nodePick);
            var tune = Ui.H(10);
            tune.AddChild(Ui.L("Rated clock", 13, Ui.Muted));
            clockSpin = Ui.Spin(0, 100, 0.05, 0, v => Reanalyze(), "", " MHz");
            clockSpin.TooltipText = "0 = 90% of what a typical die reaches";
            tune.AddChild(clockSpin);
            tune.AddChild(Ui.L("Verification", 13, Ui.Muted));
            verifSpin = Ui.Spin(DesignOps.MinVerification * 100, DesignOps.MaxVerification * 100, 5, 30, v => Reanalyze(), "", "%");
            verifSpin.TooltipText = "Share of engineering time spent testing before tape-out. More means fewer bugs, but a longer project.";
            tune.AddChild(verifSpin);
            b.AddChild(tune);
            planBox = Ui.V(3);
            b.AddChild(planBox);
            b.AddChild(Ui.Primary("Start the design", StartDraft));
        }

        public override void Refresh()
        {
            if (S == null || list == null) return;
            RefreshTeam();
            RefreshList();
            RefreshForm();
        }

        // ---------------------------------------------------------------- team

        void RefreshTeam()
        {
            Ui.Clear(teamBox);
            foreach (var t in S.DesignTeams)
            {
                var row = Ui.H(10);
                row.AddChild(Ui.L(t.Name, 15));
                row.AddChild(Ui.Spin(0, DesignOps.MaxEngineers, 1, t.Engineers, v => Act(DesignOps.SetEngineers(S, t, (int)v)), "", " engineers"));
                row.AddChild(Ui.L($"{Format.Money(t.Engineers * DesignOps.Salary * S.Inflation)} a month", 13, Ui.Muted));
                row.AddChild(Ui.Spacer());
                var c = S.Design(t.ProjectId);
                row.AddChild(Ui.L(c == null ? "Free" : $"Working on {c.Name}", 14, c == null ? Ui.Ok : Ui.Accent));
                teamBox.AddChild(row);
            }
            teamBox.AddChild(Ui.Wrap($"An engineer designs and verifies about {DesignOps.Productivity(S.YearF):N0} transistors a month this year (tools improve every year). More engineers finish a chip sooner.", 12, Ui.Muted));
        }

        // ---------------------------------------------------------------- chip list

        void RefreshList()
        {
            Ui.Clear(list);
            var chips = S.Designs.Where(c => c.Stage != DesignStage.Cancelled).OrderByDescending(c => c.StartedTick).ToList();
            if (chips.Count == 0) list.AddChild(Ui.Card(Ui.Wrap("No chips yet. Start one on the right.", 14, Ui.Muted)));
            foreach (var c in chips) list.AddChild(ChipCard(c));
        }

        Control ChipCard(ChipDesign c)
        {
            var n = D.Node(c.NodeId);
            var (card, b) = Ui.CardV(null, 6);
            var head = Ui.H(10);
            head.AddChild(Ui.Title(c.Name, 17));
            head.AddChild(Ui.L($"{n.Name} · {c.SteppingName} · {D.Package(c.PackageId)?.Name}", 13, Ui.Muted));
            head.AddChild(Ui.Spacer());
            string stage = c.Stage switch
            {
                DesignStage.Designing => "Designing",
                DesignStage.Silicon => "Waiting for silicon",
                DesignStage.Validated => "Silicon validated: your call",
                DesignStage.Released => "In production",
                _ => c.Stage.ToString(),
            };
            head.AddChild(Ui.L(stage, 14, c.Stage == DesignStage.Validated ? Ui.Warn : c.Stage == DesignStage.Released ? Ui.Ok : Ui.Accent));
            b.AddChild(head);
            b.AddChild(StatsLine(c.Stats));
            var tools = Ui.H(8);
            bool open = showPlan.Contains(c.Id);
            tools.AddChild(Ui.Btn(open ? "Hide floorplan" : "Floorplan", () => { if (!showPlan.Remove(c.Id)) showPlan.Add(c.Id); Refresh(); }));
            tools.AddChild(Ui.Btn("New chip from this floorplan", () => CopyChip(c),
                "Start a new draft with this layout, on the process picked under New chip. On a smaller node the blocks shrink in place: tighten the layout to get the smaller die."));
            b.AddChild(tools);
            if (open)
            {
                var view = new FloorplanCanvas { Data = D, Chip = c, Editable = false, CustomMinimumSize = new Vector2(420, 260) };
                view.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                b.AddChild(view);
            }

            switch (c.Stage)
            {
                case DesignStage.Designing:
                case DesignStage.Silicon:
                    b.AddChild(Ui.L(c.Stage == DesignStage.Designing
                        ? $"Tape-out in about {Math.Max(1, c.WeeksLeft / Calendar.WeeksPerMonth)} month(s), then {Format.Big((c.Stepping == 0 ? n.MaskSetCost : n.MaskSetCost * DesignOps.SteppingMaskShare) * S.Inflation)} for masks."
                        : $"First wafers and bring-up: about {Math.Max(1, c.WeeksLeft / Calendar.WeeksPerMonth)} month(s).", 13, Ui.Muted));
                    b.AddChild(Ui.Btn("Cancel chip", () => Act(DesignOps.Cancel(S, c))));
                    break;
                case DesignStage.Validated:
                    if (c.FoundBugs == 0) b.AddChild(Ui.Wrap("Validation found no bugs. Release it to production.", 14, Ui.Ok));
                    else if (c.Showstoppers > 0) b.AddChild(Ui.Wrap($"Validation found {c.FoundBugs} bug{(c.FoundBugs == 1 ? "" : "s")}, {c.Showstoppers} too serious to ship. It needs a new stepping: about {DesignOps.SteppingWeeks / Calendar.WeeksPerMonth} months of fixes plus partial masks ({Format.Big(n.MaskSetCost * DesignOps.SteppingMaskShare * S.Inflation)}), then new silicon.", 14, Ui.Bad));
                    else b.AddChild(Ui.Wrap($"Validation found {c.FoundBugs} minor bug{(c.FoundBugs == 1 ? "" : "s")}. Ship now with documented errata, or step it to fix them. Bugs validation missed will surface later either way.", 14, Ui.Warn));
                    var row = Ui.H(10);
                    var rel = Ui.Primary("Release to production", () => Act(DesignOps.Release(D, S, c), $"The {c.Name} is released. Point a {n.Name} fab at it on the Fabs tab, and price it on the Market tab."));
                    rel.Disabled = c.Showstoppers > 0;
                    row.AddChild(rel);
                    var step = Ui.Btn("New stepping", () => Act(DesignOps.Step(D, S, c)));
                    step.Disabled = c.FoundBugs == 0;
                    row.AddChild(step);
                    row.AddChild(Ui.Btn("Cancel chip", () => Act(DesignOps.Cancel(S, c))));
                    b.AddChild(row);
                    break;
                case DesignStage.Released:
                    double stock = S.Inventory.Where(x => x.DesignId == c.Id).Sum(x => x.Units);
                    int fabs = S.Fabs.Count(f => f.Makes(c.Id) && f.Producing);
                    b.AddChild(Ui.L($"Released {Calendar.DateLabel(c.ReleasedTick)}{(c.FoundBugs > 0 ? $" with {c.FoundBugs} errata" : "")} · {Ui.Units(stock)} in stock · made in {fabs} fab{(fabs == 1 ? "" : "s")}", 13, Ui.Muted));
                    if (fabs == 0) b.AddChild(Ui.L($"No fab is making it. Assign it to a {n.Name} fab on the Fabs tab.", 13, Ui.Warn));
                    break;
            }
            return card;
        }

        static Control StatsLine(DesignStats st)
        {
            if (st == null) return Ui.L("", 12);
            return Ui.L($"{st.Transistors:N0} transistors · {st.DieAreaMm2:0.0} mm² · {st.ClockMHz:0.00} MHz · {st.PowerW:0.00} W · " +
                        $"control {st.Control:0.00} / compute {st.Compute:0.00} / graphics {st.Graphics:0.00} · {Format.Money(st.CostPerGoodDie)} a good die", 12, Ui.Muted);
        }

        // ---------------------------------------------------------------- new chip form

        void RefreshForm()
        {
            var segs = D.Segments.Where(x => x.Active(S.YearF)).ToList();
            var nodes = D.Nodes.Where(n => S.NodeReady(n.Id)).OrderBy(n => n.FeatureNm).ToList();
            bool changed = !segs.Select(x => x.Id).SequenceEqual(formSegs.Select(x => x.Id)) || !nodes.Select(x => x.Id).SequenceEqual(formNodes.Select(x => x.Id));
            formSegs = segs;
            formNodes = nodes;
            if (formSeg == null || !formSegs.Any(x => x.Id == formSeg)) formSeg = formSegs.FirstOrDefault()?.Id;
            if (formNode == null || !formNodes.Any(x => x.Id == formNode))
            {
                // default: the newest node you have a fab for, else the newest ready node
                var fabNode = S.Fabs.Where(f => f.Stage != FabStage.Closed).Select(f => D.Node(f.NodeId)).OrderBy(x => x.FeatureNm).FirstOrDefault();
                formNode = (fabNode ?? formNodes.FirstOrDefault())?.Id;
            }
            if (changed)
            {
                Fill(segPick, formSegs.Select(x => x.Name).ToArray(), formSegs.FindIndex(x => x.Id == formSeg));
                Fill(nodePick, formNodes.Select(x => x.Name + (S.Fabs.Any(f => f.NodeId == x.Id && f.Stage != FabStage.Closed) ? "" : " (no fab)")).ToArray(), formNodes.FindIndex(x => x.Id == formNode));
            }
            if (string.IsNullOrWhiteSpace(nameEdit.Text)) nameEdit.Text = $"{S.CompanyName.Substring(0, Math.Min(2, S.CompanyName.Length)).ToUpperInvariant()}-{S.Designs.Count + 1}";
            if (draft == null) Replan();
            else if (draft.NodeId != formNode) NodeChanged();
            else Reanalyze();
            RefreshEditor();
        }

        static void Fill(OptionButton o, string[] items, int selected)
        {
            o.Clear();
            for (int i = 0; i < items.Length; i++) o.AddItem(items[i], i);
            if (items.Length > 0) o.Selected = Math.Clamp(selected, 0, items.Length - 1);
        }

        string planWhy;

        /// <summary>A design house plan is redone for the new node; a hand layout keeps its blocks, resized for the node.</summary>
        void NodeChanged()
        {
            if (draft == null || draft.UseDesignHouse) { Replan(); return; }
            draft.NodeId = formNode;
            DesignLab.Layout(D, draft);
            Reanalyze();
            RefreshEditor();
        }

        void Replan()
        {
            if (S == null || planBox == null) return;
            var seg = D.Segment(formSeg);
            planWhy = null;
            draft = seg == null || formNode == null ? null : DesignLab.PlanForSegment(D, S, formNode, seg.Metric, nameEdit.Text, out planWhy, maxPowerW: seg.MaxPowerW);
            if (draft != null && clockSpin != null) clockSpin.SetValueNoSignal(draft.TargetClockMHz);
            Reanalyze();
            RefreshEditor();
        }

        void Reanalyze()
        {
            if (planBox == null) return;
            Ui.Clear(planBox);
            if (draft == null)
            {
                planBox.AddChild(Ui.Wrap("The design house can't make a chip for that market on that node with this year's blocks and packages." +
                                         (planWhy != null ? $" The full-strength design fails because: {planWhy}" : ""), 13, Ui.Bad));
                planBox.AddChild(Ui.Btn("Lay out a blank die by hand", StartBlank));
                return;
            }
            draft.TargetClockMHz = clockSpin.Value;
            draft.Verification = verifSpin.Value / 100.0;
            var st = DesignLab.Analyze(D, S, draft);
            draft.Stats = st;
            if (!editing)
            {
                var edit = Ui.H(8);
                edit.AddChild(Ui.Btn("Edit the floorplan", () => { editing = true; RefreshEditor(); }, "Move, reshape, add and remove blocks yourself"));
                if (!draft.UseDesignHouse) edit.AddChild(Ui.Btn("Back to the design house plan", Replan));
                planBox.AddChild(edit);
            }
            planBox.AddChild(Ui.L(draft.UseDesignHouse ? "Layout: design house (fee, 5% slower, your team does 80% of the work)" : "Layout: by hand (no fee, full speed, your team does all the work)", 12, Ui.Muted));
            var seg = D.Segment(formSeg);
            var team = S.DesignTeams.FirstOrDefault();

            planBox.AddChild(Ui.Rule());
            planBox.AddChild(Ui.L(string.Join(" + ", draft.Blocks.Select(p => D.Block(p.BlockId).Name)), 13, Ui.Fg));
            planBox.AddChild(Ui.Row("Package", D.Package(draft.PackageId)?.Name ?? "—"));
            planBox.AddChild(Ui.Row("Transistors", $"{st.Transistors:N0}"));
            planBox.AddChild(Ui.Row("Die", $"{draft.DieW:0.00} × {draft.DieH:0.00} mm = {st.DieAreaMm2:0.0} mm² ({st.Utilisation:P0} used)"));
            planBox.AddChild(Ui.Row("Clock", $"{st.ClockMHz:0.00} MHz rated, {st.MaxClockMHz:0.00} typical"));
            planBox.AddChild(Ui.Row("Power", $"{st.PowerW:0.00} W (market limit {seg.MaxPowerW:0.#} W)", st.PowerW > seg.MaxPowerW ? Ui.Bad : null));
            double perf = Market.MetricOf(st, seg.Metric);
            double need = seg.NeedAt(S.YearF);
            planBox.AddChild(Ui.Row($"{seg.Metric}", $"{perf:0.00} (competitive today: {need:0.00}, next year: {seg.NeedAt(S.YearF + 1):0.00})", perf < need * Market.OpenMarketMinShare ? Ui.Bad : perf < need ? Ui.Warn : Ui.Ok));
            planBox.AddChild(Ui.Row("Dies per wafer", $"{st.DiesPerWafer:N0} at about {st.YieldEstimate:P0} yield"));
            planBox.AddChild(Ui.Row("Cost per good chip", Format.Money(st.CostPerGoodDie)));
            planBox.AddChild(Ui.Row("Buyers paying today", Format.Money(Market.MarketPrice(S, seg, S.YearF))));
            planBox.AddChild(Ui.Rule());
            int weeks = team == null || team.Engineers == 0 ? 0 : (int)Math.Ceiling(st.EngineerMonths / team.Engineers * Calendar.WeeksPerMonth);
            planBox.AddChild(Ui.Row("Design effort", $"{st.EngineerMonths:0} engineer-months ≈ {weeks / Calendar.WeeksPerMonth} months with {team?.Engineers ?? 0} engineers"));
            planBox.AddChild(Ui.Row("Expected bugs at first silicon", $"{DesignOps.ExpectedBugs(draft, st):0.0}"));
            planBox.AddChild(Ui.Row("Masks at tape-out", Format.Money(st.MaskCost)));
            planBox.AddChild(Ui.Row("Design house fee (now)", Format.Money(st.DesignHouseFee)));
            foreach (var note in st.Notes) planBox.AddChild(Ui.Wrap(note.Text, 12, Ui.NoteSev(note.Severity)));
            if (!S.Fabs.Any(f => f.NodeId == draft.NodeId && f.Stage != FabStage.Closed))
                planBox.AddChild(Ui.Wrap($"You have no {D.Node(draft.NodeId).Name} fab to make it in yet.", 12, Ui.Warn));
        }

        // ---------------------------------------------------------------- floorplan editor

        void BuildEditor(VBoxContainer parent)
        {
            var (card, b) = Ui.CardV("Floorplan editor");
            editorCard = card;
            card.Visible = false;
            editTools = Ui.V(6);
            b.AddChild(editTools);
            canvas = new FloorplanCanvas { Data = D, Editable = true, CustomMinimumSize = new Vector2(600, 440) };
            canvas.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            canvas.Changed = () => Edited();
            canvas.SelectionChanged = RefreshEditor;
            b.AddChild(canvas);
            b.AddChild(Ui.Wrap("Drag blocks to move them: they snap to the die edge and to each other. Right-click turns a block; scroll over the selected block to change its shape (same area). " +
                               "I/O rings must touch the die edge. Keep cores next to the memory, ROM and graphics they talk to: every mm of wire costs clock speed (dashed lines). " +
                               "Empty silicon is paid for on every wafer but defects that land there do no harm. A smaller die fits more per wafer and yields better.", 12, Ui.Muted));
            binBox = Ui.V(4);
            b.AddChild(binBox);
            parent.AddChild(card);
        }

        void StartBlank()
        {
            if (formNode == null) return;
            draft = DesignLab.Blank(D, S, nameEdit.Text, formNode);
            clockSpin.SetValueNoSignal(0);
            editing = true;
            Reanalyze();
            RefreshEditor();
        }

        void CopyChip(ChipDesign c)
        {
            string node = formNode ?? c.NodeId;
            draft = DesignLab.CopyAsNewChip(D, c, nameEdit.Text, node);
            if (draft.NodeId != c.NodeId) draft.UseDesignHouse = false;
            draft.TargetClockMHz = 0;
            clockSpin.SetValueNoSignal(0);
            editing = true;
            main.Toast(draft.NodeId == c.NodeId
                ? $"New draft from the {c.Name}'s floorplan."
                : $"The {c.Name} moved to {D.Node(node).Name}: the blocks shrank in place. Tighten the layout and fit the die.");
            Refresh();
        }

        /// <summary>After any hand edit: the layout is now yours, so the design house is out.</summary>
        void Edited(bool designHouse = false)
        {
            if (draft == null) return;
            draft.UseDesignHouse = designHouse;
            Reanalyze();
            RefreshEditor();
        }

        void RefreshEditor()
        {
            if (editorCard == null) return;
            bool show = editing && draft != null;
            editorCard.Visible = show;
            list.Visible = !show;
            if (!show) return;
            canvas.Data = D;
            canvas.Chip = draft;
            if (draft.Blocks.All(p => p.Id != canvas.Selected)) canvas.Selected = null;
            canvas.QueueRedraw();

            Ui.Clear(editTools);
            var n = D.Node(draft.NodeId);
            var head = Ui.H(10);
            head.AddChild(Ui.L($"{draft.Name} on {n.Name}", 15));
            head.AddChild(Ui.Spacer());
            head.AddChild(Ui.Primary("Done editing", () => { editing = false; RefreshEditor(); Reanalyze(); }));
            editTools.AddChild(head);

            // add a block
            var avail = D.Blocks.Where(x => x.Year <= S.Year).ToList();
            addBlockIdx = Math.Clamp(addBlockIdx, 0, Math.Max(0, avail.Count - 1));
            var add = Ui.H(8);
            add.AddChild(Ui.L("Add", 13, Ui.Muted));
            add.AddChild(Ui.Options(avail.Select(x => $"{x.Name} ({DesignLab.BlockArea(x, n):0.0} mm²)").ToArray(), addBlockIdx, i => addBlockIdx = i));
            add.AddChild(Ui.Btn("Add block", () =>
            {
                if (avail.Count == 0) return;
                var p = DesignLab.AddBlock(D, draft, avail[Math.Clamp(addBlockIdx, 0, avail.Count - 1)].Id);
                canvas.Selected = p?.Id;
                Edited();
            }));
            if (addBlockIdx < avail.Count) add.AddChild(Ui.L(avail[addBlockIdx].Description, 11, Ui.Muted));
            editTools.AddChild(add);

            // the selected block
            var sel = draft.Blocks.FirstOrDefault(p => p.Id == canvas.Selected);
            var selRow = Ui.H(8);
            if (sel != null)
            {
                var def = D.Block(sel.BlockId);
                selRow.AddChild(Ui.L(def?.Name ?? sel.BlockId, 14, FloorplanCanvas.KindColor(def?.Kind ?? BlockKind.Core)));
                selRow.AddChild(Ui.L($"{sel.W:0.00} × {sel.H:0.00} mm", 12, Ui.Muted));
                selRow.AddChild(Ui.L("Shape", 13, Ui.Muted));
                selRow.AddChild(Ui.Spin(0.25, 4, 0.05, Math.Round(sel.Aspect, 2), v => { DesignLab.SetAspect(sel, v); Edited(); }, "", " wide/tall"));
                selRow.AddChild(Ui.Btn("Turn", () => { DesignLab.SetAspect(sel, 1 / sel.Aspect); Edited(); }));
                selRow.AddChild(Ui.Btn("Find a free spot", () => { DesignLab.PlaceFree(draft, sel); Edited(); }));
                selRow.AddChild(Ui.Btn("Remove", () => { draft.Blocks.Remove(sel); canvas.Selected = null; Edited(); }));
            }
            else selRow.AddChild(Ui.L("Click a block to select it.", 12, Ui.Muted));
            editTools.AddChild(selRow);

            // die and package
            var dieRow = Ui.H(8);
            dieRow.AddChild(Ui.L("Die", 13, Ui.Muted));
            dieRow.AddChild(Ui.Spin(0.5, 40, 0.05, draft.DieW, v => { draft.DieW = v; Edited(); }, "", " mm wide"));
            dieRow.AddChild(Ui.Spin(0.5, 40, 0.05, draft.DieH, v => { draft.DieH = v; Edited(); }, "", " mm tall"));
            dieRow.AddChild(Ui.Btn("Fit die to blocks", () => { DesignLab.FitDie(draft); Edited(); }, "Trim the die to the blocks' outline"));
            dieRow.AddChild(Ui.Btn("Design house layout", () => { DesignLab.AutoPlace(D, draft); Edited(true); }, "Let the design house re-pack these blocks (fee, 5% slower)"));
            dieRow.AddChild(Ui.Btn("Clear", () => { draft.Blocks.Clear(); canvas.Selected = null; Edited(); }));
            editTools.AddChild(dieRow);

            var pkgs = D.Packages.Where(p => p.Year <= S.Year).OrderBy(p => p.Pins).ThenBy(p => p.Cost).ToList();
            var pkgRow = Ui.H(8);
            pkgRow.AddChild(Ui.L("Package", 13, Ui.Muted));
            pkgRow.AddChild(Ui.Options(pkgs.Select(p => $"{p.Name}: {p.Pins} pins, {p.MaxPowerW:0.#} W, up to {p.MaxDieMm2:0} mm², {Format.Money(p.Cost * S.Inflation)}").ToArray(),
                pkgs.FindIndex(p => p.Id == draft.PackageId), i => { draft.PackageId = pkgs[i].Id; Edited(draft.UseDesignHouse); }));
            editTools.AddChild(pkgRow);

            RefreshBins();
        }

        void RefreshBins()
        {
            Ui.Clear(binBox);
            binBox.AddChild(Ui.Heading("Speed and harvest bins"));
            binBox.AddChild(Ui.Wrap("Dies go into the first bin they qualify for, best first. A bin can accept dies with a core or graphics unit switched off by a defect, or dies that only reach a lower clock. With no bins, every working die at the rated clock is \"Standard\".", 12, Ui.Muted));
            var st = draft.Stats;
            for (int i = 0; i < draft.Bins.Count; i++)
            {
                var bin = draft.Bins[i];
                int idx = i;
                var row = Ui.H(6);
                var name = new LineEdit { Text = bin.Name, CustomMinimumSize = new Vector2(120, 0) };
                name.TextChanged += t => { bin.Name = t.Trim(); Reanalyze(); };
                row.AddChild(name);
                row.AddChild(Ui.Spin(0, Math.Max(bin.MinCores, st?.Cores ?? 0), 1, bin.MinCores, v => { bin.MinCores = (int)v; RefreshBinsLater(); }, "", " cores"));
                row.AddChild(Ui.Spin(0, Math.Max(bin.MinGraphics, st?.GraphicsUnits ?? 0), 1, bin.MinGraphics, v => { bin.MinGraphics = (int)v; RefreshBinsLater(); }, "", " graphics"));
                row.AddChild(Ui.Spin(0, 100, 0.05, bin.MinClockMHz, v => { bin.MinClockMHz = v; RefreshBinsLater(); }, "at least ", " MHz"));
                if (idx > 0) row.AddChild(Ui.Btn("Up", () => { draft.Bins.RemoveAt(idx); draft.Bins.Insert(idx - 1, bin); RefreshBinsLater(); }));
                row.AddChild(Ui.Btn("Remove", () => { draft.Bins.RemoveAt(idx); RefreshBinsLater(); }));
                binBox.AddChild(row);
            }
            binBox.AddChild(Ui.Btn("Add bin", () =>
            {
                draft.Bins.Add(new BinDef
                {
                    Name = draft.Bins.Count == 0 ? "Standard" : $"Bin {draft.Bins.Count + 1}",
                    MinCores = st?.Cores ?? 0,
                    MinGraphics = st?.GraphicsUnits ?? 0,
                    MinClockMHz = Math.Round(st?.ClockMHz ?? 0, 2),
                });
                RefreshBinsLater();
            }));

            // expected mix at the yield-estimate learning level
            if (st != null && st.Valid && draft.Blocks.Count > 0)
            {
                var n = D.Node(draft.NodeId);
                double d0 = LearningCurve.D0(n, DesignLab.EstimateLearning);
                var mix = Binning.Sample(D, new Rng(WaferMap.Hash(draft.Name + draft.NodeId + draft.Blocks.Count)), draft, d0, 2000);
                int total = Math.Max(1, mix.Values.Sum());
                binBox.AddChild(Ui.L($"Expected mix at {DesignLab.EstimateLearning:0} months of fab learning (D0 {d0:0.0}): " +
                                     string.Join(" · ", mix.Select(kv => $"{kv.Key} {kv.Value / (double)total:P0}")), 12, Ui.Fg));
            }
        }

        void RefreshBinsLater()
        {
            Reanalyze();
            RefreshBins();
        }

        void StartDraft()
        {
            if (draft == null) { main.Toast("Pick a market and node the design house can work with."); return; }
            var team = S.DesignTeams.FirstOrDefault(t => t.ProjectId == null);
            if (team == null) { main.Toast("Your design team is busy. Wait, or cancel its current chip."); return; }
            draft.Name = nameEdit.Text;
            draft.TargetClockMHz = clockSpin.Value;
            draft.Verification = verifSpin.Value / 100.0;
            string err = DesignOps.StartDesign(D, S, draft, team.Id);
            if (err == null)
            {
                draft = null;
                editing = false;
                nameEdit.Text = "";
            }
            Act(err);
        }
    }
}
