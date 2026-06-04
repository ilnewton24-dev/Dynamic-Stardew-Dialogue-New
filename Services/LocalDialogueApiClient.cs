using System.Net.Http.Json;
using System.Text.Json;
using LivingLoreDialogue.Models;

namespace LivingLoreDialogue.Services;

/// <summary>
/// Calls the local dashboard server to generate dialogue for the SMAPI mod. Logs the full
/// request/response cycle and never throws — failures return null so the caller can fall back
/// to vanilla dialogue.
/// </summary>
public sealed class LocalDialogueApiClient
{
    private const string Endpoint = "/api/dialogue/generate";

    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient httpClient;
    private readonly Action<string>? logInfo;
    private readonly Action<string>? logError;

    public LocalDialogueApiClient(HttpClient httpClient, Action<string>? logInfo = null, Action<string>? logError = null)
    {
        this.httpClient = httpClient;
        this.logInfo = logInfo;
        this.logError = logError;
    }

    public async Task<GeneratedDialogueResult?> GenerateAsync(DialogueContext context, string requestSource = "SMAPI")
    {
        // Include requestSource for diagnostics; the server ignores unknown fields.
        var payload = new
        {
            context.CharacterName,
            context.Topic,
            context.Season,
            context.Weather,
            context.Location,
            context.FriendshipLevel,
            RequestSource = requestSource
        };

        string baseUrl = this.httpClient.BaseAddress?.ToString().TrimEnd('/') ?? "(no base url)";
        string requestJson = JsonSerializer.Serialize(payload);
        this.logInfo?.Invoke($"[Server request] About to call {baseUrl}{Endpoint} (source={requestSource}).");
        this.logInfo?.Invoke($"[Server request] Payload: {Preview(requestJson)}");

        try
        {
            using HttpResponseMessage response = await this.httpClient.PostAsJsonAsync(Endpoint, payload);
            string body = await response.Content.ReadAsStringAsync();
            this.logInfo?.Invoke($"[Server response] Status {(int)response.StatusCode} {response.ReasonPhrase}.");
            this.logInfo?.Invoke($"[Server response] Body: {Preview(body)}");

            if (!response.IsSuccessStatusCode)
            {
                this.logError?.Invoke($"[Server response] Non-success status {(int)response.StatusCode}; falling back to vanilla.");
                return null;
            }

            GeneratedDialogueResult? result = JsonSerializer.Deserialize<GeneratedDialogueResult>(body, ReadOptions);
            if (result is null)
            {
                this.logError?.Invoke("[Server response] Response body could not be parsed; falling back to vanilla.");
                return null;
            }

            if (!string.IsNullOrWhiteSpace(result.Error))
                this.logError?.Invoke($"[Server response] Server reported error: {result.Error}");

            return result;
        }
        catch (Exception ex)
        {
            this.logError?.Invoke($"[Server request] Exception calling server: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private static string Preview(string text, int max = 400)
    {
        text = text.Replace("\r", " ").Replace("\n", " ");
        return text.Length <= max ? text : text[..max] + "...";
    }
}
