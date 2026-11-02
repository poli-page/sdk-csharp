namespace PoliPage.Internal;

/// <summary>
/// Writes a file so that its target changes only once the whole content is written: the
/// bytes go to a temporary file in the target's directory, which <see cref="CommitAsync"/>
/// moves over the target. Disposed without a commit, it deletes the temporary file and leaves
/// the target as it was: an existing file keeps its content and a missing one is not created.
/// Every file system failure is <c>io_error</c>, the file system's exception as its
/// <see cref="Exception.InnerException"/>.
/// </summary>
internal sealed class AtomicFileWriter : IAsyncDisposable
{
    private readonly string _targetPath;
    private readonly string _tempPath;
    private readonly FileStream _stream;
    private bool _committed;

    private AtomicFileWriter(string targetPath, string tempPath, FileStream stream)
    {
        _targetPath = targetPath;
        _tempPath = tempPath;
        _stream = stream;
    }

    /// <summary>The temporary file, open for writing.</summary>
    internal Stream Stream => _stream;

    /// <summary>
    /// Creates the target's missing parent directories and a new temporary file next to the
    /// target.
    /// </summary>
    internal static AtomicFileWriter Open(string path)
    {
        try
        {
            var targetPath = Path.GetFullPath(path);
            var directory = Path.GetDirectoryName(targetPath) ?? targetPath;
            Directory.CreateDirectory(directory);

            // In the target's directory, so the final move stays on one file system and
            // replaces the target in one step.
            var tempPath = Path.Combine(directory, $".polipage-{Guid.NewGuid():N}.tmp");
            var stream = new FileStream(
                tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize: 81920, useAsync: true);
            return new AtomicFileWriter(targetPath, tempPath, stream);
        }
        catch (Exception ex) when (SdkErrors.IsFileSystemFailure(ex))
        {
            throw SdkErrors.IoError(ex);
        }
    }

    /// <summary>Flushes and closes the temporary file, then moves it over the target.</summary>
    internal async Task CommitAsync()
    {
        try
        {
            await _stream.FlushAsync().ConfigureAwait(false);
            await _stream.DisposeAsync().ConfigureAwait(false);
            File.Move(_tempPath, _targetPath, overwrite: true);
            _committed = true;
        }
        catch (Exception ex) when (SdkErrors.IsFileSystemFailure(ex))
        {
            throw SdkErrors.IoError(ex);
        }
    }

    /// <summary>Without a commit, closes and deletes the temporary file.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_committed)
            return;

        try
        {
            await _stream.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (SdkErrors.IsFileSystemFailure(ex))
        {
            // Closing flushes what is buffered, which can fail again (a full disk): the file
            // is discarded anyway, and the error that ended the write is already in flight.
        }

        try
        {
            File.Delete(_tempPath);
        }
        catch (Exception ex) when (SdkErrors.IsFileSystemFailure(ex))
        {
            // Best effort: the target is untouched either way, and the error that ended the
            // write is already in flight.
        }
    }
}
