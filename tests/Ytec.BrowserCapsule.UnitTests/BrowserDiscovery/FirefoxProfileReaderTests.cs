using System.Runtime.Versioning;
using Ytec.BrowserCapsule.Browsers.Firefox.BrowserDiscovery;
using Ytec.BrowserCapsule.Domain.BrowserDiscovery;

namespace Ytec.BrowserCapsule.UnitTests.BrowserDiscovery;

public sealed class FirefoxProfileReaderTests
{
  [Fact]
  public void RelativeAndAbsoluteProfilesAndMultipleDefaultsAreRead()
  {
    using var temporary = new TemporaryDirectory();
    temporary.WriteFile(
        Path.Combine("Profiles", "relative.default", "prefs.js"),
        "// test");
    var absoluteProfile = temporary.CreateDirectory("External", "absolute.work");
    temporary.WriteFile(
        Path.Combine("External", "absolute.work", "prefs.js"),
        "// test");
    temporary.WriteFile(
        "profiles.ini",
        $"""
        [Profile0]
        Name=標準
        IsRelative=1
        Path=Profiles/relative.default
        Default=1

        [Profile12]
        Name=仕事用
        IsRelative=0
        Path={absoluteProfile}
        Default=1
        """);
    temporary.WriteFile(
        "installs.ini",
        """
        [InstallABC]
        Default=Profiles/relative.default
        Locked=1
        """);

    var profiles = FirefoxProfileReader.Read(
        CreateInstallation(temporary.Path),
        CancellationToken.None);

    Assert.Equal(2, profiles.Count);
    Assert.All(profiles, profile => Assert.True(profile.IsDefault));
    Assert.Contains(
        profiles,
        profile => profile.DisplayName == "標準"
            && profile.ProfileDirectoryName == "relative.default");
    Assert.Contains(
        profiles,
        profile => profile.DisplayName == "仕事用"
            && profile.AbsoluteProfilePath == absoluteProfile);
  }

  [Fact]
  public void CorruptIniFallsBackToProfilesWithFeatureFile()
  {
    using var temporary = new TemporaryDirectory();
    temporary.WriteFile("profiles.ini", "this is not an ini file");
    temporary.WriteFile(
        Path.Combine("Profiles", "abc.default", "prefs.js"),
        "// test");
    temporary.CreateDirectory("Profiles", "not-a-profile");

    var profiles = FirefoxProfileReader.Read(
        CreateInstallation(temporary.Path),
        CancellationToken.None);

    var profile = Assert.Single(profiles);
    Assert.Equal("abc.default", profile.DisplayName);
    Assert.Equal(
        ProfileDiscoveryConfidence.DirectoryFallback,
        profile.Confidence);
  }

  [Fact]
  [SupportedOSPlatform("windows")]
  public async Task MultipleWindowsUserScopesKeepOwnersAndIdsSeparate()
  {
    using var temporary = new TemporaryDirectory();
    var firstUserPath = temporary.CreateDirectory("Users", "first");
    var secondUserPath = temporary.CreateDirectory("Users", "second");
    var firstSettings = Path.Combine(
        firstUserPath,
        "AppData",
        "Roaming",
        "Mozilla",
        "Firefox");
    var secondSettings = Path.Combine(
        secondUserPath,
        "AppData",
        "Roaming",
        "Mozilla",
        "Firefox");
    Directory.CreateDirectory(
        Path.Combine(firstSettings, "Profiles", "first.default"));
    Directory.CreateDirectory(
        Path.Combine(secondSettings, "Profiles", "second.default"));
    await File.WriteAllTextAsync(
        Path.Combine(firstSettings, "Profiles", "first.default", "prefs.js"),
        "// first");
    await File.WriteAllTextAsync(
        Path.Combine(secondSettings, "Profiles", "second.default", "prefs.js"),
        "// second");
    await File.WriteAllTextAsync(
        Path.Combine(firstSettings, "profiles.ini"),
        """
        [Profile0]
        Name=first
        IsRelative=1
        Path=Profiles/first.default
        """);
    await File.WriteAllTextAsync(
        Path.Combine(secondSettings, "profiles.ini"),
        """
        [Profile0]
        Name=second
        IsRelative=1
        Path=Profiles/second.default
        """);
    var firstOwner = new WindowsUserProfileIdentity(
        "first-owner",
        "first",
        firstUserPath,
        IsCurrentUser: false);
    var secondOwner = new WindowsUserProfileIdentity(
        "second-owner",
        "second",
        secondUserPath,
        IsCurrentUser: false);
    var source = new FirefoxBrowserProfileSource(
        [firstOwner, secondOwner]);

    var installations = await source.DiscoverInstallationsAsync(
        CancellationToken.None);
    var profiles = new List<BrowserProfile>();
    foreach (var installation in installations)
    {
      profiles.AddRange(await source.DiscoverProfilesAsync(
          installation,
          CancellationToken.None));
    }

    Assert.Equal(2, installations.Count);
    Assert.Equal(
        2,
        installations.Select(item => item.InstallationId)
            .Distinct(StringComparer.Ordinal)
            .Count());
    Assert.Contains(
        profiles,
        profile => profile.DisplayName == "first"
            && profile.WindowsUser == firstOwner);
    Assert.Contains(
        profiles,
        profile => profile.DisplayName == "second"
            && profile.WindowsUser == secondOwner);
  }

  private static BrowserInstallation CreateInstallation(string root)
  {
    return new BrowserInstallation(
        "firefox",
        "firefox-test",
        "Mozilla Firefox",
        root,
        ExecutablePath: null,
        BrowserChannel.StableOrEsr);
  }
}
