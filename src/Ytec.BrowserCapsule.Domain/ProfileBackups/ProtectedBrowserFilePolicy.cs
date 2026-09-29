using System.Text.Json;
using Ytec.BrowserCapsule.Domain.BackupContainers;
using Ytec.BrowserCapsule.Domain.ProfileRestores;

namespace Ytec.BrowserCapsule.Domain.ProfileBackups;

/// <summary>
/// バックアップと復元で共有する、保護対象ファイルの除外規則です。
/// </summary>
public static class ProtectedBrowserFilePolicy
{
  private const string ResourceName =
      "Ytec.BrowserCapsule.Domain.ProfileBackups.protected-browser-files.json";
  private static readonly Lazy<ProtectedFileConfiguration> Configuration = new(LoadConfiguration);

  public static IReadOnlySet<string> ChromiumFileNames =>
      new HashSet<string>(Configuration.Value.Chromium, StringComparer.OrdinalIgnoreCase);

  public static IReadOnlySet<string> FirefoxFileNames =>
      new HashSet<string>(Configuration.Value.Firefox, StringComparer.OrdinalIgnoreCase);

  public static void ValidateRestorePath(string browserId, string relativePath)
  {
    var validated = BackupEntryPathValidator.Validate(relativePath);
    var protectedNames = browserId switch
    {
      "chrome" or "edge" => ChromiumFileNames,
      "firefox" => FirefoxFileNames,
      _ => throw new RestorePlanException("復元先ブラウザーの保護規則がありません。"),
    };
    if (validated.Split('/').Any(protectedNames.Contains))
    {
      throw new RestorePlanException("このバックアップには復元できない保護対象ファイルがあります。");
    }
  }

  private static ProtectedFileConfiguration LoadConfiguration()
  {
    using var stream = typeof(ProtectedBrowserFilePolicy).Assembly.GetManifestResourceStream(ResourceName)
        ?? throw new InvalidOperationException("保護対象外ファイル設定が見つかりません。");
    return JsonSerializer.Deserialize<ProtectedFileConfiguration>(stream)
        ?? throw new InvalidOperationException("保護対象外ファイル設定を読み取れません。");
  }

  private sealed record ProtectedFileConfiguration(
      IReadOnlyList<string> Chromium,
      IReadOnlyList<string> Firefox);
}
