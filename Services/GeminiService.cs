using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace StudyHelper.Services;

public sealed record ChatTurn(bool IsUser, string Text);

public sealed record GeminiResult(bool Success, string Text);

public class GeminiService
{
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromMinutes(3) };

    // Tried in order; if one is overloaded or unavailable, the next one is used.
    private static readonly string[] Models =
    [
        "gemini-3.5-flash-lite",
        "gemini-3.8-flash",
        "gemini-flash-latest"
    ];

    public string? ApiKey { get; set; }

    /// <summary>
    /// Sends a whole conversation to Gemini. The study material and the role of the chat go into the
    /// system instruction; <paramref name="turns"/> is the chat history of ONE tab only.
    /// </summary>
    public async Task<GeminiResult> ChatAsync(string systemInstruction, IReadOnlyList<ChatTurn> turns, bool jsonOutput)
    {
        var key = ApiKey?.Trim();
        if (string.IsNullOrEmpty(key))
        {
            return new GeminiResult(false,
                "No Gemini API key set. Open \"Gemini API Key\" at the bottom left, paste your key and click Save.");
        }

        var body = new Dictionary<string, object>
        {
            ["systemInstruction"] = new { parts = new[] { new { text = systemInstruction } } },
            ["contents"] = turns
                .Select(t => new { role = t.IsUser ? "user" : "model", parts = new[] { new { text = t.Text } } })
                .ToArray()
        };
        if (jsonOutput)
        {
            body["generationConfig"] = new { responseMimeType = "application/json" };
        }

        var json = JsonSerializer.Serialize(body);
        string? lastError = null;

        try
        {
            foreach (var model in Models)
            {
                var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent";

                for (var attempt = 1; attempt <= 3; attempt++)
                {
                    using var request = new HttpRequestMessage(HttpMethod.Post, url);
                    // Recommended way to pass the key (keeps it out of URLs / logs).
                    request.Headers.Add("x-goog-api-key", key);
                    request.Content = new StringContent(json, Encoding.UTF8, "application/json");

                    using var response = await HttpClient.SendAsync(request);
                    var responseString = await response.Content.ReadAsStringAsync();

                    if (response.IsSuccessStatusCode)
                    {
                        return ParseResponse(responseString);
                    }

                    var status = (int)response.StatusCode;
                    lastError = $"{model}: {(int)response.StatusCode} {ReadErrorMessage(responseString)}";

                    // 429/500/503 = overloaded. Google also sometimes answers with an EMPTY 404
                    // under load (a real "model not found" 404 has an error text) -> retry those too.
                    var transientNotFound = response.StatusCode == HttpStatusCode.NotFound &&
                                            string.IsNullOrWhiteSpace(responseString);
                    if (status is 429 or 500 or 503 || transientNotFound)
                    {
                        await Task.Delay(1500 * attempt);
                        continue;
                    }

                    if (response.StatusCode == HttpStatusCode.NotFound)
                    {
                        break; // model really not available for this key -> try next model
                    }

                    if (status is 400 or 401 or 403)
                    {
                        return new GeminiResult(false,
                            $"Gemini rejected the request ({status}): {ReadErrorMessage(responseString)}\n\nCheck that your API key is correct.");
                    }

                    break;
                }
            }

            return new GeminiResult(false,
                $"Gemini is not reachable right now (all models busy or unavailable). Please try again in a few seconds.\n\nLast error: {lastError}");
        }
        catch (TaskCanceledException)
        {
            return new GeminiResult(false, "Gemini took too long to answer. Please try again.");
        }
        catch (Exception ex)
        {
            return new GeminiResult(false, $"Connection error: {ex.Message}");
        }
    }

    private static GeminiResult ParseResponse(string responseString)
    {
        using var doc = JsonDocument.Parse(responseString);
        var root = doc.RootElement;

        if (!root.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
        {
            var reason = root.TryGetProperty("promptFeedback", out var fb) && fb.TryGetProperty("blockReason", out var br)
                ? br.GetString()
                : "unknown";
            return new GeminiResult(false, $"Gemini returned no answer (reason: {reason}).");
        }

        var sb = new StringBuilder();
        if (candidates[0].TryGetProperty("content", out var content) &&
            content.TryGetProperty("parts", out var parts))
        {
            foreach (var part in parts.EnumerateArray())
            {
                // Skip internal "thinking" parts, only keep the visible answer.
                if (part.TryGetProperty("thought", out var thought) && thought.ValueKind == JsonValueKind.True)
                    continue;
                if (part.TryGetProperty("text", out var text))
                    sb.Append(text.GetString());
            }
        }

        var result = sb.ToString().Trim();
        return result.Length == 0
            ? new GeminiResult(false, "Gemini returned an empty answer. Please try again.")
            : new GeminiResult(true, result);
    }

    private static string ReadErrorMessage(string responseString)
    {
        if (string.IsNullOrWhiteSpace(responseString)) return "(no details)";
        try
        {
            using var doc = JsonDocument.Parse(responseString);
            if (doc.RootElement.TryGetProperty("error", out var err) && err.TryGetProperty("message", out var msg))
                return msg.GetString() ?? responseString;
        }
        catch (JsonException)
        {
        }

        return responseString.Length > 300 ? responseString[..300] + "…" : responseString;
    }
}
