using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Ytec.BrowserCapsule.Domain.BackupContainers;
using Ytec.BrowserCapsule.Domain.ProfileRestores;
using Ytec.BrowserCapsule.Infrastructure.ProfileRestores;
using Ytec.BrowserCapsule.SecurityTests.BackupContainers;

namespace Ytec.BrowserCapsule.SecurityTests.ProfileRestores;

public sealed class SafeRestoreFileApplierTests
{
  [Theory]
  [InlineData("chrome", "Login Data")]
  [InlineData("edge", "Login Data For Account")]
  [InlineData("firefox", "nested/KEY4.DB")]
  [InlineData("firefox", "logins.json")]
  public async Task ProtectedDestinationIsRejectedBeforeAnyOperationIsApplied(
      string browserId,
      string protectedPath)
  {
    using var temporary = new TemporaryDirectory();
    var source = temporary.GetPath("synthetic-source.bin");
    var target = temporary.GetPath("synthetic-target");
    var protectedTarget = Path.Combine(target, protectedPath.Replace('/', Path.DirectorySeparatorChar));
    Directory.CreateDirectory(Path.GetDirectoryName(protectedTarget)!);
    await File.WriteAllTextAsync(protectedTarget, "synthetic-protected-before");
    var bytes = "synthetic-replacement"u8.ToArray();
    await File.WriteAllBytesAsync(source, bytes);
    var benign = new RestoreFileOperation(
        source, target, "Preferences", bytes.LongLength,
        Convert.ToHexStringLower(SHA256.HashData(bytes)), "settings", "Synthetic profile")
    {
      BrowserId = browserId,
    };
    var protectedOperation = benign with { RelativeTargetPath = protectedPath };

    await Assert.ThrowsAsync<RestorePlanException>(() => new SafeRestoreFileApplier().ApplyAsync(
        [benign, protectedOperation], _ => Task.CompletedTask, null, CancellationToken.None));

    Assert.Equal("synthetic-protected-before", await File.ReadAllTextAsync(protectedTarget));
    Assert.False(File.Exists(Path.Combine(target, "Preferences")));
  }

  [Fact]
  public async Task ActualWindowsShortNameCannotOverwriteProtectedDestination()
  {
    using var temporary = new TemporaryDirectory();
    var source = temporary.GetPath("synthetic-alias-source.bin");
    var target = temporary.GetPath("synthetic-alias-target");
    Directory.CreateDirectory(target);
    var protectedTarget = Path.Combine(target, "Login Data");
    await File.WriteAllTextAsync(protectedTarget, "synthetic-protected-before");
    var buffer = new char[32768];
    var count = GetShortPathName(protectedTarget, buffer, (uint)buffer.Length);
    Assert.InRange(count, 1u, (uint)buffer.Length - 1);
    var alias = Path.GetFileName(new string(buffer, 0, (int)count));
    Assert.False(string.Equals("Login Data", alias, StringComparison.OrdinalIgnoreCase),
        "The test volume has no actual short-name alias; this environment cannot verify this test.");
    var bytes = "synthetic-replacement"u8.ToArray();
    await File.WriteAllBytesAsync(source, bytes);
    var operation = new RestoreFileOperation(
        source, target, alias, bytes.LongLength,
        Convert.ToHexStringLower(SHA256.HashData(bytes)), "settings", "Synthetic profile")
    {
      BrowserId = "chrome",
    };

    await Assert.ThrowsAsync<RestorePlanException>(() => new SafeRestoreFileApplier().ApplyAsync(
        [operation], _ => Task.CompletedTask, null, CancellationToken.None));
    Assert.Equal("synthetic-protected-before", await File.ReadAllTextAsync(protectedTarget));
  }

  [DllImport("kernel32.dll", EntryPoint = "GetShortPathNameW", CharSet = CharSet.Unicode, ExactSpelling = true)]
  private static extern uint GetShortPathName(string path, [Out] char[] buffer, uint capacity);

  [Fact]
  public async Task TraversalIsRejectedWithoutWritingOutsideTarget()
  {
    using var temporary = new TemporaryDirectory();
    var source = temporary.GetPath("source.bin");
    var target = temporary.GetPath("target");
    await File.WriteAllTextAsync(source, "safe-source");
    Directory.CreateDirectory(target);
    var bytes = await File.ReadAllBytesAsync(source);
    var operation = new RestoreFileOperation(
        source,
        target,
        "../outside.txt",
        bytes.LongLength,
        Convert.ToHexStringLower(SHA256.HashData(bytes)),
        "settings",
        "合成プロファイル");
    var applier = new SafeRestoreFileApplier();

    await Assert.ThrowsAsync<InvalidBackupEntryPathException>(
        () => applier.ApplyAsync(
            [operation],
            _ => Task.CompletedTask,
            progress: null,
            CancellationToken.None));

    Assert.False(File.Exists(temporary.GetPath("outside.txt")));
  }

  [Fact]
  public async Task HashMismatchLeavesExistingTargetUnchanged()
  {
    using var temporary = new TemporaryDirectory();
    var source = temporary.GetPath("source.bin");
    var target = temporary.GetPath("target");
    Directory.CreateDirectory(target);
    await File.WriteAllTextAsync(source, "new-data");
    var destination = Path.Combine(target, "Preferences");
    await File.WriteAllTextAsync(destination, "original-data");
    var operation = new RestoreFileOperation(
        source,
        target,
        "Preferences",
        new FileInfo(source).Length,
        new string('0', 64),
        "settings",
        "合成プロファイル");
    var applier = new SafeRestoreFileApplier();

    await Assert.ThrowsAsync<BackupContainerIntegrityException>(
        () => applier.ApplyAsync(
            [operation],
            _ => Task.CompletedTask,
            progress: null,
            CancellationToken.None));

    Assert.Equal(
        "original-data",
        await File.ReadAllTextAsync(destination));
    Assert.Empty(
        Directory.EnumerateFiles(
            target,
            "*.ybc-partial",
            SearchOption.TopDirectoryOnly));
  }
}
