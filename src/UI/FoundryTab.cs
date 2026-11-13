using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SiliconeTycoon.Sim;

namespace SiliconeTycoon.UI
{
    /// <summary>
    /// The wafer business: your business model, other companies' requests for wafers and your bids, your wafer contracts,
    /// custom chip (ASIC) deals, and renting capacity from foundries.
    /// </summary>
    public partial class FoundryTab : GameTab
    {
        VBoxContainer left, right;
        // supply form
        string formFoundry, formNode;
        int formWafers = 500, formMonths = 24;
        // wafer bids being drafted: rfq id -> (fab, price)
        readonly Dictionary<string, (string fab, double price)> bids = new Dictionary<string, (string, double)>();

        public FoundryTab() { }
        public FoundryTab(Main m) : base(m, "Foundry") { }

        protected override void Build()
        {
            left = Ui.V(10);
            var ls = Ui.Scroll(left);
            ls.SizeFlagsStretchRatio = 1.4f;
            AddChild(ls);
            right = Ui.V(10);
            AddChild(Ui.Scroll(right));
        }

        public override void Refresh()
        {
            if (S == null || left == null) return;
            Ui.Clear(left);
            Ui.Clear(right);
            left.AddChild(ModelCard());
            left.AddChild(WaferRfqCard());
            left.AddChild(WaferContractsCard());
            left.AddChild(AsicCard());
            right.AddChild(SupplyCard());
            right.AddChild(FoundriesCard());
            right.AddChild(ClientsCard());
        }

        // ---------------------------------------------------------------- business model

        Control ModelCard()
        {
            var (card, b) = Ui.CardV("Business model");
            b.AddChild(Ui.Title(Foundry.Model(S), 17));
            bool conflict = Foundry.SellsOwnChips(S);
            b.AddChild(Ui.Wrap(conflict
                ? $"You sell chips of your own, so foundry clients worry you'll see their designs and compete with them: they rate your bids {1 - Foundry.ConflictPenalty:P0} lower than a pure-play foundry's."
                : "You don't sell chips of your own, so foundry clients trust you with their designs.", 13, conflict ? Ui.Warn : Ui.Ok));
            int own = S.Fabs.Count(f => !f.External && f.Stage != FabStage.Closed);
            int rented = S.Fabs.Count(f => f.External && f.Stage != FabStage.Closed);
            b.AddChild(Ui.L($"{own} fab{(own == 1 ? "" : "s")} of your own · {rented} supply agreement{(rented == 1 ? "" : "s")} with foundries · " +
                            $"{S.WaferContracts.Count(k => k.Status == WaferContractStatus.Active)} wafer contract(s) · {S.AsicDeals.Count(x => x.Status == AsicStatus.Accepted || x.Status == AsicStatus.Supplying)} custom chip deal(s)", 12, Ui.Muted));
            b.AddChild(Ui.Wrap("Switch models by what you do: sell or close fabs and rent wafers to go fabless; stop listing your own chips to become a trusted pure-play foundry. Fabs can be sold to a foundry from the Fabs tab.", 12, Ui.Muted));
            return card;
        }

        // ---------------------------------------------------------------- wafer requests

