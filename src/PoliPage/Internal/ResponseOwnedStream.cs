namespace PoliPage.Internal;

/// <summary>
/// Wraps the body <see cref="Stream"/> of an <see cref="HttpResponseMessage"/> so the
/// caller can dispose both with a single <c>using</c>. Disposing this stream releases
/// the response, which closes the connection.
/// </summary>
/// <remarks>
/// All reads forward to the inner body stream. Length / Position / Seek are not
/// supported because HTTP response bodies are not seekable in the general case.
/// </remarks>
internal sealed class ResponseOwnedStream : Stream
{
    private readonly Stream _inner;
    private readonly HttpResponseMessage _response;
    private bool _disposed;

    internal ResponseOwnedStream(Stream inner, HttpResponseMessage response)
    {
        _inner = inner;
        _response = response;
    }

    public override bool CanRead => !_disposed && _inner.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => false;

    public override long Length
        => throw new NotSupportedException("HTTP response bodies are not seekable.");

    public override long Position
    {
        get => throw new NotSupportedException("HTTP response bodies are not seekable.");
        set => throw new NotSupportedException("HTTP response bodies are not seekable.");
    }

    public override void Flush() => _inner.Flush();

    public override Task FlushAsync(CancellationToken cancellationToken)
        => _inner.FlushAsync(cancellationToken);

    public override int Read(byte[] buffer, int offset, int count)
        => _inner.Read(buffer, offset, count);

    public override int Read(Span<byte> buffer) => _inner.Read(buffer);

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => _inner.ReadAsync(buffer, offset, count, cancellationToken);

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        => _inner.ReadAsync(buffer, cancellationToken);

    public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
        => _inner.CopyToAsync(destination, bufferSize, cancellationToken);

    public override long Seek(long offset, SeekOrigin origin)
        => throw new NotSupportedException("HTTP response bodies are not seekable.");

    public override void SetLength(long value)
        => throw new NotSupportedException("HTTP response bodies are read-only.");

    public override void Write(byte[] buffer, int offset, int count)
        => throw new NotSupportedException("HTTP response bodies are read-only.");

    protected override void Dispose(bool disposing)
    {
        if (_disposed) return;
        _disposed = true;
        if (disposing)
        {
            _inner.Dispose();
            _response.Dispose();
        }
        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await _inner.DisposeAsync().ConfigureAwait(false);
        _response.Dispose();
        await base.DisposeAsync().ConfigureAwait(false);
    }
}
