using Ytec.BrowserCapsule.Infrastructure.BrowserDiscovery;

namespace Ytec.BrowserCapsule.UnitTests.BrowserDiscovery;

public sealed class ProfileSizeEstimatorTests
{
  [Fact]
  public async Task NestedFilesAreCountedWithoutLoadingTheirContents()
  {
    using var temporary = new TemporaryDirectory();
    temporary.WriteFile("one.txt", "12345");
    temporary.WriteFile(Path.Combine("Nested", "two.txt"), "1234567");
    var estimator = new ProfileSizeEstimator();

    var estimate = await estimator.EstimateAsync(
        temporary.Path,
        CancellationToken.None);

    Assert.Equal(12, estimate.Bytes);
    Assert.Equal(2, estimate.FileCount);
    Assert.True(estimate.IsComplete);
  }
}
