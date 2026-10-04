using System.IO.Compression;
using System.Net;
using System.Text;
using App.Application.Ingestion;
using App.Application.Sources;
using App.Infrastructure.Ingestion;

namespace App.UnitTests;

public class SourceHttpFetcherTests
{
    private static readonly Uri Url = new("https://feeds.example/feed.json");

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => respond(request, ct);
    }

    private static SourceHttpFetcher Fetcher(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond, int timeoutSeconds = 5) =>
        new(new HttpClient(new StubHandler(respond)), new IngestionOptions { HttpTimeoutSeconds = timeoutSeconds });

    private static Task<HttpResponseMessage> Ok(HttpContent content) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });

    private static async Task<SourceFetchException> FailureAsync(SourceHttpFetcher fetcher) =>
        await Assert.ThrowsAsync<SourceFetchException>(() => fetcher.GetStringAsync(Url, CancellationToken.None));

    [Fact]
    public async Task A_successful_answer_is_returned_as_text() =>
        Assert.Equal("{\"items\":[]}", await Fetcher((_, _) => Ok(new StringContent("{\"items\":[]}"))).GetStringAsync(Url, CancellationToken.None));

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.Found)]
    public async Task A_non_2xx_answer_is_HttpStatus_with_only_the_number_in_the_message(HttpStatusCode status)
    {
        var e = await FailureAsync(Fetcher((_, _) => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent("secret body") })));
        Assert.Equal(RunErrorCodes.HttpStatus, e.ErrorCode);
        Assert.Equal($"The source answered with HTTP status {(int)status}.", e.Message);
        Assert.Equal((int)status, e.HttpStatus);
    }

    [Fact]
    public async Task A_declared_length_over_5_MB_is_TooLarge()
    {
        var content = new ByteArrayContent(new byte[SourceHttpFetcher.MaxBytes + 1]);
        Assert.Equal(RunErrorCodes.TooLarge, (await FailureAsync(Fetcher((_, _) => Ok(content)))).ErrorCode);
    }

    [Fact]
    public async Task An_undeclared_length_over_5_MB_is_TooLarge()
    {
        var content = new StreamContent(new ZeroStream(SourceHttpFetcher.MaxBytes + 1));
        Assert.Equal(RunErrorCodes.TooLarge, (await FailureAsync(Fetcher((_, _) => Ok(content)))).ErrorCode);
    }

    [Fact]
    public async Task Exactly_5_MB_is_accepted()
    {
        var text = await Fetcher((_, _) => Ok(new StreamContent(new ZeroStream(SourceHttpFetcher.MaxBytes)))).GetStringAsync(Url, CancellationToken.None);
        Assert.Equal(SourceHttpFetcher.MaxBytes, text.Length);
    }

    [Fact]
    public async Task A_slow_answer_is_Timeout()
    {
        var fetcher = Fetcher(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return new HttpResponseMessage();
        }, timeoutSeconds: 1);
        Assert.Equal(RunErrorCodes.Timeout, (await FailureAsync(fetcher)).ErrorCode);
    }

    [Fact]
    public async Task A_body_that_never_finishes_is_Timeout()
    {
        var fetcher = Fetcher((_, _) => Ok(new StreamContent(new HangingStream())), timeoutSeconds: 1);
        Assert.Equal(RunErrorCodes.Timeout, (await FailureAsync(fetcher)).ErrorCode);
    }

    [Fact]
    public async Task The_callers_cancellation_is_not_reported_as_a_timeout()
    {
        using var cts = new CancellationTokenSource();
        var fetcher = Fetcher(async (_, ct) =>
        {
            await cts.CancelAsync();
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return new HttpResponseMessage();
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fetcher.GetStringAsync(Url, cts.Token));
    }

    [Fact]
    public async Task A_refusal_by_the_guard_is_BlockedAddress_even_when_wrapped()
    {
        var fetcher = Fetcher((_, _) => throw new HttpRequestException("x", new BlockedAddressException()));
        var e = await FailureAsync(fetcher);
        Assert.Equal(RunErrorCodes.BlockedAddress, e.ErrorCode);
        Assert.DoesNotContain("127.0.0.1", e.Message);
    }

    [Fact]
    public async Task Other_network_errors_are_Unreachable_without_the_exception_text()
    {
        var e = await FailureAsync(Fetcher((_, _) => throw new HttpRequestException("connect to 10.1.2.3:443 failed")));
        Assert.Equal(RunErrorCodes.Unreachable, e.ErrorCode);
        Assert.DoesNotContain("10.1.2.3", e.Message);
    }

    [Fact]
    public async Task Loopback_link_local_and_localhost_are_refused_by_the_real_connect_callback()
    {
        using var http = new HttpClient(SourceHttpFetcher.CreateHandler());
        var fetcher = new SourceHttpFetcher(http, new IngestionOptions { HttpTimeoutSeconds = 5 });
        foreach (var url in new[] { "http://localhost/", "http://127.0.0.1:1/", "http://[::1]/", "http://169.254.169.254/" })
        {
            var e = await Assert.ThrowsAsync<SourceFetchException>(() => fetcher.GetStringAsync(new Uri(url), CancellationToken.None));
            Assert.Equal(RunErrorCodes.BlockedAddress, e.ErrorCode);
        }
    }

    [Fact]
    public void The_handler_has_the_guard_no_proxy_and_the_redirect_limit()
    {
        using var handler = SourceHttpFetcher.CreateHandler();
        Assert.False(handler.UseProxy);
        Assert.False(handler.UseCookies);
        Assert.True(handler.AllowAutoRedirect);
        Assert.Equal(5, handler.MaxAutomaticRedirections);
        Assert.NotNull(handler.ConnectCallback);
    }

    [Fact]
    public async Task The_declared_charset_is_honoured()
    {
        var content = new ByteArrayContent(Encoding.Latin1.GetBytes("Shtëpia"));
        content.Headers.ContentType = new("application/json") { CharSet = "iso-8859-1" };
        Assert.Equal("Shtëpia", await Fetcher((_, _) => Ok(content)).GetStringAsync(Url, CancellationToken.None));
    }

    [Fact]
    public async Task An_unknown_charset_falls_back_to_utf8()
    {
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes("Shtëpia"));
        content.Headers.TryAddWithoutValidation("Content-Type", "text/plain; charset=made-up");
        Assert.Equal("Shtëpia", await Fetcher((_, _) => Ok(content)).GetStringAsync(Url, CancellationToken.None));
    }

    [Fact]
    public async Task A_corrupt_compressed_body_fails_the_run_instead_of_crashing_it()
    {
        var content = new StreamContent(new GZipStream(new MemoryStream([1, 2, 3, 4, 5, 6, 7, 8, 9, 10]), CompressionMode.Decompress));
        var e = await FailureAsync(Fetcher((_, _) => Ok(content)));
        Assert.Equal(RunErrorCodes.MalformedPayload, e.ErrorCode);
    }

    private sealed class ZeroStream(long length) : Stream
    {
        private long _left = length;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            var n = (int)Math.Min(buffer.Length, _left);
            buffer[..n].Fill((byte)'a');
            _left -= n;
            return n;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class HangingStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            await Task.Delay(Timeout.Infinite, ct);
            return 0;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
