# Sandbox

基于 .NET 10 的金融计算控制台工具集，包含回撤计算、蒙特卡罗模拟、股票数据导入，以及 EF Core SQL Server 数据访问基础设施。

## 快速开始

安装 .NET 10 SDK，在仓库根目录执行：

```powershell
dotnet restore Sandbox.slnx
dotnet build Sandbox.slnx
dotnet run --project Sandbox.Console
```

在菜单中输入任务编号。任务执行完后按回车返回菜单，输入 `0` 或 `q` 退出。回撤计算不需要数据库或网络；蒙特卡罗模拟优先读取数据库、数据不足时访问东方财富行情接口；股票数据导入需要数据库并访问东方财富行情接口。

## 当前工具

| 编号 | 工具 | 说明 |
| --- | --- | --- |
| 1 | 蒙特卡罗模拟 | 对历史日收益率进行随机抽样，估算盈利概率、目标收益概率、触及止损概率和收益分布 |
| 2 | 回撤计算 | 输入上涨起点与最高价，输出四个常用比例对应的回踩价格 |
| 3 | 股票数据导入 | 输入股票代码，从东方财富导入历史日 K 线数据到数据库 |

### 回撤计算

上涨行情使用公式：

```text
P = H - (H - L) × R
```

`H` 为本轮上涨最高价，`L` 为上涨起点，`R` 为回撤比例，`P` 为计算价格。当前菜单固定输出 23.6%、38.2%、50%、61.8%，使用 `decimal` 计算，要求 `H > L`。

例如输入 `L = 11.44`、`H = 11.72`：

```text
回撤比例    预计价格    位置
23.6%       11.65392    回踩价位
38.2%       11.61304    回踩价位
50%         11.58       回踩价位
61.8%       11.54696    回踩价位
```

公式中 `R > 1` 可表示跌破起点，但当前菜单尚未提供自定义比例输入。

### 蒙特卡罗模拟

运行时先列出数据库 `Stocks` 表中已导入的股票供选择（可输入 `0` 手动输入代码，或 `q` 退出）；随后依次输入买入价（默认 10.6）、持有天数（默认 10）、模拟次数（默认 100,000）、目标收益率%（默认 20）、止损幅度%（默认 10，输入 10 或 -10 均表示亏损 10%）。任意一步输入 `q` 可退出，直接回车使用方括号内的默认值。

随后可选择持有到期或执行止损模式，并设置随机种子（默认 42）。执行止损时按首次触发日的模拟收盘价退出，可能低于止损线；不模拟盘中触及或实际成交限制。持有到期模式只记录是否触及止损线。两种模式均按本金扣除 0.1% 往返成本。买入价只决定价格尺度，不改变收益率概率。目标收益以扣费后的退出/到期收益判断，止损以每日收盘的未扣费收益判断。

历史行情限定东方财富前复权，数据库端只取最近 250 条，再恢复日期升序。当整个窗口不足、不是当天统一刷新或存在重复日期时，从接口重新获取完整窗口，本次回退数据不自动入库。输出包含样本起止日期；为避免使用未完成的日线，始终排除运行当天。导入功能可刷新数据库缓存。

持有期为 1～2520 个交易日，模拟次数为 1～1,000,000，且两者乘积不超过 100,000,000。输入拒绝非有限数和越界值，止损幅度可输入正数、负数或 0，绝对值必须小于 100%。输入统一取负绝对值：10 和 -10 均对应 -10% 止损线，0 对应保本线；每日收盘收益率小于或等于该线即触发。计算仍采用独立日收益率抽样，未加入连续区间抽样或滚动回测。

离线算法验证（无需连接数据库或行情服务）：

```powershell
dotnet run --project tests/SimulationChecks/SimulationChecks.csproj
```

### 股票数据导入

