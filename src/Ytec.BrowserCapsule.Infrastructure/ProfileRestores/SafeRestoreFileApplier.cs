using System.Buffers;
using System.Diagnostics;
using System.Security.Cryptography;
using Ytec.BrowserCapsule.Domain.BackupContainers;
using Ytec.BrowserCapsule.Domain.ProfileBackups;
using Ytec.BrowserCapsule.Domain.ProfileRestores;

namespace Ytec.BrowserCapsule.Infrastructure.ProfileRestores;

/// <summary>
/// 一時ファイルを同一ディレクトリーへ検証後、原子的な名前変更で置換します。
/// </summary>
public sealed class SafeRestoreFileApplier : IRestoreFileApplier
{
  private const int BufferSize = 128 * 1024;

  public async Task ApplyAsync(
      IReadOnlyList<RestoreFileOperation> operations,
      Func<CancellationToken, Task> ensureBrowserClosedAsync,
      IProgress<ProfileRestoreProgress>? progress,
      CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(operations);
    ArgumentNullException.ThrowIfNull(ensureBrowserClosedAsync);
    ValidateNoCollisions(operations);
    foreach (var operation in operations)
    {
      ValidateProtectedDestination(operation);
    }
    var lastProgressTimestamp = Stopwatch.GetTimestamp();

    for (var index = 0; index < operations.Count; index++)
    {
      cancellationToken.ThrowIfCancellationRequested();
      if (index % 20 == 0)
      {
        await ensureBrowserClosedAsync(cancellationToken)
            .ConfigureAwait(false);
      }

      var operation = operations[index];
      await ApplyOneAsync(
          operation,
          cancellationToken).ConfigureAwait(false);
      var now = Stopwatch.GetTimestamp();
      if (index == operations.Count - 1
          || now - lastProgressTimestamp >= Stopwatch.Frequency / 10)
      {
        progress?.Report(new ProfileRestoreProgress(
            ProfileRestoreStage.Applying,
            index + 1,
            operations.Count,
            operation.TargetProfileDisplayName));
        lastProgressTimestamp = now;
      }
    }

    await ensureBrowserClosedAsync(cancellationToken)
        .ConfigureAwait(false);
  }

  private static void ValidateProtectedDestination(RestoreFileOperation operation)
  {
    if (operation.BrowserId is null)
    {
      return;
    }

    var relativePath = BackupEntryPathValidator.Validate(
        operation.RelativeTargetPath.Replace(Path.DirectorySeparatorChar, '/'));
    ProtectedBrowserFilePolicy.ValidateRestorePath(operation.BrowserId, relativePath);
    var root = Path.GetFullPath(operation.TargetRoot);
    var destination = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
    EnsureWithinRoot(root, destination);
    var canonicalRoot = WindowsRestorePathResolver.Resolve(root, allowMissing: false);
    var canonicalDestination = WindowsRestorePathResolver.Resolve(destination, allowMissing: true);
    EnsureWithinRoot(canonicalRoot, canonicalDestination);
    ProtectedBrowserFilePolicy.ValidateRestorePath(
        operation.BrowserId,
        Path.GetRelativePath(canonicalRoot, canonicalDestination).Replace(Path.DirectorySeparatorChar, '/'));
  }

  private static async Task ApplyOneAsync(
      RestoreFileOperation operation,
      CancellationToken cancellationToken)
  {
    var sourcePath = Path.GetFullPath(operation.SourcePath);
    var targetRoot = Path.GetFullPath(operation.TargetRoot);
    if (!File.Exists(sourcePath)
        || IsReparsePoint(sourcePath)
        || !Directory.Exists(targetRoot)
        || IsReparsePoint(targetRoot))
    {
      throw new RestorePlanException(
          "復元元または復元先を安全に確認できません。");
    }

    var normalizedRelative = BackupEntryPathValidator.Validate(
        operation.RelativeTargetPath.Replace(
            Path.DirectorySeparatorChar,
            '/'));
    var destinationPath = Path.GetFullPath(
        Path.Combine(
            targetRoot,
            normalizedRelative.Replace(
                '/',
                Path.DirectorySeparatorChar)));
    EnsureWithinRoot(targetRoot, destinationPath);
    var parent = Path.GetDirectoryName(destinationPath)
        ?? throw new RestorePlanException(
            "復元先フォルダーを決定できません。");
    EnsureSafeDirectoryTree(targetRoot, parent);
    if (File.Exists(destinationPath)
        && IsReparsePoint(destinationPath))
    {
      throw new RestorePlanException(
          "復元先にリンクまたはジャンクションがあります。");
    }

    ValidateProtectedDestination(operation);
    var temporaryPath = Path.Combine(
        parent,
        $".{Path.GetFileName(destinationPath)}."
        + $"{Guid.NewGuid():N}.ybc-partial");
    try
    {
      var actualHash = await CopyAndHashAsync(
          sourcePath,
          temporaryPath,
          operation.Length,
          cancellationToken).ConfigureAwait(false);
      try
      {
        if (!string.Equals(
            Convert.ToHexStringLower(actualHash),
            operation.Sha256,
            StringComparison.Ordinal))
        {
          throw new BackupContainerIntegrityException();
        }
      }
      finally
      {
        CryptographicOperations.ZeroMemory(actualHash);
      }

      ValidateProtectedDestination(operation);
      File.Move(
          temporaryPath,
          destinationPath,
          overwrite: true);
      var destinationInfo = new FileInfo(destinationPath);
      destinationInfo.Refresh();
      if (!destinationInfo.Exists
          || destinationInfo.Length != operation.Length
          || IsReparsePoint(destinationPath))
      {
        throw new IOException(
            "復元後のファイル状態を確認できません。");
      }

      var postRestoreHash = await ComputeHashAsync(
          destinationPath,
          cancellationToken).ConfigureAwait(false);
      try
      {
        if (!string.Equals(
            Convert.ToHexStringLower(postRestoreHash),
            operation.Sha256,
            StringComparison.Ordinal))
        {
          throw new IOException(
              "復元後のファイルハッシュが一致しません。");
        }
      }
      finally
      {
        CryptographicOperations.ZeroMemory(postRestoreHash);
      }
    }
    finally
    {
      TryDeletePartial(temporaryPath);
    }
  }

