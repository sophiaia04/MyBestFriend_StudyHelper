using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudyHelper.Models;
using StudyHelper.Services;

namespace StudyHelper.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private const int HistoryTabIndex = 3;
    private const int MaxTurnsSentToGemini = 30;

    private const string FormattingRules = """
                                           FORMATTING RULES (STRICT):
                                           - Do NOT use any Markdown syntax such as #, ##, ###, **, *, or backticks (`).
                                           - Use UPPERCASE titles for section headings.
                                           - Use clean bullet points (•) and line breaks for readability.
                                           - Answer in the language the student writes in.
                                           """;

    private readonly GeminiService _geminiService = new();
    private readonly AppStorage.Settings _settings;
    private CancellationTokenSource? _popupCts;

    public MainWindowViewModel()
    {
        SummaryChat = new ChatSession(ChatKind.Summary, "Summary", "#2563EB",
            "Ask about the summary (e.g. 'Make it shorter' or 'Only chapter 2')…",
            "Click \"Summarize\" on the left, or ask a question here.",
            SendAsync);
        ExplanationChat = new ChatSession(ChatKind.Explanation, "Explanation", "#0D9488",
            "Ask what you didn't understand…",
            "Click \"Explain\" on the left, or ask what you want explained.",
            SendAsync);
        QuizChat = new ChatSession(ChatKind.Quiz, "Quiz", "#7C3AED",
            "Ask for a quiz (e.g. '10 hard questions about chapter 3')…",
            "Click \"Make Quiz\" on the left, or describe the quiz you want.",
            SendAsync);

        Chats = [SummaryChat, ExplanationChat, QuizChat];
        foreach (var chat in Chats)
        {
            chat.Cleared += _ => { RebuildHistory(); SaveChats(); };
        }

        SelectedFiles.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasNoFiles));

        _settings = AppStorage.LoadSettings();
        var envKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
        _geminiService.ApiKey = !string.IsNullOrWhiteSpace(envKey) ? envKey : _settings.ApiKey;
        _apiKeyInput = _settings.ApiKey ?? string.Empty;
        UpdateApiKeyStatus();

        LoadChats();
        RebuildHistory();
    }

    // ---------------- Chats (one per tab, independent) ----------------

    public ChatSession SummaryChat { get; }
    public ChatSession ExplanationChat { get; }
    public ChatSession QuizChat { get; }
    public IReadOnlyList<ChatSession> Chats { get; }

    [ObservableProperty]
    private int _selectedTabIndex;

    // ---------------- History tab ----------------

    public ObservableCollection<HistoryEntry> History { get; } = [];
    public bool HasNoHistory => History.Count == 0;

    [RelayCommand]
    private void OpenHistoryEntry(HistoryEntry? entry)
    {
        if (entry is null) return;
        SelectedTabIndex = Chats.ToList().IndexOf(entry.Session);
    }

    private void RebuildHistory()
    {
        History.Clear();
        var entries = Chats
            .SelectMany(chat => chat.Messages
                .Where(m => m.IsUser)
                .Select(m => new HistoryEntry { Session = chat, Message = m }))
            .OrderByDescending(e => e.Message.Timestamp);
        foreach (var entry in entries)
        {
            History.Add(entry);
        }
        OnPropertyChanged(nameof(HasNoHistory));
    }

    // ---------------- Files ----------------

    public ObservableCollection<StudyDocument> SelectedFiles { get; } = [];
    public bool HasNoFiles => SelectedFiles.Count == 0;

    [RelayCommand]
    private void RemoveFile(StudyDocument? doc)
    {
        if (doc is not null) SelectedFiles.Remove(doc);
    }

    [RelayCommand]
    private async Task UploadFileAsync(TopLevel? topLevel)
    {
        if (topLevel is null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Study Documents",
            AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType("Study documents") { Patterns = ["*.pdf", "*.docx", "*.txt", "*.md"] },
                FilePickerFileTypes.All
            ]
        });

        if (files.Count == 0) return;

        var problems = new List<string>();

        foreach (var file in files)
        {
            var extension = Path.GetExtension(file.Name).ToLowerInvariant();
            if (!DocumentReader.AllowedExtensions.Contains(extension))
            {
                problems.Add($"{file.Name} (unsupported type)");
                continue;
            }

            if (SelectedFiles.Any(f => f.FileName == file.Name))
                continue;

            try
            {
                var text = await DocumentReader.ReadTextAsync(file, extension);
                if (string.IsNullOrWhiteSpace(text))
                {
                    problems.Add($"{file.Name} (no text found, maybe a scanned PDF)");
                    continue;
                }

                SelectedFiles.Add(new StudyDocument { FileName = file.Name, Content = text });
            }
            catch (Exception ex)
            {
                problems.Add($"{file.Name} ({ex.Message})");
            }
        }

        if (problems.Count > 0)
        {
            ShowPopup($"Skipped: {string.Join(", ", problems)}");
        }
    }

    private string GetCombinedDocumentContent()
    {
        var sb = new StringBuilder();
        foreach (var doc in SelectedFiles)
        {
            sb.AppendLine($"--- FILE: {doc.FileName} ---");
            sb.AppendLine(doc.Content);
            sb.AppendLine();
        }
        return sb.ToString();
    }

    // ---------------- Sidebar buttons ----------------

    [RelayCommand]
    private Task SummarizeAsync() =>
        StartFromSidebarAsync(SummaryChat, 0, "Summarize my uploaded documents.");

    [RelayCommand]
    private Task ExplainAsync() =>
        StartFromSidebarAsync(ExplanationChat, 1, "Explain the content of my uploaded documents in detail.");

    [RelayCommand]
    private Task MakeQuizAsync() =>
        StartFromSidebarAsync(QuizChat, 2, "Make a quiz with 5 multiple-choice questions about my documents.");

    private Task StartFromSidebarAsync(ChatSession chat, int tabIndex, string text)
    {
        SelectedTabIndex = tabIndex;
        if (HasNoFiles)
        {
            ShowPopup("Please upload at least one document first.");
            return Task.CompletedTask;
        }
        return chat.SendTextAsync(text);
    }

    // ---------------- Sending a message (only affects ONE chat) ----------------

    private async Task SendAsync(ChatSession chat, string text)
    {
        if (string.IsNullOrWhiteSpace(text) || chat.IsBusy) return;

        var userMessage = new ChatMessage { Role = ChatRole.User, Text = text };
        chat.Messages.Add(userMessage);
        RebuildHistory();

        // Conversation of THIS chat only (no loading/error bubbles).
        var turns = chat.Messages
            .Where(m => !m.IsLoading && !m.IsError && (m.IsUser || !string.IsNullOrWhiteSpace(m.ModelText)))
            .TakeLast(MaxTurnsSentToGemini)
            .Select(m => new ChatTurn(m.IsUser, m.ModelText))
            .ToList();

        var answer = new ChatMessage { Role = ChatRole.Assistant, IsLoading = true, Text = "Thinking…" };
        chat.Messages.Add(answer);
        chat.IsBusy = true;

        try
        {
            var isQuiz = chat.Kind == ChatKind.Quiz;
            var result = await _geminiService.ChatAsync(BuildSystemInstruction(chat.Kind), turns, isQuiz);

            if (!result.Success)
            {
                answer.IsError = true;
                answer.Text = result.Text;
            }
            else if (isQuiz)
            {
                FillQuizAnswer(answer, result.Text);
            }
            else
            {
                answer.Text = result.Text;
            }
        }
        catch (Exception ex)
        {
            answer.IsError = true;
            answer.Text = $"Something went wrong: {ex.Message}";
        }
        finally
        {
            answer.IsLoading = false;
            chat.IsBusy = false;
            SaveChats();
        }
    }

    private string BuildSystemInstruction(ChatKind kind)
    {
        var role = kind switch
        {
            ChatKind.Summary => """
                                You are a friendly study buddy. In this chat you write and refine SUMMARIES of the student's study material:
                                clear structure, headings, key takeaways and bullet points.
                                When the student asks for changes (shorter, longer, focus on a topic, ...), rewrite the summary accordingly.
                                When the student asks a question, answer it based on the study material.
                                """,
            ChatKind.Explanation => """
                                    You are a patient tutor. In this chat you EXPLAIN the study material for someone who finds it hard to understand.
                                    Go through the material page by page in detail, including comments and diagrams.
                                    Explain core concepts from first principles with intuitive examples and analogies.
                                    Answer follow-up questions in the same easy-to-understand way.
                                    """,
            _ => """
                 You are a quiz master. In this chat you create multiple-choice practice quizzes about the study material.
                 Return ONLY a JSON object with exactly this schema (no markdown, no extra text):
                 {
                   "message": "Short friendly text for the student (plain text, no markdown).",
                   "questions": [
                     {
                       "question": "Question text?",
                       "options": ["Option A", "Option B", "Option C", "Option D"],
                       "correctIndex": 0,
                       "explanation": "Brief explanation of why the answer is correct."
                     }
                   ]
                 }
                 If the student asks for a quiz, fill "questions" (default 5 questions unless the student asks for another number).
                 If the student only asks a question (e.g. about a previous quiz answer), answer it in "message" and return "questions": [].
                 """
        };

        var material = HasNoFiles
            ? "No study documents are uploaded yet. Help with general study questions and remind the student they can upload files on the left."
            : GetCombinedDocumentContent();

        return $"""
                {role}

                {FormattingRules}

                --- STUDY MATERIAL ---
                {material}
                """;
    }

    // ---------------- Quiz parsing ----------------

    private static void FillQuizAnswer(ChatMessage answer, string rawJson)
    {
        answer.RawQuizJson = rawJson;
        try
        {
            var (message, questions) = ParseQuiz(rawJson, answer.Id);
            foreach (var q in questions) answer.QuizQuestions.Add(q);

            answer.Text = !string.IsNullOrWhiteSpace(message)
                ? message
                : questions.Count > 0 ? $"Here is your quiz with {questions.Count} questions:" : "No questions were generated.";
        }
        catch (Exception ex)
        {
            answer.IsError = true;
            answer.Text = $"Could not read the quiz from Gemini ({ex.Message}). Please ask again.";
        }
    }

    private static (string Message, List<QuizQuestion> Questions) ParseQuiz(string rawJson, string groupPrefix)
    {
        var json = rawJson.Trim();
        if (json.StartsWith("```"))
        {
            json = json.Trim('`').Trim();
            if (json.StartsWith("json", StringComparison.OrdinalIgnoreCase)) json = json[4..];
        }

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var message = string.Empty;
        JsonElement questionsElement;
        if (root.ValueKind == JsonValueKind.Array)
        {
            questionsElement = root; // older format: plain array
        }
        else
        {
            if (root.TryGetProperty("message", out var m)) message = m.GetString() ?? string.Empty;
            if (!root.TryGetProperty("questions", out questionsElement)) return (message, []);
        }

        var questions = new List<QuizQuestion>();
        var number = 1;
        foreach (var item in questionsElement.EnumerateArray())
        {
            var options = item.GetProperty("options").EnumerateArray()
                .Select((opt, idx) => new QuizOption
                {
                    Index = idx,
                    Text = opt.GetString() ?? string.Empty,
                    GroupName = $"{groupPrefix}-Q{number}"
                })
                .ToList();

            questions.Add(new QuizQuestion
            {
                Number = number,
                Question = item.GetProperty("question").GetString() ?? string.Empty,
                Options = options,
                CorrectIndex = item.TryGetProperty("correctIndex", out var ci) ? ci.GetInt32() : 0,
                Explanation = item.TryGetProperty("explanation", out var ex) ? ex.GetString() ?? string.Empty : string.Empty
            });
            number++;
        }

        return (message, questions);
    }

    // ---------------- Saving / loading chats ----------------

    private void SaveChats()
    {
        var data = Chats.ToDictionary(
            c => c.Kind.ToString(),
            c => c.Messages
                .Where(m => !m.IsLoading)
                .Select(m => new AppStorage.StoredMessage
                {
                    IsUser = m.IsUser,
                    Text = m.Text,
                    RawQuizJson = m.RawQuizJson,
                    IsError = m.IsError,
                    Timestamp = m.Timestamp
                })
                .ToList());
        AppStorage.SaveChats(data);
    }

    private void LoadChats()
    {
        var data = AppStorage.LoadChats();
        foreach (var chat in Chats)
        {
            if (!data.TryGetValue(chat.Kind.ToString(), out var messages)) continue;

            foreach (var stored in messages)
            {
                var message = new ChatMessage
                {
                    Role = stored.IsUser ? ChatRole.User : ChatRole.Assistant,
                    Timestamp = stored.Timestamp,
                    IsError = stored.IsError
                };

                if (stored.RawQuizJson is not null)
                    FillQuizAnswer(message, stored.RawQuizJson);
                else
                    message.Text = stored.Text;

                chat.Messages.Add(message);
            }
        }
    }

    // ---------------- API key ----------------

    [ObservableProperty]
    private string _apiKeyInput;

    [ObservableProperty]
    private string _apiKeyStatus = string.Empty;

    [ObservableProperty]
    private bool _hasApiKey;

    [RelayCommand]
    private void SaveApiKey()
    {
        var key = ApiKeyInput.Trim();
        _settings.ApiKey = string.IsNullOrEmpty(key) ? null : key;
        AppStorage.SaveSettings(_settings);

        var envKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
        _geminiService.ApiKey = !string.IsNullOrWhiteSpace(envKey) ? envKey : _settings.ApiKey;
        UpdateApiKeyStatus();
        ShowPopup(string.IsNullOrEmpty(key) ? "API key removed." : "API key saved.");
    }

    private void UpdateApiKeyStatus()
    {
        HasApiKey = !string.IsNullOrWhiteSpace(_geminiService.ApiKey);
        ApiKeyStatus = HasApiKey ? "✓ Key is set" : "No key yet – paste it and click Save";
    }

    // ---------------- Popup ----------------

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isStatusPopupOpen;

    private async void ShowPopup(string message)
    {
        _popupCts?.Cancel();
        _popupCts = new CancellationTokenSource();
        var token = _popupCts.Token;

        StatusMessage = message;
        IsStatusPopupOpen = true;
        try
        {
            await Task.Delay(3000, token);
            IsStatusPopupOpen = false;
        }
        catch (TaskCanceledException)
        {
        }
    }
}
