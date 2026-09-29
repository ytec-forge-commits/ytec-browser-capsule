using System.Runtime.Versioning;
using Ytec.BrowserCapsule.Application.ProfileBackups;
using Ytec.BrowserCapsule.Application.ProfileRestores;
using Ytec.BrowserCapsule.Browsers.Chromium.ProfileBackups;
using Ytec.BrowserCapsule.Browsers.Common.ProfileBackups;
using Ytec.BrowserCapsule.Browsers.Firefox.ProfileBackups;
using Ytec.BrowserCapsule.Domain.BackupContainers;
using Ytec.BrowserCapsule.Domain.BrowserDiscovery;
using Ytec.BrowserCapsule.Domain.PasswordCsv;
using Ytec.BrowserCapsule.Domain.ProfileBackups;
using Ytec.BrowserCapsule.Domain.ProfileRestores;
using Ytec.BrowserCapsule.Infrastructure.BackupContainers;
using Ytec.BrowserCapsule.Infrastructure.ProfileRestores;
using Ytec.BrowserCapsule.IntegrationTests.ProfileBackups;

namespace Ytec.BrowserCapsule.IntegrationTests.ProfileRestores;

[SupportedOSPlatform("windows")]
public sealed class ProfileRestoreServiceTests
{
  private static readonly char[] Passphrase =
      "synthetic restore passphrase".ToCharArray();

