using SnackCounter;

namespace SnackCounter.ChatSim;

// The chat tool, simulated in the terminal: instead of calling Slack/Teams, it prints the bot's messages.
// This is the only part a real integration would replace (with e.g. SlackChat : IChat).
public class ConsoleChat : IChat
{
    public void SendTo(string employeeId, string message)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("  [bot -> " + employeeId + "] " + message);
        Console.ResetColor();
    }

    public void SendToOfficeContact(Office office, string message)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("  [bot -> " + office + " contact] " + message);
        Console.ResetColor();
    }
}