        Control WaferRfqCard()
        {
            var (card, b) = Ui.CardV("Companies looking for a foundry");
            var fabs = S.Fabs.Where(f => !f.External && f.Stage != FabStage.Closed).ToList();
            if (S.WaferRfqs.Count == 0) b.AddChild(Ui.L("No open requests right now.", 13, Ui.Muted));
            foreach (var r in S.WaferRfqs.OrderBy(r => r.CloseMonth))
            {
                var c = D.Client(r.ClientId);
                b.AddChild(Ui.Rule());
                var head = Ui.H(10);
                head.AddChild(Ui.Title(c?.Name ?? r.ClientId, 15));
                head.AddChild(Ui.L(c?.Country ?? "", 12, Ui.Muted));
                head.AddChild(Ui.Spacer());
                head.AddChild(Ui.L($"Bids close end of {Calendar.DateLabel(r.CloseMonth * Calendar.WeeksPerMonth)}", 12, Ui.Warn));
                b.AddChild(head);
                b.AddChild(Ui.L($"{r.Wafers:N0} wafers a month on {r.MaxFeatureNm / 1000:0.#} µm or finer for {r.LengthMonths} months from {Calendar.DateLabel(r.StartMonth * Calendar.WeeksPerMonth)} · " +
                                $"up to {Format.Money(r.MaxPrice)} a wafer (foundries usually charge about {Format.Money(r.ReferencePrice * Foundry.OthersMarkup(S.YearF))}) · " +
                                $"defects no worse than {r.MaxD0:0.0}/cm² or they pay less", 12, Ui.Muted));
                var ok = fabs.Where(f => D.Node(f.NodeId).FeatureNm <= r.MaxFeatureNm + 1e-6).ToList();
                if (ok.Count == 0)
                {
                    b.AddChild(Ui.L(fabs.Count == 0 ? "You have no fab of your own to make them." : "None of your fabs runs a fine enough process.", 12, Ui.Muted));
                    continue;
                }
                if (!bids.TryGetValue(r.Id, out var draft))
                {
                    draft = (r.BidFabId ?? ok[0].Id, r.BidFabId != null ? r.BidPrice : Math.Round(r.ReferencePrice * Foundry.OthersMarkup(S.YearF) * 0.95, 2));
                    bids[r.Id] = draft;
                }
                if (!ok.Any(f => f.Id == draft.fab)) { draft = (ok[0].Id, draft.price); bids[r.Id] = draft; }
                var fab = ok.First(f => f.Id == draft.fab);
                string id = r.Id;
                var row = Ui.H(8);
                row.AddChild(Ui.Options(ok.Select(f => $"{f.Name} ({D.Node(f.NodeId).Name})").ToArray(), ok.FindIndex(f => f.Id == draft.fab), i => { bids[id] = (ok[i].Id, bids[id].price); Refresh(); }));
                row.AddChild(Ui.Spin(1, Math.Max(2, r.MaxPrice), 0.5, draft.price, v => { bids[id] = (bids[id].fab, v); Refresh(); }, "$", " a wafer"));
                row.AddChild(Ui.Btn(r.BidFabId == null ? "Bid" : "Change bid", () => Act(Foundry.BidWafers(D, S, id, bids[id].fab, bids[id].price), "Bid submitted.")));
                if (r.BidFabId != null) row.AddChild(Ui.Btn("Withdraw", () => Act(Foundry.WithdrawWaferBid(S, id))));
                b.AddChild(row);

                string problem = Foundry.CheckWaferBid(D, S, r, fab, draft.price);
                if (problem != null) b.AddChild(Ui.L(problem, 12, Ui.Bad));
                else
                {
                    double score = Foundry.WaferScore(D, S, r, fab, draft.price), bar = Foundry.WaferOthersScore(r);
                    double d0 = fab.Producing ? FabOps.D0(D, fab) : LearningCurve.D0(D.Node(fab.NodeId), FabOps.StartingLearning(D, S, fab.NodeId));
                    double cost = FabOps.WaferCost(D, S, fab) + FabOps.RunningCost(D, S, fab) / Math.Max(1, fab.Capacity) + fab.MonthlyDepreciation / Math.Max(1, fab.Capacity);
                    int free = fab.Capacity - fab.ContractStarts;
                    b.AddChild(Ui.L($"Scores {score:0.00} against other foundries' {bar:0.00} · your cost is about {Format.Money(cost)} a wafer · defects {d0:0.0}/cm² · " +
                                    $"{free:N0} of {fab.Capacity:N0} starts not promised to clients{(r.Wafers > free ? " (not enough)" : "")}", 12, score > bar && r.Wafers <= free ? Ui.Ok : Ui.Warn));
                }
            }
            return card;
        }

