using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace StudyHelper.Services;

/// <summary>
/// Stores the API key and the chat history in the user's app-data folder
/// (NOT in the project folder, so the key never ends up in git).
/// </summary>
public static class AppStorage
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string Folder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StudyHelper");

    private static string SettingsPath => Path.Combine(Folder, "settings.json");
    private static string ChatsPath => Path.Combine(Folder, "chats.json");

    // ---------- Settings ----------

    public sealed class Settings
    {
        public string? ApiKey { get; set; }
    }

    public static Settings LoadSettings() => Load<Settings>(SettingsPath) ?? new Settings();

    public static void SaveSettings(Settings settings) => Save(SettingsPath, settings);

    // ---------- Chats ----------

    public sealed class StoredMessage
    {
        public bool IsUser { get; set; }
        public string Text { get; set; } = string.Empty;
        public string? RawQuizJson { get; set; }
        public bool IsError { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>Key = chat kind (Summary / Explanation / Quiz).</summary>
    public static Dictionary<string, List<StoredMessage>> LoadChats() =>
        Load<Dictionary<string, List<StoredMessage>>>(ChatsPath) ?? new();

    public static void SaveChats(Dictionary<string, List<StoredMessage>> chats) => Save(ChatsPath, chats);

    // ---------- Helpers ----------

    private static T? Load<T>(string path)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path)) : default;
        }
        catch (Exception)
        {
            return default; // corrupt file -> start fresh instead of crashing
        }
    }

    private static void Save<T>(string path, T value)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(path, JsonSerializer.Serialize(value, JsonOptions));
        }
        catch (Exception)
        {
            // Saving is best effort; the app keeps working without it.
        }
    }
}
