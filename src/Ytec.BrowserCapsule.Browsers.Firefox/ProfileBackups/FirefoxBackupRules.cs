using Ytec.BrowserCapsule.Browsers.Common.ProfileBackups;
using Ytec.BrowserCapsule.Domain.ProfileBackups;

namespace Ytec.BrowserCapsule.Browsers.Firefox.ProfileBackups;

/// <summary>
/// Firefox Stable / ESRのコンポーネント許可リストと除外規則です。
/// </summary>
public static class FirefoxBackupRules
{
  public static BrowserBackupRuleSet Create()
  {
    var excludedFiles = new HashSet<string>(
        ProtectedBrowserFileExclusions.FirefoxFileNames,
        StringComparer.OrdinalIgnoreCase)
    {
      "parent.lock",
      "lock",
      ".parentlock",
    };

    return new BrowserBackupRuleSet(
        ComponentPaths:
        [
          new(BackupComponent.Settings, "prefs.js", IsRequired: true),
          new(BackupComponent.Settings, "user.js", IsRequired: false),
          new(BackupComponent.Settings, "containers.json", IsRequired: false),
          new(BackupComponent.Settings, "handlers.json", IsRequired: false),
          new(BackupComponent.Settings, "permissions.sqlite", IsRequired: false),
          new(BackupComponent.Settings, "content-prefs.sqlite", IsRequired: false),
          new(BackupComponent.Settings, "formhistory.sqlite", IsRequired: false),
          new(BackupComponent.Settings, "search.json.mozlz4", IsRequired: false),
          new(BackupComponent.Settings, "xulstore.json", IsRequired: false),
          new(BackupComponent.Bookmarks, "places.sqlite", IsRequired: false),
          new(BackupComponent.Bookmarks, "bookmarkbackups", IsRequired: false),
          new(BackupComponent.History, "places.sqlite", IsRequired: false),
          new(BackupComponent.Extensions, "extensions.json", IsRequired: false),
          new(BackupComponent.Extensions, "extensions", IsRequired: false),
          new(BackupComponent.Extensions, "browser-extension-data", IsRequired: false),
          new(BackupComponent.CookiesAndSiteData, "cookies.sqlite", IsRequired: false),
          new(BackupComponent.CookiesAndSiteData, "storage", IsRequired: false),
          new(BackupComponent.CookiesAndSiteData, "webappsstore.sqlite", IsRequired: false),
          new(BackupComponent.Sessions, "sessionstore.jsonlz4", IsRequired: false),
          new(BackupComponent.Sessions, "sessionstore-backups", IsRequired: false),
        ],
        ExcludedDirectoryNames: new HashSet<string>(
        [
          "cache2",
          "startupCache",
          "shader-cache",
          "crashes",
          "minidumps",
          "saved-telemetry-pings",
          "datareporting",
        ],
        StringComparer.OrdinalIgnoreCase),
        ExcludedFileNames: excludedFiles,
        ExcludedFileSuffixes: new HashSet<string>(
        [
          ".tmp",
          ".log",
          ".dmp",
        ],
        StringComparer.OrdinalIgnoreCase));
  }
}
