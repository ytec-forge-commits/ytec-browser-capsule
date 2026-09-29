namespace Ytec.BrowserCapsule.Browsers.Common.BrowserDiscovery;

/// <summary>
/// プロファイル検出時にリンク先へ移動しないための共通判定です。
/// </summary>
public static class FileSystemSafety
{
  public static bool IsReparsePoint(string path)
  {
    try
    {
      return ShouldSkip(File.GetAttributes(path));
    }
    catch (Exception exception) when (
        exception is IOException
        or UnauthorizedAccessException
        or ArgumentException
        or NotSupportedException)
    {
      return true;
    }
  }

  public static bool ShouldSkip(FileAttributes attributes)
  {
    return (attributes & FileAttributes.ReparsePoint) != 0;
  }

  public static bool IsDirectChildDirectory(string rootPath, string candidatePath)
  {
    try
    {
      var root = EnsureTrailingSeparator(Path.GetFullPath(rootPath));
      var candidate = Path.GetFullPath(candidatePath);
      var parent = Directory.GetParent(candidate)?.FullName;

      return parent is not null
          && string.Equals(
              EnsureTrailingSeparator(parent),
              root,
              StringComparison.OrdinalIgnoreCase)
          && Directory.Exists(candidate)
          && !IsReparsePoint(candidate);
    }
    catch (Exception exception) when (
        exception is IOException
        or UnauthorizedAccessException
        or ArgumentException
        or NotSupportedException)
    {
      return false;
    }
  }

  public static bool IsWithinRoot(string rootPath, string candidatePath)
  {
    try
    {
      var root = EnsureTrailingSeparator(Path.GetFullPath(rootPath));
      var candidate = Path.GetFullPath(candidatePath);
      return candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }
    catch (Exception exception) when (
        exception is IOException
        or UnauthorizedAccessException
        or ArgumentException
        or NotSupportedException)
    {
      return false;
    }
  }

  private static string EnsureTrailingSeparator(string path)
  {
    return Path.EndsInDirectorySeparator(path)
        ? path
        : path + Path.DirectorySeparatorChar;
  }
}