  private static async Task<byte[]> CopyAndHashAsync(
      string sourcePath,
      string destinationPath,
      long expectedLength,
      CancellationToken cancellationToken)
  {
    await using var input = new FileStream(
        sourcePath,
        FileMode.Open,
        FileAccess.Read,
        FileShare.Read,
        BufferSize,
        FileOptions.Asynchronous | FileOptions.SequentialScan);
    await using var output = new FileStream(
        destinationPath,
        FileMode.CreateNew,
        FileAccess.Write,
        FileShare.None,
        BufferSize,
        FileOptions.Asynchronous | FileOptions.SequentialScan);
    if (input.Length != expectedLength)
    {
      throw new BackupContainerIntegrityException();
    }

    var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
    using var hasher = IncrementalHash.CreateHash(
        HashAlgorithmName.SHA256);
    try
    {
      long copied = 0;
      while (true)
      {
        var read = await input.ReadAsync(
            buffer.AsMemory(0, buffer.Length),
            cancellationToken).ConfigureAwait(false);
        if (read == 0)
        {
          break;
        }

        copied = checked(copied + read);
        if (copied > expectedLength)
        {
          throw new BackupContainerIntegrityException();
        }

        hasher.AppendData(buffer, 0, read);
        await output.WriteAsync(
            buffer.AsMemory(0, read),
            cancellationToken).ConfigureAwait(false);
      }

      if (copied != expectedLength)
      {
        throw new BackupContainerIntegrityException();
      }

      await output.FlushAsync(cancellationToken).ConfigureAwait(false);
      output.Flush(flushToDisk: true);
      return hasher.GetHashAndReset();
    }
    finally
    {
      CryptographicOperations.ZeroMemory(buffer.AsSpan());
      ArrayPool<byte>.Shared.Return(buffer);
    }
  }

  private static async Task<byte[]> ComputeHashAsync(
      string path,
      CancellationToken cancellationToken)
  {
    await using var input = new FileStream(
        path,
        FileMode.Open,
        FileAccess.Read,
        FileShare.Read,
        BufferSize,
        FileOptions.Asynchronous | FileOptions.SequentialScan);
    using var hasher = IncrementalHash.CreateHash(
        HashAlgorithmName.SHA256);
    var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
    try
    {
      while (true)
      {
        var read = await input.ReadAsync(
            buffer.AsMemory(0, buffer.Length),
            cancellationToken).ConfigureAwait(false);
        if (read == 0)
        {
          return hasher.GetHashAndReset();
        }

        hasher.AppendData(buffer, 0, read);
      }
    }
    finally
    {
      CryptographicOperations.ZeroMemory(buffer.AsSpan());
      ArrayPool<byte>.Shared.Return(buffer);
    }
  }

  private static void ValidateNoCollisions(
      IReadOnlyList<RestoreFileOperation> operations)
  {
    foreach (var group in operations.GroupBy(
        operation => Path.GetFullPath(operation.TargetRoot),
        StringComparer.OrdinalIgnoreCase))
    {
      BackupEntryPathValidator.ValidateNoCaseInsensitiveCollisions(
          group.Select(operation => operation.RelativeTargetPath));
    }
  }

  private static void EnsureWithinRoot(
      string targetRoot,
      string destinationPath)
  {
    var rootWithSeparator = targetRoot.EndsWith(
        Path.DirectorySeparatorChar)
        ? targetRoot
        : targetRoot + Path.DirectorySeparatorChar;
    if (!destinationPath.StartsWith(
        rootWithSeparator,
        StringComparison.OrdinalIgnoreCase))
    {
      throw new RestorePlanException(
          "復元先プロファイルの外へ書き込もうとしました。");
    }
  }

  private static void EnsureSafeDirectoryTree(
      string targetRoot,
      string directory)
  {
    var relative = Path.GetRelativePath(targetRoot, directory);
    if (relative.StartsWith("..", StringComparison.Ordinal)
        || Path.IsPathFullyQualified(relative))
    {
      throw new RestorePlanException(
          "復元先プロファイルの外へ書き込もうとしました。");
    }

    var current = targetRoot;
    foreach (var segment in relative.Split(
        Path.DirectorySeparatorChar,
        StringSplitOptions.RemoveEmptyEntries))
    {
      current = Path.Combine(current, segment);
      if (!Directory.Exists(current))
      {
        Directory.CreateDirectory(current);
      }

      if (IsReparsePoint(current))
      {
        throw new RestorePlanException(
            "復元先にリンクまたはジャンクションがあります。");
      }
    }
  }

  private static bool IsReparsePoint(string path)
  {
    return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
  }

  private static void TryDeletePartial(string path)
  {
    try
    {
      if (File.Exists(path))
      {
        File.Delete(path);
      }
    }
    catch (Exception exception) when (
        exception is IOException
        or UnauthorizedAccessException)
    {
      // 次回の手動清掃対象。完成ファイルとしては扱いません。
    }
  }
}
