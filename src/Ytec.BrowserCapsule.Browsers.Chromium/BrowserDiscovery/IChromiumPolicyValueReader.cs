namespace Ytec.BrowserCapsule.Browsers.Chromium.BrowserDiscovery;

/// <summary>
/// Chromium系ブラウザーのUserDataDirポリシー値を読み取ります。
/// </summary>
public interface IChromiumPolicyValueReader
{
  string? ReadCurrentUser(string policySubKey);

  string? ReadLocalMachine(string policySubKey);
}
