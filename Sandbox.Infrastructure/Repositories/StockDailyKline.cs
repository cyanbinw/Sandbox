using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Sandbox.Infrastructure.Repositories
{
    /// <summary>
    /// 股票日 K 线数据实体（OHLCV）
    /// 用于存储股票每日的开盘价、最高价、最低价、收盘价和成交量。
    /// </summary>
    [Table("StockDailyKline", Schema = "market")]
    public class StockDailyKline
    {
        /// <summary>
        /// 数据主键，自增 ID。
        /// </summary>
        [Key]
        public long Id { get; set; }

        /// <summary>
        /// 股票代码。
        /// </summary>
        [MaxLength(10)]
        public string StockCode { get; set; } = string.Empty;

        /// <summary>
        /// 交易日期，不包含时间。
        /// </summary>
        public DateOnly TradeDate { get; set; }

        /// <summary>
        /// 开盘价（Open），单位：元/股。
        /// </summary>
        [Column(TypeName = "decimal(18,4)")]
        public decimal OpenPrice { get; set; }

        /// <summary>
        /// 最高价（High），单位：元/股。
        /// </summary>
        [Column(TypeName = "decimal(18,4)")]
        public decimal HighPrice { get; set; }

        /// <summary>
        /// 最低价（Low），单位：元/股。
        /// </summary>
        [Column(TypeName = "decimal(18,4)")]
        public decimal LowPrice { get; set; }

        /// <summary>
        /// 收盘价（Close），单位：元/股。
        /// </summary>
        [Column(TypeName = "decimal(18,4)")]
        public decimal ClosePrice { get; set; }

        /// <summary>
        /// 当日成交量（Volume）。
        /// 东方财富日 K 线接口单位为手，1 手 = 100 股。
        /// </summary>
        public long Volume { get; set; }

        /// <summary>
        /// 价格复权类型，对应东方财富接口的 fqt 参数。
        /// 0 = 不复权（实际历史交易价格）
        /// 1 = 前复权（以近期价格为基准调整历史价格）
        /// 2 = 后复权（以早期价格为基准调整后续价格）
        /// 默认使用前复权。
        /// </summary>
        public byte AdjustType { get; set; } = 1;

        /// <summary>
        /// 行情数据来源，例如 EastMoney、TuShare。
        /// 用于区分不同数据提供商的数据。
        /// </summary>
        [MaxLength(30)]
        public string DataSource { get; set; } = "EastMoney";

        /// <summary>
        /// 数据最后更新时间（UTC）。
        /// 新建对象时初始化，更新已有记录时需主动刷新。
        /// </summary>
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
