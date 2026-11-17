using System.Text;
using Microsoft.Extensions.Logging;

namespace FilmSearch.AI.Diagnostics;

/// <summary>
/// Development-only handler that logs the raw HTTP traffic to AI providers (<c>AI:LogRawHttp</c>): the JSON request
/// body (messages, tools, response_format) and the response body, including server-sent events of a streaming call.
/// It is a learning aid to see what Microsoft.Extensions.AI actually sends. Headers are not logged, so the API key
/// never reaches the log. Bodies are cut at <see cref="MaxBodyChars"/>.
/// </summary>
#pragma warning disable CA1873 // Arguments are computed only after the IsEnabled check in SendAsync.
internal sealed partial class RawHttpLoggingHandler(ILogger<RawHttpLoggingHandler> logger) : DelegatingHandler
{
    internal const int MaxBodyChars = 16_000;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!logger.IsEnabled(LogLevel.Information))
        {
            return await base.SendAsync(request, cancellationToken);
        }

        var requestBody = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        LogRequest(logger, request.Method.Method, request.RequestUri, Truncate(requestBody));

        var response = await base.SendAsync(request, cancellationToken);

        if (response.Content.Headers.ContentType?.MediaType == "text/event-stream")
        {
            // Streaming: log the events when the caller has read them, without delaying the stream.
            response.Content = new LoggedStreamContent(response.Content, body =>
                LogResponse(logger, (int)response.StatusCode, request.RequestUri, Truncate(body)));
        }
        else
        {
            await response.Content.LoadIntoBufferAsync(cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            LogResponse(logger, (int)response.StatusCode, request.RequestUri, Truncate(body));
        }

        return response;
    }

    internal static string Truncate(string body) =>
        body.Length <= MaxBodyChars ? body : string.Concat(body.AsSpan(0, MaxBodyChars), $"… ({body.Length - MaxBodyChars} more chars)");

    [LoggerMessage(Level = LogLevel.Information, Message = "AI HTTP request {Method} {Uri}\n{Body}")]
    private static partial void LogRequest(ILogger logger, string method, Uri? uri, string body);

    [LoggerMessage(Level = LogLevel.Information, Message = "AI HTTP response {StatusCode} {Uri}\n{Body}")]
    private static partial void LogResponse(ILogger logger, int statusCode, Uri? uri, string body);

    /// <summary>Passes the response stream through and copies what was read (up to a limit) for the log.</summary>
    private sealed class LoggedStreamContent : HttpContent
    {
        private readonly HttpContent _inner;
        private readonly Action<string> _onCompleted;

        public LoggedStreamContent(HttpContent inner, Action<string> onCompleted)
        {
            _inner = inner;
            _onCompleted = onCompleted;
            foreach (var header in inner.Headers)
            {
                Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        protected override async Task<Stream> CreateContentReadStreamAsync(CancellationToken cancellationToken) =>
            new TeeStream(await _inner.ReadAsStreamAsync(cancellationToken), _onCompleted);

        protected override Task<Stream> CreateContentReadStreamAsync() => CreateContentReadStreamAsync(CancellationToken.None);

        protected override async Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context)
        {
            await using var source = await CreateContentReadStreamAsync(CancellationToken.None);
            await source.CopyToAsync(stream);
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    private sealed class TeeStream(Stream inner, Action<string> onCompleted) : Stream
    {
        private readonly MemoryStream _copy = new();
        private bool _logged;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => Copy(buffer.AsSpan(offset, inner.Read(buffer, offset, count)));

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await inner.ReadAsync(buffer, cancellationToken);
            return Copy(buffer.Span[..read]);
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        private int Copy(ReadOnlySpan<byte> read)
        {
            if (read.Length == 0)
            {
                Complete();
            }
            else if (_copy.Length < MaxBodyChars * 4)
            {
                _copy.Write(read);
            }

            return read.Length;
        }

        private void Complete()
        {
            if (_logged)
            {
                return;
            }

            _logged = true;
            onCompleted(Encoding.UTF8.GetString(_copy.GetBuffer(), 0, (int)_copy.Length));
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // A stream closed early (cancelled call) still logs what was received.
                Complete();
                inner.Dispose();
                _copy.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
