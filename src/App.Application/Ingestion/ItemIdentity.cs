using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using App.Domain.Content;
using App.Domain.Reference;

namespace App.Application.Ingestion;

/// <summary>Fingerprint and content hash of a parsed item (design.md §2).</summary>
public static class ItemIdentity
{
    private static readonly JsonSerializerOptions HashJson = new() { WriteIndented = false };

    /// <summary>sha256(sourceId + (externalId ?? url)); null when the item has neither.</summary>
    public static string? Fingerprint(Guid sourceId, string? externalId, string? url)
    {
        var key = externalId ?? url;
        return key is null ? null : Sha256($"{sourceId:N}|{key}");
    }

    /// <summary>sha256 of the normalized fields as parsed, the AI text and the evidence.</summary>
    public static string ContentHash(ItemKind kind, CandidateFields fields, string? text, string evidence) =>
        Sha256(JsonSerializer.Serialize(new { kind, fields, text, evidence }, HashJson));

    private static string Sha256(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
