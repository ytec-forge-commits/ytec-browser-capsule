using System.Security.Cryptography;
using System.Text;

namespace Ytec.BrowserCapsule.Infrastructure.BackupContainers;

internal static class BvbKeyDerivation
{
  public static byte[] DeriveKey(
      ReadOnlyMemory<char> passphrase,
      ReadOnlySpan<byte> salt,
      int iterations)
  {
    var passphraseBytes = GC.AllocateUninitializedArray<byte>(
        Encoding.UTF8.GetMaxByteCount(passphrase.Length));

    try
    {
      var byteCount = Encoding.UTF8.GetBytes(
          passphrase.Span,
          passphraseBytes);
      var key = new byte[32];
      Rfc2898DeriveBytes.Pbkdf2(
          passphraseBytes.AsSpan(0, byteCount),
          salt,
          key,
          iterations,
          HashAlgorithmName.SHA256);
      return key;
    }
    finally
    {
      CryptographicOperations.ZeroMemory(passphraseBytes);
    }
  }
}
