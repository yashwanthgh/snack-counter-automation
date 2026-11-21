using SnackCounter;

namespace SnackCounter.Web;

// The chat tool for the web pages: instead of printing, it remembers each bot message
// and who it was for, so each page can show everything or only one person's messages.
public class WebChat : IChat
{
    private SimulatorHost _host;

    public WebChat(SimulatorHost host)
    {
        _host = host;
    }

    public void SendTo(string employeeId, string message)
    {
        _host.AddEntry("bot", "bot", employeeId, message);
    }

    public void SendToOfficeContact(Office office, string message)
    {
        _host.AddEntry("bot", "bot", "contact:" + office, message);
    }
}
