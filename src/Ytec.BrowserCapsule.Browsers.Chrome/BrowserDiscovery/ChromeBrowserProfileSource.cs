using System.Runtime.Versioning;
using Ytec.BrowserCapsule.Browsers.Chromium.BrowserDiscovery;
using Ytec.BrowserCapsule.Browsers.Common.BrowserDiscovery;
using Ytec.BrowserCapsule.Domain.BrowserDiscovery;

namespace Ytec.BrowserCapsule.Browsers.Chrome.BrowserDiscovery;

/// <summary>
/// Google Chrome Stableのプロファイルを検出します。
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ChromeBrowserProfileSource : ChromiumBrowserProfileSource
{
  private static readonly ChromiumBrowserConfiguration Configuration = new(
      BrowserId: "chrome",
      DisplayName: "Google Chrome",
      PolicySubKey: @"SOFTWARE\Policies\Google\Chrome",
      StandardUserDataRoot:
          @"${local_app_data}\Google\Chrome\User Data",
      ExecutableCandidates:
      [
        @"${local_app_data}\Google\Chrome\Application\chrome.exe",
        @"${program_files}\Google\Chrome\Application\chrome.exe",
        @"${program_files_x86}\Google\Chrome\Application\chrome.exe",
      ],
      ProcessName: "chrome",
      Channel: BrowserChannel.Stable);

  public ChromeBrowserProfileSource()
      : base(Configuration)
  {
  }

  public ChromeBrowserProfileSource(
      IReadOnlyList<WindowsUserProfileIdentity> windowsUsers)
      : base(Configuration, windowsUsers)
  {
  }

  public ChromeBrowserProfileSource(
      IChromiumPolicyValueReader policyReader,
      SafePathVariableExpander pathExpander)
      : base(Configuration, policyReader, pathExpander)
  {
  }
}
