using System;
using System.Collections.Generic;
using System.Text;

namespace Sandbox.Infrastructure.ConsoleInfrastructure
{
    public interface IWorker
    {
        void Run();
        Task RunAsync();
    }
}
