using System.Collections.Generic;

namespace SiliconeTycoon.Sim
{
    /// <summary>Finished, tested chips of one design and bin, waiting in the warehouse.</summary>
    public class StockItem
    {
        public string DesignId;
        public string Bin;
        public double Units;
        public double MadeThisMonth;
        public double SoldThisMonth;
        public double MadeLastMonth;
        public double SoldLastMonth;
    }

    /// <summary>A chip offered to distributors at a list price, in every segment where it qualifies.</summary>
    public class Listing
    {
        public string DesignId;
        public string Bin;
        public double Price;
        /// <summary>When above 0, the price follows what buyers are paying each month, times this (1 = the market price).</summary>
        public double Track;
    }

    public class Bid
    {
        /// <summary><see cref="Market.Player"/> or a rival id.</summary>
        public string BidderId;
        public string DesignId;
        public string Bin;
        public double Price;
        /// <summary>Most chips a month the bidder offers to supply. 0 = the customer's whole volume.</summary>
        public double MaxUnits;
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
        /// <summary>Most package pins the customer's board takes (0 = no limit).</summary>
        public int MaxPins;
        /// <summary>Lowest clock the customer's product works at (0 = none).</summary>
        public double MinClockMHz;
        /// <summary>Features the chip must have.</summary>
        public List<string> Features = new List<string>();
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
        /// <summary>Most the customer orders in a month from this supplier; above that it second-sources. 0 = no cap.</summary>
        public double MaxMonthly;
        /// <summary>Share of the customer's volume this contract covers (less than 1 when the order was split between suppliers).</summary>
        public double VolumeShare = 1;
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
        /// <summary>Nodes the rival has researched.</summary>
        public List<string> NodesReady = new List<string>();
        /// <summary>Nodes the rival has a working fab for: it can only make chips on these.</summary>
        public List<string> ProductionNodes = new List<string>();
        public string NextNodeId;
        public double NextNodeDue;
        /// <summary>Node a fab is being converted to, and when production starts (fractional year).</summary>
        public string ConvertingNodeId;
        public double ConvertedYear;
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
        /// <summary>Share of distributor demand nobody could supply this month (0-1).</summary>
        public double Shortage;
        /// <summary>What buyers were paying (going price with any scarcity premium).</summary>
        public double Price;
    }
}