        Control WaferContractsCard()
        {
            var (card, b) = Ui.CardV("Your wafer contracts");
            var list = S.WaferContracts.OrderBy(k => k.Status).ThenBy(k => k.StartMonth).ToList();
            if (list.Count == 0) b.AddChild(Ui.L("None yet.", 13, Ui.Muted));
            foreach (var k in list)
            {
                var c = D.Client(k.ClientId);
                var fab = S.Fabs.FirstOrDefault(f => f.Id == k.FabId);
                int now = S.MonthIndex;
                string when = now < k.StartMonth ? $"starts {Calendar.DateLabel(k.StartMonth * Calendar.WeeksPerMonth)}"
                    : k.Status == WaferContractStatus.Active ? $"{k.EndMonth - now + 1} months left" : k.Status.ToString().ToLower();
                var col = k.Status == WaferContractStatus.Cancelled ? Ui.Bad : k.ShortMonths > 0 ? Ui.Warn : k.Status == WaferContractStatus.Active ? Ui.Fg : Ui.Muted;
                b.AddChild(Ui.L($"{c?.Name}: {k.Wafers:N0} wafers a month at {Format.Money(k.Price)} from {fab?.Name ?? "a sold fab"} · {when}", 14, col));
                if (k.Status == WaferContractStatus.Active && now > k.StartMonth)
                    b.AddChild(Ui.L($"   Last month: started {k.LastMonthStarted:N0} · delivered {k.Delivered:N0} wafers for {Format.Big(k.Revenue)} so far · paid {k.QualityPaid:P0} of the price after the yield clause" +
                                    (k.ShortMonths > 0 ? $" · short {k.ShortMonths} month(s) running: three and they leave" : ""), 12, k.ShortMonths > 0 || k.QualityPaid < 0.95 ? Ui.Warn : Ui.Muted));
            }
            b.AddChild(Ui.Wrap($"Clients pay per wafer when it comes off the line, less (down to {Foundry.YieldFloor:P0}) if its yield falls short of what the agreed defect density promises. Missed starts cost {Foundry.ShortfallPenalty:P0} of the price a wafer.", 12, Ui.Muted));
            return card;
        }

        // ---------------------------------------------------------------- custom chips

