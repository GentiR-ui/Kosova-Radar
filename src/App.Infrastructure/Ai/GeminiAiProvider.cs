using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using App.Application.Ai;

namespace App.Infrastructure.Ai;

/// <summary>
/// Gemini API <c>generateContent</c> with a JSON-schema response (design.md §6, ADR-034). The key travels only in the
/// <c>x-goog-api-key</c> header, never in the URL, a log line or an exception message. Like the other providers it only
/// parses the answer; <see cref="AiOutputValidator"/> checks ranges, lengths and slugs.
/// </summary>
public sealed class GeminiAiProvider(IHttpClientFactory httpClients, AiOptions options) : IAiProvider
{
    public const string ClientName = "gemini";
    public const string DefaultModel = "gemini-3.5-flash-lite";
    // Larger than the answer needs: on Gemini 3 models thinking tokens may count against the limit (a cut-off answer is AiUnavailable).
    private const int MaxOutputTokens = 4096;
    private const string BaseAddress = "https://generativelanguage.googleapis.com/v1beta/models/";

    public string Name => "Gemini";
    public bool IsEnabled => true;

    /// <summary>The model goes into the URL path, so only plain model names are accepted.</summary>
    public static bool IsValidModel(string? model) =>
        !string.IsNullOrEmpty(model) && model.Length <= 100 && model.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_');

    public static Uri Endpoint(string model) => new($"{BaseAddress}{model}:generateContent");

    public async Task<AiExtractionResult> ExtractAsync(AiExtractionRequest request, CancellationToken ct)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, Endpoint(options.Model))
        {
            Content = new StringContent(BuildBody(request), Encoding.UTF8, "application/json"),
        };
        message.Headers.Add("x-goog-api-key", options.ApiKey);
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var http = httpClients.CreateClient(ClientName);
        using var response = await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.StatusCode != HttpStatusCode.OK)
            throw new HttpRequestException($"The Gemini API answered with HTTP status {(int)response.StatusCode}.", null, response.StatusCode);

        var json = await AiExtractionContract.ReadLimitedAsync(response, ct);
        return Parse(json);
    }

    private string BuildBody(AiExtractionRequest request)
    {
        var body = new JsonObject
        {
            ["systemInstruction"] = new JsonObject { ["parts"] = new JsonArray(new JsonObject { ["text"] = AiExtractionContract.SystemPrompt }) },
            ["contents"] = new JsonArray(new JsonObject
            {
                ["role"] = "user",
                ["parts"] = new JsonArray(new JsonObject { ["text"] = AiExtractionContract.UserMessage(request) }),
            }),
            ["generationConfig"] = new JsonObject
            {
                ["temperature"] = 0,
                ["maxOutputTokens"] = MaxOutputTokens,
                ["responseMimeType"] = "application/json",
                ["responseJsonSchema"] = AiExtractionContract.Schema(request, Nullable),
            },
        };
        return body.ToJsonString();
    }

    // Nullable fields as a type array, as in the Gemini structured-output docs.
    private static JsonObject Nullable(string type) => new() { ["type"] = new JsonArray(type, "null") };

    /// <summary>Reads a <c>generateContent</c> response; a blocked, cut-off, empty or non-schema answer is <see cref="AiInvalidOutputException"/>.</summary>
    private AiExtractionResult Parse(string responseJson)
    {
        try
        {
            using var envelope = JsonDocument.Parse(responseJson);
            var root = envelope.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw AiExtractionContract.Invalid("The AI response is not an object.");

            if (root.TryGetProperty("promptFeedback", out var feedback) && feedback.ValueKind == JsonValueKind.Object
                && AiExtractionContract.Str(feedback, "blockReason") is not null)
                throw AiExtractionContract.Invalid("The AI request was blocked.");

            if (!root.TryGetProperty("candidates", out var candidates) || candidates.ValueKind != JsonValueKind.Array || candidates.GetArrayLength() == 0
                || candidates[0].ValueKind != JsonValueKind.Object)
                throw AiExtractionContract.Invalid("The AI response has no candidate.");
            var candidate = candidates[0];

            // A safety stop, a cut-off answer or a recitation block is never a usable extraction.
            if (AiExtractionContract.Str(candidate, "finishReason") is not "STOP")
                throw AiExtractionContract.Invalid("The AI response did not end normally.");

            string? text = null;
            if (candidate.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Object
                && content.TryGetProperty("parts", out var parts) && parts.ValueKind == JsonValueKind.Array)
                foreach (var part in parts.EnumerateArray())
                {
                    if (part.ValueKind != JsonValueKind.Object) continue;
                    if (part.TryGetProperty("thought", out var thought) && thought.ValueKind == JsonValueKind.True) continue;
                    if (AiExtractionContract.Str(part, "text") is { } t) { text = t; break; }
                }
            if (string.IsNullOrWhiteSpace(text)) throw AiExtractionContract.Invalid("The AI response has no text.");

            var (confidence, fields, category) = AiExtractionContract.ParseAnswer(text);
            var usage = root.TryGetProperty("usageMetadata", out var u) && u.ValueKind == JsonValueKind.Object ? u : default;
            return new AiExtractionResult(
                AiExtractionContract.Str(root, "modelVersion") ?? options.Model,
                AiExtractionContract.Str(root, "responseId"),
                confidence,
                fields,
                CategorySlug: category,
                InputTokens: usage.ValueKind == JsonValueKind.Object ? AiExtractionContract.Int(usage, "promptTokenCount") : null,
                OutputTokens: usage.ValueKind == JsonValueKind.Object ? AiExtractionContract.Int(usage, "candidatesTokenCount") : null);
        }
        catch (JsonException)
        {
            throw AiExtractionContract.Invalid("The AI response is not valid JSON.");
        }
    }
}
