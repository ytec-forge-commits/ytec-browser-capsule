using System.Runtime.Versioning;
using Ytec.BrowserCapsule.Browsers.Chrome.BrowserDiscovery;
using Ytec.BrowserCapsule.Browsers.Chromium.BrowserDiscovery;
using Ytec.BrowserCapsule.Browsers.Common.BrowserDiscovery;
using Ytec.BrowserCapsule.Browsers.Edge.BrowserDiscovery;

namespace Ytec.BrowserCapsule.UnitTests.BrowserDiscovery;

[SupportedOSPlatform("windows")]
public sealed class ChromiumInstallationLocatorTests
{
  [Theory]
  [InlineData("chrome")]
  [InlineData("edge")]
  public async Task UserDataPolicyOverridesAreDiscovered(
      string browserId)
  {
    using var temporary = new TemporaryDirectory();
    var policyRoot = temporary.CreateDirectory("PolicyRoot");
    temporary.CreateDirectory("PolicyRoot", "Profile 7");
    temporary.WriteFile(
        Path.Combine("PolicyRoot", "Local State"),
        """
        {
          "profile": {
            "info_cache": {
              "Profile 7": { "name": "ポリシー用" }
            }
          }
        }
        """);

    var expander = new SafePathVariableExpander(
        new Dictionary<string, string>
        {
          ["test_root"] = temporary.Path,
        });
    var policyReader = new FakePolicyValueReader(
        @"${test_root}\PolicyRoot");

    var source = browserId == "chrome"
        ? (Domain.BrowserDiscovery.IBrowserProfileSource)
            new ChromeBrowserProfileSource(policyReader, expander)
        : new EdgeBrowserProfileSource(policyReader, expander);

    var installations = await source.DiscoverInstallationsAsync(
        CancellationToken.None);
    var installation = Assert.Single(installations);
    var profiles = await source.DiscoverProfilesAsync(
        installation,
        CancellationToken.None);

    Assert.Equal(policyRoot, installation.UserDataRoot);
    Assert.Equal("ポリシー用", Assert.Single(profiles).DisplayName);
  }

  [Fact]
  public void UnknownOrRelativeVariablesAreRejected()
  {
    var expander = new SafePathVariableExpander(
        new Dictionary<string, string>
        {
          ["known"] = @"C:\Known",
        });

    Assert.Equal(
        Path.GetFullPath(@"C:\Known\Profile"),
        expander.TryExpandAbsolutePath(@"${known}\Profile"));
    Assert.Null(expander.TryExpandAbsolutePath(@"${unknown}\Profile"));
    Assert.Null(expander.TryExpandAbsolutePath(@"relative\Profile"));
  }

  private sealed class FakePolicyValueReader(string? currentUserValue)
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
}
