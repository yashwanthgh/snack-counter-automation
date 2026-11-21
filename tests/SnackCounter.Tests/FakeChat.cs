using SnackCounter;

namespace SnackCounter.Tests;

// Fake chat: sends nothing, just remembers every message so a test can check "was Anna told X?".
// Messages to an office contact are stored with To = "contact:Oslo" / "contact:Bengaluru".
public class FakeChat : IChat
{
    public List<ChatMessage> Sent = new List<ChatMessage>();

    public void SendTo(string employeeId, string message)
    {
        Sent.Add(new ChatMessage(employeeId, message));
    }

    public void SendToOfficeContact(Office office, string message)
    {
        Sent.Add(new ChatMessage("contact:" + office, message));
    }

    public bool WasTold(string to, string text)
    {
        foreach (ChatMessage m in Sent)
        {
            if (m.To == to && m.Text.Contains(text)) return true;
        }
        return false;
    }
}
