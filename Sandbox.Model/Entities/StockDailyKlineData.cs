using System;
using System.Collections.Generic;
using System.Text;

namespace Sandbox.Model.Entities
{
    public class StockDailyKlineData
    {
        public long Id { get; set; }
        public string StockCode { get; set; } = string.Empty;
        public DateOnly TradeDate { get; set; }
        public decimal ClosePrice { get; set; }
    }
}
