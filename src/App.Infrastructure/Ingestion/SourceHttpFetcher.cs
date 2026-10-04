using System.Diagnostics;
using System.Net;
using System.Text;
using App.Application.Ingestion;
using App.Application.Sources;

namespace App.Infrastructure.Ingestion;

/// <summary>
/// Downloads a source URL through the guarded named client <see cref="ClientName"/> (SSRF guard in the connect
/// callback, retries from the resilience handler) and turns every failure into a run error code. Exception messages
/// are constants (plus the numeric status): they are stored on the source and shown to admins (design.md §6).
/// </summary>
public sealed class SourceHttpFetcher(HttpClient http, IngestionOptions options)
{
    public const string ClientName = "sources";

    /// <summary>Response limit (design.md §6); applies to the decoded body.</summary>
    public const int MaxBytes = 5 * 1024 * 1024;

    /// <summary>
    /// Request option set by the fetcher: <see cref="HostThrottleHandler"/> reports how long it is going to wait, so the
    /// politeness delay is not counted against the deadline.
    /// </summary>
    internal static readonly HttpRequestOptionsKey<Action<TimeSpan>> ThrottleWaitKey = new("KosovaRadar.ThrottleWait");

    /// <summary>The primary handler of the named client: SSRF guard, no proxy, 5 redirects (design.md §6).</summary>
    public static SocketsHttpHandler CreateHandler() => new()
    {
        UseProxy = false, // the ConnectCallback must see the real target, not a proxy
        UseCookies = false, // sources are independent; nothing is shared between them
        AllowAutoRedirect = true,
        MaxAutomaticRedirections = 5,
        AutomaticDecompression = DecompressionMethods.All,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5), // DNS is re-checked on every new connection
        ConnectCallback = GuardedConnect.ConnectAsync,
    };

    /// <exception cref="SourceFetchException">Mapped from the failure: Timeout, BlockedAddress, HttpStatus, TooLarge, Unreachable.</exception>
    public async Task<string> GetStringAsync(Uri url, CancellationToken ct)
    {
        // One deadline for the whole fetch including retries and reading the body (a slow body must not hang a run).
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var budget = TimeSpan.FromSeconds(options.HttpTimeoutSeconds);
        var started = Stopwatch.GetTimestamp();
        deadline.CancelAfter(budget);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Options.Set(ThrottleWaitKey, wait =>
        {
            budget += wait;
            var remaining = budget - Stopwatch.GetElapsedTime(started);
            deadline.CancelAfter(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero);
        });
        try
        {
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            if (!response.IsSuccessStatusCode)
                throw new SourceFetchException(RunErrorCodes.HttpStatus, $"The source answered with HTTP status {(int)response.StatusCode}.", (int)response.StatusCode);
            return await ReadLimitedAsync(response, deadline.Token);
        }
        catch (Exception e) when (e is OperationCanceledException && !ct.IsCancellationRequested)
        {
            throw new SourceFetchException(RunErrorCodes.Timeout, "The source did not answer in time.");
        }
        catch (InvalidDataException)
        {
            // A corrupt gzip/brotli/deflate body: the source's fault, so a failed run and not a crash.
            throw new SourceFetchException(RunErrorCodes.MalformedPayload, "The source answer could not be decoded.");
        }
        catch (Exception e) when (e is HttpRequestException or IOException)
        {
            if (IsBlocked(e)) throw new SourceFetchException(RunErrorCodes.BlockedAddress, "The source address is not allowed.");
            throw new SourceFetchException(RunErrorCodes.Unreachable, "The source could not be reached.");
        }
    }

    /// <summary>True when the guard in the connect callback refused the connection.</summary>
    public static bool IsBlocked(Exception e)
    {
        for (var inner = e; inner is not null; inner = inner.InnerException)
            if (inner is BlockedAddressException) return true;
        return false;
    }

    private static Encoding ResponseEncoding(HttpResponseMessage response)
    {
        try
        {
            return response.Content.Headers.ContentType?.CharSet is { Length: > 0 } charset
                ? Encoding.GetEncoding(charset.Trim('"'))
                : new UTF8Encoding(false);
        }
        catch (ArgumentException)
        {
            return new UTF8Encoding(false);
        }
    }

    private static async Task<string> ReadLimitedAsync(HttpResponseMessage response, CancellationToken ct)
    {
        const string tooLarge = "The source answer is larger than 5 MB.";
        if (response.Content.Headers.ContentLength > MaxBytes)
            throw new SourceFetchException(RunErrorCodes.TooLarge, tooLarge);

        await using var body = await response.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await body.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > MaxBytes) throw new SourceFetchException(RunErrorCodes.TooLarge, tooLarge);
            buffer.Write(chunk, 0, read);
        }

        buffer.Position = 0;
        // The declared charset (feeds in windows-1252 or ISO-8859-2 exist); a BOM or an unknown name falls back to UTF-8.
        using var reader = new StreamReader(buffer, ResponseEncoding(response), detectEncodingFromByteOrderMarks: true);
        return await reader.ReadToEndAsync(ct);
    }
}
