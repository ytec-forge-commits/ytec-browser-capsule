namespace Ytec.BrowserCapsule.Infrastructure.BackupContainers;

/// <summary>
/// BVB形式の暗号パラメーターです。
/// </summary>
public sealed record BvbContainerSecurityOptions(
    int Pbkdf2Iterations,
    int ChunkSizeBytes,
    Version MinimumAppVersion)
{
  public const int MinimumPbkdf2Iterations = 600_000;
  public const int MaximumPbkdf2Iterations = 20_000_000;
  public const int DefaultChunkSizeBytes = 4 * 1024 * 1024;
  public const int MinimumChunkSizeBytes = 64 * 1024;
  public const int MaximumChunkSizeBytes = 16 * 1024 * 1024;

  public static BvbContainerSecurityOptions CreateMinimumSecureDefault()
  {
    return new BvbContainerSecurityOptions(
        MinimumPbkdf2Iterations,
        DefaultChunkSizeBytes,
        new Version(0, 2, 0));
  }

  public static BvbContainerSecurityOptions CreateCalibrated()
  {
    return new BvbContainerSecurityOptions(
        Pbkdf2WorkFactorCalibrator.CalibrateIterations(),
        DefaultChunkSizeBytes,
        new Version(0, 2, 0));
  }

  internal void Validate()
  {
    if (Pbkdf2Iterations is < MinimumPbkdf2Iterations
        or > MaximumPbkdf2Iterations)
    {
      throw new ArgumentOutOfRangeException(
          nameof(Pbkdf2Iterations),
          "PBKDF2反復回数が許容範囲外です。");
    }

    if (ChunkSizeBytes is < MinimumChunkSizeBytes
        or > MaximumChunkSizeBytes)
    {
      throw new ArgumentOutOfRangeException(
          nameof(ChunkSizeBytes),
          "暗号チャンクサイズが許容範囲外です。");
    }

    ArgumentNullException.ThrowIfNull(MinimumAppVersion);
    if (MinimumAppVersion.Major is < 0 or > ushort.MaxValue
        || MinimumAppVersion.Minor is < 0 or > ushort.MaxValue
        || Math.Max(0, MinimumAppVersion.Build) > ushort.MaxValue)
    {
      throw new ArgumentOutOfRangeException(
          nameof(MinimumAppVersion),
          "最小アプリバージョンをヘッダーへ格納できません。");
    }
  }
}
