using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace SiliconeTycoon.Sim
{
    /// <summary>Account names used in the monthly income statement.</summary>
    public static class Accounts
    {
        public const string Sales = "Chip sales";
        public const string FoundryRevenue = "Foundry revenue";
        public const string Materials = "Wafer materials";
        public const string FabRunning = "Fab running costs";
        public const string FabEngineering = "Fab engineering";
        public const string Depreciation = "Depreciation";
        public const string ProcessResearch = "Process research";
        public const string Interest = "Interest";
        public const string Overhead = "Head office";
        public const string DesignStaff = "Design engineers";
        public const string MaskSets = "Mask sets";
        public const string DesignHouse = "Design house fees";
        public const string Penalties = "Contract penalties";
    }

    public class Loan
    {
        public string Id;
        public double Principal;
        public double AnnualRate;
        public int TakenTick;
    }

    /// <summary>One closed month: income statement lines plus cash position.</summary>
    public class MonthReport
    {
        public int Month;
        public Dictionary<string, double> Lines = new Dictionary<string, double>();
        public double Revenue;
        public double Costs;
        public double Profit;
        public double Capex;
        public double Cash;
        public double Debt;
    }

    /// <summary>
    /// Cash and the books. Income and running costs hit both cash and profit (<see cref="Book"/>), depreciation hits
    /// profit only (<see cref="Accrue"/>), and capital spending hits cash only (<see cref="Invest"/>).
    /// </summary>
    public class Ledger
    {
        public double Cash;
        public List<Loan> Loans = new List<Loan>();
        public Dictionary<string, double> Current = new Dictionary<string, double>();
        public double CurrentCapex;
        public List<MonthReport> Months = new List<MonthReport>();

        public const int MaxMonthsKept = 720;

        [JsonIgnore] public double Debt => Loans.Sum(l => l.Principal);

        /// <summary>Income (positive) or expense (negative) that moves cash and counts toward profit.</summary>
        public void Book(string account, double amount)
        {
            Cash += amount;
            Add(account, amount);
        }

        /// <summary>A non-cash expense, such as depreciation. Pass a negative amount.</summary>
        public void Accrue(string account, double amount) => Add(account, amount);

        /// <summary>Capital spending: cash goes out, but profit is charged later through depreciation.</summary>
        public void Invest(double amount)
        {
            Cash -= amount;
            CurrentCapex += amount;
        }

        void Add(string account, double amount)
        {
            Current.TryGetValue(account, out var v);
            Current[account] = v + amount;
        }

        public MonthReport Close(int month)
        {
            var r = new MonthReport
            {
                Month = month,
                Lines = new Dictionary<string, double>(Current),
                Revenue = Current.Values.Where(v => v > 0).Sum(),
                Costs = -Current.Values.Where(v => v < 0).Sum(),
                Profit = Current.Values.Sum(),
                Capex = CurrentCapex,
                Cash = Cash,
                Debt = Debt,
            };
            Months.Add(r);
            if (Months.Count > MaxMonthsKept) Months.RemoveAt(0);
            Current.Clear();
            CurrentCapex = 0;
            return r;
        }

        [JsonIgnore] public MonthReport LastMonth => Months.Count > 0 ? Months[Months.Count - 1] : null;

        /// <summary>Average monthly revenue over the last <paramref name="n"/> closed months.</summary>
        public double AverageRevenue(int n) => Months.Count == 0 ? 0 : Months.Skip(System.Math.Max(0, Months.Count - n)).Average(m => m.Revenue);
    }
}
