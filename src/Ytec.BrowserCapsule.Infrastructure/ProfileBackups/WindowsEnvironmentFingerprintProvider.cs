using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using Ytec.BrowserCapsule.Domain.ProfileBackups;

namespace Ytec.BrowserCapsule.Infrastructure.ProfileBackups;

/// <summary>
/// PC名と現在ユーザーSIDを不可逆な同一環境IDへ変換します。
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsEnvironmentFingerprintProvider
    : IEnvironmentFingerprintProvider
{
  public string GetCurrentFingerprint()
  {
    using var identity = WindowsIdentity.GetCurrent();
    var sid = identity.User?.Value
        ?? throw new InvalidOperationException(
            "現在のWindowsユーザーSIDを取得できません。");
    var source = Encoding.UTF8.GetBytes(
        $"{Environment.MachineName}\0{sid}");

    try
    {
      return Convert.ToHexStringLower(SHA256.HashData(source));
    }
    finally
    {
      CryptographicOperations.ZeroMemory(source);
    }
  }
}
