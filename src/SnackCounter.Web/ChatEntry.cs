namespace SnackCounter.Web;

// One line in the chat, as the web pages show it.
// Kind: "user" (a person typed it), "bot" (the snack bot wrote it), "system" (the simulator answered a command).
// To: an employee id, "contact:Oslo" for an office contact, or the sender for "system" replies.
public class ChatEntry
{
    public int Id;
    public string Time;
    public string Kind;
    public string From;
    public string To;
    public string Text;

    public ChatEntry(int id, string time, string kind, string from, string to, string text)
    {
        Id = id;
        Time = time;
        Kind = kind;
        From = from;
        To = to;
        Text = text;
    }
}
