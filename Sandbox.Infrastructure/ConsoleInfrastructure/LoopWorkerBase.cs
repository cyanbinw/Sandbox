namespace Sandbox.Infrastructure.ConsoleInfrastructure
{
    /// <summary>
    /// 支持连续执行的工具。子类负责单轮业务和输入，返回 Exit 时结束工具执行。
    /// 异常向调用方传播，不自动重试，以免重复执行有副作用的操作。
    /// </summary>
    public abstract class LoopWorkerBase : IWorker
    {
        public void Run() => RunAsync().GetAwaiter().GetResult();

        public virtual async Task RunAsync()
        {
            bool flag = true;
            while (flag)
            {
                var result = await RunOnceAsync();

                if (result == WorkerLoopResult.Exit)
                {
                    flag = false;
                }

                Console.WriteLine("本轮结束, 按任意键继续...");
                Console.ReadLine();
            }
        }

        protected abstract Task<WorkerLoopResult> RunOnceAsync();
    }
}
