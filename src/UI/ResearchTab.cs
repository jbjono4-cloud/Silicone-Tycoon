using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SiliconeTycoon.Sim;

namespace SiliconeTycoon.UI
{
    /// <summary>The node race: process research programmes, their budgets and slip risk, and where the rivals stand.</summary>
    public partial class ResearchTab : GameTab
    {
        VBoxContainer list, side;
        readonly Dictionary<string, double> budget = new Dictionary<string, double>();

        public ResearchTab() { }
        public ResearchTab(Main m) : base(m, "Research") { }

        protected override void Build()
        {
            list = Ui.V(10);
            var ls = Ui.Scroll(list);
            ls.SizeFlagsStretchRatio = 1.6f;
            AddChild(ls);
            side = Ui.V(10);
            AddChild(Ui.Scroll(side));
        }

        public override void Refresh()
        {
            if (S == null || list == null) return;
            Ui.Clear(list);
            foreach (var n in D.Nodes.OrderBy(n => n.Year)) list.AddChild(NodeCard(n));
            RefreshSide();
        }

        Control NodeCard(ProcessNodeDef n)
        {
            var (card, b) = Ui.CardV(null, 6);
            var head = Ui.H(10);
            head.AddChild(Ui.Title(n.Name, 17));
            head.AddChild(Ui.L($"historically {n.Year}", 13, Ui.Muted));
            head.AddChild(Ui.Spacer());
            var p = ProcessResearch.Active(S, n.Id);
            bool ready = S.NodeReady(n.Id);
            string status = ready ? "Ready" : p != null ? (p.Warned ? "Researching — running late" : "Researching") : "Not started";
            head.AddChild(Ui.L(status, 14, ready ? Ui.Ok : p != null ? (p.Warned ? Ui.Bad : Ui.Accent) : Ui.Muted));
            b.AddChild(head);

            b.AddChild(Ui.L($"{n.LogicDensity:N0} logic / {n.SramDensity:N0} SRAM transistors per mm² · up to {n.MaxClockMHz:0.#} MHz at {n.Voltage:0.#} V · " +
                            $"{n.WaferMm:0} mm wafers · defects {n.D0Launch:0.0} → {n.D0Mature:0.0}/cm² · masks {Format.Big(n.MaskSetCost * S.Inflation)}", 12, Ui.Muted));

            if (ready) return card;
            if (p != null)
            {
                var row = Ui.H(10);
                row.AddChild(Ui.Bar(Math.Min(p.WeeksDone, p.PlannedWeeks), p.PlannedWeeks, p.Warned ? Ui.Bad : null, 260));
                int left = Math.Max(0, p.PlannedWeeks - p.WeeksDone) / Calendar.WeeksPerMonth;
                row.AddChild(Ui.L(p.WeeksDone <= p.PlannedWeeks ? $"{left} months to the planned date" : $"{(p.WeeksDone - p.PlannedWeeks) / Calendar.WeeksPerMonth} months past the plan", 13, p.Warned ? Ui.Bad : Ui.Muted));
                b.AddChild(row);
                b.AddChild(Ui.L($"Budget {p.SpendFactor:0.0}× · {Format.Money(ProcessResearch.WeeklyCost(S, n, p) * Calendar.WeeksPerMonth)} a month · spent {Format.Big(p.Spent)}", 13, Ui.Muted));
                if (p.Warned) b.AddChild(Ui.Wrap("Test wafers keep failing. Nobody knows how late this will be; the money keeps going out until it works.", 12, Ui.Bad));
                b.AddChild(Ui.Btn("Cancel programme", () => Act(ProcessResearch.Cancel(D, S, n.Id))));
                return card;
            }

            double factor = budget.TryGetValue(n.Id, out var f) ? f : 1.0;
            string why = ProcessResearch.CanStart(D, S, n.Id, factor);
            double total = n.ResearchCost * factor * S.Inflation;
            var start = Ui.H(10);
            start.AddChild(Ui.L("Budget", 14, Ui.Muted));
            start.AddChild(Ui.Spin(ProcessResearch.MinSpend, ProcessResearch.MaxSpend, 0.1, factor, v => { budget[n.Id] = v; Refresh(); }, "", "× normal"));
            start.AddChild(Ui.L($"{Format.Big(total)} over a planned {n.ResearchMonths} months · {ProcessResearch.SlipChance(n, factor):P0} chance of slipping", 13, Ui.Muted));
            var go = Ui.Btn("Start research", () => Act(ProcessResearch.Start(D, S, n.Id, budget.TryGetValue(n.Id, out var bf) ? bf : 1.0)));
            go.Disabled = why != null;
            start.AddChild(go);
            b.AddChild(start);
            if (why != null) b.AddChild(Ui.L(why, 12, Ui.Muted));
            return card;
        }

        void RefreshSide()
        {
            Ui.Clear(side);
            var (card, b) = Ui.CardV("The node race");
            var mine = D.Nodes.Where(n => S.NodeReady(n.Id)).OrderBy(n => n.FeatureNm).FirstOrDefault();
            b.AddChild(Ui.Row(S.CompanyName, mine?.Name ?? "—", Ui.Accent));
            foreach (var r in S.Rivals)
            {
                var best = Rivals.BestNode(D, r);
                string next = r.NextNodeId != null ? $" (next: {D.Node(r.NextNodeId).Name})" : "";
                b.AddChild(Ui.Row(r.Name, (best?.Name ?? "—") + next));
            }
            b.AddChild(Ui.Wrap("Each programme has a hidden chance of slipping. More budget cuts the risk, but a slip keeps costing money every week until the process works. A node you've researched still needs a fab before you can make chips on it.", 12, Ui.Muted));
            side.AddChild(card);
        }
    }
}
