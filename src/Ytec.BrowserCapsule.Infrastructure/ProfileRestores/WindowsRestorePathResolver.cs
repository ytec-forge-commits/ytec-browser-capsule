using System.Runtime.InteropServices;
using Ytec.BrowserCapsule.Domain.ProfileRestores;

namespace Ytec.BrowserCapsule.Infrastructure.ProfileRestores;

internal static class WindowsRestorePathResolver
{
  public static string Resolve(string absolutePath, bool allowMissing)
  {
    if (!OperatingSystem.IsWindows())
    {
      throw new RestorePlanException("復元先の保護検査にはWindowsが必要です。");
    }

    var current = Path.GetFullPath(absolutePath);
    var missingSegments = new Stack<string>();
    var buffer = new char[32768];
    while (true)
    {
      var extendedPath = current.StartsWith(@"\\?\", StringComparison.Ordinal)
          ? current
          : @"\\?\" + current;
      var length = GetLongPathName(extendedPath, buffer, (uint)buffer.Length);
      if (length > 0 && length < buffer.Length)
      {
        var resolved = new string(buffer, 0, (int)length);
        if (resolved.StartsWith(@"\\?\", StringComparison.Ordinal))
        {
          resolved = resolved[4..];
        }

        while (missingSegments.TryPop(out var segment))
        {
          resolved = Path.Combine(resolved, segment);
        }

        return Path.GetFullPath(resolved);
      }

      var error = Marshal.GetLastPInvokeError();
      if (length != 0 || !allowMissing || error is not (2 or 3))
      {
        throw new RestorePlanException("復元先の実効ファイル名を安全に確認できません。");
      }

      // Resolve an existing ancestor, then append only the genuinely absent segments.
      var parent = Path.GetDirectoryName(current);
      if (parent is null || parent == current)
      {
        throw new RestorePlanException("復元先の実効ファイル名を安全に確認できません。");
      }

      missingSegments.Push(Path.GetFileName(current));
      current = parent;
    }
  }

  [DllImport("kernel32.dll", EntryPoint = "GetLongPathNameW", CharSet = CharSet.Unicode,
      ExactSpelling = true, SetLastError = true)]
  private static extern uint GetLongPathName(string path, [Out] char[] buffer, uint capacity);
}
