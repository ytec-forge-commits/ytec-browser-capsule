namespace Ytec.BrowserCapsule.Domain.ProfileBackups;

/// <summary>
/// 同一Windows環境かを判定するための不可逆な識別値を提供します。
/// </summary>
public interface IEnvironmentFingerprintProvider
{
  string GetCurrentFingerprint();
}
