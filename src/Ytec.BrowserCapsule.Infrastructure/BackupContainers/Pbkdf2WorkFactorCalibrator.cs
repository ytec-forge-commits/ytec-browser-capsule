using System.Diagnostics;
using System.Security.Cryptography;

namespace Ytec.BrowserCapsule.Infrastructure.BackupContainers;

/// <summary>
/// 現在の端末でPBKDF2が約750msになる反復回数を見積もります。
/// </summary>
public static class Pbkdf2WorkFactorCalibrator
{
  private const int ProbeIterations = 100_000;
  private const int RoundingUnit = 10_000;

  public static int CalibrateIterations(TimeSpan? targetDuration = null)
  {
    var target = targetDuration ?? TimeSpan.FromMilliseconds(750);
    if (target <= TimeSpan.Zero)
    {
      throw new ArgumentOutOfRangeException(
          nameof(targetDuration),
          "校正時間は正の値である必要があります。");
    }

    byte[] password = "Y-TEC Browser Capsule calibration"u8.ToArray();
    byte[] salt = new byte[32];
    byte[] key = new byte[32];
    RandomNumberGenerator.Fill(salt);

    try
    {
      var stopwatch = Stopwatch.StartNew();
      Rfc2898DeriveBytes.Pbkdf2(
          password,
          salt,
          key,
          ProbeIterations,
          HashAlgorithmName.SHA256);
      stopwatch.Stop();

      var elapsedMilliseconds = Math.Max(
          1,
          stopwatch.Elapsed.TotalMilliseconds);
      var scaled = ProbeIterations
          * target.TotalMilliseconds
          / elapsedMilliseconds;
      var rounded = (long)Math.Ceiling(scaled / RoundingUnit)
          * RoundingUnit;

      return (int)Math.Clamp(
          rounded,
          BvbContainerSecurityOptions.MinimumPbkdf2Iterations,
          BvbContainerSecurityOptions.MaximumPbkdf2Iterations);
    }
    finally
    {
      CryptographicOperations.ZeroMemory(password);
      CryptographicOperations.ZeroMemory(salt);
      CryptographicOperations.ZeroMemory(key);
    }
  }
}
