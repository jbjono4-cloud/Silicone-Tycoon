using System;
using System.Linq;
using System.Text;
using Godot;
using SiliconeTycoon.Sim;

namespace SiliconeTycoon.UI
{
    /// <summary>The company at a glance: last month's books, the bank, history charts, auto-pause settings and news.</summary>
    public partial class CompanyTab : GameTab
    {
        VBoxContainer books, bank, standing;
        LineChart moneyChart, cashChart;
        RichTextLabel news;

        public CompanyTab() { }
        public CompanyTab(Main m) : base(m, "Company") { }

        protected override void Build()
        {
            var left = Ui.V(10);
            var ls = Ui.Scroll(left);
            ls.SizeFlagsStretchRatio = 1.3f;
            AddChild(ls);

            var (booksCard, bb) = Ui.CardV("Last month");
            books = Ui.V(3);
            bb.AddChild(books);
            left.AddChild(booksCard);

            var (bankCard, kb) = Ui.CardV("The bank");
            bank = Ui.V(6);
            kb.AddChild(bank);
            left.AddChild(bankCard);

            var (repCard, rb) = Ui.CardV("Standing");
            standing = Ui.V(4);
            rb.AddChild(standing);
            left.AddChild(repCard);

            var (chartCard, cb) = Ui.CardV("Revenue and profit by month");
            moneyChart = new LineChart { ValueFormat = v => Format.Big(v) };
            moneyChart.CustomMinimumSize = new Vector2(300, 220);
            cb.AddChild(moneyChart);
            left.AddChild(chartCard);

            var (cashCard, ccb) = Ui.CardV("Cash and debt");
            cashChart = new LineChart { ValueFormat = v => Format.Big(v) };
            cashChart.CustomMinimumSize = new Vector2(300, 200);
            ccb.AddChild(cashChart);
            left.AddChild(cashCard);

            var (pauseCard, pb) = Ui.CardV("Pause automatically when");
            pb.AddChild(Check("a customer asks for bids", main.PauseOnRfq, v => main.PauseOnRfq = v));
            pb.AddChild(Check("a chip's silicon needs a decision", main.PauseOnChips, v => main.PauseOnChips = v));
            pb.AddChild(Check("a process node is ready", main.PauseOnNodes, v => main.PauseOnNodes = v));
            pb.AddChild(Check("something goes badly wrong", main.PauseOnBadNews, v => main.PauseOnBadNews = v));
            left.AddChild(pauseCard);

            var (newsCard, nb) = Ui.CardV("News");
            news = new RichTextLabel { BbcodeEnabled = true, ScrollActive = true };
            news.SizeFlagsVertical = SizeFlags.ExpandFill;
            news.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            nb.AddChild(news);
            nb.SizeFlagsVertical = SizeFlags.ExpandFill;
            newsCard.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            newsCard.SizeFlagsVertical = SizeFlags.ExpandFill;
            AddChild(newsCard);
        }

        CheckBox Check(string text, bool on, Action<bool> set)
        {
            var c = new CheckBox { Text = text, ButtonPressed = on };
            c.Toggled += v => { set(v); main.SaveSettings(); };
            return c;
        }

        public override void Refresh()
        {
            if (S == null || books == null) return;
            RefreshBooks();
            RefreshBank();
            RefreshStanding();
            RefreshCharts();
            RefreshNews();
        }

        void RefreshBooks()
        {
            Ui.Clear(books);
            var m = S.Ledger.LastMonth;
            if (m == null)
            {
                books.AddChild(Ui.Wrap("No month closed yet.", 13, Ui.Muted));
                return;
            }
            books.AddChild(Ui.L(Calendar.DateLabel(m.Month * Calendar.WeeksPerMonth), 13, Ui.Muted));
            foreach (var kv in m.Lines.OrderByDescending(x => x.Value))
                books.AddChild(Ui.Row(kv.Key, Format.Money(kv.Value), kv.Value < 0 ? Ui.Bad : Ui.Ok));
            books.AddChild(Ui.Rule());
            books.AddChild(Ui.Row("Revenue", Format.Money(m.Revenue)));
            books.AddChild(Ui.Row("Costs", Format.Money(-m.Costs)));
            books.AddChild(Ui.Row("Profit", Format.Money(m.Profit), m.Profit < 0 ? Ui.Bad : Ui.Ok, 16));
            if (m.Capex > 0) books.AddChild(Ui.Row("Spent on fabs (capital)", Format.Money(-m.Capex), Ui.Warn));
            books.AddChild(Ui.Wrap("Depreciation and write-offs reduce profit but not cash; fab construction uses cash but is charged to profit later as depreciation.", 12, Ui.Muted));
        }

