using Ytec.BrowserCapsule.Domain.BackupContainers;
using Ytec.BrowserCapsule.Domain.ProfileBackups;

namespace Ytec.BrowserCapsule.Application.ProfileRestores;

/// <summary>
/// 検証済みマニフェストから、復元画面で選べる項目を判定します。
/// </summary>
public static class RestoreComponentSelectionPolicy
{
  public static BackupComponent GetAvailableComponents(
      BackupManifest manifest)
  {
    ArgumentNullException.ThrowIfNull(manifest);

    var available = BackupComponent.None;
    foreach (var profile in manifest.Profiles)
    {
      foreach (var file in profile.Files)
      {
        available |= ToComponent(file.Component);
      }

      if (profile.PasswordCsv?.Included is true)
      {
        available |= BackupComponent.PasswordCsv;
      }
    }

    return available;
  }

  private static BackupComponent ToComponent(string? component)
  {
    return component switch
    {
      null => BackupComponent.FullProfile,
      "settings" => BackupComponent.Settings,
      "bookmarks" => BackupComponent.Bookmarks,
      "history" => BackupComponent.History,
      "extensions" => BackupComponent.Extensions,
      "cookies" => BackupComponent.CookiesAndSiteData,
      "sessions" => BackupComponent.Sessions,
      "fullProfile" => BackupComponent.FullProfile,
      "passwordCsv" => BackupComponent.PasswordCsv,
      _ => BackupComponent.None,
    };
  }
}
