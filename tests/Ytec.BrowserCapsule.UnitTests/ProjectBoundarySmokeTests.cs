using ApplicationMarker = Ytec.BrowserCapsule.Application.AssemblyMarker;
using ChromeMarker = Ytec.BrowserCapsule.Browsers.Chrome.AssemblyMarker;
using ChromiumMarker = Ytec.BrowserCapsule.Browsers.Chromium.AssemblyMarker;
using CommonMarker = Ytec.BrowserCapsule.Browsers.Common.AssemblyMarker;
using DomainMarker = Ytec.BrowserCapsule.Domain.AssemblyMarker;
using EdgeMarker = Ytec.BrowserCapsule.Browsers.Edge.AssemblyMarker;
using FirefoxMarker = Ytec.BrowserCapsule.Browsers.Firefox.AssemblyMarker;

namespace Ytec.BrowserCapsule.UnitTests;

public sealed class ProjectBoundarySmokeTests
{
  [Fact]
  public void PhaseZeroLibraryAssembliesAreLoadable()
  {
    var expectedAssemblies = new Dictionary<Type, string>
    {
      [typeof(ApplicationMarker)] = "Ytec.BrowserCapsule.Application",
      [typeof(DomainMarker)] = "Ytec.BrowserCapsule.Domain",
      [typeof(CommonMarker)] = "Ytec.BrowserCapsule.Browsers.Common",
      [typeof(ChromiumMarker)] = "Ytec.BrowserCapsule.Browsers.Chromium",
      [typeof(ChromeMarker)] = "Ytec.BrowserCapsule.Browsers.Chrome",
      [typeof(EdgeMarker)] = "Ytec.BrowserCapsule.Browsers.Edge",
      [typeof(FirefoxMarker)] = "Ytec.BrowserCapsule.Browsers.Firefox",
    };

    Assert.All(
        expectedAssemblies,
        expected => Assert.Equal(
            expected.Value,
            expected.Key.Assembly.GetName().Name));
  }
}
