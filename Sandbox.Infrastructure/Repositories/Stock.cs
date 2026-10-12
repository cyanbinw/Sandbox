using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Sandbox.Infrastructure.Repositories
{
    /// <summary>
    /// 股票基础信息，用于选股时展示已导入的股票。
    /// </summary>
    [Table("Stock", Schema = "market")]
    [Index(nameof(StockCode), IsUnique = true)]
    public class Stock
    {
        /// <summary>
        /// 主键，自增 ID。
        /// </summary>
        [Key]
        public long Id { get; set; }

        /// <summary>
        /// 股票代码，唯一。
        /// </summary>
        [MaxLength(10)]
        public string StockCode { get; set; } = string.Empty;

        /// <summary>
        /// 股票名称，可为空（导入时从行情接口获取）。
        /// </summary>
        [MaxLength(50)]
        public string StockName { get; set; } = string.Empty;

        /// <summary>
        /// 数据最后更新时间（UTC）。
        /// </summary>
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
