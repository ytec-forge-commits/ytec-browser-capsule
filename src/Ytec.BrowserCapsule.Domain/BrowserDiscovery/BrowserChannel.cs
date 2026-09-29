namespace Ytec.BrowserCapsule.Domain.BrowserDiscovery;

/// <summary>
/// ブラウザーの配布チャンネルを表します。
/// </summary>
public enum BrowserChannel
{
  Unknown = 0,
  Stable = 1,
  StableOrEsr = 2,
  Esr = 3,
}
