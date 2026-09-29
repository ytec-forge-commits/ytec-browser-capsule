using System.Text.Json;
using Ytec.BrowserCapsule.Domain.Settings;
using Ytec.BrowserCapsule.Infrastructure.Settings;

namespace Ytec.BrowserCapsule.UnitTests.Settings;

public sealed class JsonAppPreferencesStoreTests
{
  [Fact]
  public async Task MissingSettingsReturnsDefaults()
  {
    var root = CreateTemporaryRoot();
    try
    {
      var store = new JsonAppPreferencesStore(root);

      var settings = await store.LoadAsync(CancellationToken.None);

      Assert.Equal(AppPreferences.Default, settings);
    }
    finally
    {
      DeleteTemporaryRoot(root);
    }
  }

  [Fact]
  public async Task RoundTripUsesAtomicSettingsFile()
  {
    var root = CreateTemporaryRoot();
    var backupDirectory = Path.Combine(root, "Backups");
    Directory.CreateDirectory(backupDirectory);
    try
    {
      var store = new JsonAppPreferencesStore(root);

      await store.SaveAsync(
          new AppPreferences(
              backupDirectory,
              AppLanguage.English),
          CancellationToken.None);
      var settings = await store.LoadAsync(CancellationToken.None);

      Assert.Equal(
          Path.GetFullPath(backupDirectory),
          settings.DefaultBackupDirectory);
      Assert.Equal(AppLanguage.English, settings.Language);
      Assert.True(File.Exists(Path.Combine(root, "settings.json")));
      Assert.False(File.Exists(
          Path.Combine(root, "settings.json.partial")));
    }
    finally
    {
      DeleteTemporaryRoot(root);
    }
  }

  [Fact]
  public async Task VersionOneSettingsKeepBackupDirectoryAndUseSystemLanguage()
  {
    var root = CreateTemporaryRoot();
    var backupDirectory = Path.Combine(root, "Backups");
    Directory.CreateDirectory(backupDirectory);
    try
    {
      await File.WriteAllTextAsync(
          Path.Combine(root, "settings.json"),
          JsonSerializer.Serialize(new
          {
            SchemaVersion = 1,
            DefaultBackupDirectory = backupDirectory,
          }));
      var store = new JsonAppPreferencesStore(root);

      var settings = await store.LoadAsync(CancellationToken.None);

      Assert.Equal(
          Path.GetFullPath(backupDirectory),
          settings.DefaultBackupDirectory);
      Assert.Equal(AppLanguage.SystemDefault, settings.Language);
    }
    finally
    {
      DeleteTemporaryRoot(root);
    }
  }

  [Theory]
  [InlineData(@"\\server\share")]
  [InlineData(@"\\?\C:\Backups")]
  [InlineData("relative")]
  public async Task SaveRejectsNonLocalOrRelativeDirectory(string directory)
  {
    var root = CreateTemporaryRoot();
    try
    {
      var store = new JsonAppPreferencesStore(root);

      await Assert.ThrowsAsync<ArgumentException>(() =>
          store.SaveAsync(
              new AppPreferences(directory),
              CancellationToken.None));

      Assert.False(File.Exists(Path.Combine(root, "settings.json")));
    }
    finally
    {
      DeleteTemporaryRoot(root);
    }
  }

  [Fact]
  public async Task InvalidOrUnknownSettingsFallBackToDefaults()
  {
    var root = CreateTemporaryRoot();
    try
    {
      Directory.CreateDirectory(root);
      await File.WriteAllTextAsync(
          Path.Combine(root, "settings.json"),
          JsonSerializer.Serialize(new
          {
            SchemaVersion = 99,
            DefaultBackupDirectory = @"\\server\share",
          }));
      var store = new JsonAppPreferencesStore(root);

      var settings = await store.LoadAsync(CancellationToken.None);

      Assert.Equal(AppPreferences.Default, settings);
    }
    finally
    {
      DeleteTemporaryRoot(root);
    }
  }

  private static string CreateTemporaryRoot()
  {
    return Path.Combine(
        Path.GetTempPath(),
        "YtecBrowserCapsuleTests",
        Guid.NewGuid().ToString("N"));
  }

  private static void DeleteTemporaryRoot(string root)
  {
    if (Directory.Exists(root))
    {
      Directory.Delete(root, recursive: true);
    }
  }
}
