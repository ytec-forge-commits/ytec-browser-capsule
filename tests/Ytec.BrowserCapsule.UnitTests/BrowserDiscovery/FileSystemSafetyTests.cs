using Ytec.BrowserCapsule.Browsers.Common.BrowserDiscovery;

namespace Ytec.BrowserCapsule.UnitTests.BrowserDiscovery;

public sealed class FileSystemSafetyTests
{
  [Fact]
  public void ReparsePointAttributeIsAlwaysSkipped()
  {
    Assert.True(FileSystemSafety.ShouldSkip(FileAttributes.ReparsePoint));
    Assert.True(FileSystemSafety.ShouldSkip(
        FileAttributes.Directory | FileAttributes.ReparsePoint));
    Assert.False(FileSystemSafety.ShouldSkip(FileAttributes.Directory));
  }
}
