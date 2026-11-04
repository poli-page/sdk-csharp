namespace PoliPage.Internal;

/// <summary>
/// Writes a file so that its target changes only once the whole content is written: the
/// bytes go to a temporary file in the target's directory, which <see cref="CommitAsync"/>
/// moves over the target. Disposed without a commit, it deletes the temporary file and leaves
/// the target as it was: an existing file keeps its content and a missing one is not created.
/// Every file system failure is <c>io_error</c>, the file system's exception as its
/// <see cref="Exception.InnerException"/>.
/// </summary>
/// <remarks>
/// A target that is a symbolic link is resolved first, a relative link against the real
/// directory that holds it: the file it points at is the one replaced, so the link stays a
/// link. An existing target that cannot be written (read-only, or a directory) fails at once,
/// before anything is written. An existing target keeps its permissions on Unix (its
/// mode is copied onto the temporary file before the move). The move gives the target a new
/// inode, so its owner becomes the writing user and any hard link to the old file keeps the
/// old content.
/// </remarks>
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
            var targetPath = ResolveLinks(path);
            EnsureWritableTarget(targetPath);
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
            KeepTargetMode();
            File.Move(_tempPath, _targetPath, overwrite: true);
            _committed = true;
        }
        catch (Exception ex) when (SdkErrors.IsFileSystemFailure(ex))
        {
            throw SdkErrors.IoError(ex);
        }
    }

    /// <summary>Most links followed in one path, as Linux allows (ELOOP past it).</summary>
    private const int MaxLinkHops = 40;

    /// <summary>
    /// The real path of <paramref name="path"/>: every symbolic link in it, the directories
    /// and the file itself, replaced by what it points at. A relative link target is resolved
    /// against the real directory that holds the link, as the operating system does, not
    /// against the path as written. A part that does not exist yet (a directory to create, a
    /// new file, the missing file a link points at) is kept as written.
    /// </summary>
    private static string ResolveLinks(string path)
    {
        var hops = 0;
        return Resolve(Absolute(path), ref hops);
    }

    // Unix resolves "dir/.." after following dir, so the path is not normalized up front
    // there; Windows normalizes ".." in the path as written, as Path.GetFullPath does.
    private static string Absolute(string path)
        => OperatingSystem.IsWindows() ? Path.GetFullPath(path) : Path.Combine(Directory.GetCurrentDirectory(), path);

    private static string Resolve(string absolutePath, ref int hops)
    {
        var root = Path.GetPathRoot(absolutePath)!;
        var current = root;
        var parts = absolutePath[root.Length..].Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            if (string.Equals(part, ".", StringComparison.Ordinal))
                continue;
            if (string.Equals(part, "..", StringComparison.Ordinal))
            {
                // `current` is already real, so its parent is the real parent.
                current = Path.GetDirectoryName(current) ?? current;
                continue;
            }

            var next = Path.Combine(current, part);
            var target = new FileInfo(next).LinkTarget;
            if (target is null)
            {
                current = next;
                continue;
            }

            if (++hops > MaxLinkHops)
                throw new IOException($"Too many levels of symbolic links: {next}");
            current = Resolve(Path.IsPathRooted(target) ? target : Path.Combine(current, target), ref hops);
        }

        return current;
    }

    /// <summary>
    /// Fails now, before any render, when the target exists but cannot be written: a
    /// read-only file, or a directory. The rename would replace a read-only file, so this
    /// opens it for writing (without truncating it) to ask the file system.
    /// </summary>
    private static void EnsureWritableTarget(string targetPath)
    {
        if (!File.Exists(targetPath) && !Directory.Exists(targetPath))
            return;

        using var probe = new FileStream(
            targetPath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
    }

    /// <summary>On Unix, gives the temporary file the mode of the target it replaces.</summary>
    private void KeepTargetMode()
    {
        if (OperatingSystem.IsWindows() || !File.Exists(_targetPath))
            return;

        File.SetUnixFileMode(_tempPath, File.GetUnixFileMode(_targetPath));
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
