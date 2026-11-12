using System.Collections.Generic;

namespace SiliconeTycoon.Sim
{
    /// <summary>Finished, tested chips of one design and bin, waiting in the warehouse.</summary>
    public class StockItem
    {
        public string DesignId;
        public string Bin;
        public double Units;
    }

    /// <summary>A chip offered to distributors at a list price, in every segment where it qualifies.</summary>
    public class Listing
    {
        public string DesignId;
        public string Bin;
        public double Price;
    }

    public class Bid
    {
        /// <summary><see cref="Market.Player"/> or a rival id.</summary>
        public string BidderId;
        public string DesignId;
        public string Bin;
        public double Price;
        public int SubmittedTick;
    }

    /// <summary>A customer's request for quotes for its next product. Months are month indexes (see <see cref="Calendar.MonthIndex"/>).</summary>
    public class Rfq
    {
        public string Id;
        public string CustomerId;
        public string SegmentId;
        public int OpenMonth;
        /// <summary>Bids close at the end of this month.</summary>
        public int CloseMonth;
        public int StartMonth;
        public int LengthMonths;
        public double MinMetric;
        public double MaxPowerW;
        /// <summary>Highest price per chip the customer will consider, in dollars of the opening year.</summary>
        public double MaxPrice;
        public double ExpectedPrice;
        public double EstimatedUnits;
        public List<Bid> Bids = new List<Bid>();
    }

    public enum ContractStatus { Active, Completed, Cancelled }

    /// <summary>A design win: the supplier delivers the customer's share of segment demand every month at an agreed price.</summary>
    public class Contract
    {
        public string Id;
        public string CustomerId;
        public string SupplierId;
        public string DesignId;
        public string Bin;
        public double BasePrice;
        public int StartMonth;
        public int EndMonth;
        public ContractStatus Status = ContractStatus.Active;
        /// <summary>Consecutive months with more than half the order missing.</summary>
        public int ShortMonths;
        /// <summary>Most the customer orders in a month (its forecast plus upside); above that it second-sources. 0 = no cap.</summary>
        public double MaxMonthly;
        public double Delivered;
        public double Shortfall;
        public double LastMonthOrdered;
        public double LastMonthDelivered;
    }

    public class CustomerState
    {
        public string Id;
        /// <summary>Current supplier: the player, a rival, or <see cref="Market.Others"/> (the rest of the industry).</summary>
        public string SupplierId = Market.Others;
        public string ContractId;
        public int ContractEndMonth;
        public int NextRfqMonth;
        /// <summary>How happy the customer is with you, 0-1.</summary>
        public double Satisfaction = 0.5;
    }

    public class RivalProduct
    {
        public ChipDesign Design;
        public string SegmentId;
        public int ReleasedTick;
        /// <summary>Cost per good chip at release, in 1970 dollars.</summary>
        public double CostReal;
    }

    public class RivalProject
    {
        public string SegmentId;
        public ChipDesign Design;
        public int WeeksLeft;
    }

    public class RivalState
    {
        public string Id;
        public string Name;
        public double Rep;
        /// <summary>Current price as a share of the going price; falls in a price war.</summary>
        public double PriceFactor;
        public double BasePriceFactor;
        /// <summary>Chips a month it can make.</summary>
        public double Capacity;
        public double UsedThisMonth;
        public double LastUtilisation;
        public double RevenueThisMonth;
        public double LastMonthRevenue;
        public double TotalRevenue;
        public bool PriceWar;
        public int ProductsMade;
        public List<string> NodesReady = new List<string>();
        public string NextNodeId;
        public double NextNodeDue;
        public List<RivalProduct> Products = new List<RivalProduct>();
        public RivalProject Project;
    }

    /// <summary>One segment's month: total demand and who supplied it.</summary>
    public class MarketRecord
    {
        public int Month;
        public string SegmentId;
        public double Demand;
        public double OpenDemand;
        public double PlayerUnits;
        public double RivalUnits;
        public double PlayerRevenue;
    }
}
