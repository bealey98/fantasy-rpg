using Battle.Core;

namespace Battle.Sim;

// The command-line simulator. Commands are added as the build steps land:
//   bench    measure decisions per second (step 8)
public static class Program
{
    public static int Main(string[] args)
    {
        string command = args.Length > 0 ? args[0] : "help";
        switch (command)
        {
            case "bench":
                Console.WriteLine($"rules {RulesVersion.Current}: no battles to benchmark yet (the benchmark arrives in step 8)");
                return 0;
            default:
                Console.WriteLine("usage: battle-sim <command> [options]");
                Console.WriteLine("  bench    measure decisions per second");
                return command == "help" ? 0 : 1;
        }
    }
}
