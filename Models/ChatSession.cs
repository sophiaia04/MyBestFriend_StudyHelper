using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace StudyHelper.Models;

public enum ChatKind
{
    Summary,
    Explanation,
    Quiz
}

/// <summary>
/// One independent chat (one per tab). Each has its own messages, input text and busy state,
/// so writing in one tab never affects another.
/// </summary>
public partial class ChatSession : ObservableObject
{
    private readonly Func<ChatSession, string, Task> _sendHandler;

    public ChatSession(ChatKind kind, string title, string accentColor, string placeholder, string emptyHint,
        Func<ChatSession, string, Task> sendHandler)
    {
        Kind = kind;
        Title = title;
        AccentColor = accentColor;
        Placeholder = placeholder;
        EmptyHint = emptyHint;
        _sendHandler = sendHandler;
        Messages.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(IsEmpty));
            ClearCommand.NotifyCanExecuteChanged();
        };
    }

    public ChatKind Kind { get; }
    public string Title { get; }
    public string AccentColor { get; }
    public string Placeholder { get; }
    public string EmptyHint { get; }

    public ObservableCollection<ChatMessage> Messages { get; } = [];
    public bool IsEmpty => Messages.Count == 0;

    /// <summary>Raised after the chat was cleared, so the history list and saved file can update.</summary>
    public event Action<ChatSession>? Cleared;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private string _input = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearCommand))]
    private bool _isBusy;

    private bool CanSend() => !IsBusy && !string.IsNullOrWhiteSpace(Input);

    [RelayCommand(CanExecute = nameof(CanSend))]
    private Task SendAsync()
    {
        var text = Input.Trim();
        Input = string.Empty;
        return _sendHandler(this, text);
    }

    /// <summary>Sends a message programmatically (used by the sidebar buttons).</summary>
    public Task SendTextAsync(string text) => IsBusy ? Task.CompletedTask : _sendHandler(this, text);

    private bool CanClear() => !IsBusy && Messages.Count > 0;

    [RelayCommand(CanExecute = nameof(CanClear))]
    private void Clear()
    {
        Messages.Clear();
        Cleared?.Invoke(this);
    }
}
