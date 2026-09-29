using Ytec.BrowserCapsule.Infrastructure.BackupContainers;

namespace Ytec.BrowserCapsule.UnitTests.BackupContainers;

public sealed class Pbkdf2WorkFactorTests
{
  [Fact]
  public void CalibrationNeverDropsBelowSecurityMinimum()
  {
    var iterations = Pbkdf2WorkFactorCalibrator.CalibrateIterations(
        TimeSpan.FromMilliseconds(1));

    Assert.InRange(
        iterations,
        BvbContainerSecurityOptions.MinimumPbkdf2Iterations,
        BvbContainerSecurityOptions.MaximumPbkdf2Iterations);
  }

  [Fact]
  public void ServiceRejectsAnInsecureIterationCount()
  {
    var options = new BvbContainerSecurityOptions(
        BvbContainerSecurityOptions.MinimumPbkdf2Iterations - 1,
        BvbContainerSecurityOptions.DefaultChunkSizeBytes,
        new Version(0, 2, 0));

    Assert.Throws<ArgumentOutOfRangeException>(
        () => new BvbBackupContainerService(options));
  }
}
