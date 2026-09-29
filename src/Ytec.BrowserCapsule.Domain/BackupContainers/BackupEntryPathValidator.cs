using System.Text;

namespace Ytec.BrowserCapsule.Domain.BackupContainers;

/// <summary>
/// コンテナ内パスをWindowsへ安全に復元できる相対形式へ制限します。
/// </summary>
public static class BackupEntryPathValidator
{
  public const int MaximumPathUtf8Bytes = 4096;
  public const int MaximumSegmentCharacters = 255;

  private static readonly HashSet<string> ReservedDeviceNames =
      CreateReservedDeviceNames();

  public static string Validate(string entryPath)
  {
    if (string.IsNullOrWhiteSpace(entryPath))
    {
      throw new InvalidBackupEntryPathException(
          "バックアップ内のパスが空です。");
    }

    if (entryPath.StartsWith('/')
        || entryPath.StartsWith('\\')
        || entryPath.Contains('\\')
        || (entryPath.Length >= 2
            && char.IsAsciiLetter(entryPath[0])
            && entryPath[1] == ':')
        || entryPath.Contains(':'))
    {
      throw new InvalidBackupEntryPathException(
          "バックアップ内のパスは安全な相対パスではありません。");
    }

    if (Encoding.UTF8.GetByteCount(entryPath) > MaximumPathUtf8Bytes)
    {
      throw new InvalidBackupEntryPathException(
          "バックアップ内のパスが長すぎます。");
    }

    var segments = entryPath.Split(
        '/',
        StringSplitOptions.None);
    foreach (var segment in segments)
    {
      ValidateSegment(segment);
    }

    return entryPath;
  }

  public static void ValidateNoCaseInsensitiveCollisions(
      IEnumerable<string> entryPaths)
  {
    ArgumentNullException.ThrowIfNull(entryPaths);
    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    foreach (var path in entryPaths)
    {
      var validated = Validate(path);
      if (!seen.Add(validated))
      {
        throw new InvalidBackupEntryPathException(
            "大文字と小文字だけが異なる重複パスがあります。");
      }
    }
  }

  private static void ValidateSegment(string segment)
  {
    if (segment.Length == 0
        || segment is "." or ".."
        || segment.Length > MaximumSegmentCharacters
        || segment.EndsWith(' ')
        || segment.EndsWith('.'))
    {
      throw new InvalidBackupEntryPathException(
          "バックアップ内のパス区切りまたは末尾が安全ではありません。");
    }

    foreach (var character in segment)
    {
      if (char.IsControl(character)
          || character is '<' or '>' or '"' or '|' or '?' or '*')
      {
        throw new InvalidBackupEntryPathException(
            "バックアップ内のパスに使用できない文字があります。");
      }
    }

    var deviceCandidate = segment.Split('.', 2)[0];
    if (ReservedDeviceNames.Contains(deviceCandidate))
    {
      throw new InvalidBackupEntryPathException(
          "バックアップ内のパスにWindows予約名があります。");
    }
  }

  private static HashSet<string> CreateReservedDeviceNames()
  {
    var names = new HashSet<string>(
        ["CON", "PRN", "AUX", "NUL"],
        StringComparer.OrdinalIgnoreCase);

    for (var index = 1; index <= 9; index++)
    {
      names.Add($"COM{index}");
      names.Add($"LPT{index}");
    }

    return names;
  }
}
