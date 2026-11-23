using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace StudyHelper.Models;

public enum ChatRole
{
    User,
    Assistant
}

public partial class ChatMessage : ObservableObject
{
    public ChatMessage()
    {
        QuizQuestions.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasQuiz));
    }

    /// <summary>Unique id, used to keep radio-button groups of different quizzes apart.</summary>
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    public ChatRole Role { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.Now;

    public bool IsUser => Role == ChatRole.User;
    public bool IsAssistant => Role == ChatRole.Assistant;
    public string TimeLabel => Timestamp.ToString("HH:mm");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasText))]
    private string _text = string.Empty;

    public bool HasText => !string.IsNullOrWhiteSpace(Text);

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isError;

    /// <summary>Raw JSON returned by Gemini for quiz answers (sent back as context, and persisted).</summary>
    public string? RawQuizJson { get; set; }

    public ObservableCollection<QuizQuestion> QuizQuestions { get; } = [];
    public bool HasQuiz => QuizQuestions.Count > 0;

    [ObservableProperty]
    private string _quizScore = string.Empty;

    /// <summary>What gets sent to Gemini as this message's content in the conversation history.</summary>
    public string ModelText => RawQuizJson ?? Text;

    [RelayCommand]
    private void CheckAnswers()
    {
        if (QuizQuestions.Count == 0) return;

        var correctCount = 0;
        foreach (var q in QuizQuestions)
        {
            var selected = q.Options.FirstOrDefault(o => o.IsSelected);
            var correctText = q.CorrectIndex >= 0 && q.CorrectIndex < q.Options.Count
                ? q.Options[q.CorrectIndex].Text
                : "?";
            q.HasFeedback = true;

            if (selected is null)
            {
                q.FeedbackColor = "#B45309";
                q.FeedbackText = $"No answer selected. Correct: {correctText} — {q.Explanation}";
            }
            else if (selected.Index == q.CorrectIndex)
            {
                correctCount++;
                q.FeedbackColor = "#15803D";
                q.FeedbackText = $"Correct! {q.Explanation}";
            }
            else
            {
                q.FeedbackColor = "#B91C1C";
                q.FeedbackText = $"Incorrect. Correct: {correctText} — {q.Explanation}";
            }
        }

        QuizScore = $"Score: {correctCount} / {QuizQuestions.Count} correct";
    }
}
