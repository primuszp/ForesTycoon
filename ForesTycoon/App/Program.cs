using System;

namespace ForesTycoon
{
    static class Program
    {
        static void Main(string[] args)
        {
            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
                Console.Error.WriteLine(args.ExceptionObject?.ToString() ?? "Unknown fatal error");

            bool smokeTest = Array.Exists(args, argument =>
                string.Equals(argument, "--smoke-test", StringComparison.OrdinalIgnoreCase));
            using Viewport game = new Viewport(smokeTest ? 120UL : null);
            game.Run();
        }
    }
}
