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
    private const string BranchingEndpoint = "/api/dialogue/branching";

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
        string effectiveSource = string.IsNullOrWhiteSpace(context.RequestSource) ? requestSource : context.RequestSource;
        var payload = new
        {
            context.InterceptedNpcName,
            context.CharacterName,
            context.DisplayName,
            context.InternalLocationId,
            context.DisplayLocation,
            LocationName = string.IsNullOrWhiteSpace(context.DisplayLocation) ? context.Location : context.DisplayLocation,
            context.Topic,
            context.Season,
            context.Weather,
            context.Location,
            context.FriendshipLevel,
            RequestSource = effectiveSource,
            ActivePlayerProfileId = (long?)null,  // SMAPI does not know the profile id; server resolves it
            SaveContext = context.SaveContext
        };

        string baseUrl = this.httpClient.BaseAddress?.ToString().TrimEnd('/') ?? "(no base url)";
        string requestJson = JsonSerializer.Serialize(payload);
        this.logInfo?.Invoke($"[Server request] About to call {baseUrl}{Endpoint} (source={effectiveSource}).");

        // Log the save identity fields so they are visible in the SMAPI console for diagnostics.
        if (context.SaveContext is SaveFileContextSnapshot sc)
        {
            this.logInfo?.Invoke(
                $"[Server request] Save context: saveFileName={sc.SaveFileName ?? "(none)"}, " +
                $"playerName={sc.PlayerName}, farmName={sc.FarmName}, location={sc.Location}.");
            this.logInfo?.Invoke($"[Server request] Active player profile id sent: (none — server will auto-resolve).");
        }
        else
        {
            this.logInfo?.Invoke("[Server request] No save context attached — server will use defaults.");
        }

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

    public async Task<BranchingDialogueResponse?> GenerateBranchingAsync(BranchingDialogueRequest request, string requestSource = "SMAPI-Branching")
    {
        if (string.IsNullOrWhiteSpace(request.Context.RequestSource))
            request.Context.RequestSource = requestSource;

        string baseUrl = this.httpClient.BaseAddress?.ToString().TrimEnd('/') ?? "(no base url)";
        string requestJson = JsonSerializer.Serialize(request);
        this.logInfo?.Invoke($"[Branching request] About to call {baseUrl}{BranchingEndpoint} (mode={request.Mode}, source={request.Context.RequestSource}).");
        this.logInfo?.Invoke(
            $"[Branching request] NPC={request.Context.CharacterName} Turn={request.TurnCount}/{request.MaxTurnCount} " +
            $"SelectedOption=\"{request.SelectedOptionText}\" HistoryCount={request.History.Count} SessionId={request.SessionId}");
        this.logInfo?.Invoke($"[Branching request] Payload: {Preview(requestJson)}");

        try
        {
            using HttpResponseMessage response = await this.httpClient.PostAsJsonAsync(BranchingEndpoint, request);
            string body = await response.Content.ReadAsStringAsync();
            this.logInfo?.Invoke($"[Branching response] Status {(int)response.StatusCode} {response.ReasonPhrase}.");
            this.logInfo?.Invoke($"[Branching response] Body: {Preview(body)}");

            if (!response.IsSuccessStatusCode)
            {
                this.logError?.Invoke(
                    $"[Branching response] HTTP {(int)response.StatusCode} {response.ReasonPhrase} from {baseUrl}{BranchingEndpoint}. " +
                    $"NPC={request.Context.CharacterName}, turn={request.TurnCount}, mode={request.Mode}, session={request.SessionId}. " +
                    $"Response body: {body}");
                return null;
            }

            BranchingDialogueResponse? result = JsonSerializer.Deserialize<BranchingDialogueResponse>(body, ReadOptions);
            if (result is null || result.PlayerOptions.Count == 0)
            {
                this.logError?.Invoke("[Branching response] Response body was empty or malformed; using fallback options.");
                return null;
            }

            this.logInfo?.Invoke(
                $"[Branching response] Parsed: npcResponse=\"{Preview(result.NpcResponse, 200)}\", " +
                $"options={result.PlayerOptions.Count}, conversationShouldEnd={result.ConversationShouldEnd}, " +
                $"error=\"{result.Error}\".");

            if (!string.IsNullOrWhiteSpace(result.Error))
                this.logError?.Invoke($"[Branching response] Server reported error: {result.Error}");

            return result;
        }
        catch (Exception ex)
        {
            this.logError?.Invoke($"[Branching request] Exception calling {baseUrl}{BranchingEndpoint}: {ex}");
            return null;
        }
    }

    /// <summary>
    /// Registers vanilla dialogue for one character with the server so the prompt builder has
    /// canonical examples even when no Content Patcher mods are installed. Never throws.
    /// </summary>
    public async Task RegisterVanillaDialogueAsync(string characterName, IReadOnlyDictionary<string, string> entries)
    {
        if (string.IsNullOrWhiteSpace(characterName) || entries.Count == 0)
            return;

        const string endpoint = "/api/dialogue/register-vanilla";
        var payload = new { characterName, entries };

        this.logInfo?.Invoke($"[VanillaDialogue] Registering {entries.Count} line(s) for '{characterName}'.");

        try
        {
            using HttpResponseMessage response = await this.httpClient.PostAsJsonAsync(endpoint, payload);
            if (!response.IsSuccessStatusCode)
            {
                this.logError?.Invoke($"[VanillaDialogue] Server returned {(int)response.StatusCode} for '{characterName}'; skipping.");
                return;
            }

            string body = await response.Content.ReadAsStringAsync();
            this.logInfo?.Invoke($"[VanillaDialogue] '{characterName}' registered. Response: {Preview(body)}");
        }
        catch (Exception ex)
        {
            this.logError?.Invoke($"[VanillaDialogue] Exception registering '{characterName}': {ex.Message}");
        }
    }

    private static string Preview(string text, int max = 400)
    {
        text = text.Replace("\r", " ").Replace("\n", " ");
        return text.Length <= max ? text : text[..max] + "...";
    }
}
