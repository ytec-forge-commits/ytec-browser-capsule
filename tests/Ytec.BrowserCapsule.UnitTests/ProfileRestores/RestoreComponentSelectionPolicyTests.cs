using Ytec.BrowserCapsule.Application.ProfileRestores;
using Ytec.BrowserCapsule.Domain.BackupContainers;
using Ytec.BrowserCapsule.Domain.ProfileBackups;

namespace Ytec.BrowserCapsule.UnitTests.ProfileRestores;

public sealed class RestoreComponentSelectionPolicyTests
{
  [Fact]
  public void ReturnsOnlyComponentsPresentInVerifiedFiles()
  {
    var manifest = CreateManifest(
        [
          new BackupFileManifest(
              "profiles/profile-1/Preferences",
              2,
              new string('0', 64),
              "settings"),
          new BackupFileManifest(
              "profiles/profile-1/Bookmarks",
              2,
              new string('1', 64),
              "bookmarks"),
        ],
        passwordCsvIncluded: true);

    var result =
        RestoreComponentSelectionPolicy.GetAvailableComponents(manifest);

    Assert.Equal(
        BackupComponent.Settings
        | BackupComponent.Bookmarks
        | BackupComponent.PasswordCsv,
        result);
  }

  [Fact]
  public void LegacyFilesWithoutComponentRequireFullProfileSelection()
  {
    var manifest = CreateManifest(
        [
          new BackupFileManifest(
              "profiles/profile-1/legacy.dat",
              2,
              new string('0', 64)),
        ],
        passwordCsvIncluded: false);

    var result =
        RestoreComponentSelectionPolicy.GetAvailableComponents(manifest);

    Assert.Equal(BackupComponent.FullProfile, result);
  }

  [Fact]
  public void EmptyManifestHasNoAvailableComponents()
  {
    var manifest = CreateManifest([], passwordCsvIncluded: false);

    var result =
        RestoreComponentSelectionPolicy.GetAvailableComponents(manifest);

    Assert.Equal(BackupComponent.None, result);
  }

  private static BackupManifest CreateManifest(
      IReadOnlyList<BackupFileManifest> files,
      bool passwordCsvIncluded)
  {
    return new BackupManifest(
        FormatVersion: 1,
        BackupId: Guid.Parse(
            "00000000-0000-0000-0000-000000000001"),
        CreatedUtc: DateTimeOffset.UnixEpoch,
        AppVersion: "0.9.0",
        EnvironmentFingerprint: "test-environment",
        [
          new BackupProfileManifest(
              BackupProfileId: "profile-1",
              BrowserId: "chrome",
              SourceProfileId: "source-1",
              DisplayName: "テスト",
              Components: [],
              Files: files,
              PasswordCsv: passwordCsvIncluded
                  ? new BackupPasswordCsvDescriptor(
                      Included: true,
                      RecordCount: 1,
                      OriginalFileNameStored: false)
                  : null,
              Warnings: []),
        ]);
  }
}
