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
                row.AddChild(Ui.Bar(Math.Min(p.Progress, p.PlannedWeeks), p.PlannedWeeks, p.Warned ? Ui.Bad : null, 260));
                int left = ProcessResearch.WeeksToPlan(p) / Calendar.WeeksPerMonth;
                row.AddChild(Ui.L(p.Progress < p.PlannedWeeks
                    ? $"{p.Progress / p.PlannedWeeks:P0} of the planned work · about {left} month{(left == 1 ? "" : "s")} to go at this budget"
                    : "Planned work done: still not working", 13, p.Warned ? Ui.Bad : Ui.Muted));
                b.AddChild(row);

                var ctl = Ui.H(10);
                ctl.AddChild(Ui.L("Budget", 14, Ui.Muted));
                ctl.AddChild(Ui.Spin(ProcessResearch.MinSpend, ProcessResearch.MaxSpend, 0.1, p.SpendFactor, v => Act(ProcessResearch.SetBudget(D, S, n.Id, v)), "", "× normal"));
                ctl.AddChild(Ui.L($"{Format.Money(ProcessResearch.WeeklyCost(S, n, p) * Calendar.WeeksPerMonth)} a month · speed ×{ProcessResearch.Speed(p.SpendFactor):0.00} · spent {Format.Big(p.Spent)}", 13, Ui.Muted));
                b.AddChild(ctl);
                double avg = ProcessResearch.AverageFactor(p);
                b.AddChild(Ui.L($"Average budget so far {avg:0.00}× · {p.PriorExperience:0} months of experience on {(n.Requires != null ? D.Node(n.Requires).Name : "—")} · chance of a slip {ProcessResearch.SlipChance(n, avg, p.PriorExperience):P0}", 12, Ui.Muted));
                if (p.Warned) b.AddChild(Ui.Wrap("Test wafers keep failing. Nobody knows how late this will be; the money keeps going out until it works. Raising the budget can still pull it back.", 12, Ui.Bad));
                else b.AddChild(Ui.Wrap("More budget speeds the work up and, over time, lowers the chance of a slip. Cutting it saves cash but raises the risk.", 12, Ui.Muted));
                b.AddChild(Ui.Btn("Cancel programme", () => Act(ProcessResearch.Cancel(D, S, n.Id))));
                return card;
            }

            double factor = budget.TryGetValue(n.Id, out var f) ? f : 1.0;
            string why = ProcessResearch.CanStart(D, S, n.Id, factor);
            var start = Ui.H(10);
            start.AddChild(Ui.L("Budget", 14, Ui.Muted));
            start.AddChild(Ui.Spin(ProcessResearch.MinSpend, ProcessResearch.MaxSpend, 0.1, factor, v => { budget[n.Id] = v; Refresh(); }, "", "× normal"));
            int months = (int)Math.Ceiling(n.ResearchMonths / ProcessResearch.Speed(factor));
            start.AddChild(Ui.L($"{Format.Money(ProcessResearch.WeeklyCost(S, n, factor) * Calendar.WeeksPerMonth)} a month for about {months} months ({Format.Big(ProcessResearch.WeeklyCost(S, n, factor) * Calendar.WeeksPerMonth * months)}) · {ProcessResearch.SlipChance(n, factor, ProcessResearch.PriorExperience(D, S, n)):P0} chance of slipping", 13, Ui.Muted));
            var go = Ui.Btn("Start research", () => Act(ProcessResearch.Start(D, S, n.Id, budget.TryGetValue(n.Id, out var bf) ? bf : 1.0)));
            go.Disabled = why != null;
            start.AddChild(go);
            b.AddChild(start);
            if (why != null) b.AddChild(Ui.L(why, 12, Ui.Muted));
            else if (n.Requires != null)
            {
                double exp = ProcessResearch.PriorExperience(D, S, n);
                b.AddChild(Ui.L(exp > 0.05
                    ? $"Your {exp:0} months of {D.Node(n.Requires).Name} fab experience cut the slip risk (it keeps growing while that fab runs)."
                    : $"No {D.Node(n.Requires).Name} fab experience yet: running one would cut the slip risk.", 12, exp > 0.05 ? Ui.Ok : Ui.Muted));
            }
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
                string conv = r.ConvertingNodeId != null
                    ? $", converting to {D.Node(r.ConvertingNodeId).Name} (about {Calendar.DateLabel((int)Math.Round((r.ConvertedYear - Calendar.StartYear) * Calendar.WeeksPerYear))})"
                    : r.NextNodeId != null ? $", researching {D.Node(r.NextNodeId).Name}" : "";
                b.AddChild(Ui.Row(r.Name, $"producing on {best?.Name ?? "—"}{conv}"));
            }
            b.AddChild(Ui.Wrap("Each programme has a hidden chance of slipping that depends on your average budget over the programme. You can change the budget at any time: more money works faster and lowers the risk, but a slip keeps costing money every week until the process works. A node you've researched still needs a fab before you can make chips on it.", 12, Ui.Muted));
            side.AddChild(card);
        }
    }
}
