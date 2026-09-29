using Ytec.BrowserCapsule.Domain.BrowserDiscovery;

namespace Ytec.BrowserCapsule.Infrastructure.BrowserDiscovery;

/// <summary>
/// リパースポイントを追跡せず、プロファイル配下のファイル容量を集計します。
/// </summary>
public sealed class ProfileSizeEstimator : IProfileSizeEstimator
{
  public Task<ProfileSizeEstimate> EstimateAsync(
      string profilePath,
      CancellationToken cancellationToken)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(profilePath);

    return Task.Run(
        () => Estimate(profilePath, cancellationToken),
        cancellationToken);
  }

  private static ProfileSizeEstimate Estimate(
      string profilePath,
      CancellationToken cancellationToken)
  {
    var pendingDirectories = new Stack<string>();
    pendingDirectories.Push(profilePath);

    long bytes = 0;
    long fileCount = 0;
    var skippedEntryCount = 0;

    while (pendingDirectories.Count > 0)
    {
      cancellationToken.ThrowIfCancellationRequested();
      var directory = pendingDirectories.Pop();

      if (!TryReadAttributes(directory, out var directoryAttributes)
          || IsReparsePoint(directoryAttributes))
      {
        skippedEntryCount++;
        continue;
      }

      IEnumerable<string> entries;
      try
      {
        entries = Directory.EnumerateFileSystemEntries(
            directory,
            "*",
            SearchOption.TopDirectoryOnly);
      }
      catch (Exception exception) when (
          exception is IOException
          or UnauthorizedAccessException
          or DirectoryNotFoundException)
      {
        skippedEntryCount++;
        continue;
      }

      try
      {
        foreach (var entry in entries)
        {
          cancellationToken.ThrowIfCancellationRequested();
          if (!TryReadAttributes(entry, out var attributes)
              || IsReparsePoint(attributes))
          {
            skippedEntryCount++;
            continue;
          }

          if ((attributes & FileAttributes.Directory) != 0)
          {
            pendingDirectories.Push(entry);
            continue;
          }

          try
          {
            bytes = checked(bytes + new FileInfo(entry).Length);
            fileCount++;
          }
          catch (Exception exception) when (
              exception is IOException
              or UnauthorizedAccessException
              or FileNotFoundException)
          {
            skippedEntryCount++;
          }
        }
      }
      catch (Exception exception) when (
          exception is IOException
          or UnauthorizedAccessException
          or DirectoryNotFoundException)
      {
        skippedEntryCount++;
      }
    }

    return new ProfileSizeEstimate(
        bytes,
        fileCount,
        skippedEntryCount,
        IsComplete: skippedEntryCount == 0);
  }

  private static bool TryReadAttributes(
      string path,
      out FileAttributes attributes)
  {
    try
    {
      attributes = File.GetAttributes(path);
      return true;
    }
    catch (Exception exception) when (
        exception is IOException
        or UnauthorizedAccessException
        or ArgumentException
        or NotSupportedException)
    {
      attributes = default;
      return false;
    }
  }

  private static bool IsReparsePoint(FileAttributes attributes)
  {
    return (attributes & FileAttributes.ReparsePoint) != 0;
  }
}
