using System.Runtime.Versioning;
using Ytec.BrowserCapsule.Application.ProfileBackups;
using Ytec.BrowserCapsule.Application.ProfileRestores;
using Ytec.BrowserCapsule.Browsers.Chrome.BrowserDiscovery;
using Ytec.BrowserCapsule.Browsers.Chromium.BrowserDiscovery;
using Ytec.BrowserCapsule.Browsers.Common.BrowserDiscovery;
using Ytec.BrowserCapsule.Browsers.Edge.BrowserDiscovery;
using Ytec.BrowserCapsule.Browsers.Firefox.BrowserDiscovery;
using Ytec.BrowserCapsule.Domain.BackupContainers;
using Ytec.BrowserCapsule.Domain.BrowserDiscovery;
using Ytec.BrowserCapsule.Domain.PasswordCsv;
using Ytec.BrowserCapsule.Domain.ProfileBackups;
using Ytec.BrowserCapsule.Domain.ProfileRestores;
using Ytec.BrowserCapsule.Infrastructure.BackupContainers;
using Ytec.BrowserCapsule.Infrastructure.PasswordCsv;
using Ytec.BrowserCapsule.Infrastructure.ProfileRestores;

namespace Ytec.BrowserCapsule.IntegrationTests.ProfileBackups;

[SupportedOSPlatform("windows")]
public sealed class MixedBrowserBackupTests
{
  private static readonly char[] Passphrase =
      "integration backup passphrase".ToCharArray();

