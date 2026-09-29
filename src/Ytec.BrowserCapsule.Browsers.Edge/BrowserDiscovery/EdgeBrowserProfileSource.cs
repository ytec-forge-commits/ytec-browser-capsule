using System.Runtime.Versioning;
using Ytec.BrowserCapsule.Browsers.Chromium.BrowserDiscovery;
using Ytec.BrowserCapsule.Browsers.Common.BrowserDiscovery;
using Ytec.BrowserCapsule.Domain.BrowserDiscovery;

namespace Ytec.BrowserCapsule.Browsers.Edge.BrowserDiscovery;

/// <summary>
/// Microsoft Edge Stableのプロファイルを検出します。
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class EdgeBrowserProfileSource : ChromiumBrowserProfileSource
{
  private static readonly ChromiumBrowserConfiguration Configuration = new(
      BrowserId: "edge",
      DisplayName: "Microsoft Edge",
      PolicySubKey: @"SOFTWARE\Policies\Microsoft\Edge",
      StandardUserDataRoot:
          @"${local_app_data}\Microsoft\Edge\User Data",
      ExecutableCandidates:
      [
        @"${program_files_x86}\Microsoft\Edge\Application\msedge.exe",
        @"${program_files}\Microsoft\Edge\Application\msedge.exe",
        @"${local_app_data}\Microsoft\Edge\Application\msedge.exe",
      ],
      ProcessName: "msedge",
      Channel: BrowserChannel.Stable);

  public EdgeBrowserProfileSource()
      : base(Configuration)
  {
  }

  public EdgeBrowserProfileSource(
      IReadOnlyList<WindowsUserProfileIdentity> windowsUsers)
      : base(Configuration, windowsUsers)
  {
  }

  public EdgeBrowserProfileSource(
      IChromiumPolicyValueReader policyReader,
      SafePathVariableExpander pathExpander)
      : base(Configuration, policyReader, pathExpander)
  {
  }
}
