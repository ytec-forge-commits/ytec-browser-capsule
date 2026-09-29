using Ytec.BrowserCapsule.Domain.ProfileBackups;

namespace Ytec.BrowserCapsule.Browsers.Common.ProfileBackups;

/// <summary>
/// 移行不能な端末保護データを、解析せずバックアップ対象外にします。
/// </summary>
public static class ProtectedBrowserFileExclusions
{
  public static IReadOnlySet<string> ChromiumFileNames =>
      ProtectedBrowserFilePolicy.ChromiumFileNames;

  public static IReadOnlySet<string> FirefoxFileNames =>
      ProtectedBrowserFilePolicy.FirefoxFileNames;
}