  [Theory]
  [InlineData("chrome", "Login Data", true, false)]
  [InlineData("chrome", "Login Data For Account", true, true)]
  [InlineData("edge", "Login Data", true, true)]
  [InlineData("edge", "Login Data For Account", true, false)]
  [InlineData("firefox", "key4.db", true, false)]
  [InlineData("firefox", "logins.json", true, true)]
  [InlineData("chrome", "nested/LOGIN DATA", false, true)]
  [InlineData("edge", "nested/login data for account", false, false)]
  [InlineData("firefox", "nested/KEY4.DB", false, true)]
  [InlineData("firefox", "nested/LOGINS.JSON", false, false)]
  public async Task AuthenticatedProtectedEntriesAreRejectedBeforeAnyRestoreSideEffects(
      string browserId,
      string protectedPath,
      bool fullProfile,
      bool recoveryKey)
  {
    using var temporary = new TemporaryDirectory();
    var setup = CreateSetup(temporary, browserId: browserId);
    var protectedTarget = Path.Combine(
        setup.TargetProfile.AbsoluteProfilePath,
        protectedPath.Replace('/', Path.DirectorySeparatorChar));
    Directory.CreateDirectory(Path.GetDirectoryName(protectedTarget)!);
    await File.WriteAllTextAsync(protectedTarget, "synthetic-protected-before");
    var component = fullProfile ? "fullProfile" : "bookmarks";
    const string backupProfileId = "synthetic-backup-profile";
    var prefix = $"profiles/{browserId}/{backupProfileId}/profile/";
    var benignBytes = "synthetic-replacement-settings"u8.ToArray();
    var protectedBytes = "synthetic-protected-replacement"u8.ToArray();
    var containerRequest = new BackupContainerRequest(
        temporary.GetPath("protected-input.bvb"),
        "1.2.1",
        "synthetic-environment",
        [
          new BackupProfileDescriptor(
              backupProfileId,
              browserId,
              "synthetic-source",
              "Synthetic profile",
              [component],
              PasswordCsv: null,
              Warnings: []),
        ],
        [
          new BackupContainerEntrySource(
              backupProfileId,
              prefix + "Preferences",
              benignBytes.LongLength,
              _ => ValueTask.FromResult<Stream>(new MemoryStream(benignBytes, false)),
              component),
          new BackupContainerEntrySource(
              backupProfileId,
              prefix + protectedPath,
              protectedBytes.LongLength,
              _ => ValueTask.FromResult<Stream>(new MemoryStream(protectedBytes, false)),
              component),
        ]);
    var containerService = CreateContainerService();
    var keyPath = temporary.GetPath("synthetic-input.ybckey");
    var written = recoveryKey
        ? await containerService.CreateAsync(containerRequest, keyPath, CancellationToken.None)
        : await containerService.CreateAsync(containerRequest, Passphrase, CancellationToken.None);
    Assert.Equal(recoveryKey ? 2 : 1, written.Verification.Header.FormatVersion);
    var credential = recoveryKey
        ? BackupContainerCredential.FromRecoveryKeyFile(keyPath)
        : BackupContainerCredential.FromLegacyPassphrase(Passphrase);
    var rollbackDirectory = temporary.GetPath("protected-rollbacks");

    await Assert.ThrowsAsync<RestorePlanException>(
        () => setup.RestoreService.RestoreAsync(
            new ProfileRestoreRequest(
                written.FinalPath,
                [
                  new ProfileRestoreMapping(
                      backupProfileId,
                      setup.TargetProfile,
                      fullProfile ? BackupComponent.FullProfile : BackupComponent.Bookmarks),
                ],
                ProfileRestoreMode.SameEnvironment,
                AllowFullRestoreAcrossEnvironment: false,
                rollbackDirectory,
                "1.2.1"),
            credential,
            progress: null,
            CancellationToken.None));

    Assert.Equal("synthetic-protected-before", await File.ReadAllTextAsync(protectedTarget));
    Assert.Equal(
        "target-preferences",
        await File.ReadAllTextAsync(Path.Combine(setup.TargetProfile.AbsoluteProfilePath, "Preferences")));
    Assert.False(Directory.Exists(rollbackDirectory));
    Assert.Empty(Directory.EnumerateDirectories(
        Path.GetDirectoryName(setup.TargetProfile.AbsoluteProfilePath)!,
        ".ytec-browser-capsule-*"));
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task LegitimateFullRestorePreservesProtectedStateAndOfficialCsv(bool recoveryKey)
  {
    using var temporary = new TemporaryDirectory();
    var setup = CreateSetup(temporary);
    var sourceProtected = Path.Combine(setup.SourceProfile.AbsoluteProfilePath, "Login Data");
    var targetProtected = Path.Combine(setup.TargetProfile.AbsoluteProfilePath, "Login Data");
    await File.WriteAllTextAsync(sourceProtected, "synthetic-source-protected");
    await File.WriteAllTextAsync(targetProtected, "synthetic-target-protected");
    await File.WriteAllTextAsync(
        Path.Combine(setup.SourceProfile.AbsoluteProfilePath, "Login Data.notes"),
        "synthetic-ordinary-notes");
    var csvBytes = "url,username,password\r\nhttps://example.test,synthetic,synthetic-only\r\n"u8.ToArray();
    await using var csv = new MemoryProtectedPasswordCsv(csvBytes);
    var request = new ProfileBackupRequest(
        temporary.GetPath("legitimate-full.bvb"),
        [new ProfileBackupSelection(setup.SourceProfile, BackupComponent.FullProfile | BackupComponent.PasswordCsv)],
        "1.2.1",
        [new ProfilePasswordCsvSelection(setup.SourceProfile.ProfileId, csv)]);
    var keyPath = temporary.GetPath("legitimate-full.ybckey");
    var backup = recoveryKey
        ? await setup.BackupService.CreateAsync(request, keyPath, null, CancellationToken.None)
        : await setup.BackupService.CreateAsync(request, Passphrase, null, CancellationToken.None);
    var profile = Assert.Single(backup.Container.Verification.Manifest.Profiles);
    Assert.DoesNotContain(profile.Files, file => file.EntryPath.EndsWith("/Login Data", StringComparison.Ordinal));
    var credential = recoveryKey
        ? BackupContainerCredential.FromRecoveryKeyFile(keyPath)
        : BackupContainerCredential.FromLegacyPassphrase(Passphrase);

    var restored = await setup.RestoreService.RestoreAsync(
        new ProfileRestoreRequest(
            backup.Container.FinalPath,
            [new ProfileRestoreMapping(profile.BackupProfileId, setup.TargetProfile, BackupComponent.FullProfile | BackupComponent.PasswordCsv)],
            ProfileRestoreMode.SameEnvironment,
            AllowFullRestoreAcrossEnvironment: false,
            temporary.GetPath("legitimate-full-rollbacks"),
            "1.2.1"),
        credential, null, CancellationToken.None);

    Assert.Equal("synthetic-target-protected", await File.ReadAllTextAsync(targetProtected));
    Assert.Equal("source-preferences", await File.ReadAllTextAsync(Path.Combine(setup.TargetProfile.AbsoluteProfilePath, "Preferences")));
    Assert.Equal("source-bookmarks", await File.ReadAllTextAsync(Path.Combine(setup.TargetProfile.AbsoluteProfilePath, "Bookmarks")));
    Assert.Equal("synthetic-ordinary-notes", await File.ReadAllTextAsync(Path.Combine(setup.TargetProfile.AbsoluteProfilePath, "Login Data.notes")));
    Assert.True(File.Exists(restored.RollbackPath));
    Assert.Equal(3, restored.RestoredFileCount);
    var extracted = await setup.RestoreService.ExtractPasswordCsvForImportAsync(
        backup.Container.FinalPath, credential, new HashSet<string> { profile.BackupProfileId },
        temporary.CreateDirectory("legitimate-csv-staging"), CancellationToken.None);
    var importFile = Assert.Single(extracted);
    Assert.Equal(csvBytes, await File.ReadAllBytesAsync(importFile.AbsolutePath));
    Assert.Equal("synthetic-target-protected", await File.ReadAllTextAsync(targetProtected));
  }

  [Fact]
  public async Task RestoreCreatesEncryptedRollbackAndAppliesSelectedFiles()
  {
    using var temporary = new TemporaryDirectory();
    var setup = CreateSetup(temporary);
    var sourceBackup = await setup.BackupService.CreateAsync(
        new ProfileBackupRequest(
            temporary.GetPath("source.bvb"),
            [
              new ProfileBackupSelection(
                  setup.SourceProfile,
                  BackupComponent.Settings
                  | BackupComponent.Bookmarks),
            ],
            "0.5.0"),
        Passphrase,
        progress: null,
        CancellationToken.None);
    await File.WriteAllTextAsync(
        Path.Combine(setup.TargetProfile.AbsoluteProfilePath, "Preferences"),
        "target-before");

    var result = await setup.RestoreService.RestoreAsync(
        new ProfileRestoreRequest(
            sourceBackup.Container.FinalPath,
            [
              new ProfileRestoreMapping(
                  Assert.Single(
                      sourceBackup.Container.Verification.Manifest.Profiles)
                      .BackupProfileId,
                  setup.TargetProfile,
                  BackupComponent.Settings
                  | BackupComponent.Bookmarks),
            ],
            ProfileRestoreMode.SameEnvironment,
            AllowFullRestoreAcrossEnvironment: false,
            temporary.CreateDirectory("rollbacks"),
            "0.5.0"),
        Passphrase,
        progress: null,
        CancellationToken.None);

    Assert.Equal(
        "source-preferences",
        await File.ReadAllTextAsync(
            Path.Combine(
                setup.TargetProfile.AbsoluteProfilePath,
                "Preferences")));
    Assert.Equal(
        "source-bookmarks",
        await File.ReadAllTextAsync(
            Path.Combine(
                setup.TargetProfile.AbsoluteProfilePath,
                "Bookmarks")));
    Assert.True(File.Exists(result.RollbackPath));
    Assert.True(result.EnvironmentMatched);
    Assert.True(result.RestoredFileCount >= 2);
    Assert.Empty(
        Directory.EnumerateDirectories(
            Path.GetDirectoryName(
                setup.TargetProfile.AbsoluteProfilePath)!,
            ".ytec-browser-capsule-*",
            SearchOption.TopDirectoryOnly));
  }

  [Fact]
  public async Task FailureAfterPrimaryApplyAutomaticallyRestoresOriginalFiles()
  {
    using var temporary = new TemporaryDirectory();
    var failOnceApplier = new FailAfterFirstApplyApplier();
    var setup = CreateSetup(temporary, failOnceApplier);
    var sourceBackup = await setup.BackupService.CreateAsync(
        new ProfileBackupRequest(
            temporary.GetPath("source-failure.bvb"),
            [
              new ProfileBackupSelection(
                  setup.SourceProfile,
                  BackupComponent.Settings
                  | BackupComponent.Bookmarks),
            ],
            "0.5.0"),
        Passphrase,
        progress: null,
        CancellationToken.None);
    var targetPreferences = Path.Combine(
        setup.TargetProfile.AbsoluteProfilePath,
        "Preferences");
    var targetBookmarks = Path.Combine(
        setup.TargetProfile.AbsoluteProfilePath,
        "Bookmarks");
    await File.WriteAllTextAsync(targetPreferences, "original-target");
    File.Delete(targetBookmarks);

    var exception =
        await Assert.ThrowsAsync<RestoreFailedRolledBackException>(
            () => setup.RestoreService.RestoreAsync(
                new ProfileRestoreRequest(
                    sourceBackup.Container.FinalPath,
                    [
                      new ProfileRestoreMapping(
                          Assert.Single(
                              sourceBackup.Container.Verification
                                  .Manifest.Profiles)
                          .BackupProfileId,
                          setup.TargetProfile,
                          BackupComponent.Settings
                          | BackupComponent.Bookmarks),
                    ],
                    ProfileRestoreMode.SameEnvironment,
                    AllowFullRestoreAcrossEnvironment: false,
                    temporary.CreateDirectory("failure-rollbacks"),
                    "0.5.0"),
                Passphrase,
                progress: null,
                CancellationToken.None));

    Assert.Contains(
        "復元前の状態",
        exception.Message,
        StringComparison.Ordinal);
    Assert.Equal(
        "original-target",
        await File.ReadAllTextAsync(targetPreferences));
    Assert.False(File.Exists(targetBookmarks));
    Assert.Equal(2, failOnceApplier.CallCount);
  }

  [Fact]
  public async Task PasswordCsvOnlyRestoreDoesNotModifyProfileOrCreateRollback()
  {
    using var temporary = new TemporaryDirectory();
    var setup = CreateSetup(temporary);
    var protectedCsv = new MemoryProtectedPasswordCsv(
        "url,username,password\r\nhttps://example.test,user,secret\r\n"u8
            .ToArray());
    await using var capture = protectedCsv;
    var sourceBackup = await setup.BackupService.CreateAsync(
        new ProfileBackupRequest(
            temporary.GetPath("csv-only.bvb"),
            [
              new ProfileBackupSelection(
                  setup.SourceProfile,
                  BackupComponent.PasswordCsv),
            ],
            "0.5.0",
            [
              new ProfilePasswordCsvSelection(
                  setup.SourceProfile.ProfileId,
                  protectedCsv),
            ]),
        Passphrase,
        progress: null,
        CancellationToken.None);
    var targetPreferences = Path.Combine(
        setup.TargetProfile.AbsoluteProfilePath,
        "Preferences");
    var before = await File.ReadAllTextAsync(targetPreferences);
    var rollbackDirectory = temporary.GetPath("csv-only-rollbacks");

    var result = await setup.RestoreService.RestoreAsync(
        new ProfileRestoreRequest(
            sourceBackup.Container.FinalPath,
            [
              new ProfileRestoreMapping(
                  Assert.Single(
                      sourceBackup.Container.Verification.Manifest.Profiles)
                      .BackupProfileId,
                  setup.TargetProfile,
                  BackupComponent.PasswordCsv),
            ],
            ProfileRestoreMode.Migration,
            AllowFullRestoreAcrossEnvironment: false,
            rollbackDirectory,
            "0.5.0"),
        Passphrase,
        progress: null,
        CancellationToken.None);

    Assert.Null(result.RollbackPath);
    Assert.Equal(0, result.RestoredFileCount);
    Assert.False(Directory.Exists(rollbackDirectory));
    Assert.Equal(before, await File.ReadAllTextAsync(targetPreferences));
    Assert.Contains(
        result.Warnings,
        warning => warning.Code == "password-csv-import-required");
  }

  [Fact]
  public async Task EnvironmentMismatchFallsBackToMigration()
  {
    using var temporary = new TemporaryDirectory();
    var setup = CreateSetup(
        temporary,
        fingerprintProvider: new FixedFingerprintProvider("target"));
    var sourceContainerService = CreateContainerService();
    var sourceBackupService = new ProfileBackupService(
        [setup.Source],
        new FixedProcessInspector(),
        new FixedFingerprintProvider("source"),
        sourceContainerService);
    var sourceBackup = await sourceBackupService.CreateAsync(
        new ProfileBackupRequest(
            temporary.GetPath("different-environment.bvb"),
            [
              new ProfileBackupSelection(
                  setup.SourceProfile,
                  BackupComponent.Settings),
            ],
            "0.5.0"),
        Passphrase,
        progress: null,
        CancellationToken.None);

    var result = await setup.RestoreService.RestoreAsync(
        new ProfileRestoreRequest(
            sourceBackup.Container.FinalPath,
            [
              new ProfileRestoreMapping(
                  Assert.Single(
                      sourceBackup.Container.Verification.Manifest.Profiles)
                      .BackupProfileId,
                  setup.TargetProfile,
                  BackupComponent.Settings),
            ],
            ProfileRestoreMode.SameEnvironment,
            AllowFullRestoreAcrossEnvironment: false,
            temporary.CreateDirectory("migration-rollbacks"),
            "0.5.0"),
        Passphrase,
        progress: null,
        CancellationToken.None);

    Assert.False(result.EnvironmentMatched);
    Assert.Equal(ProfileRestoreMode.Migration, result.EffectiveMode);
    Assert.Contains(
        result.Warnings,
        warning => warning.Code == "environment-mismatch-mode-changed");
  }

  [Fact]
  public async Task DifferentWindowsUserFallsBackToMigrationAndOwnerIsEncrypted()
  {
    using var temporary = new TemporaryDirectory();
    var setup = CreateSetup(temporary);
    var sourceOwner = new WindowsUserProfileIdentity(
        "source-user-hash",
        "source-user",
        temporary.CreateDirectory("windows-users", "source"),
        IsCurrentUser: false);
    var targetOwner = new WindowsUserProfileIdentity(
        "target-user-hash",
        "target-user",
        temporary.CreateDirectory("windows-users", "target"),
        IsCurrentUser: true);
    var sourceProfile = setup.SourceProfile with
    {
      WindowsUser = sourceOwner,
    };
    var targetProfile = setup.TargetProfile with
    {
      WindowsUser = targetOwner,
    };
    var sourceBackup = await setup.BackupService.CreateAsync(
        new ProfileBackupRequest(
            temporary.GetPath("different-windows-user.bvb"),
            [
              new ProfileBackupSelection(
                  sourceProfile,
                  BackupComponent.Settings),
            ],
            "0.9.0"),
        Passphrase,
        progress: null,
        CancellationToken.None);
    var manifestProfile = Assert.Single(
        sourceBackup.Container.Verification.Manifest.Profiles);

    Assert.Equal(
        sourceOwner.StableId,
        manifestProfile.SourceWindowsUserId);
    Assert.Equal(
        sourceOwner.DisplayName,
        manifestProfile.SourceWindowsUserDisplayName);

    var result = await setup.RestoreService.RestoreAsync(
        new ProfileRestoreRequest(
            sourceBackup.Container.FinalPath,
            [
              new ProfileRestoreMapping(
                  manifestProfile.BackupProfileId,
                  targetProfile,
                  BackupComponent.Settings),
            ],
            ProfileRestoreMode.SameEnvironment,
            AllowFullRestoreAcrossEnvironment: false,
            temporary.CreateDirectory("windows-user-rollbacks"),
            "0.9.0"),
        Passphrase,
        progress: null,
        CancellationToken.None);

    Assert.True(result.EnvironmentMatched);
    Assert.Equal(ProfileRestoreMode.Migration, result.EffectiveMode);
    Assert.Contains(
        result.Warnings,
        warning => warning.Code == "environment-mismatch-mode-changed"
            && warning.Message.Contains(
                "Windowsユーザー",
                StringComparison.Ordinal));
  }

  [Fact]
  public async Task MultipleProfilesRestoreOnlyToTheirMappedTargets()
  {
    using var temporary = new TemporaryDirectory();
    var userDataRoot = temporary.CreateDirectory("multi-user-data");
    var sourcePathA = temporary.CreateDirectory(
        "multi-user-data",
        "source-a");
    var sourcePathB = temporary.CreateDirectory(
        "multi-user-data",
        "source-b");
    var targetPathA = temporary.CreateDirectory(
        "multi-user-data",
        "target-a");
    var targetPathB = temporary.CreateDirectory(
        "multi-user-data",
        "target-b");
    temporary.WriteFile(
        Path.Combine("multi-user-data", "source-a", "Preferences"),
        "source-a-settings");
    temporary.WriteFile(
        Path.Combine("multi-user-data", "source-a", "Bookmarks"),
        "source-a-bookmarks");
    temporary.WriteFile(
        Path.Combine("multi-user-data", "source-b", "Preferences"),
        "source-b-settings");
    temporary.WriteFile(
        Path.Combine("multi-user-data", "source-b", "Bookmarks"),
        "source-b-bookmarks");
    temporary.WriteFile(
        Path.Combine("multi-user-data", "target-a", "Preferences"),
        "target-a-before");
    temporary.WriteFile(
        Path.Combine("multi-user-data", "target-a", "Bookmarks"),
        "target-a-bookmarks-before");
    temporary.WriteFile(
        Path.Combine("multi-user-data", "target-b", "Preferences"),
        "target-b-before");
    temporary.WriteFile(
        Path.Combine("multi-user-data", "target-b", "Bookmarks"),
        "target-b-bookmarks-before");

    var source = new SyntheticBrowserSource(userDataRoot);
    var sourceProfileA = CreateProfile("source-a", sourcePathA);
    var sourceProfileB = CreateProfile("source-b", sourcePathB);
    var targetProfileA = CreateProfile("target-a", targetPathA);
    var targetProfileB = CreateProfile("target-b", targetPathB);
    var processInspector = new FixedProcessInspector();
    var fingerprint =
        new FixedFingerprintProvider("multi-profile-environment");
    var containerService = CreateContainerService();
    var backupService = new ProfileBackupService(
        [source],
        processInspector,
        fingerprint,
        containerService);
    var restoreService = new ProfileRestoreService(
        [source],
        processInspector,
        fingerprint,
        containerService,
        backupService,
        new SafeRestoreFileApplier());
    const BackupComponent components =
        BackupComponent.Settings | BackupComponent.Bookmarks;

    var backup = await backupService.CreateAsync(
        new ProfileBackupRequest(
            temporary.GetPath("multi-profile.bvb"),
            [
              new ProfileBackupSelection(sourceProfileA, components),
              new ProfileBackupSelection(sourceProfileB, components),
            ],
            "0.9.0"),
        Passphrase,
        progress: null,
        CancellationToken.None);
    var manifestBySource = backup.Container.Verification.Manifest.Profiles
        .ToDictionary(
            profile => profile.SourceProfileId,
            StringComparer.Ordinal);

    var result = await restoreService.RestoreAsync(
        new ProfileRestoreRequest(
            backup.Container.FinalPath,
            [
              new ProfileRestoreMapping(
                  manifestBySource[sourceProfileA.ProfileId]
                      .BackupProfileId,
                  targetProfileA,
                  components),
              new ProfileRestoreMapping(
                  manifestBySource[sourceProfileB.ProfileId]
                      .BackupProfileId,
                  targetProfileB,
                  components),
            ],
            ProfileRestoreMode.SameEnvironment,
            AllowFullRestoreAcrossEnvironment: false,
            temporary.CreateDirectory("multi-rollbacks"),
            "0.9.0"),
        Passphrase,
        progress: null,
        CancellationToken.None);

    Assert.Equal(
        "source-a-settings",
        await File.ReadAllTextAsync(
            Path.Combine(targetPathA, "Preferences")));
    Assert.Equal(
        "source-a-bookmarks",
        await File.ReadAllTextAsync(
            Path.Combine(targetPathA, "Bookmarks")));
    Assert.Equal(
        "source-b-settings",
        await File.ReadAllTextAsync(
            Path.Combine(targetPathB, "Preferences")));
    Assert.Equal(
        "source-b-bookmarks",
        await File.ReadAllTextAsync(
            Path.Combine(targetPathB, "Bookmarks")));
    Assert.True(File.Exists(result.RollbackPath));
    Assert.Equal(4, result.RestoredFileCount);
  }

  [Theory]
  [InlineData(true)]
  [InlineData(false)]
  public async Task NetworkRestoreOrRollbackPathIsRejectedBeforeContainerRead(
      bool networkPathIsTarget)
  {
    using var temporary = new TemporaryDirectory();
    var setup = CreateSetup(temporary);
    const string networkPath =
        @"\\invalid-ytec-server\browser-capsule\profile";
    var target = networkPathIsTarget
        ? setup.TargetProfile with
        {
          AbsoluteProfilePath = networkPath,
        }
        : setup.TargetProfile;
    var rollbackDirectory = networkPathIsTarget
        ? temporary.GetPath("unused-local-rollbacks")
        : networkPath;

    var exception = await Assert.ThrowsAsync<RestorePlanException>(
        () => setup.RestoreService.RestoreAsync(
            new ProfileRestoreRequest(
                temporary.GetPath("missing-container.bvb"),
                [
                  new ProfileRestoreMapping(
                      "not-read",
                      target,
                      BackupComponent.Settings),
                ],
                ProfileRestoreMode.Migration,
                AllowFullRestoreAcrossEnvironment: false,
                rollbackDirectory,
                "0.9.0"),
            Passphrase,
            progress: null,
            CancellationToken.None));

    Assert.Contains(
        "ローカル",
        exception.Message,
        StringComparison.Ordinal);
  }

  private static RestoreSetup CreateSetup(
      TemporaryDirectory temporary,
      IRestoreFileApplier? applier = null,
      IEnvironmentFingerprintProvider? fingerprintProvider = null,
      string browserId = "chrome")
  {
    var sourcePath = temporary.CreateDirectory(
        "profiles",
        "source");
    var targetPath = temporary.CreateDirectory(
        "profiles",
        "target");
    temporary.WriteFile(
        Path.Combine("profiles", "source", "Preferences"),
        "source-preferences");
    temporary.WriteFile(
        Path.Combine("profiles", "source", "Bookmarks"),
        "source-bookmarks");
    temporary.WriteFile(
        Path.Combine("profiles", "target", "Preferences"),
        "target-preferences");
    temporary.WriteFile(
        Path.Combine("profiles", "target", "Bookmarks"),
        "target-bookmarks");
    var source = new SyntheticBrowserSource(
        temporary.CreateDirectory("user-data"), browserId);
    var sourceProfile = CreateProfile("source-profile", sourcePath) with { BrowserId = browserId };
    var targetProfile = CreateProfile("target-profile", targetPath) with { BrowserId = browserId };
    var processInspector = new FixedProcessInspector();
    var fingerprint = fingerprintProvider
        ?? new FixedFingerprintProvider("synthetic-environment");
    var containerService = CreateContainerService();
    var backupService = new ProfileBackupService(
        [source],
        processInspector,
        fingerprint,
        containerService);
    var restoreService = new ProfileRestoreService(
        [source],
        processInspector,
        fingerprint,
        containerService,
        backupService,
        applier ?? new SafeRestoreFileApplier());
    return new RestoreSetup(
        source,
        sourceProfile,
        targetProfile,
        backupService,
        restoreService);
  }

  private static BvbBackupContainerService CreateContainerService()
  {
    return new BvbBackupContainerService(
        new BvbContainerSecurityOptions(
            BvbContainerSecurityOptions.MinimumPbkdf2Iterations,
            64 * 1024,
            new Version(0, 5, 0)));
  }

  private static BrowserProfile CreateProfile(
      string profileId,
      string path)
  {
    return new BrowserProfile(
        "chrome",
        "synthetic-installation",
        profileId,
        profileId,
        Path.GetFileName(path),
        path,
        Path.GetDirectoryName(path)!,
        IsDefault: false,
        LastUsedUtc: null,
        BrowserChannel.Stable,
        ProfileDiscoveryConfidence.Metadata);
  }

  private sealed class SyntheticBrowserSource(string userDataRoot, string browserId = "chrome") :
      IBrowserProfileSource,
      IBrowserBackupPlanner
  {
    private readonly FileSystemBackupPlanBuilder _builder =
        new(browserId == "firefox" ? FirefoxBackupRules.Create() : ChromiumBackupRules.Create(), TimeSpan.Zero);

    public string BrowserId => browserId;

    public string DisplayName => "合成Chrome";

    public Task<IReadOnlyList<BrowserInstallation>>
        DiscoverInstallationsAsync(CancellationToken cancellationToken)
    {
      return Task.FromResult<IReadOnlyList<BrowserInstallation>>(
      [
        new BrowserInstallation(
            BrowserId,
            "synthetic-installation",
            DisplayName,
            userDataRoot,
            ExecutablePath: null,
            BrowserChannel.Stable),
      ]);
    }

    public Task<IReadOnlyList<BrowserProfile>> DiscoverProfilesAsync(
        BrowserInstallation installation,
        CancellationToken cancellationToken)
    {
      return Task.FromResult<IReadOnlyList<BrowserProfile>>([]);
    }

    public IReadOnlyList<ProcessMatchRule> GetProcessMatchRules(
        BrowserInstallation installation)
    {
      return [];
    }

    public Task<BrowserBackupPlan> BuildBackupPlanAsync(
        BrowserProfile profile,
        string backupProfileId,
        BackupComponent selection,
        CancellationToken cancellationToken)
    {
      return _builder.BuildAsync(
          profile,
          backupProfileId,
          selection,
          cancellationToken);
    }
  }

  private sealed class FixedProcessInspector : IBrowserProcessInspector
  {
    public Task<bool> IsRunningAsync(
        IReadOnlyList<ProcessMatchRule> rules,
        CancellationToken cancellationToken)
    {
      return Task.FromResult(false);
    }
  }

  private sealed class FixedFingerprintProvider(string fingerprint) :
      IEnvironmentFingerprintProvider
  {
    public string GetCurrentFingerprint()
    {
      return fingerprint;
    }
  }

  private sealed class FailAfterFirstApplyApplier : IRestoreFileApplier
  {
    private readonly SafeRestoreFileApplier _inner = new();

    public int CallCount { get; private set; }

    public async Task ApplyAsync(
        IReadOnlyList<RestoreFileOperation> operations,
        Func<CancellationToken, Task> ensureBrowserClosedAsync,
        IProgress<ProfileRestoreProgress>? progress,
        CancellationToken cancellationToken)
    {
      CallCount++;
      await _inner.ApplyAsync(
          operations,
          ensureBrowserClosedAsync,
          progress,
          cancellationToken);
      if (CallCount == 1)
      {
        throw new IOException("synthetic failure after primary apply");
      }
    }
  }

  private sealed class MemoryProtectedPasswordCsv(byte[] bytes) :
      IProtectedPasswordCsv
  {
    public PasswordCsvValidationResult Validation { get; } =
        new(
            ["url", "username", "password"],
            RecordCount: 1,
            FileBytes: bytes.LongLength);

    public long PlaintextLength => bytes.LongLength;

    public string? ResidualPlaintextPath => null;

    public ValueTask<Stream> OpenReadAsync(
        CancellationToken cancellationToken)
    {
      cancellationToken.ThrowIfCancellationRequested();
      return ValueTask.FromResult<Stream>(
          new MemoryStream(bytes, writable: false));
    }

    public ValueTask DisposeAsync()
    {
      Array.Clear(bytes);
      return ValueTask.CompletedTask;
    }
  }

  private sealed record RestoreSetup(
      IBrowserProfileSource Source,
      BrowserProfile SourceProfile,
      BrowserProfile TargetProfile,
      ProfileBackupService BackupService,
      ProfileRestoreService RestoreService);
}
