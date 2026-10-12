using Sandbox.Console.Factory;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Sandbox.Console;

var environmentName = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");
if (string.IsNullOrWhiteSpace(environmentName))
    environmentName = "Production";

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .AddJsonFile($"appsettings.{environmentName}.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

using var configurationLifetime = configuration as IDisposable;

var services = new ServiceCollection();
services.AddSingleton<IConfiguration>(configuration);
services.AddSandbox(configuration);
await using var provider = services.BuildServiceProvider(new ServiceProviderOptions
{
    ValidateScopes = true,
    ValidateOnBuild = true
});

bool flag = true;
while (flag)
{
    // 每次任务单独创建作用域，执行完毕后异步释放 Worker 及其 DbContext 等依赖。
    await using (var scope = provider.CreateAsyncScope())
    {
        var factory = scope.ServiceProvider.GetRequiredService<WorkerFactory>();
        var workType = WorkerMenu.Select(factory);
        if (workType.HasValue)
            await factory.Workers[workType.Value].CreateWorker().RunAsync();
        else
            flag = false;
    }

    if (flag)
    {
        Console.WriteLine("\n按任意键继续...");
        Console.ReadLine();
    }
}
