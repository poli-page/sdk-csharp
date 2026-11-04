namespace PoliPage.Internal;

/// <summary>
/// Wraps the body <see cref="Stream"/> of a presigned download so the caller can dispose
/// it and its <see cref="HttpResponseMessage"/> with a single <c>using</c>. Disposing this
/// stream releases the response, which closes the connection.
/// </summary>
/// <remarks>
/// All reads forward to the inner body stream, each bounded by the idle timeout: a read that
/// gets no byte within it fails, while the time between two reads, the caller's pace, is not
/// bounded. A read that fails raises the SDK's own error, never the HTTP stack's:
/// <c>aborted</c> when the call's token or the read's token is cancelled,
/// <c>DOWNLOAD_FAILED</c> (<c>Failed to download PDF: timeout</c>) when the idle timeout
/// fired, otherwise <c>DOWNLOAD_FAILED</c> without a status (a connection cut mid-body, or a
/// body short of its <c>Content-Length</c>). Once a read timed out, every later read fails the
/// same way. Length / Position / Seek are not supported because HTTP response bodies are not
/// seekable in the general case.
/// </remarks>
internal sealed class ResponseOwnedStream : Stream
{
    private readonly Stream _inner;
    private readonly HttpResponseMessage _response;
    private readonly CancellationToken _callToken;
    private readonly TimeSpan _idleTimeout;
    private volatile bool _timedOut;
    private bool _disposed;

    internal ResponseOwnedStream(Stream inner, HttpResponseMessage response, TimeSpan idleTimeout, CancellationToken callToken)
    {
        _inner = inner;
        _response = response;
        _callToken = callToken;
        _idleTimeout = idleTimeout;
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
        => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        ThrowIfTimedOut();
        // A synchronous read takes no token: when the idle timeout fires, the response is
        // released, which ends the read.
        using var idle = StartIdleTimer();
        using var abort = idle?.Token.UnsafeRegister(static state => ((ResponseOwnedStream)state!).AbortOnTimeout(), this);
        try
        {
            return _inner.Read(buffer);
        }
        catch (Exception ex) when (IsReadFailure(ex, idle, CancellationToken.None))
        {
            throw Failure(ex, idle, CancellationToken.None);
        }
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ThrowIfTimedOut();
        using var idle = StartIdleTimer();
        using var linked = idle is null ? null : CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, idle.Token);
        try
        {
            return await _inner.ReadAsync(buffer, linked?.Token ?? cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsReadFailure(ex, idle, cancellationToken))
        {
            throw Failure(ex, idle, cancellationToken);
        }
    }

    /// <summary>
    /// Copies through the mapped reads; a cancellation of <paramref name="cancellationToken"/>
    /// while <paramref name="destination"/> writes is <c>aborted</c> too, so a cancelled copy
    /// (and <c>RenderToFileAsync</c>) never throws a raw <see cref="OperationCanceledException"/>.
    /// Any other failure of the destination is its own and propagates unchanged.
    /// </summary>
    public override async Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
    {
        ValidateCopyToArguments(destination, bufferSize);
        var buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(bufferSize);
        try
        {
            int read;
            while ((read = await ReadAsync(buffer.AsMemory(0, bufferSize), cancellationToken).ConfigureAwait(false)) != 0)
            {
                try
                {
                    await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
                {
                    throw SdkErrors.Aborted(ex);
                }
            }
        }
        finally
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>A timer for one read, or <see langword="null"/> with an infinite timeout.</summary>
    private CancellationTokenSource? StartIdleTimer()
        => _idleTimeout == System.Threading.Timeout.InfiniteTimeSpan ? null : new CancellationTokenSource(_idleTimeout);

    private void AbortOnTimeout()
    {
        _timedOut = true;
        _response.Dispose();
    }

    private void ThrowIfTimedOut()
    {
        if (_timedOut && !_disposed)
            throw SdkErrors.DownloadTimeout(inner: null);
    }

    private bool TimedOut(CancellationTokenSource? idle)
    {
        if (idle?.IsCancellationRequested == true)
            _timedOut = true;
        return _timedOut;
    }

    private bool IsReadFailure(Exception ex, CancellationTokenSource? idle, CancellationToken readToken)
        => !_disposed
            && (SdkErrors.IsTransportFailure(ex)
                || _callToken.IsCancellationRequested
                || readToken.IsCancellationRequested
                || TimedOut(idle));

    private PoliPageException Failure(Exception ex, CancellationTokenSource? idle, CancellationToken readToken)
        => readToken.IsCancellationRequested
            ? SdkErrors.Aborted(ex)
            : PoliPageClient.DownloadFailure(ex, TimedOut(idle), _callToken);

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
