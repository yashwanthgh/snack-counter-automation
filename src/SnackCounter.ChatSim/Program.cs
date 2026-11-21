using SnackCounter;
using SnackCounter.HrSim;

namespace SnackCounter.ChatSim;

// Chat simulator: type messages as different people and watch the bot react.
// Run:  dotnet run --project src/SnackCounter.ChatSim
// Optional argument: the config folder (default "config").
public class Program
{
    public static void Main(string[] args)
    {
        string configFolder = args.Length > 0 ? args[0] : "config";

        // Nothing is hard-coded: budgets, timers, rates and people all come from files.
        string settingsPath = Path.Combine(configFolder, "settings.json");
        Settings settings;
        List<Employee> people;
        try
        {
            settings = SettingsLoader.LoadFromFile(settingsPath);
            people = PeopleLoader.LoadFromFile(Path.Combine(configFolder, "people.json"));
        }
        catch (Exception e)
        {
            Console.WriteLine("Could not load config: " + e.Message);
            return;
        }

        // Simulated time starts Monday 2 Nov 2026, 09:00 UTC.
        SimClock clock = new SimClock(new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.Zero));
        ConsoleChat chat = new ConsoleChat();
        FakeHr hr = new FakeHr();
        BudgetService budget = new BudgetService(settings.Budgets);
        ReportService report = new ReportService(budget);

        RefillService refills = new RefillService(people, chat, clock, hr, budget, settings.Timers);
        CommandHandler handler = new CommandHandler(people, refills, budget, report, hr, clock, settings, settingsPath);

        Console.WriteLine("Snack counter chat simulator. Type 'help' for commands.");
        bool echo = Console.IsInputRedirected; // when reading a script, print each line so the output reads like a chat

        while (true)
        {
            if (!echo) Console.Write("> ");
            string? line = Console.ReadLine();
            if (line == null) break;
            if (line.StartsWith("#")) continue; // comments in demo scripts
            if (echo && line.Trim() != "") Console.WriteLine("> " + line);
            if (!handler.Handle(line)) break;
        }
    }
}
