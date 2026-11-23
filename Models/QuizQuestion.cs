using System.Collections.Generic;
// GroupName must be unique per quiz message, otherwise radio buttons of different quizzes interfere.
using CommunityToolkit.Mvvm.ComponentModel;

namespace StudyHelper.Models;

public partial class QuizOption : ObservableObject
{
    public int Index { get; set; }
    public string Text { get; set; } = string.Empty;
    public string GroupName { get; set; } = string.Empty;

    [ObservableProperty]
    private bool _isSelected;
}

public partial class QuizQuestion : ObservableObject
{
    public int Number { get; set; }
    public string Question { get; set; } = string.Empty;
    public List<QuizOption> Options { get; set; } = [];
    public int CorrectIndex { get; set; }
    public string Explanation { get; set; } = string.Empty;

    [ObservableProperty]
    private string _feedbackText = string.Empty;

    [ObservableProperty]
    private string _feedbackColor = "#64748B";

    [ObservableProperty]
    private bool _hasFeedback;
}