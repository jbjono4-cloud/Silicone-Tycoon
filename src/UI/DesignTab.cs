using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SiliconeTycoon.Sim;

namespace SiliconeTycoon.UI
{
    /// <summary>
    /// Chip projects: the design team, chips through tape-out, silicon, steppings and release, and starting new chips.
    /// New chips are laid out by the design house from a market brief for now; the floorplan editor arrives in milestone 5.
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
            list = Ui.V(10);
            left.AddChild(list);

            var form = Ui.V(10);
            AddChild(Ui.Scroll(form));
            var (card, b) = Ui.CardV("New chip");
            form.AddChild(card);
            b.AddChild(Ui.Wrap("Give the design house a brief and it lays out a chip for that market from this year's blocks: valid, but a little loose and 5% slower than a hand layout. (Your own floorplan editor comes next.)", 13, Ui.Muted));
            b.AddChild(Ui.Heading("Name"));
            nameEdit = new LineEdit { PlaceholderText = "Chip name" };
            b.AddChild(nameEdit);
            b.AddChild(Ui.Heading("Market"));
            segPick = Ui.Options(new string[0], 0, i => { formSeg = formSegs[i].Id; Replan(); });
            b.AddChild(segPick);
            b.AddChild(Ui.Heading("Process node"));
            nodePick = Ui.Options(new string[0], 0, i => { formNode = formNodes[i].Id; Replan(); });
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
                    int fabs = S.Fabs.Count(f => f.DesignId == c.Id && f.Producing);
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
            if (draft == null || draft.NodeId != formNode) Replan();
            else Reanalyze();
        }

        static void Fill(OptionButton o, string[] items, int selected)
        {
            o.Clear();
            for (int i = 0; i < items.Length; i++) o.AddItem(items[i], i);
            if (items.Length > 0) o.Selected = Math.Clamp(selected, 0, items.Length - 1);
        }

        void Replan()
        {
            if (S == null || planBox == null) return;
            var seg = D.Segment(formSeg);
            draft = seg == null || formNode == null ? null : DesignLab.PlanForSegment(D, S, formNode, seg.Metric, nameEdit.Text, maxPowerW: seg.MaxPowerW);
            if (draft != null && clockSpin != null) clockSpin.SetValueNoSignal(draft.TargetClockMHz);
            Reanalyze();
        }

        void Reanalyze()
        {
            if (planBox == null) return;
            Ui.Clear(planBox);
            if (draft == null)
            {
                planBox.AddChild(Ui.Wrap("The design house can't make a chip for that market on that node with this year's blocks and packages.", 13, Ui.Bad));
                return;
            }
            draft.TargetClockMHz = clockSpin.Value;
            draft.Verification = verifSpin.Value / 100.0;
            var st = DesignLab.Analyze(D, S, draft);
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
            planBox.AddChild(Ui.Row("Going price today", Format.Money(Market.ExpectedPrice(seg, S.YearF))));
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
                nameEdit.Text = "";
            }
            Act(err);
        }
    }
}
