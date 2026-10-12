using Sandbox.Infrastructure.ConsoleInfrastructure;

namespace Sandbox.Console.Factory
{
    public static class WorkerMenu
    {
        public static WorkType? Select(WorkerFactory factory)
        {
            System.Console.WriteLine("HelperConsole 辅助工具");
            System.Console.WriteLine("选择一个任务执行，完成后程序退出。需要的名称或路径会在选择后提示输入。");
            System.Console.WriteLine("参数后面的 [内容] 是默认值，直接回车即可使用。输入 0 或 q 退出。");

            string? currentGroup = null;
            foreach (var type in factory.Workers.Keys.OrderBy(type => (int)type))
            {
                var item = factory.Workers[type];
                if (currentGroup != item.Group)
                {
                    currentGroup = item.Group;
                    System.Console.WriteLine($"\n【{currentGroup}】");
                }
                System.Console.WriteLine($"  {(int)type,2}. {item.Name} — {item.Description}");
            }

            while (true)
            {
                System.Console.Write("\n请输入任务编号（0 退出）: ");
                var input = System.Console.ReadLine()?.Trim();
                if (input == null || input == "0" || string.Equals(input, "q", StringComparison.OrdinalIgnoreCase))
                    return null;
                if (int.TryParse(input, out var number) && factory.Workers.ContainsKey((WorkType)number))
                    return (WorkType)number;
                System.Console.WriteLine("编号无效，请输入菜单中的任务编号。");
            }
        }
    }
}
