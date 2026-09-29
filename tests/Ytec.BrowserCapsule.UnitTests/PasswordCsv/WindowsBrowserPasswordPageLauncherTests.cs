using System.Runtime.Versioning;
using Ytec.BrowserCapsule.Domain.BrowserDiscovery;
using Ytec.BrowserCapsule.Infrastructure.PasswordCsv;

namespace Ytec.BrowserCapsule.UnitTests.PasswordCsv;

[SupportedOSPlatform("windows")]
public sealed class WindowsBrowserPasswordPageLauncherTests
{
  [Theory]
  [InlineData(
      "chrome",
      "Profile 2",
      "--profile-directory=Profile 2",
      "chrome://password-manager/settings")]
  [InlineData(
      "edge",
      "Default",
      "--profile-directory=Default",
      "edge://wallet/passwords")]
  public void ChromiumLaunchKeepsProfileAndInternalPageAsSeparateArguments(
      string browserId,
      string directoryName,
      string expectedProfileArgument,
      string expectedPage)
  {
    var profile = CreateProfile(browserId, directoryName);
    var installation = CreateInstallation(browserId);

    var request = WindowsBrowserPasswordPageLauncher.BuildLaunchRequest(
        profile,
        installation);

    Assert.Equal(
        [expectedProfileArgument, expectedPage],
        request.Arguments);
  }

  [Fact]
  public void FirefoxLaunchUsesExactProfilePathAndAboutLogins()
  {
    var profile = CreateProfile("firefox", "abc.default");
    var installation = CreateInstallation("firefox");

    var request = WindowsBrowserPasswordPageLauncher.BuildLaunchRequest(
        profile,
        installation);

    Assert.Equal(
        ["-profile", profile.AbsoluteProfilePath, "about:logins"],
        request.Arguments);
  }

  [Fact]
  public void CrossBrowserInstallationIsRejected()
  {
    var profile = CreateProfile("chrome", "Default");
    var installation = CreateInstallation("edge");

    Assert.Throws<InvalidOperationException>(
        () => WindowsBrowserPasswordPageLauncher.BuildLaunchRequest(
            profile,
            installation));
  }

  [Fact]
  public void OtherWindowsUserProfileRequiresThatUserToSignIn()
  {
    var profile = CreateProfile("chrome", "Default") with
    {
      WindowsUser = new WindowsUserProfileIdentity(
          "synthetic-other-user",
          "other-user",
          Path.GetTempPath(),
          IsCurrentUser: false),
    };
    var installation = CreateInstallation("chrome") with
    {
      WindowsUser = profile.WindowsUser,
    };

    var exception = Assert.Throws<InvalidOperationException>(
        () => WindowsBrowserPasswordPageLauncher.BuildLaunchRequest(
            profile,
            installation));

    Assert.Contains(
        "対象のWindowsユーザー",
        exception.Message,
        StringComparison.Ordinal);
  }

  private static BrowserProfile CreateProfile(
      string browserId,
      string directoryName)
  {
    var profilePath = Path.GetFullPath(
        Path.Combine(
            Path.GetTempPath(),
            "YtecBrowserCapsule.Tests",
            browserId,
            directoryName));
    return new BrowserProfile(
        browserId,
        $"{browserId}-installation",
        $"{browserId}:profile",
        "合成プロファイル",
        directoryName,
        profilePath,
        Path.GetDirectoryName(profilePath)!,
        IsDefault: true,
        LastUsedUtc: null,
        BrowserChannel.Stable,
        ProfileDiscoveryConfidence.Metadata);
  }

  private static BrowserInstallation CreateInstallation(string browserId)
  {
    return new BrowserInstallation(
        browserId,
        $"{browserId}-installation",
        browserId,
        Path.GetTempPath(),
        Path.Combine(Path.GetTempPath(), $"{browserId}.exe"),
        BrowserChannel.Stable);
  }
}
