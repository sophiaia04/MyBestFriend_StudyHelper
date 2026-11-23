namespace StudyHelper.Models;

/// <summary>One of your questions, shown in the History tab.</summary>
public class HistoryEntry
{
    public required ChatSession Session { get; init; }
    public required ChatMessage Message { get; init; }

    public string TabName => Session.Title;
    public string AccentColor => Session.AccentColor;
    public string Text => Message.Text;
    public string TimeLabel => Message.Timestamp.ToString("dd.MM. HH:mm");
}