        void RefreshBank()
        {
            Ui.Clear(bank);
            double limit = Finance.LoanLimit(S);
            double room = Finance.Headroom(S);
            bank.AddChild(Ui.Row("Owed", Format.Money(S.Ledger.Debt)));
            bank.AddChild(Ui.Row("Credit limit", Format.Money(limit)));
            bank.AddChild(Ui.Row("A new loan would cost", $"{Finance.InterestRate(S):P1} a year"));
            foreach (var l in S.Ledger.Loans)
                bank.AddChild(Ui.Row($"  Loan from {Calendar.DateLabel(l.TakenTick)}", $"{Format.Money(l.Principal)} at {l.AnnualRate:P1}", Ui.Muted, 13));
            bank.AddChild(Ui.Wrap("The limit grows with revenue, your fabs' book value and your reputation.", 12, Ui.Muted));

            double step = Math.Max(100_000, Math.Round(500_000 * S.Inflation / 100_000) * 100_000);
            var row = Ui.H(8);
            foreach (double amt in new[] { step, step * 4 })
            {
                var b = Ui.Btn($"Borrow {Format.Big(amt)}", () => Act(Finance.Borrow(S, amt)));
                b.Disabled = room < amt;
                row.AddChild(b);
            }
            var all = Ui.Btn($"Borrow all ({Format.Big(room)})", () => Act(Finance.Borrow(S, room)));
            all.Disabled = room < 1000;
            row.AddChild(all);
            var repay = Ui.Btn("Repay what I can", () => Act(Finance.Repay(S, Math.Min(S.Ledger.Debt, S.Cash))));
            repay.Disabled = S.Ledger.Debt <= 0 || S.Cash <= 0;
            row.AddChild(repay);
            bank.AddChild(row);

            var recent = S.Ledger.Months.Skip(Math.Max(0, S.Ledger.Months.Count - 3)).ToList();
            double cashFlow = recent.Count == 0 ? 0 : recent.Average(x => x.Profit - x.Capex - x.Lines.Where(l => l.Key == Accounts.Depreciation || l.Key == Accounts.FabClosure || l.Key == Accounts.Retooling).Sum(l => l.Value));
            if (S.Cash < 0)
                bank.AddChild(Ui.Wrap($"Overdrawn for {S.OverdrawnMonths} month{(S.OverdrawnMonths == 1 ? "" : "s")}. After {Simulation.MaxOverdrawnMonths} the company is bankrupt.", 14, Ui.Bad));
            else if (cashFlow < 0)
            {
                int months = (int)(S.Cash / -cashFlow);
                bank.AddChild(Ui.Wrap($"Cash is falling by about {Format.Money(-cashFlow)} a month: about {months} month{(months == 1 ? "" : "s")} left without borrowing.", 14, months < 6 ? Ui.Bad : Ui.Warn));
            }
            else if (recent.Count > 0) bank.AddChild(Ui.Wrap($"Cash is rising by about {Format.Money(cashFlow)} a month.", 14, Ui.Ok));
        }

        void RefreshStanding()
        {
            Ui.Clear(standing);
            standing.AddChild(Ui.Wrap($"Reputation {S.Rep * 100:+0;-0;0} on a scale of −100 to 100: {GameState.RepWord(S.Rep)}. Customers and distributors weigh it in every decision, and the bank lends more to a trusted name.", 14));
            standing.AddChild(Ui.Wrap("It grows as you ship chips and deliver contracts in full, and falls with missed deliveries and cancelled contracts.", 12, Ui.Muted));
            int contracts = S.Contracts.Count(k => k.SupplierId == Market.Player && k.Status == ContractStatus.Active);
            int chips = S.Designs.Count(c => c.Stage == DesignStage.Released);
            int fabs = S.Fabs.Count(f => f.Stage != FabStage.Closed);
            standing.AddChild(Ui.L($"{chips} chip{(chips == 1 ? "" : "s")} on sale · {fabs} fab{(fabs == 1 ? "" : "s")} · {contracts} active contract{(contracts == 1 ? "" : "s")}", 13, Ui.Muted));
        }

        void RefreshCharts()
        {
            var months = S.Ledger.Months.Skip(Math.Max(0, S.Ledger.Months.Count - 72)).ToList();
            var labels = months.Select(m => Calendar.DateLabel(m.Month * Calendar.WeeksPerMonth)).ToList();
            moneyChart.SetData(labels,
                ("Revenue", Ui.Accent, months.Select(m => m.Revenue).ToList()),
                ("Profit", Ui.Ok, months.Select(m => m.Profit).ToList()));
            cashChart.SetData(labels,
                ("Cash", Ui.Accent, months.Select(m => m.Cash).ToList()),
                ("Debt", Ui.Bad, months.Select(m => m.Debt).ToList()));
        }

        void RefreshNews()
        {
            var sb = new StringBuilder();
            foreach (var n in S.News.AsEnumerable().Reverse().Take(150))
            {
                string col = Ui.Hex(Ui.Sev(n.Severity));
                sb.Append($"[color={Ui.Hex(Ui.Muted)}]{Calendar.DateLabel(n.Tick)}[/color]  [color={col}]{Ui.Esc(n.Text)}[/color]\n\n");
            }
            news.Text = sb.ToString();
        }
    }
}
