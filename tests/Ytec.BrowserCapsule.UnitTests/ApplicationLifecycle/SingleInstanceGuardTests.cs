using Ytec.BrowserCapsule.Infrastructure.ApplicationLifecycle;

namespace Ytec.BrowserCapsule.UnitTests.ApplicationLifecycle;

public sealed class SingleInstanceGuardTests
{
  [Fact]
  public void OnlyOneGuardOwnsTheSameNameAtATime()
  {
    var name = $@"Local\YtecBrowserCapsuleTest-{Guid.NewGuid():N}";
    using var first = new SingleInstanceGuard(name);
    using var second = new SingleInstanceGuard(name);

    Assert.True(first.TryAcquire());
    Assert.True(first.TryAcquire());
    Assert.False(second.TryAcquire());

    first.Dispose();

    Assert.True(second.TryAcquire());
  }
}
