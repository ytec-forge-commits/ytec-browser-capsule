using Ytec.BrowserCapsule.Browsers.Common.ProfileBackups;
using Ytec.BrowserCapsule.Domain.ProfileBackups;

namespace Ytec.BrowserCapsule.Browsers.Chromium.ProfileBackups;

/// <summary>
/// Chrome / Edge共通のコンポーネント許可リストと除外規則です。
/// </summary>
public static class ChromiumBackupRules
{
  public static BrowserBackupRuleSet Create()
  {
    var excludedFiles = new HashSet<string>(
        ProtectedBrowserFileExclusions.ChromiumFileNames,
        StringComparer.OrdinalIgnoreCase)
    {
      "SingletonCookie",
      "SingletonLock",
      "SingletonSocket",
      "LOCK",
    };

    return new BrowserBackupRuleSet(
        ComponentPaths:
        [
          new(BackupComponent.Settings, "Preferences", IsRequired: true),
          new(BackupComponent.Settings, "Secure Preferences", IsRequired: false),
          new(BackupComponent.Settings, "Web Data", IsRequired: false),
          new(BackupComponent.Bookmarks, "Bookmarks", IsRequired: false),
          new(BackupComponent.Bookmarks, "Bookmarks.bak", IsRequired: false),
          new(BackupComponent.History, "History", IsRequired: false),
          new(BackupComponent.Extensions, "Extensions", IsRequired: false),
          new(BackupComponent.Extensions, "Extension State", IsRequired: false),
          new(BackupComponent.Extensions, "Local Extension Settings", IsRequired: false),
          new(BackupComponent.Extensions, "Sync Extension Settings", IsRequired: false),
          new(BackupComponent.Extensions, "Managed Extension Settings", IsRequired: false),
          new(BackupComponent.CookiesAndSiteData, "Network", IsRequired: false),
          new(BackupComponent.CookiesAndSiteData, "Local Storage", IsRequired: false),
          new(BackupComponent.CookiesAndSiteData, "IndexedDB", IsRequired: false),
          new(BackupComponent.CookiesAndSiteData, "Service Worker", IsRequired: false),
          new(BackupComponent.CookiesAndSiteData, "Session Storage", IsRequired: false),
          new(BackupComponent.CookiesAndSiteData, "WebStorage", IsRequired: false),
          new(BackupComponent.CookiesAndSiteData, "SharedStorage", IsRequired: false),
          new(BackupComponent.CookiesAndSiteData, "Storage", IsRequired: false),
          new(BackupComponent.Sessions, "Sessions", IsRequired: false),
          new(BackupComponent.Sessions, "Current Session", IsRequired: false),
          new(BackupComponent.Sessions, "Current Tabs", IsRequired: false),
          new(BackupComponent.Sessions, "Last Session", IsRequired: false),
          new(BackupComponent.Sessions, "Last Tabs", IsRequired: false),
        ],
        ExcludedDirectoryNames: new HashSet<string>(
        [
          "Cache",
          "Code Cache",
          "GPUCache",
          "ShaderCache",
          "GrShaderCache",
          "GraphiteDawnCache",
          "DawnGraphiteCache",
          "DawnWebGPUCache",
          "Crashpad",
          "BrowserMetrics",
          "component_crx_cache",
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
