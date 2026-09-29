using Ytec.BrowserCapsule.Browsers.Chromium.BrowserDiscovery;
using Ytec.BrowserCapsule.Domain.BrowserDiscovery;

namespace Ytec.BrowserCapsule.UnitTests.BrowserDiscovery;

public sealed class ChromiumProfileReaderTests
{
  [Fact]
  public void MetadataListsMultipleProfilesWithUnicodeAndTwoDigitDirectory()
  {
    using var temporary = new TemporaryDirectory();
    temporary.CreateDirectory("Profile 10");
    temporary.CreateDirectory("Profile 2");
    temporary.WriteFile(
        "Local State",
        """
        {
          "profile": {
            "info_cache": {
              "Profile 10": { "name": "仕事用 🚀 \"重要\"" },
              "Profile 2": { "name": "家族用" }
            }
          }
        }
        """);

    var installation = CreateInstallation("chrome", temporary.Path);
    var profiles = ChromiumProfileReader.Read(
        installation,
        CancellationToken.None);

    Assert.Equal(2, profiles.Count);
    Assert.Contains(
        profiles,
        profile => profile.ProfileDirectoryName == "Profile 10"
            && profile.DisplayName == "仕事用 🚀 \"重要\""
            && !profile.IsDefault);
    Assert.All(
        profiles,
        profile => Assert.Equal(
            ProfileDiscoveryConfidence.Metadata,
            profile.Confidence));
  }

  [Fact]
  public void CorruptMetadataFallsBackToFeatureFilesAndExcludesSpecialProfiles()
  {
    using var temporary = new TemporaryDirectory();
    temporary.WriteFile("Local State", "{ broken");
    temporary.WriteFile(
        Path.Combine("Work", "Preferences"),
        "{}");
    temporary.WriteFile(
        Path.Combine("Guest Profile", "Preferences"),
        "{}");
    temporary.CreateDirectory("NotAProfile");

    var profiles = ChromiumProfileReader.Read(
        CreateInstallation("edge", temporary.Path),
        CancellationToken.None);

    var profile = Assert.Single(profiles);
    Assert.Equal("Work", profile.ProfileDirectoryName);
    Assert.Equal(
        ProfileDiscoveryConfidence.DirectoryFallback,
        profile.Confidence);
  }

  private static BrowserInstallation CreateInstallation(
      string browserId,
      string root)
  {
    return new BrowserInstallation(
        browserId,
        $"{browserId}-test",
        browserId,
        root,
        ExecutablePath: null,
        BrowserChannel.Stable);
  }
}
