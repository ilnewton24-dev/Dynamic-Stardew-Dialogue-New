using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using LivingLoreDialogue.Models;

namespace LivingLoreDialogue.Services;

public sealed class OpenAiDialogueService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly HttpClient httpClient;
    private readonly PromptBuilder promptBuilder;
    private readonly string apiKey;
    private readonly string model;

    public OpenAiDialogueService(HttpClient httpClient, PromptBuilder promptBuilder, string apiKey, string model)
    {
        this.httpClient = httpClient;
        this.promptBuilder = promptBuilder;
        this.apiKey = apiKey;
        this.model = model;
    }

    public string Model => this.model;

    public bool HasApiKey => !string.IsNullOrWhiteSpace(this.apiKey);

    /// <summary>
    /// Verifies the API key by calling the lightweight models endpoint. Returns whether the call
    /// succeeded plus an optional error message for display.
    /// </summary>
    public async Task<(bool Connected, string? Error)> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        if (!this.HasApiKey)
            return (false, "No API key configured.");

        try
        {
            using HttpRequestMessage request = new(HttpMethod.Get, "https://api.openai.com/v1/models");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", this.apiKey);

            using HttpResponseMessage response = await this.httpClient.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
                return (true, null);

            return (false, $"OpenAI returned {(int)response.StatusCode} {response.ReasonPhrase}.");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public async Task<GeneratedDialogue> GenerateDialogueAsync(DialogueContext context, DialogueLoreBundle lore)
    {
        string prompt = this.promptBuilder.Build(context, lore);
        return await this.GenerateDialogueFromPromptAsync(prompt);
    }

    public async Task<GeneratedDialogue> GenerateDialogueFromPromptAsync(string prompt)
    {
        object requestBody = new
        {
            model = this.model,
            input = prompt,
            max_output_tokens = 220,
            text = new
            {
                format = new
                {
                    type = "json_schema",
                    name = "living_lore_dialogue",
                    strict = true,
                    schema = new
                    {
                        type = "object",
                        additionalProperties = false,
                        properties = new
                        {
                            character = new { type = "string" },
                            dialogue = new { type = "string" },
                            emotion = new
                            {
                                type = "string",
                                @enum = new[] { "neutral", "happy", "sad", "angry", "surprised", "shy", "concerned" }
                            },
                            topic = new { type = "string" }
                        },
                        required = new[] { "character", "dialogue", "emotion", "topic" }
                    }
                }
            }
        };

        using HttpRequestMessage request = new(HttpMethod.Post, "https://api.openai.com/v1/responses");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", this.apiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(requestBody, JsonOptions), Encoding.UTF8, "application/json");

        using HttpResponseMessage response = await this.httpClient.SendAsync(request);
        string responseJson = await response.Content.ReadAsStringAsync();
        response.EnsureSuccessStatusCode();

        string outputText = ExtractOutputText(responseJson);
        GeneratedDialogue? dialogue = JsonSerializer.Deserialize<GeneratedDialogue>(outputText, JsonOptions);

        if (dialogue is null || string.IsNullOrWhiteSpace(dialogue.Dialogue))
            throw new InvalidOperationException("OpenAI returned an empty dialogue payload.");

        return dialogue;
    }

    public async Task<PlayerProfileAutocompleteResult> GeneratePlayerProfileAsync(string concept, CancellationToken cancellationToken = default)
    {
        if (!this.HasApiKey)
            throw new InvalidOperationException("OpenAI API key is not configured.");

        string prompt = string.Join(Environment.NewLine, new[]
        {
            "You are helping draft a Living Lore Dialogue player/farmer profile for Stardew Valley dialogue generation.",
            "",
            "Expand the user's short concept into a grounded, concise, Stardew Valley-compatible profile. The profile is roleplay background only. It must help NPC dialogue generation without replacing NPC voice, save context, spouse/dating status, hearts, season, quests, or events.",
            "",
            "Rules:",
            "- Return only JSON matching the provided schema.",
            "- Do not include markdown, bullets, code fences, or JSON-as-text inside fields.",
            "- Keep fields natural, concise, and useful for prompts.",
            "- Avoid overly dramatic chosen-one language unless the concept explicitly asks for it.",
            "- Keep relationshipNotes empty or generic unless the concept names specific characters.",
            "- Do not invent current spouse, dating status, heart levels, current quests, events, or save-state facts.",
            "- Keep customLore flexible and compatible with different saves.",
            "",
            "User concept:",
            concept
        });

        object requestBody = new
        {
            model = this.model,
            input = prompt,
            max_output_tokens = 900,
            text = new
            {
                format = new
                {
                    type = "json_schema",
                    name = "living_lore_player_profile",
                    strict = true,
                    schema = new
                    {
                        type = "object",
                        additionalProperties = false,
                        properties = new
                        {
                            profileName = new { type = "string" },
                            description = new { type = "string" },
                            backstory = new { type = "string" },
                            personality = new { type = "string" },
                            roleplayStyle = new { type = "string" },
                            preferredDialogueTone = new { type = "string" },
                            importantHistory = new { type = "string" },
                            currentGoals = new { type = "string" },
                            relationshipNotes = new { type = "string" },
                            customLore = new { type = "string" }
                        },
                        required = new[]
                        {
                            "profileName",
                            "description",
                            "backstory",
                            "personality",
                            "roleplayStyle",
                            "preferredDialogueTone",
                            "importantHistory",
                            "currentGoals",
                            "relationshipNotes",
                            "customLore"
                        }
                    }
                }
            }
        };

        using HttpRequestMessage request = new(HttpMethod.Post, "https://api.openai.com/v1/responses");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", this.apiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(requestBody, JsonOptions), Encoding.UTF8, "application/json");

        using HttpResponseMessage response = await this.httpClient.SendAsync(request, cancellationToken);
        string responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        response.EnsureSuccessStatusCode();

        string outputText = ExtractOutputText(responseJson);
        PlayerProfileAutocompleteResult? profile = JsonSerializer.Deserialize<PlayerProfileAutocompleteResult>(outputText, JsonOptions);
        if (profile is null)
            throw new InvalidOperationException("OpenAI returned an empty profile payload.");

        profile = profile.Trimmed();
        if (profile.HasMarkdown())
            throw new InvalidOperationException("Generated profile included markdown; please regenerate.");

        return profile;
    }

    private static string ExtractOutputText(string responseJson)
    {
        using JsonDocument document = JsonDocument.Parse(responseJson);
        JsonElement root = document.RootElement;

        if (root.TryGetProperty("output_text", out JsonElement outputTextElement))
            return outputTextElement.GetString() ?? "";

        if (!root.TryGetProperty("output", out JsonElement output) || output.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("OpenAI response did not include output text.");

        foreach (JsonElement outputItem in output.EnumerateArray())
        {
            if (!outputItem.TryGetProperty("content", out JsonElement content) || content.ValueKind != JsonValueKind.Array)
                continue;

            foreach (JsonElement contentItem in content.EnumerateArray())
            {
                if (contentItem.TryGetProperty("text", out JsonElement text))
                    return text.GetString() ?? "";
            }
        }

        throw new InvalidOperationException("OpenAI response output text was not found.");
    }
}

public sealed record PlayerProfileAutocompleteResult(
    string ProfileName,
    string Description,
    string Backstory,
    string Personality,
    string RoleplayStyle,
    string PreferredDialogueTone,
    string ImportantHistory,
    string CurrentGoals,
    string RelationshipNotes,
    string CustomLore)
{
    public PlayerProfileAutocompleteResult Trimmed() => this with
    {
        ProfileName = Clean(ProfileName),
        Description = Clean(Description),
        Backstory = Clean(Backstory),
        Personality = Clean(Personality),
        RoleplayStyle = Clean(RoleplayStyle),
        PreferredDialogueTone = Clean(PreferredDialogueTone),
        ImportantHistory = Clean(ImportantHistory),
        CurrentGoals = Clean(CurrentGoals),
        RelationshipNotes = Clean(RelationshipNotes),
        CustomLore = Clean(CustomLore)
    };

    public bool HasMarkdown() => new[]
    {
        ProfileName,
        Description,
        Backstory,
        Personality,
        RoleplayStyle,
        PreferredDialogueTone,
        ImportantHistory,
        CurrentGoals,
        RelationshipNotes,
        CustomLore
    }.Any(value => value.Contains("```", StringComparison.Ordinal)
        || value.TrimStart().StartsWith("- ", StringComparison.Ordinal)
        || value.TrimStart().StartsWith("* ", StringComparison.Ordinal));

    private static string Clean(string? value) => (value ?? "").ReplaceLineEndings("\n").Trim();
}
