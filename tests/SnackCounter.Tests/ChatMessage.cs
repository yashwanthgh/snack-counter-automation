using SnackCounter;

namespace SnackCounter.Tests;

// One message the fake chat "sent".
public class ChatMessage
{
    public string To;
    public string Text;

    public ChatMessage(string to, string text)
    {
        To = to;
        Text = text;
    }
}