  [Fact]
  public async Task ThreeBrowsersAreWrittenToOneVerifiedContainer()
  {
    using var temporary = new TemporaryDirectory();
    var (sources, selections) =
        await CreateSyntheticBrowsersAsync(temporary);
    var destination = temporary.GetPath("mixed.bvb");
    var progress = new SynchronousProgress();
    var service = CreateService(
        sources,
        new FixedProcessInspector(isRunning: false));

    var result = await service.CreateAsync(
        new ProfileBackupRequest(
            destination,
            selections,
            AppVersion: "0.3.0"),
        Passphrase,
        progress,
        CancellationToken.None);

    Assert.True(File.Exists(result.Container.FinalPath));
    Assert.False(File.Exists(destination + ".partial"));
    Assert.Equal(
        6,
        result.Container.Verification.Manifest.Profiles.Count);
    Assert.Equal(
        ["chrome", "edge", "firefox"],
        result.Container.Verification.Manifest.Profiles
            .Select(profile => profile.BrowserId)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal));
    Assert.All(
        result.Container.Verification.Manifest.Profiles
            .GroupBy(profile => profile.BrowserId),
        group => Assert.Equal(2, group.Count()));
    Assert.Contains(
        progress.Values,
        value => value.Stage is ProfileBackupStage.Completed);
    Assert.DoesNotContain(
        result.Container.Verification.Manifest.Profiles
            .SelectMany(profile => profile.Files),
        file => file.EntryPath.Contains(
            "Cache",
            StringComparison.OrdinalIgnoreCase));
  }

  [Fact]
  public async Task ThreeBrowsersRoundTripSixProfilesToExplicitTargets()
  {
    using var temporary = new TemporaryDirectory();
    var (sources, selections) =
        await CreateSyntheticBrowsersAsync(temporary);
    var processInspector = new FixedProcessInspector(isRunning: false);
    var fingerprintProvider = new FixedFingerprintProvider();
    var containerService = new BvbBackupContainerService(
        new BvbContainerSecurityOptions(
            BvbContainerSecurityOptions.MinimumPbkdf2Iterations,
            64 * 1024,
            new Version(0, 9, 0)));
    var backupService = new ProfileBackupService(
        sources,
        processInspector,
        fingerprintProvider,
        containerService);
    var destination = temporary.GetPath("mixed-round-trip.bvb");
    var backup = await backupService.CreateAsync(
        new ProfileBackupRequest(
            destination,
            selections,
            AppVersion: "0.9.0"),
        Passphrase,
        progress: null,
        CancellationToken.None);
    var targetPairs = await CreateRestoreTargetsAsync(
        sources,
        selections);
    var manifestBySource = backup.Container.Verification.Manifest.Profiles
        .ToDictionary(
            profile => profile.SourceProfileId,
            StringComparer.Ordinal);
    const BackupComponent components =
        BackupComponent.Settings
        | BackupComponent.Bookmarks
        | BackupComponent.History;
    var restoreService = new ProfileRestoreService(
        sources,
        processInspector,
        fingerprintProvider,
        containerService,
        backupService,
        new SafeRestoreFileApplier());

    var result = await restoreService.RestoreAsync(
        new ProfileRestoreRequest(
            backup.Container.FinalPath,
            targetPairs.Select(pair =>
                new ProfileRestoreMapping(
                    manifestBySource[pair.Source.Profile.ProfileId]
                        .BackupProfileId,
                    pair.Target,
                    components)).ToArray(),
            ProfileRestoreMode.SameEnvironment,
            AllowFullRestoreAcrossEnvironment: false,
            temporary.CreateDirectory("mixed-rollbacks"),
            "0.9.0"),
        Passphrase,
        progress: null,
        CancellationToken.None);

    Assert.True(result.EnvironmentMatched);
    Assert.Equal(ProfileRestoreMode.SameEnvironment, result.EffectiveMode);
    Assert.Equal(12, result.RestoredFileCount);
    Assert.NotNull(result.RollbackPath);
    Assert.NotNull(result.RollbackRecoveryKeyPath);
    var rollback = await containerService.VerifyAsync(
        result.RollbackPath,
        BackupContainerCredential.FromRecoveryKeyFile(
            result.RollbackRecoveryKeyPath),
        CancellationToken.None);
    Assert.Equal(6, rollback.Manifest.Profiles.Count);
    foreach (var pair in targetPairs)
    {
      foreach (var relativePath in GetExpectedPaths(
          pair.Source.Profile.BrowserId))
      {
        Assert.Equal(
            await File.ReadAllBytesAsync(Path.Combine(
                pair.Source.Profile.AbsoluteProfilePath,
                relativePath)),
            await File.ReadAllBytesAsync(Path.Combine(
                pair.Target.AbsoluteProfilePath,
                relativePath)));
      }
    }
  }

  [Fact]
  public async Task RunningBrowserBlocksBackupBeforeAFileIsCreated()
  {
    using var temporary = new TemporaryDirectory();
    var (sources, selections) =
        await CreateSyntheticBrowsersAsync(temporary);
    var destination = temporary.GetPath("blocked.bvb");
    var service = CreateService(
        sources,
        new FixedProcessInspector(isRunning: true));

    await Assert.ThrowsAsync<BrowserMustBeClosedException>(
        () => service.CreateAsync(
            new ProfileBackupRequest(
                destination,
                [selections[0]],
                AppVersion: "0.3.0"),
            Passphrase,
            progress: null,
            CancellationToken.None));

    Assert.False(File.Exists(destination));
    Assert.False(File.Exists(destination + ".partial"));
  }

  [Fact]
  public async Task ProtectedPasswordCsvIsIncludedWithoutLeavingPlaintext()
  {
    using var temporary = new TemporaryDirectory();
    var (sources, selections) =
        await CreateSyntheticBrowsersAsync(temporary);
    var chromeSelection = selections[0] with
    {
      Components = BackupComponent.PasswordCsv,
    };
    var containerService = new BvbBackupContainerService(
        new BvbContainerSecurityOptions(
            BvbContainerSecurityOptions.MinimumPbkdf2Iterations,
            64 * 1024,
            new Version(0, 5, 0)));
    var service = new ProfileBackupService(
        sources,
        new FixedProcessInspector(isRunning: false),
        new FixedFingerprintProvider(),
        containerService);
    var stagingFactory = new SecurePasswordCsvStagingFactory(
        temporary.GetPath("CsvStaging"));
    await using var stagingSession = await stagingFactory.CreateAsync(
        CancellationToken.None);
    var plaintextPath = Path.Combine(
        stagingSession.StagingDirectory,
        "browser-export.csv");
    var csvBytes =
        "url,username,password\r\nhttps://example.test,user,secret\r\n"u8
            .ToArray();
    await File.WriteAllBytesAsync(plaintextPath, csvBytes);
    var protectedCsv = await stagingSession.ProtectAsync(
        plaintextPath,
        allowEmpty: false,
        CancellationToken.None);

    Assert.False(File.Exists(plaintextPath));
    var destination = temporary.GetPath("password-csv.bvb");
    var result = await service.CreateAsync(
        new ProfileBackupRequest(
            destination,
            [chromeSelection],
            AppVersion: "0.5.0",
            [
              new ProfilePasswordCsvSelection(
                  chromeSelection.Profile.ProfileId,
                  protectedCsv),
            ]),
        Passphrase,
        progress: null,
        CancellationToken.None);

    var manifestProfile = Assert.Single(
        result.Container.Verification.Manifest.Profiles);
    Assert.NotNull(manifestProfile.PasswordCsv);
    Assert.True(manifestProfile.PasswordCsv.Included);
    Assert.False(manifestProfile.PasswordCsv.OriginalFileNameStored);
    Assert.Equal(1, manifestProfile.PasswordCsv.RecordCount);
    var csvManifest = Assert.Single(manifestProfile.Files);
    Assert.Equal("passwordCsv", csvManifest.Component);
    Assert.EndsWith(
        "/passwords/passwords.csv",
        csvManifest.EntryPath,
        StringComparison.Ordinal);

    var extractionRoot = temporary.CreateDirectory("CsvExtract");
    var extraction = await containerService.ExtractSelectedVerifiedAsync(
        destination,
        Passphrase,
        extractionRoot,
        new HashSet<string>(StringComparer.Ordinal)
        {
          csvManifest.EntryPath,
        },
        CancellationToken.None);
    var extracted = Assert.Single(extraction.Files);
    Assert.Equal(
        csvBytes,
        await File.ReadAllBytesAsync(extracted.AbsolutePath));
    Assert.False(File.Exists(plaintextPath));
  }

  private static ProfileBackupService CreateService(
      IReadOnlyList<IBrowserProfileSource> sources,
      IBrowserProcessInspector processInspector)
  {
    return new ProfileBackupService(
        sources,
        processInspector,
        new FixedFingerprintProvider(),
        new BvbBackupContainerService(
            new BvbContainerSecurityOptions(
                BvbContainerSecurityOptions.MinimumPbkdf2Iterations,
                64 * 1024,
                new Version(0, 3, 0))));
  }

  private static async Task<(
      IReadOnlyList<IBrowserProfileSource> Sources,
      IReadOnlyList<ProfileBackupSelection> Selections)>
      CreateSyntheticBrowsersAsync(TemporaryDirectory temporary)
  {
    var chromeRoot = temporary.CreateDirectory("ChromeRoot");
    temporary.CreateDirectory(
        "ChromeRoot",
        "Profile 1");
    temporary.WriteFile(
        Path.Combine("ChromeRoot", "Profile 1", "Preferences"),
        "{}");
    temporary.WriteFile(
        Path.Combine("ChromeRoot", "Profile 1", "Bookmarks"),
        "{}");
    temporary.WriteFile(
        Path.Combine("ChromeRoot", "Profile 1", "Cache", "ignored.bin"),
        "cache");
    temporary.CreateDirectory(
        "ChromeRoot",
        "Profile 10");
    temporary.WriteFile(
        Path.Combine("ChromeRoot", "Profile 10", "Preferences"),
        """{"profile":"chrome-2"}""");
    temporary.WriteFile(
        Path.Combine("ChromeRoot", "Profile 10", "Bookmarks"),
        "chrome-2-bookmarks");

    var edgeRoot = temporary.CreateDirectory("EdgeRoot");
    temporary.CreateDirectory(
        "EdgeRoot",
        "Default");
    temporary.WriteFile(
        Path.Combine("EdgeRoot", "Default", "Preferences"),
        "{}");
    temporary.WriteFile(
        Path.Combine("EdgeRoot", "Default", "History"),
        "history");
    temporary.CreateDirectory(
        "EdgeRoot",
        "Profile 2");
    temporary.WriteFile(
        Path.Combine("EdgeRoot", "Profile 2", "Preferences"),
        """{"profile":"edge-2"}""");
    temporary.WriteFile(
        Path.Combine("EdgeRoot", "Profile 2", "History"),
        "edge-2-history");

    var firefoxRoot = temporary.CreateDirectory("FirefoxRoot");
    temporary.CreateDirectory(
        "FirefoxRoot",
        "Profiles",
        "test.default");
    temporary.WriteFile(
        Path.Combine(
            "FirefoxRoot",
            "Profiles",
            "test.default",
            "prefs.js"),
        "// settings");
    temporary.WriteFile(
        Path.Combine(
            "FirefoxRoot",
            "Profiles",
            "test.default",
            "places.sqlite"),
        "places");
    temporary.CreateDirectory(
        "FirefoxRoot",
        "Profiles",
        "work.default-release");
    temporary.WriteFile(
        Path.Combine(
            "FirefoxRoot",
            "Profiles",
            "work.default-release",
            "prefs.js"),
        "// work settings");
    temporary.WriteFile(
        Path.Combine(
            "FirefoxRoot",
            "Profiles",
            "work.default-release",
            "places.sqlite"),
        "work places");

    var expander = new SafePathVariableExpander(
        new Dictionary<string, string>
        {
          ["chrome_root"] = chromeRoot,
          ["edge_root"] = edgeRoot,
        });
    IBrowserProfileSource chrome = new ChromeBrowserProfileSource(
        new FixedPolicyReader(@"${chrome_root}"),
        expander);
    IBrowserProfileSource edge = new EdgeBrowserProfileSource(
        new FixedPolicyReader(@"${edge_root}"),
        expander);
    IBrowserProfileSource firefox = new FirefoxBrowserProfileSource(
        firefoxRoot,
        []);

    const BackupComponent selection =
        BackupComponent.Settings
        | BackupComponent.Bookmarks
        | BackupComponent.History;
    IReadOnlyList<IBrowserProfileSource> sources =
    [
      chrome,
      edge,
      firefox,
    ];
    var selections = new List<ProfileBackupSelection>(capacity: 6);
    foreach (var source in sources)
    {
      var installation = Assert.Single(
          await source.DiscoverInstallationsAsync(
              CancellationToken.None));
      var profiles = await source.DiscoverProfilesAsync(
          installation,
          CancellationToken.None);
      Assert.Equal(2, profiles.Count);
      selections.AddRange(profiles.Select(profile =>
          new ProfileBackupSelection(profile, selection)));
    }

    return (sources, selections);
  }

  private static async Task<IReadOnlyList<RestoreTargetPair>>
      CreateRestoreTargetsAsync(
          IReadOnlyList<IBrowserProfileSource> sources,
          IReadOnlyList<ProfileBackupSelection> selections)
  {
    var targetPaths = new Dictionary<string, Queue<string>>(
        StringComparer.Ordinal);
    foreach (var browserGroup in selections.GroupBy(
        selection => selection.Profile.BrowserId))
    {
      var paths = new Queue<string>();
      var index = 0;
      foreach (var selection in browserGroup)
      {
        index++;
        string targetPath;
        if (string.Equals(
            selection.Profile.BrowserId,
            "firefox",
            StringComparison.Ordinal))
        {
          targetPath = Path.Combine(
              selection.Profile.UserDataRoot,
              "Profiles",
              $"restore-{index:D2}.default-release");
          Directory.CreateDirectory(targetPath);
          await File.WriteAllTextAsync(
              Path.Combine(targetPath, "prefs.js"),
              $"// target-before-{index}");
          await File.WriteAllTextAsync(
              Path.Combine(targetPath, "places.sqlite"),
              $"target-places-before-{index}");
        }
        else
        {
          targetPath = Path.Combine(
              selection.Profile.UserDataRoot,
              $"Profile {50 + index}");
          Directory.CreateDirectory(targetPath);
          await File.WriteAllTextAsync(
              Path.Combine(targetPath, "Preferences"),
              $"target-preferences-before-{index}");
          var secondaryFile = string.Equals(
              selection.Profile.BrowserId,
              "chrome",
              StringComparison.Ordinal)
              ? "Bookmarks"
              : "History";
          await File.WriteAllTextAsync(
              Path.Combine(targetPath, secondaryFile),
              $"target-secondary-before-{index}");
        }

        paths.Enqueue(Path.GetFullPath(targetPath));
      }

      targetPaths.Add(browserGroup.Key, paths);
    }

    var targetsByPath = new Dictionary<string, BrowserProfile>(
        StringComparer.OrdinalIgnoreCase);
    foreach (var source in sources)
    {
      var installation = Assert.Single(
          await source.DiscoverInstallationsAsync(
              CancellationToken.None));
      var profiles = await source.DiscoverProfilesAsync(
          installation,
          CancellationToken.None);
      foreach (var targetPath in targetPaths[source.BrowserId])
      {
        targetsByPath.Add(
            targetPath,
            profiles.Single(profile => string.Equals(
                profile.AbsoluteProfilePath,
                targetPath,
                StringComparison.OrdinalIgnoreCase)));
      }
    }

    var pairs = new List<RestoreTargetPair>(selections.Count);
    var nextTargetByBrowser = targetPaths.ToDictionary(
        pair => pair.Key,
        pair => new Queue<string>(pair.Value),
        StringComparer.Ordinal);
    foreach (var selection in selections)
    {
      var targetPath =
          nextTargetByBrowser[selection.Profile.BrowserId].Dequeue();
      pairs.Add(new RestoreTargetPair(
          selection,
          targetsByPath[targetPath]));
    }

    return pairs;
  }

  private static IReadOnlyList<string> GetExpectedPaths(string browserId)
  {
    return browserId switch
    {
      "chrome" => ["Preferences", "Bookmarks"],
      "edge" => ["Preferences", "History"],
      "firefox" => ["prefs.js", "places.sqlite"],
      _ => throw new InvalidOperationException(
          "未対応の合成ブラウザーです。"),
    };
  }

  private sealed class FixedPolicyReader(string currentUserValue)
      : IChromiumPolicyValueReader
  {
    public string? ReadCurrentUser(string policySubKey)
    {
      return currentUserValue;
    }

    public string? ReadLocalMachine(string policySubKey)
    {
      return null;
    }
  }

  private sealed class FixedProcessInspector(bool isRunning)
      : IBrowserProcessInspector
  {
    public Task<bool> IsRunningAsync(
        IReadOnlyList<ProcessMatchRule> rules,
        CancellationToken cancellationToken)
    {
      return Task.FromResult(isRunning);
    }
  }

  private sealed class FixedFingerprintProvider
      : IEnvironmentFingerprintProvider
  {
    public string GetCurrentFingerprint()
    {
      return "synthetic-fingerprint";
    }
  }

  private sealed class SynchronousProgress
      : IProgress<ProfileBackupProgress>
  {
    public List<ProfileBackupProgress> Values { get; } = [];

    public void Report(ProfileBackupProgress value)
    {
      Values.Add(value);
    }
  }

  private sealed record RestoreTargetPair(
      ProfileBackupSelection Source,
      BrowserProfile Target);
}
