using FluentAssertions;
using PoliPage.Internal;
using Xunit;
using static PoliPage.Internal.TargetFileCheck;

namespace PoliPage.Tests;

/// <summary>The decisions behind <c>RenderToFileAsync</c>'s check of an existing target.</summary>
public sealed class TargetFileCheckTests : IDisposable
{
    private const uint Me = 501;
    private const uint MyGroup = 20;
    private const int Regular = 0x8000;

    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"polipage-target-check-{Guid.NewGuid():N}");

    public TargetFileCheckTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private static bool InMyGroup(uint gid) => gid == MyGroup;

    [Theory]
    [InlineData(0x8000 | 0x1A4, true)] // -rw-r--r--
    [InlineData(0x4000 | 0x1ED, false)] // directory
    [InlineData(0x1000 | 0x1A4, false)] // FIFO
    [InlineData(0x2000 | 0x1B6, false)] // character device
    [InlineData(0x6000 | 0x1B0, false)] // block device
    [InlineData(0xC000 | 0x1ED, false)] // socket
    [InlineData(0xA000 | 0x1FF, false)] // symbolic link (never reached: links are resolved first)
    public void Only_a_regular_file_is_replaceable(int mode, bool regular)
        => IsRegular(mode).Should().Be(regular);

    [Theory]
    [InlineData(0x180, Me, 999u, true)] // rw------- owned by me
    [InlineData(0x112, Me, 999u, false)] // r---w--w- owned by me: the owner's bit decides
    [InlineData(0x1B0, 7u, MyGroup, true)] // rw-rw---- my group's
    [InlineData(0x186, 7u, MyGroup, false)] // rw----rw- my group's: the group's bit decides
    [InlineData(0x182, 7u, 999u, true)] // rw-----w- someone else's: the others' bit
    [InlineData(0x1B0, 7u, 999u, false)] // rw-rw---- someone else's
    public void The_effective_users_class_of_bits_decides(int permissions, uint uid, uint gid, bool writable)
        => MayWrite(new FileStatus(Regular | permissions, uid, gid), Me, InMyGroup).Should().Be(writable);

    [Fact]
    public void Root_may_write_any_regular_file()
        => MayWrite(new FileStatus(Regular | 0x124, 7, 999), userId: 0, InMyGroup).Should().BeTrue();

    [Theory]
    [InlineData(FileAttributes.Normal, false, true, true)]
    [InlineData(FileAttributes.Archive, false, true, true)]
    [InlineData(FileAttributes.ReadOnly, false, true, false)]
    [InlineData(FileAttributes.ReadOnly, true, true, true)]
    [InlineData(FileAttributes.Directory, false, false, true)]
    [InlineData(FileAttributes.Device, false, false, true)]
    public void The_attribute_check_refuses_directories_devices_and_read_only_files(
        FileAttributes attributes, bool privileged, bool regular, bool writable)
        => FromAttributes(attributes, privileged).Should().Be((regular, writable));

    [UnixFact]
    public void The_effective_group_is_one_of_the_process_groups()
        => Native.InGroup(Native.GetEffectiveGroupId()).Should().BeTrue();

    [UnixFact]
    public void A_group_the_process_is_not_in_is_not_one_of_its_groups()
        => Native.InGroup(uint.MaxValue - 7).Should().BeFalse();

    [Fact]
    public void A_missing_target_passes()
    {
        var act = () => EnsureReplaceable(Path.Combine(_directory, "missing.pdf"));

        act.Should().NotThrow();
    }

    [Fact]
    public void A_writable_regular_file_passes()
    {
        var path = Path.Combine(_directory, "invoice.pdf");
        File.WriteAllBytes(path, [1]);

        var act = () => EnsureReplaceable(path);

        act.Should().NotThrow();
    }

    [Fact]
    public void A_directory_is_not_a_regular_file()
    {
        var act = () => EnsureReplaceable(_directory);

        act.Should().Throw<IOException>().WithMessage($"{_directory}: not a regular file");
    }
}