        Control AsicCard()
        {
            var (card, b) = Ui.CardV("Custom chip deals");
            var deals = S.AsicDeals.Where(x => x.Status != AsicStatus.Declined).OrderBy(x => x.Status).ToList();
            if (deals.Count == 0) b.AddChild(Ui.L("No offers right now. Clients come to you every couple of years.", 13, Ui.Muted));
            foreach (var x in deals)
            {
                var c = D.Client(x.ClientId);
                var seg = D.Segment(x.SegmentId);
                b.AddChild(Ui.Rule());
                var head = Ui.H(10);
                head.AddChild(Ui.Title(c?.Name ?? x.ClientId, 15));
                head.AddChild(Ui.L($"{seg?.Name.ToLower()} · {c?.Country}", 12, Ui.Muted));
                head.AddChild(Ui.Spacer());
                string status = x.Status switch
                {
                    AsicStatus.Offered => $"Offer open until {Calendar.DateLabel(x.ExpiresMonth * Calendar.WeeksPerMonth)}",
                    AsicStatus.Accepted => $"Due {Calendar.DateLabel(x.DeadlineMonth * Calendar.WeeksPerMonth)}",
                    AsicStatus.Supplying => "Supplying",
                    AsicStatus.Done => "Done",
                    AsicStatus.Failed => "Failed",
                    _ => x.Status.ToString(),
                };
                head.AddChild(Ui.L(status, 13, x.Status == AsicStatus.Failed ? Ui.Bad : x.Status == AsicStatus.Offered ? Ui.Warn : Ui.Ok));
                b.AddChild(head);
                b.AddChild(Ui.Wrap("Spec: " + string.Join(" · ", x.Spec.Describe()), 13, Ui.Fg));
                b.AddChild(Ui.L($"Engineering fees {Format.Big(x.Nre)} ({Foundry.NreUpfront:P0} on signing, the rest on delivery) · then {Format.Units(x.MonthlyUnits)} chips a month at {Format.Money(x.UnitPrice)} for {x.LengthMonths} months", 12, Ui.Muted));
                switch (x.Status)
                {
                    case AsicStatus.Offered:
                    {
                        var row = Ui.H(8);
                        string id = x.Id;
                        row.AddChild(Ui.Primary("Accept", () => Act(Foundry.AcceptAsic(D, S, id))));
                        row.AddChild(Ui.Btn("Decline", () => Act(Foundry.DeclineAsic(D, S, id))));
                        row.AddChild(Ui.L($"You'd have until about {Calendar.DateLabel((S.MonthIndex + Foundry.AsicDesignMonths - 2) * Calendar.WeeksPerMonth)} to release it to spec.", 12, Ui.Muted));
                        b.AddChild(row);
                        break;
                    }
                    case AsicStatus.Accepted:
                    {
                        var chip = S.Design(x.DesignId);
                        if (chip == null)
                        {
                            var row = Ui.H(8);
                            row.AddChild(Ui.L("No chip yet: on the Design tab, pick this deal as the brief and start it.", 12, Ui.Warn));
                            row.AddChild(Ui.Btn("Design tab", () => main.ShowTab("Design")));
                            b.AddChild(row);
                        }
                        else
                        {
                            var gaps = chip.Stats == null ? new List<string>() : Specs.Check(D, x.Spec, chip, Binning.BinsFor(chip)[0].Name);
                            b.AddChild(Ui.L($"The {chip.Name} ({chip.Stage.ToString().ToLower()}) " + (gaps.Count == 0 ? "meets the spec." : "doesn't meet it yet: it " + string.Join("; ", gaps)), 12, gaps.Count == 0 ? Ui.Ok : Ui.Bad));
                        }
                        break;
                    }
                    case AsicStatus.Supplying:
                    {
                        var k = S.Contracts.FirstOrDefault(z => z.Id == x.ContractId);
                        if (k != null) b.AddChild(Ui.L($"Delivered {Format.Units(k.Delivered)} so far · last month {Format.Units(k.LastMonthDelivered)} of {Format.Units(k.LastMonthOrdered)} ordered", 12, k.ShortMonths > 0 ? Ui.Warn : Ui.Muted));
                        break;
                    }
                }
            }
            return card;
        }

        // ---------------------------------------------------------------- renting capacity

