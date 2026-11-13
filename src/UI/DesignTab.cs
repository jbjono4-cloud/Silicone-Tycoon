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
        OptionButton segPick, nodePick, briefPick;
        SpinBox clockSpin, verifSpin, timingSpin;
        List<Rfq> formRfqs = new List<Rfq>();
        string formRfq;
        VBoxContainer planBox;
        List<ProcessNodeDef> formNodes = new List<ProcessNodeDef>();
        List<SegmentDef> formSegs = new List<SegmentDef>();
        string formSeg, formNode;
        ChipDesign draft;
        // floorplan editor
        bool editing;
        PanelContainer editorCard;
        VBoxContainer sideBox, tuneHolder, formBody;
        HBoxContainer tuneRow;
        bool overlayOpen;
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
            segPick = Ui.Options(new string[0], 0, i => { formSeg = formSegs[i].Id; formRfq = null; RefreshBriefs(); if (draft == null || draft.UseDesignHouse) Replan(); else Reanalyze(); });
            b.AddChild(segPick);
            b.AddChild(Ui.Heading("Brief"));
            briefPick = Ui.Options(new string[0], 0, i => { formRfq = i == 0 ? null : briefIds[i - 1]; if (draft == null || draft.UseDesignHouse) Replan(); else Reanalyze(); });
            briefPick.TooltipText = "Plan for the market as a whole, or to meet one customer's request (features, clock, pins)";
            b.AddChild(briefPick);
            b.AddChild(Ui.Heading("Process node"));
            nodePick = Ui.Options(new string[0], 0, i => { formNode = formNodes[i].Id; NodeChanged(); });
            b.AddChild(nodePick);
            var tune = Ui.H(10);
            tuneRow = tune;
            formBody = b;
            tune.AddChild(Ui.L("Rated clock", 13, Ui.Muted));
            clockSpin = Ui.Spin(0, 100, 0.05, 0, v => { Reanalyze(); if (overlayOpen) RefreshEditor(); }, "", " MHz");
            clockSpin.TooltipText = "0 = 90% of what a typical die reaches";
            tune.AddChild(clockSpin);
            tune.AddChild(Ui.L("Verification", 13, Ui.Muted));
            verifSpin = Ui.Spin(DesignOps.MinVerification * 100, DesignOps.MaxVerification * 100, 5, 30, v => { Reanalyze(); if (overlayOpen) RefreshEditor(); }, "", "%");
            verifSpin.TooltipText = "Share of engineering time spent testing before tape-out. More means fewer bugs, but a longer project.";
            tune.AddChild(verifSpin);
            tune.AddChild(Ui.L("Timing closure", 13, Ui.Muted));
            timingSpin = Ui.Spin(0, DesignLab.MaxTimingEffort * 100, 5, 0, v => { Reanalyze(); if (overlayOpen) RefreshEditor(); }, "+", "%");
            timingSpin.TooltipText = "Extra engineering spent closing timing: each 10% raises the clock a typical die reaches by 5%, but adds 15% to the design effort.";
            tune.AddChild(timingSpin);
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
            var forDeal = Foundry.Deal(S, c.CustomFor);
            if (forDeal != null) b.AddChild(Ui.L($"Custom chip for {D.Client(forDeal.ClientId)?.Name}: only they can buy it.", 12, Ui.Accent));
            var chipFeatures = Features.Of(D, c);
            if (chipFeatures.Count > 0) b.AddChild(Ui.L("Features: " + Features.List(chipFeatures.OrderBy(x => x)), 12, Ui.Muted));
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
            var nodes = D.Nodes.Where(n => Foundry.CanDesignFor(D, S, n.Id)).OrderBy(n => n.FeatureNm).ToList();
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
                Fill(nodePick, formNodes.Select(x => x.Name + (S.Fabs.Any(f => f.NodeId == x.Id && f.Stage != FabStage.Closed) ? "" : S.NodeReady(x.Id) ? " (no fab)" : " (at a foundry)")).ToArray(), formNodes.FindIndex(x => x.Id == formNode));
            }
            RefreshBriefs();
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

        Rfq BriefRfq => formRfq == null ? null : S.Rfqs.FirstOrDefault(r => r.Id == formRfq);
        /// <summary>A custom chip deal picked as the brief (the chip is made for that client).</summary>
        AsicDeal BriefDeal => formRfq == null ? null : S.AsicDeals.FirstOrDefault(x => x.Id == formRfq && x.Status == AsicStatus.Accepted);
        List<string> briefIds = new List<string>();

        /// <summary>The brief the design house works to: the market as a whole, or one customer's request.</summary>
        ChipSpec Brief()
        {
            var seg = D.Segment(formSeg);
            if (seg == null) return null;
            var deal = BriefDeal;
            if (deal != null) return deal.Spec;
            var r = BriefRfq;
            return r != null ? ChipSpec.ForRfq(D, r) : ChipSpec.ForSegment(seg, S.YearF);
        }

        void RefreshBriefs()
        {
            var rfqs = S.Rfqs.Where(r => r.SegmentId == formSeg).OrderBy(r => r.CloseMonth).ToList();
            var deals = S.AsicDeals.Where(x => x.Status == AsicStatus.Accepted && x.SegmentId == formSeg && S.Design(x.DesignId) == null).ToList();
            var ids = deals.Select(x => x.Id).Concat(rfqs.Select(r => r.Id)).ToList();
            if (formRfq != null && !ids.Contains(formRfq)) formRfq = null;
            if (ids.SequenceEqual(briefIds) && briefPick.ItemCount == ids.Count + 1) return;
            briefIds = ids;
            formRfqs = rfqs;
            var names = new[] { "The market as a whole" }
                .Concat(deals.Select(x => $"Custom chip for {D.Client(x.ClientId)?.Name} (due {Calendar.DateLabel(x.DeadlineMonth * Calendar.WeeksPerMonth)})"))
                .Concat(rfqs.Select(r => $"{D.Customer(r.CustomerId)?.Name}'s request (bids close {Calendar.DateLabel(r.CloseMonth * Calendar.WeeksPerMonth)})")).ToArray();
            Fill(briefPick, names, formRfq == null ? 0 : 1 + ids.IndexOf(formRfq));
        }

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
            planWhy = null;
            var brief = Brief();
            draft = brief == null || formNode == null ? null : DesignLab.PlanForSpec(D, S, formNode, brief, nameEdit.Text, out planWhy);
            if (draft != null && clockSpin != null) clockSpin.SetValueNoSignal(draft.TargetClockMHz);
            Reanalyze();
            RefreshEditor();
        }

        void Reanalyze()
        {
            if (planBox == null) return;
            Ui.Clear(planBox);
            if (sideBox != null) Ui.Clear(sideBox);
            var box = overlayOpen ? sideBox : planBox;
            if (overlayOpen) planBox.AddChild(Ui.L("Editing the floorplan...", 13, Ui.Muted));
            if (draft == null)
            {
                box.AddChild(Ui.Wrap("The design house can't make a chip for that market on that node with this year's blocks and packages." +
                                         (planWhy != null ? $" The full-strength design fails because: {planWhy}" : ""), 13, Ui.Bad));
                box.AddChild(Ui.Btn("Lay out a blank die by hand", StartBlank));
                return;
            }
            draft.TargetClockMHz = clockSpin.Value;
            draft.Verification = verifSpin.Value / 100.0;
            draft.TimingEffort = timingSpin.Value / 100.0;
            var st = DesignLab.Analyze(D, S, draft);
            draft.Stats = st;
            if (canvas != null) { canvas.Stats = st; canvas.QueueRedraw(); }
            if (!editing)
            {
                var edit = Ui.H(8);
                edit.AddChild(Ui.Btn("Edit the floorplan", () => { editing = true; RefreshEditor(); }, "Move, reshape, add and remove blocks yourself"));
                if (!draft.UseDesignHouse) edit.AddChild(Ui.Btn("Back to the design house plan", Replan));
                box.AddChild(edit);
            }
            box.AddChild(Ui.L(draft.UseDesignHouse ? "Layout: design house (fee, 5% slower, your team does 80% of the work)" : "Layout: by hand (no fee, full speed, your team does all the work)", 12, Ui.Muted));
            var seg = D.Segment(formSeg);
            var team = S.DesignTeams.FirstOrDefault();

            box.AddChild(Ui.Rule());
            box.AddChild(Ui.L(string.Join(" + ", draft.Blocks.Select(p => D.Block(p.BlockId).Name)), 13, Ui.Fg));
            box.AddChild(Ui.Row("Package", D.Package(draft.PackageId)?.Name ?? "—"));
            box.AddChild(Ui.Row("Transistors", $"{st.Transistors:N0}"));
            box.AddChild(Ui.Row("Features", st.Features.Count == 0 ? "none" : Features.List(st.Features)));
            box.AddChild(Ui.Row("Pins", $"{st.PinsNeeded} needed · {st.PadsSupplied} pads · {st.PackagePins}-pin package"));
            if (st.RamBytes > 0 || st.RomBytes > 0) box.AddChild(Ui.Row("Memory", $"{st.RamBytes:N0} B RAM · {st.RomBytes:N0} B ROM"));
            box.AddChild(Ui.Row("Die", $"{draft.DieW:0.00} × {draft.DieH:0.00} mm = {st.DieAreaMm2:0.0} mm² ({st.Utilisation:P0} used)"));
            box.AddChild(Ui.Row("Clock", $"{st.ClockMHz:0.00} MHz rated, {st.MaxClockMHz:0.00} typical"));
            box.AddChild(Ui.Row("Power", $"{st.PowerW:0.00} W (market limit {seg.MaxPowerW:0.#} W)", st.PowerW > seg.MaxPowerW ? Ui.Bad : null));
            if (!double.IsInfinity(st.HeatLimit))
                box.AddChild(Ui.Row("Hottest spot", $"{st.PeakHeat:0.000} of {st.HeatLimit:0.00} W/mm² (heat allows {Math.Min(st.ThermalClockMHz, 999):0.00} MHz)", st.PeakHeat > st.HeatLimit ? Ui.Bad : st.PeakHeat > st.HeatLimit * 0.85 ? Ui.Warn : null));
            if (st.CongestionLoss > 0) box.AddChild(Ui.Row("Routing congestion", $"−{st.CongestionLoss:P0} clock at {st.Utilisation:P0} full", Ui.Warn));
            if (st.LongestWire != null && st.LongestWireMm > 0.05) box.AddChild(Ui.Row("Slowest wire", $"{st.LongestWire}: {st.LongestWireMm:0.00} mm weighted"));
            var wantsNow = seg.WantsAt(S.YearF).ToList();
            var lacking = wantsNow.Where(w => !st.Features.Contains(w.Feature)).ToList();
            if (lacking.Count > 0)
                box.AddChild(Ui.Wrap($"Buyers mark it down for lacking {string.Join(", ", lacking.Select(w => $"{Features.Name(w.Feature)} (−{w.Weight:P0})"))}." +
                                         (seg.RequiredAt(S.YearF).Any(f => !st.Features.Contains(f)) ? " Distributors won't stock it without " + Features.List(seg.RequiredAt(S.YearF).Where(f => !st.Features.Contains(f))) + "." : ""), 12, Ui.Warn));
            var briefDeal = BriefDeal;
            if (briefDeal != null)
            {
                var gaps = Specs.Check(D, briefDeal.Spec, draft, Binning.BinsFor(draft)[0].Name);
                string who = D.Client(briefDeal.ClientId)?.Name;
                box.AddChild(gaps.Count == 0
                    ? Ui.Wrap($"Meets {who}'s spec. Once started it can only be sold to them.", 12, Ui.Ok)
                    : Ui.Wrap($"Doesn't meet {who}'s spec yet: it {string.Join("; ", gaps)}.", 12, Ui.Bad));
            }
            var briefRfq = BriefRfq;
            if (briefRfq != null)
            {
                var gaps = Specs.Check(D, ChipSpec.ForRfq(D, briefRfq), draft, Binning.BinsFor(draft)[0].Name);
                box.AddChild(gaps.Count == 0
                    ? Ui.Wrap($"Meets {D.Customer(briefRfq.CustomerId)?.Name}'s request.", 12, Ui.Ok)
                    : Ui.Wrap($"Doesn't meet {D.Customer(briefRfq.CustomerId)?.Name}'s request: the customer {string.Join("; ", gaps)}.", 12, Ui.Bad));
            }
            double perf = Market.MetricOf(st, seg.Metric);
            double need = seg.NeedAt(S.YearF);
            box.AddChild(Ui.Row($"{seg.Metric}", $"{perf:0.00} (competitive today: {need:0.00}, next year: {seg.NeedAt(S.YearF + 1):0.00})", perf < need * Market.OpenMarketMinShare ? Ui.Bad : perf < need ? Ui.Warn : Ui.Ok));
            box.AddChild(Ui.Row("Dies per wafer", $"{st.DiesPerWafer:N0} at about {st.YieldEstimate:P0} yield"));
            box.AddChild(Ui.Row("Cost per good chip", Format.Money(st.CostPerGoodDie)));
            box.AddChild(Ui.Row("Buyers paying today", Format.Money(Market.MarketPrice(S, seg, S.YearF))));
            box.AddChild(Ui.Rule());
            int weeks = team == null || team.Engineers == 0 ? 0 : (int)Math.Ceiling(st.EngineerMonths / team.Engineers * Calendar.WeeksPerMonth);
            box.AddChild(Ui.Row("Design effort", $"{st.EngineerMonths:0} engineer-months ≈ {weeks / Calendar.WeeksPerMonth} months with {team?.Engineers ?? 0} engineers"));
            box.AddChild(Ui.Row("Expected bugs at first silicon", $"{DesignOps.ExpectedBugs(draft, st):0.0}"));
            box.AddChild(Ui.Row("Masks at tape-out", Format.Money(st.MaskCost)));
            box.AddChild(Ui.Row("Design house fee (now)", Format.Money(st.DesignHouseFee)));
            foreach (var note in st.Notes) box.AddChild(Ui.Wrap(note.Text, 12, Ui.NoteSev(note.Severity)));
            if (!S.Fabs.Any(f => f.NodeId == draft.NodeId && f.Stage != FabStage.Closed))
                box.AddChild(Ui.Wrap($"You have no {D.Node(draft.NodeId).Name} fab to make it in yet.", 12, Ui.Warn));
        }

        // ---------------------------------------------------------------- floorplan editor

        /// <summary>
        /// The floorplan editor opens over the whole window: the die large on the left, with the tools above it and the
        /// bins below, and the chip's numbers, notes and tuning on the right.
        /// </summary>
        void BuildEditor(VBoxContainer parent)
        {
            var overlay = new PanelContainer { Visible = false, ZIndex = 15 };
            overlay.SetAnchorsPreset(LayoutPreset.FullRect);
            var bg = new StyleBoxFlat { BgColor = Ui.Bg };
            bg.SetContentMarginAll(14);
            overlay.AddThemeStyleboxOverride("panel", bg);
            editorCard = overlay;

            var split = Ui.H(14);
            overlay.AddChild(split);

            var leftCol = Ui.V(8);
            leftCol.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            leftCol.SizeFlagsStretchRatio = 2.3f;
            editTools = Ui.V(6);
            leftCol.AddChild(editTools);
            canvas = new FloorplanCanvas { Data = D, Editable = true, CustomMinimumSize = new Vector2(600, 420) };
            canvas.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            canvas.SizeFlagsVertical = SizeFlags.ExpandFill;
            canvas.Changed = () => Edited();
            canvas.SelectionChanged = RefreshEditor;
            var canvasCard = Ui.Card(canvas);
            canvasCard.SizeFlagsVertical = SizeFlags.ExpandFill;
            leftCol.AddChild(canvasCard);
            leftCol.AddChild(Ui.Wrap("Drag blocks to move them: they snap to the die edge and to each other. Right-click turns a block; scroll over the selected block to change its shape (same area). " +
                                     "I/O rings must touch the die edge. Keep cores next to the memory, ROM and graphics they talk to: every mm of wire costs clock speed (dashed lines). " +
                                     "Empty silicon is paid for on every wafer but defects that land there do no harm. A smaller die fits more per wafer and yields better.", 12, Ui.Muted));
            binBox = Ui.V(4);
            var bins = Ui.Scroll(binBox);
            bins.SizeFlagsVertical = SizeFlags.Fill;
            bins.CustomMinimumSize = new Vector2(0, 150);
            leftCol.AddChild(bins);
            split.AddChild(leftCol);

            var rightCol = Ui.V(8);
            tuneHolder = Ui.V(6);
            rightCol.AddChild(tuneHolder);
            sideBox = Ui.V(3);
            rightCol.AddChild(sideBox);
            var (sideCard, sb) = Ui.CardV("The chip");
            sb.AddChild(rightCol);
            var sideScroll = Ui.Scroll(sideCard);
            sideScroll.SizeFlagsStretchRatio = 1f;
            split.AddChild(sideScroll);

            main.AddChild(overlay);
        }

        public override void _ExitTree()
        {
            if (editorCard != null && IsInstanceValid(editorCard)) editorCard.QueueFree();
            editorCard = null;
        }

        /// <summary>Opens or closes the editor window, moving the clock and effort settings into it while it's open.</summary>
        void SetOverlay(bool open)
        {
            if (open == overlayOpen || editorCard == null) return;
            overlayOpen = open;
            editorCard.Visible = open;
            tuneRow.GetParent()?.RemoveChild(tuneRow);
            if (open)
            {
                tuneHolder.AddChild(tuneRow);
                main.PauseGame();
            }
            else
            {
                formBody.AddChild(tuneRow);
                formBody.MoveChild(tuneRow, planBox.GetIndex());
            }
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
            if (show != overlayOpen)
            {
                SetOverlay(show);
                Reanalyze();
            }
            if (!show) return;
            canvas.Data = D;
            canvas.Chip = draft;
            canvas.Stats = draft.Stats;
            if (draft.Blocks.All(p => p.Id != canvas.Selected)) canvas.Selected = null;
            canvas.QueueRedraw();

            Ui.Clear(editTools);
            var n = D.Node(draft.NodeId);
            var head = Ui.H(10);
            head.AddChild(Ui.Title($"Floorplan: {draft.Name}", 18));
            head.AddChild(Ui.L($"{n.Name} · {Calendar.DateLabel(S.Tick)} · {Format.Money(S.Cash)} in the bank · the game is paused while you edit", 13, Ui.Muted));
            head.AddChild(Ui.Spacer());
            head.AddChild(Ui.Btn("Close", () => { editing = false; RefreshEditor(); Reanalyze(); }, "Back to the Design tab; the draft keeps your layout"));
            var go = Ui.Primary("Start the design", () => { StartDraft(); RefreshEditor(); });
            string why = StartProblem();
            go.Disabled = why != null;
            go.TooltipText = why ?? "Hand the chip to your design team";
            head.AddChild(go);
            editTools.AddChild(head);
            var nameRow = Ui.H(8);
            nameRow.AddChild(Ui.L("Name", 13, Ui.Muted));
            var nameField = new LineEdit { Text = nameEdit.Text, CustomMinimumSize = new Vector2(180, 0) };
            nameField.TextChanged += t => { nameEdit.Text = t; draft.Name = t; };
            nameField.TextSubmitted += _ => RefreshEditor();
            nameRow.AddChild(nameField);
            if (why != null) nameRow.AddChild(Ui.Wrap("Can't start yet: " + why, 13, Ui.Bad));
            editTools.AddChild(nameRow);

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
                if (def != null && def.MaxPipeline > 1)
                    selRow.AddChild(Ui.Spin(1, def.MaxPipeline, 1, sel.Pipeline, v => { sel.Pipeline = (int)v; Edited(); }, "", " stages"));
                if (def != null && def.MaxScale > 1)
                    selRow.AddChild(Ui.Spin(1, def.MaxScale, 1, sel.Scale, v => { sel.Scale = (int)v; Edited(); }, "×", def.Kind == BlockKind.MemoryInterface ? " wide" : " size"));
                var cells = new[] { CellLibrary.Standard, CellLibrary.Fast, CellLibrary.Dense };
                selRow.AddChild(Ui.Options(new[] { "Standard cells", "Fast cells", "Dense cells" }, Array.IndexOf(cells, sel.Cells), i => { sel.Cells = cells[i]; Edited(); }));
                selRow.AddChild(Ui.Btn("Find a free spot", () => { DesignLab.PlaceFree(draft, sel); Edited(); }));
                selRow.AddChild(Ui.Btn("Remove", () => { draft.Blocks.Remove(sel); canvas.Selected = null; Edited(); }));
            }
            else selRow.AddChild(Ui.L("Click a block to select it.", 12, Ui.Muted));
            editTools.AddChild(selRow);
            if (sel != null && D.Block(sel.BlockId) is BlockDef sd)
            {
                var e = DesignLab.Eff(sd, sel);
                var bits = new List<string>();
                if (sd.Kind == BlockKind.Core) bits.Add($"{e.Ipc:0.00} instructions per clock");
                if (e.RamBytes > 0) bits.Add($"{e.RamBytes:N0} B RAM");
                if (e.RomBytes > 0) bits.Add($"{e.RomBytes:N0} B ROM");
                if (e.BytesPerClock > 0) bits.Add($"{e.BytesPerClock:0.##} bytes per clock");
                if (e.Pins > 0) bits.Add($"{e.Pins} {(sd.Kind == BlockKind.Io ? "pads" : "pins")}");
                bits.Add($"critical path ×{e.Depth:0.00}");
                bits.Add($"{e.Transistors:N0} transistors");
                if (sd.Features.Count > 0) bits.Add("gives " + Features.List(sd.Features));
                editTools.AddChild(Ui.L(string.Join(" · ", bits), 12, Ui.Muted));
            }

            // die and package
            var dieRow = Ui.H(8);
            dieRow.AddChild(Ui.L("Die", 13, Ui.Muted));
            dieRow.AddChild(Ui.Spin(0.5, 40, 0.05, draft.DieW, v => { draft.DieW = v; Edited(); }, "", " mm wide"));
            dieRow.AddChild(Ui.Spin(0.5, 40, 0.05, draft.DieH, v => { draft.DieH = v; Edited(); }, "", " mm tall"));
            dieRow.AddChild(Ui.Btn("Fit die to blocks", () => { DesignLab.FitDie(draft); Edited(); }, "Trim the die to the blocks' outline"));
            dieRow.AddChild(Ui.Btn("Design house layout", () => { DesignLab.AutoPlace(D, draft); Edited(true); }, "Let the design house re-pack these blocks (fee, 5% slower)"));
            dieRow.AddChild(Ui.Btn("Clear", () => { draft.Blocks.Clear(); canvas.Selected = null; Edited(); }));
            var heatBox = new CheckBox { Text = "Show heat", ButtonPressed = canvas.ShowHeat };
            heatBox.Toggled += on => { canvas.ShowHeat = on; canvas.QueueRedraw(); };
            dieRow.AddChild(heatBox);
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
            // features that a defect can switch off (so a bin can take dies without them)
            var harvestable = draft.Blocks.Select(p => D.Block(p.BlockId)).Where(b => b != null && b.Disableable && b.Kind != BlockKind.Core)
                                   .SelectMany(b => b.Features).Distinct().ToList();
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
                foreach (var f in harvestable)
                {
                    var tick = new CheckBox { Text = Features.Name(f), ButtonPressed = bin.Features == null || bin.Features.Contains(f) };
                    tick.TooltipText = "Only dies that still have this feature go in this bin";
                    tick.Toggled += on =>
                    {
                        var list = bin.Features ?? Features.Of(D, draft).ToList();
                        list.Remove(f);
                        if (on) list.Add(f);
                        bin.Features = list;
                        RefreshBinsLater();
                    };
                    row.AddChild(tick);
                }
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

        /// <summary>Why the draft can't be started right now (for the editor's button), or null.</summary>
        string StartProblem()
        {
            if (draft == null) return "There's no draft.";
            var team = S.DesignTeams.FirstOrDefault(t => t.ProjectId == null) ?? S.DesignTeams.FirstOrDefault();
            if (team == null) return "You have no design team.";
            draft.Name = nameEdit.Text;
            draft.TargetClockMHz = clockSpin.Value;
            draft.Verification = verifSpin.Value / 100.0;
            draft.TimingEffort = timingSpin.Value / 100.0;
            return DesignOps.CanStart(D, S, draft, team.Id);
        }

        void StartDraft()
        {
            if (draft == null) { main.Toast("Pick a market and node the design house can work with."); return; }
            var team = S.DesignTeams.FirstOrDefault(t => t.ProjectId == null);
            if (team == null) { main.Toast("Your design team is busy. Wait, or cancel its current chip."); return; }
            draft.Name = nameEdit.Text;
            draft.TargetClockMHz = clockSpin.Value;
            draft.Verification = verifSpin.Value / 100.0;
            draft.TimingEffort = timingSpin.Value / 100.0;
            var deal = BriefDeal;
            string err = DesignOps.StartDesign(D, S, draft, team.Id);
            if (err == null)
            {
                if (deal != null) Foundry.AssignChip(S, deal.Id, draft);
                formRfq = null;
                draft = null;
                editing = false;
                nameEdit.Text = "";
            }
            Act(err);
        }
    }
}
