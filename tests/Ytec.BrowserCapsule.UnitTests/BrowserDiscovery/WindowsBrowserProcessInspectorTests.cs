using System.Runtime.Versioning;
using Ytec.BrowserCapsule.Domain.BrowserDiscovery;
using Ytec.BrowserCapsule.Infrastructure.BrowserDiscovery;

namespace Ytec.BrowserCapsule.UnitTests.BrowserDiscovery;

[SupportedOSPlatform("windows")]
public sealed class WindowsBrowserProcessInspectorTests
{
  [Fact]
  public void ExecutablePathRequiresExactCaseInsensitiveMatch()
  {
    var expected = Path.GetFullPath(
        Path.Combine(Path.GetTempPath(), "Browser", "browser.exe"));
    var different = Path.GetFullPath(
        Path.Combine(Path.GetTempPath(), "WebView", "browser.exe"));

    Assert.True(WindowsBrowserProcessInspector.ExecutablePathMatches(
        expected.ToUpperInvariant(),
        [expected]));
    Assert.False(WindowsBrowserProcessInspector.ExecutablePathMatches(
        different,
        [expected]));
  }

  [Fact]
  public async Task CloseControllerDoesNothingWithoutExactRules()
  {
    var inspector = new WindowsBrowserProcessInspector();
    var rule = new ProcessMatchRule(
        $"ytec-no-such-browser-{Guid.NewGuid():N}",
        [Path.Combine(Path.GetTempPath(), "missing-browser.exe")]);

    var graceful = await inspector.RequestCloseAsync(
        [rule],
        TimeSpan.Zero,
        CancellationToken.None);
    var forced = await inspector.TerminateRemainingAsync(
        [rule],
        TimeSpan.Zero,
        CancellationToken.None);

    Assert.Equal(0, graceful.MatchedProcessCount);
    Assert.Equal(0, forced.MatchedProcessCount);
  }
}