输入 6 位股票代码后，从东方财富拉取历史日 K 线（OHLCV，前复权）及股票名称，按「股票代码 + 复权类型 + 数据来源 + 交易日期」新增或刷新数据库 `StockDailyKlines`（`market.StockDailyKline`）；同时更新 `Stocks`（`market.Stock`）表。返回区间内已存在的行情也会刷新，并记录相同批次更新时间，避免不同版本的前复权价格拼接。默认导入范围 `2020-01-01` 至当天，可通过 `StockHistoryService.GetKlineDataAsync` 的 `begin`/`end` 参数调整。

## 项目结构

```text
Sandbox.Console/          控制台入口、菜单、工具工厂、依赖注入与配置
Sandbox.Application/      工具实现及应用服务
Sandbox.Domain/           历史行情获取、蒙特卡罗计算等逻辑
Sandbox.Infrastructure/   IWorker、WorkType、EF 上下文与数据库实体
Sandbox.Model/            数据模型
Sandbox.slnx              解决方案
```

## 环境与配置

程序从应用输出目录读取配置，按以下顺序加载，后加载的同名配置覆盖前面的值：

1. `appsettings.json`
2. `appsettings.{环境名}.json`
3. 环境变量

环境名取自 `DOTNET_ENVIRONMENT`，未设置或为空时使用 `Production`。`Properties/launchSettings.json` 将本地启动配置设为 `Development`，因此默认的 `dotnet run` 会加载 `appsettings.Development.json`。

使用生产环境启动的 PowerShell 示例：

```powershell
$env:DOTNET_ENVIRONMENT = "Production"
dotnet run --project Sandbox.Console --no-launch-profile
```

JSON 配置会在构建和发布时复制到输出目录。当前启动代码没有加载 `appsettings.Local.json`，即使该文件已被 Git 忽略，也不会生效。

## SQL Server 与 EF Core

在对应环境的配置文件中填写连接字符串，例如使用 Windows 身份验证：

```json
{
  "ConnectionStrings": {
    "QuantResearch": "Server=localhost;Database=QuantResearch;Integrated Security=True;Encrypt=True"
  }
}
```

将服务器和数据库名替换为实际值。包含密码的连接字符串可通过环境变量提供，避免提交到仓库：

```powershell
$env:ConnectionStrings__QuantResearch = "你的 SQL Server 连接字符串"
dotnet run --project Sandbox.Console
```

环境变量中的 `__` 对应配置层级分隔符 `:`。代码通过 `configuration.GetConnectionString("QuantResearch")` 读取最终值。

`QuantResearchDbContext` 已通过 `AddDbContext` 注册，使用 SQL Server 提供程序。`StockDailyKlines` 对应 `market.StockDailyKline` 表，`Stocks` 对应 `market.Stock` 表（`StockCode` 唯一索引）。当前没有迁移文件，也没有自动建库、建表或更新数据库的启动逻辑；实际读写前需要准备相应数据库结构。

## 依赖注入与新增工具

所有服务在 `Sandbox.Console/DependencyInjection.cs` 的 `AddSandbox` 中注册。每次菜单任务创建独立作用域，执行结束后异步释放工具及其依赖。工具工厂和工具使用 `Scoped` 生命周期，可以通过构造函数注入 `QuantResearchDbContext` 或其他已注册服务。

新增工具的步骤：

1. 在 `Sandbox.Application` 下创建类（如 `SmallTools` 或 `Services` 目录），实现 `IWorker` 的 `Run()` 和 `RunAsync()`。
2. 在 `WorkType` 中新增枚举项，使用非零且不重复的编号，`0` 保留为退出操作。
3. 在 `AddSandbox` 中添加 `services.AddScoped<YourWorker>();`。
4. 在 `WorkerFactory` 构造函数中注册菜单项：

```csharp
RegisterSmallTools<YourWorker>(
    WorkType.YourWorker,
    "小工具",
    "工具名称",
    "工具说明");
```

如需数据库，在工具构造函数中接收 `QuantResearchDbContext`，由容器提供当前作用域的实例。异步数据库操作应在 `RunAsync()` 中等待完成，使上下文在任务完成后正常释放。

## 许可证

本项目使用 Apache License 2.0，详见仓库根目录的 `LICENSE` 文件。