        Control SupplyCard()
        {
            var (card, b) = Ui.CardV("Rent wafers from a foundry");
            var open = Foundry.OpenFoundries(D, S).ToList();
            if (open.Count == 0)
            {
                b.AddChild(Ui.Wrap("No foundry is taking outside customers yet.", 13, Ui.Muted));
                return card;
            }
            if (formFoundry == null || !open.Any(f => f.Id == formFoundry)) formFoundry = open[0].Id;
            var fd = D.Foundry(formFoundry);
            var nodes = Foundry.NodesOffered(D, S, fd).ToList();
            if (formNode == null || !nodes.Any(n => n.Id == formNode)) formNode = nodes.FirstOrDefault()?.Id;

            b.AddChild(Ui.Heading("Foundry"));
            b.AddChild(Ui.Options(open.Select(f => f.Name).ToArray(), open.FindIndex(f => f.Id == formFoundry), i => { formFoundry = open[i].Id; formNode = null; Refresh(); }));
            b.AddChild(Ui.Heading("Process"));
            if (nodes.Count == 0) { b.AddChild(Ui.L("It runs no process you could use yet.", 13, Ui.Muted)); return card; }
            b.AddChild(Ui.Options(nodes.Select(n => n.Name).ToArray(), nodes.FindIndex(n => n.Id == formNode), i => { formNode = nodes[i].Id; Refresh(); }));
            var row = Ui.H(10);
            row.AddChild(Ui.Spin(Foundry.MinSupplyWafers, 50000, 50, formWafers, v => { formWafers = (int)v; Refresh(); }, "", " wafers a month"));
            var terms = new[] { 12, 24, 36 };
            row.AddChild(Ui.Options(terms.Select(t => $"{t} months").ToArray(), Array.IndexOf(terms, formMonths), i => { formMonths = terms[i]; Refresh(); }));
            b.AddChild(row);

            var q = Foundry.QuoteSupply(D, S, formFoundry, formNode, formWafers, formMonths);
            if (q.Node != null && q.Price > 0)
            {
                b.AddChild(Ui.Row("Price", $"{Format.Money(q.Price)} a wafer"));
                b.AddChild(Ui.Row("Defect density", $"{q.D0:0.00}/cm² (mature {q.Node.D0Mature:0.0})"));
                b.AddChild(Ui.Row("Free capacity", $"{q.Available:N0} wafers a month"));
                b.AddChild(Ui.Row("You pay at least", $"{Format.Money(q.MonthlyMinimum)} a month ({Foundry.MinTakeShare:P0} of the wafers)"));
                b.AddChild(Ui.Row("First wafers", $"in about {Foundry.SupplyLeadMonths} months"));
            }
            if (q.Problem != null) b.AddChild(Ui.Wrap(q.Problem, 13, Ui.Bad));
            var sign = Ui.Primary("Sign the agreement", () => Act(Foundry.SignSupply(D, S, formFoundry, formNode, formWafers, formMonths)));
            sign.Disabled = q.Problem != null;
            b.AddChild(sign);
            b.AddChild(Ui.Wrap("Rented wafers appear with your fabs: assign chips to them there. You need no process research to design for a foundry's process, and no capex, but you pay its margin, wait for its nodes, and when chips are short everywhere it rations its customers.", 12, Ui.Muted));
            return card;
        }

        Control FoundriesCard()
        {
            var (card, b) = Ui.CardV("Foundries");
            foreach (var f in D.Foundries)
            {
                bool isOpen = Foundry.IsOpen(S, f);
                b.AddChild(Ui.Title(f.Name, 14));
                if (!isOpen) { b.AddChild(Ui.L($"Opens to customers in {f.From}.", 12, Ui.Muted)); continue; }
                var nodes = Foundry.NodesOffered(D, S, f).ToList();
                b.AddChild(Ui.L($"{D.Location(f.LocationId)?.Name} · {(f.PurePlay ? "pure-play" : f.RivalId != null ? "a rival's spare capacity" : "independent")} · " +
                                $"{Foundry.Capacity(S, f) - Foundry.Booked(S, f.Id):N0} of {Foundry.Capacity(S, f):N0} wafers a month free", 12, Ui.Muted));
                foreach (var n in nodes.Take(4))
                    b.AddChild(Ui.L($"   {n.Name}: {Format.Money(Foundry.SupplyPrice(S, f, n, 24))} a wafer on 24 months · defects {Foundry.D0(S, f, n):0.0}/cm²", 12, Ui.Muted));
            }
            return card;
        }

        Control ClientsCard()
        {
            var (card, b) = Ui.CardV("Clients");
            foreach (var cs in S.Clients)
            {
                var c = D.Client(cs.Id);
                if (c == null) continue;
                string what = c.Kind == ClientKind.Wafers ? $"wafers ({Curves.At(c.Wafers, S.YearF):N0} a month)" : $"custom {D.Segment(c.SegmentId)?.Name.ToLower()} chips";
                string state = !c.Active(S.Year) ? $"from {c.From}" : cs.SupplierId == Market.Player ? "your client" : "uses other foundries";
                b.AddChild(Ui.L($"{c.Name} ({c.Country}): {what} · {state}", 12, cs.SupplierId == Market.Player ? Ui.Ok : Ui.Muted));
            }
            return card;
        }
    }
}
