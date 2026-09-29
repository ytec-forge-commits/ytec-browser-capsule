using Ytec.BrowserCapsule.Browsers.Chromium.ProfileBackups;
using Ytec.BrowserCapsule.Browsers.Common.ProfileBackups;
using Ytec.BrowserCapsule.Browsers.Firefox.ProfileBackups;
using Ytec.BrowserCapsule.Domain.BrowserDiscovery;
using Ytec.BrowserCapsule.Domain.ProfileBackups;

namespace Ytec.BrowserCapsule.UnitTests.ProfileBackups;

public sealed class FileSystemBackupPlanBuilderTests
{
  [Fact]
  public async Task ChromiumFullProfileExcludesCachesLocksTemporaryAndProtectedFiles()
  {
    using var temporary =
        new BrowserDiscovery.TemporaryDirectory();
    temporary.WriteFile("Preferences", "{}");
    temporary.WriteFile("Bookmarks", "{}");
    temporary.WriteFile(
        Path.Combine("Cache", "cached.bin"),
        "cache");
    temporary.WriteFile("SingletonLock", "lock");
    temporary.WriteFile("scratch.tmp", "temp");
    temporary.WriteFile("Login Data", "not-read");
    temporary.WriteFile(
        Path.Combine("Safe", "data.bin"),
        "safe");
    var builder = new FileSystemBackupPlanBuilder(
        ChromiumBackupRules.Create(),
        TimeSpan.Zero);

    var plan = await builder.BuildAsync(
        CreateProfile("chrome", temporary.Path),
        "backup-profile",
        BackupComponent.FullProfile,
        CancellationToken.None);
    var names = plan.Files
        .Select(file => Path.GetFileName(file.SourcePath))
        .ToArray();

    Assert.Contains("Preferences", names);
    Assert.Contains("Bookmarks", names);
    Assert.Contains("data.bin", names);
    Assert.DoesNotContain("cached.bin", names);
    Assert.DoesNotContain("SingletonLock", names);
    Assert.DoesNotContain("scratch.tmp", names);
    Assert.DoesNotContain("Login Data", names);
  }

  [Fact]
  public async Task SelectedChromiumComponentDoesNotPullUnselectedHistory()
  {
    using var temporary =
        new BrowserDiscovery.TemporaryDirectory();
    temporary.WriteFile("Preferences", "{}");
    temporary.WriteFile("History", "history");
    var builder = new FileSystemBackupPlanBuilder(
        ChromiumBackupRules.Create(),
        TimeSpan.Zero);

    var plan = await builder.BuildAsync(
        CreateProfile("edge", temporary.Path),
        "backup-profile",
        BackupComponent.Settings,
        CancellationToken.None);

    Assert.Contains(
        plan.Files,
        file => Path.GetFileName(file.SourcePath) == "Preferences");
    Assert.DoesNotContain(
        plan.Files,
        file => Path.GetFileName(file.SourcePath) == "History");
  }

  [Fact]
  public async Task FirefoxRulesExcludeCacheAndProtectedFiles()
  {
    using var temporary =
        new BrowserDiscovery.TemporaryDirectory();
    temporary.WriteFile("prefs.js", "// settings");
    temporary.WriteFile("places.sqlite", "places");
    temporary.WriteFile("key4.db", "not-read");
    temporary.WriteFile("logins.json", "not-read");
    temporary.WriteFile(
        Path.Combine("cache2", "cached.bin"),
        "cache");
    var builder = new FileSystemBackupPlanBuilder(
        FirefoxBackupRules.Create(),
        TimeSpan.Zero);

    var plan = await builder.BuildAsync(
        CreateProfile("firefox", temporary.Path),
        "backup-profile",
        BackupComponent.FullProfile,
        CancellationToken.None);
    var names = plan.Files
        .Select(file => Path.GetFileName(file.SourcePath))
        .ToArray();

    Assert.Contains("prefs.js", names);
    Assert.Contains("places.sqlite", names);
    Assert.DoesNotContain("key4.db", names);
    Assert.DoesNotContain("logins.json", names);
    Assert.DoesNotContain("cached.bin", names);
  }

  [Fact]
  public async Task MissingRequiredSettingsFileFailsPlanning()
  {
    using var temporary =
        new BrowserDiscovery.TemporaryDirectory();
    var builder = new FileSystemBackupPlanBuilder(
        ChromiumBackupRules.Create(),
        TimeSpan.Zero);

    await Assert.ThrowsAsync<BackupPlanException>(
        () => builder.BuildAsync(
            CreateProfile("chrome", temporary.Path),
            "backup-profile",
            BackupComponent.Settings,
            CancellationToken.None));
  }

  private static BrowserProfile CreateProfile(
      string browserId,
      string root)
  {
    return new BrowserProfile(
        browserId,
        $"{browserId}-test",
        $"{browserId}-profile",
        "合成プロファイル",
        Path.GetFileName(root),
        root,
        Path.GetDirectoryName(root) ?? root,
        IsDefault: true,
        LastUsedUtc: null,
        browserId == "firefox"
            ? BrowserChannel.StableOrEsr
            : BrowserChannel.Stable,
        ProfileDiscoveryConfidence.Metadata);
  }
}
