using System;
using System.Linq;

namespace SiliconeTycoon.Sim
{
    /// <summary>Bank loans, interest and head-office costs. (Borrow/Repay follow Console Tycoon's Sim.)</summary>
    public static class Finance
    {
        // Approximate base lending rate by year (tuning values, not sourced).
        static readonly (double, double)[] BaseRate =
        {
            (1970, 0.08), (1974, 0.10), (1977, 0.07), (1980, 0.15), (1981, 0.19), (1983, 0.11), (1985, 0.10),
            (1990, 0.10), (1995, 0.09), (2000, 0.09), (2003, 0.04), (2007, 0.08), (2010, 0.0325), (2021, 0.0325),
            (2023, 0.08), (2026, 0.07),
        };

        /// <summary>Fixed head-office cost per month in 1970 dollars.</summary>
        public const double HeadOffice = 10000;
        /// <summary>Sales, marketing and admin as a share of the month's revenue.</summary>
        public const double SgaShare = 0.08;

        /// <summary>The rate a new loan would carry: base rate plus a spread that falls with reputation.</summary>
        public static double InterestRate(GameState s) =>
            Calendar.Lerp(BaseRate, s.YearF) + 0.02 + 0.04 * Math.Clamp(1.25 - s.RepFactor, 0, 1);

        public static double LoanLimit(GameState s)
        {
            double collateral = s.Fabs.Sum(f => f.Stage == FabStage.Construction ? f.CapexPaid : f.BookValue);
            double limit = Math.Max(3_000_000 * s.Inflation, 12 * s.Ledger.AverageRevenue(3)) + 0.7 * collateral;
            return limit * Math.Max(0.3, 0.75 + 0.35 * s.Rep);
        }

        public static double Headroom(GameState s) => Math.Max(0, LoanLimit(s) - s.Ledger.Debt);

        public static string Borrow(GameState s, double amount)
        {
            if (amount <= 0) return "Enter an amount to borrow.";
            double room = Headroom(s);
            if (amount > room + 0.005) return $"The bank will lend you at most {Format.Money(room)} more.";
            double rate = InterestRate(s);
            s.Ledger.Loans.Add(new Loan { Id = s.NewId("loan"), Principal = amount, AnnualRate = rate, TakenTick = s.Tick });
            s.Ledger.Cash += amount;   // borrowing moves cash but isn't income
            s.Log($"You borrow {Format.Money(amount)} at {rate:P1} a year.", 0);
            return null;
        }

        /// <summary>Repays the most expensive loans first.</summary>
        public static string Repay(GameState s, double amount)
        {
            if (amount <= 0) return "Enter an amount to repay.";
            if (s.Ledger.Debt <= 0) return "You have no loans.";
            amount = Math.Min(amount, s.Ledger.Debt);
            if (amount > s.Cash) return "You don't have that much cash.";
            double left = amount;
            foreach (var loan in s.Ledger.Loans.OrderByDescending(l => l.AnnualRate).ToList())
            {
                double pay = Math.Min(left, loan.Principal);
                loan.Principal -= pay;
                left -= pay;
                if (loan.Principal <= 0.005) s.Ledger.Loans.Remove(loan);
                if (left <= 0) break;
            }
            s.Ledger.Cash -= amount;
            s.Log($"You repay {Format.Money(amount)} of debt.", 1);
            return null;
        }

        public static void MonthEnd(GameState s)
        {
            double interest = s.Ledger.Loans.Sum(l => l.Principal * l.AnnualRate / 12.0);
            if (interest > 0) s.Ledger.Book(Accounts.Interest, -interest);
            double revenue = s.Ledger.Current.Values.Where(v => v > 0).Sum();
            s.Ledger.Book(Accounts.Overhead, -(HeadOffice * s.Inflation + SgaShare * revenue));
        }
    }
}
