using Xunit;

namespace SiliconeTycoon.Sim.Tests
{
    public class FinanceTests
    {
        static GameData D => TestData.Load();

        [Fact]
        public void BorrowingIsCappedByTheBank()
        {
            var s = TestData.NewGame(cash: 1_000_000);
            double limit = Finance.LoanLimit(s);
            Assert.Null(Finance.Borrow(s, 1_000_000));
            Assert.Equal(1_000_000, s.Ledger.Debt, 6);
            Assert.Equal(2_000_000, s.Cash, 6);
            Assert.Contains("at most", Finance.Borrow(s, limit));
        }

        [Fact]
        public void InterestIsChargedMonthly()
        {
            var s = TestData.NewGame(cash: 1_000_000);
            Finance.Borrow(s, 1_000_000);
            double rate = s.Ledger.Loans[0].AnnualRate;
            Simulation.AdvanceMonths(D, s, 1);
            Assert.Equal(-1_000_000 * rate / 12, s.Ledger.LastMonth.Lines[Accounts.Interest], 4);
        }

        [Fact]
        public void RepayingReducesDebt()
        {
            var s = TestData.NewGame(cash: 1_000_000);
            Finance.Borrow(s, 1_000_000);
            Assert.Null(Finance.Repay(s, 400_000));
            Assert.Equal(600_000, s.Ledger.Debt, 6);
            Assert.Null(Finance.Repay(s, 5_000_000));   // clamps to what's owed
            Assert.Equal(0, s.Ledger.Debt, 6);
            Assert.Empty(s.Ledger.Loans);
        }

        [Fact]
        public void CannotRepayWithoutCash()
        {
            var s = TestData.NewGame(cash: 1_000_000);
            Finance.Borrow(s, 1_000_000);
            s.Ledger.Cash = 100;
            Assert.NotNull(Finance.Repay(s, 1_000_000));
        }

        [Fact]
        public void SixMonthsOverdrawnIsTheLimit()
        {
            var s = TestData.NewGame(cash: 0);
            Simulation.AdvanceMonths(D, s, Simulation.MaxOverdrawnMonths);
            Assert.False(s.GameOver);
            Simulation.AdvanceMonths(D, s, 1);
            Assert.True(s.GameOver);
        }

        [Fact]
        public void BooksSeparateCashFromProfit()
        {
            var l = new Ledger { Cash = 1000 };
            l.Book(Accounts.Sales, 500);
            l.Accrue(Accounts.Depreciation, -200);
            l.Invest(300);
            var r = l.Close(0);
            Assert.Equal(1200, l.Cash, 9);
            Assert.Equal(300, r.Profit, 9);
            Assert.Equal(300, r.Capex, 9);
            Assert.Empty(l.Current);
        }
    }
}
