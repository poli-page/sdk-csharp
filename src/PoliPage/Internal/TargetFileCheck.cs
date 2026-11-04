using System.Runtime.InteropServices;
using System.Text;

namespace PoliPage.Internal;

/// <summary>
/// Checks, from its file system attributes and without opening it, that an existing target
/// of <see cref="PoliPageClient.RenderToFileAsync"/> can be replaced: it must be a regular
/// file (not a directory, a FIFO, a device or a socket) that the process may write. Opening a
/// FIFO to ask would block until a reader comes, and the rename would then replace the pipe.
/// </summary>
/// <remarks>
/// On Linux and macOS the file type, owner and mode come from <c>statx(2)</c> /
/// <c>stat(2)</c>, and the mode bits are read for the process's effective user: the owner's
/// bits when it owns the file, else the group's when one of its groups owns it, else the
/// others'. Root may write any regular file. Access control lists are not read. On Windows,
/// a directory or a device is not a regular file, and the <c>ReadOnly</c> attribute is not
/// writable. Elsewhere, or when the native call is not available, only a directory and the
/// <c>ReadOnly</c> attribute are checked.
/// </remarks>
internal static class TargetFileCheck
{
    private const int StatFileTypeMask = 0xF000; // S_IFMT
    private const int StatRegularFile = 0x8000; // S_IFREG
    private const int OwnerWrite = 0x80; // S_IWUSR
    private const int GroupWrite = 0x10; // S_IWGRP
    private const int OtherWrite = 0x02; // S_IWOTH

    /// <summary>
    /// Throws, for an existing <paramref name="targetPath"/>, an <see cref="IOException"/>
    /// when it is not a regular file and an <see cref="UnauthorizedAccessException"/> when
    /// the process may not write it. A missing target passes.
    /// </summary>
    internal static void EnsureReplaceable(string targetPath)
    {
        if (!File.Exists(targetPath) && !Directory.Exists(targetPath))
            return;

        var (isRegular, isWritable) = Inspect(targetPath);
        if (!isRegular)
            throw new IOException($"{targetPath}: not a regular file");
        if (!isWritable)
            throw new UnauthorizedAccessException($"Access to the path '{targetPath}' is denied.");
    }

    private static (bool IsRegular, bool IsWritable) Inspect(string path)
    {
        if (!OperatingSystem.IsWindows() && Native.TryStat(path, out var status))
            return ((status.Mode & StatFileTypeMask) == StatRegularFile, Native.MayWrite(status));

        var attributes = File.GetAttributes(path);
        var isRegular = !attributes.HasFlag(FileAttributes.Directory) && !attributes.HasFlag(FileAttributes.Device);
        // On Unix, the ReadOnly attribute is the mode's write bit for the effective user.
        var isWritable = !attributes.HasFlag(FileAttributes.ReadOnly)
            || (!OperatingSystem.IsWindows() && Environment.IsPrivilegedProcess);
        return (isRegular, isWritable);
    }

    /// <summary>The fields of a file's status the check reads.</summary>
    [StructLayout(LayoutKind.Auto)]
    private readonly record struct FileStatus(int Mode, uint Uid, uint Gid);

    private static class Native
    {
        private const int AtCurrentDirectory = -100; // AT_FDCWD on Linux; ignored for an absolute path
        private const uint StatxBasicFields = 0x01 | 0x02 | 0x08 | 0x10; // STATX_TYPE | STATX_MODE | STATX_UID | STATX_GID

        /// <summary>The file's status, following links, or <see langword="false"/> when unknown.</summary>
        internal static bool TryStat(string path, out FileStatus status)
        {
            status = default;
            var nativePath = Encoding.UTF8.GetBytes(path + "\0");
            try
            {
                if (OperatingSystem.IsLinux())
                {
                    if (Statx(AtCurrentDirectory, nativePath, 0, StatxBasicFields, out var statx) != 0
                        || (statx.Mask & StatxBasicFields) != StatxBasicFields)
                    {
                        return false;
                    }

                    status = new FileStatus(statx.Mode, statx.Uid, statx.Gid);
                    return true;
                }

                if (OperatingSystem.IsMacOS())
                {
                    // x64 keeps the 32-bit-inode stat under the plain name; arm64 has only the
                    // 64-bit-inode layout.
                    var result = RuntimeInformation.ProcessArchitecture == Architecture.X64
                        ? DarwinStatInode64(nativePath, out var stat)
                        : DarwinStat(nativePath, out stat);
                    if (result != 0)
                        return false;

                    status = new FileStatus(stat.Mode, stat.Uid, stat.Gid);
                    return true;
                }
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
            {
                // An older C library (statx needs glibc 2.28 or musl 1.2.5): the managed check.
            }

            return false;
        }

        /// <summary>Whether the mode lets the process's effective user write the file.</summary>
        internal static bool MayWrite(FileStatus status)
        {
            var userId = GetEffectiveUserId();
            if (userId == 0)
                return true;
            if (status.Uid == userId)
                return (status.Mode & OwnerWrite) != 0;
            if (InGroup(status.Gid))
                return (status.Mode & GroupWrite) != 0;
            return (status.Mode & OtherWrite) != 0;
        }

        private static bool InGroup(uint groupId)
        {
            if (GetEffectiveGroupId() == groupId)
                return true;

            var count = GetGroups(0, null);
            if (count <= 0)
                return false;
            var groups = new uint[count];
            count = GetGroups(count, groups);
            return count > 0 && Array.IndexOf(groups, groupId, 0, count) >= 0;
        }

        // struct statx: the same layout on every Linux architecture.
        [StructLayout(LayoutKind.Explicit, Size = 256)]
        private struct StatxBuffer
        {
            [FieldOffset(0)] public uint Mask;
            [FieldOffset(20)] public uint Uid;
            [FieldOffset(24)] public uint Gid;
            [FieldOffset(28)] public ushort Mode;
        }

        // Darwin's struct stat with 64-bit inodes (144 bytes; the buffer leaves room).
        [StructLayout(LayoutKind.Explicit, Size = 256)]
        private struct DarwinStatBuffer
        {
            [FieldOffset(4)] public ushort Mode;
            [FieldOffset(16)] public uint Uid;
            [FieldOffset(20)] public uint Gid;
        }

        [DllImport("libc", EntryPoint = "statx")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        private static extern int Statx(int directory, byte[] path, int flags, uint mask, out StatxBuffer buffer);

        [DllImport("libc", EntryPoint = "stat")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        private static extern int DarwinStat(byte[] path, out DarwinStatBuffer buffer);

        [DllImport("libc", EntryPoint = "stat$INODE64")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        private static extern int DarwinStatInode64(byte[] path, out DarwinStatBuffer buffer);

        [DllImport("libc", EntryPoint = "geteuid")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        private static extern uint GetEffectiveUserId();

        [DllImport("libc", EntryPoint = "getegid")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        private static extern uint GetEffectiveGroupId();

        [DllImport("libc", EntryPoint = "getgroups")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        private static extern int GetGroups(int size, uint[]? list);
    }
}
