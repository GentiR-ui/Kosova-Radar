using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using App.Application.Ai;

namespace App.Infrastructure.Ai;

/// <summary>
/// Claude Messages API with a JSON-schema structured output (design.md §6, ADR-029). The answer is only parsed here
/// (type shape, ISO dates with an offset); ranges, lengths and slugs are checked by <see cref="AiOutputValidator"/>.
/// Failure messages are constants: they are logged, so they never contain the key, the feed text or the response body.
/// </summary>
public sealed class AnthropicAiProvider(IHttpClientFactory httpClients, AiOptions options) : IAiProvider
{
    public const string ClientName = "anthropic";
    public static readonly Uri Endpoint = new("https://api.anthropic.com/v1/messages");

    private const string ApiVersion = "2023-06-01";

    public string Name => "Anthropic";
    public bool IsEnabled => true;

    public async Task<AiExtractionResult> ExtractAsync(AiExtractionRequest request, CancellationToken ct)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = new StringContent(BuildBody(request), Encoding.UTF8, "application/json"),
        };
        message.Headers.Add("x-api-key", options.ApiKey);
        message.Headers.Add("anthropic-version", ApiVersion);
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var http = httpClients.CreateClient(ClientName);
        using var response = await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.StatusCode != HttpStatusCode.OK)
            throw new HttpRequestException($"The Anthropic API answered with HTTP status {(int)response.StatusCode}.", null, response.StatusCode);

        var json = await AiExtractionContract.ReadLimitedAsync(response, ct);
        return Parse(json);
    }

    private string BuildBody(AiExtractionRequest request)
    {
        var body = new JsonObject
        {
            ["model"] = options.Model,
            ["max_tokens"] = AiExtractionContract.MaxOutputTokens,
            ["system"] = AiExtractionContract.SystemPrompt,
            ["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = AiExtractionContract.UserMessage(request) }),
            ["output_config"] = new JsonObject
            {
                ["format"] = new JsonObject { ["type"] = "json_schema", ["schema"] = AiExtractionContract.Schema(request, Nullable) },
            },
        };
        return body.ToJsonString();
    }

    // anyOf is the documented way to say "string or null" in the structured-output schema subset.
    private static JsonObject Nullable(string type) =>
        new() { ["anyOf"] = new JsonArray(new JsonObject { ["type"] = type }, new JsonObject { ["type"] = "null" }) };

    /// <summary>Reads a Messages API response body; anything that does not match the schema is <see cref="AiInvalidOutputException"/>.</summary>
    private AiExtractionResult Parse(string responseJson)
    {
        try
        {
            using var envelope = JsonDocument.Parse(responseJson);
            var root = envelope.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw AiExtractionContract.Invalid("The AI response is not an object.");

            // A refusal or a cut-off answer is never a usable extraction.
            if (AiExtractionContract.Str(root, "stop_reason") is not "end_turn" and not "stop_sequence")
                throw AiExtractionContract.Invalid("The AI response did not end normally.");

            string? text = null;
            if (root.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
                foreach (var block in content.EnumerateArray())
                    if (block.ValueKind == JsonValueKind.Object && AiExtractionContract.Str(block, "type") == "text") { text = AiExtractionContract.Str(block, "text"); break; }
            if (text is null) throw AiExtractionContract.Invalid("The AI response has no text.");

            var (confidence, fields, category) = AiExtractionContract.ParseAnswer(text);
            var usage = root.TryGetProperty("usage", out var u) && u.ValueKind == JsonValueKind.Object ? u : default;
            return new AiExtractionResult(
                AiExtractionContract.Str(root, "model") ?? options.Model,
                AiExtractionContract.Str(root, "id"),
                confidence,
                fields,
                CategorySlug: category,
                InputTokens: usage.ValueKind == JsonValueKind.Object ? AiExtractionContract.Int(usage, "input_tokens") : null,
                OutputTokens: usage.ValueKind == JsonValueKind.Object ? AiExtractionContract.Int(usage, "output_tokens") : null);
        }
        catch (JsonException)
        {
            throw AiExtractionContract.Invalid("The AI response is not valid JSON.");
        }
    }
}
