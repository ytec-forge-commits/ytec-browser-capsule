using System.Xml.Linq;

namespace Ytec.BrowserCapsule.IntegrationTests;

public sealed class SolutionArchitectureTests
{
  [Fact]
  public void ProductionProjectReferencesFollowTheDocumentedDirection()
  {
    var expectedReferences = new Dictionary<string, string[]>
    {
      ["Ytec.BrowserCapsule.Domain"] = [],
      ["Ytec.BrowserCapsule.Application"] = ["Ytec.BrowserCapsule.Domain"],
      ["Ytec.BrowserCapsule.Infrastructure"] =
            ["Ytec.BrowserCapsule.Application", "Ytec.BrowserCapsule.Domain"],
      ["Ytec.BrowserCapsule.Browsers.Common"] = ["Ytec.BrowserCapsule.Domain"],
      ["Ytec.BrowserCapsule.Browsers.Chromium"] = ["Ytec.BrowserCapsule.Browsers.Common"],
      ["Ytec.BrowserCapsule.Browsers.Chrome"] = ["Ytec.BrowserCapsule.Browsers.Chromium"],
      ["Ytec.BrowserCapsule.Browsers.Edge"] = ["Ytec.BrowserCapsule.Browsers.Chromium"],
      ["Ytec.BrowserCapsule.Browsers.Firefox"] = ["Ytec.BrowserCapsule.Browsers.Common"],
      ["Ytec.BrowserCapsule.App"] =
        [
            "Ytec.BrowserCapsule.Application",
                "Ytec.BrowserCapsule.Browsers.Chrome",
                "Ytec.BrowserCapsule.Browsers.Edge",
                "Ytec.BrowserCapsule.Browsers.Firefox",
                "Ytec.BrowserCapsule.Infrastructure",
            ],
    };

    var root = FindSolutionRoot();

    foreach (var expected in expectedReferences)
    {
      var projectPath = Path.Combine(
          root,
          "src",
          expected.Key,
          $"{expected.Key}.csproj");

      var actualReferences = XDocument
          .Load(projectPath)
          .Descendants("ProjectReference")
          .Select(reference => reference.Attribute("Include")?.Value)
          .Where(include => include is not null)
          .Select(include => Path.GetFileNameWithoutExtension(include!))
          .Order(StringComparer.Ordinal)
          .ToArray();

      Assert.Equal(
          expected.Value.Order(StringComparer.Ordinal),
          actualReferences);
    }
  }

  [Fact]
  public void AppProjectIsWpfAndRequestsNoElevation()
  {
    var root = FindSolutionRoot();
    var projectPath = Path.Combine(
        root,
        "src",
        "Ytec.BrowserCapsule.App",
        "Ytec.BrowserCapsule.App.csproj");
    var manifestPath = Path.Combine(
        root,
        "src",
        "Ytec.BrowserCapsule.App",
        "app.manifest");

    var project = XDocument.Load(projectPath);
    var targetFramework = project.Descendants("TargetFramework").Single().Value;
    var useWpf = project.Descendants("UseWPF").Single().Value;
    var applicationManifest = project.Descendants("ApplicationManifest").Single().Value;
    var assemblyName = project.Descendants("AssemblyName").Single().Value;
    var productName = project.Descendants("Product").Single().Value;
    var manifest = File.ReadAllText(manifestPath);

    Assert.StartsWith("net10.0-windows", targetFramework, StringComparison.Ordinal);
    Assert.Equal("true", useWpf, ignoreCase: true);
    Assert.Equal("app.manifest", applicationManifest);
    Assert.Equal("YtecBrowserCapsule", assemblyName);
    Assert.Equal("Y-TEC Browser Capsule", productName);
    Assert.Contains(
      "requestedExecutionLevel level=\"asInvoker\"",
      manifest,
        StringComparison.Ordinal);
  }

  private static string FindSolutionRoot()
  {
    for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
         directory is not null;
         directory = directory.Parent)
    {
      if (File.Exists(Path.Combine(directory.FullName, "YtecBrowserCapsule.sln")))
      {
        return directory.FullName;
      }
    }

    throw new DirectoryNotFoundException("YtecBrowserCapsule.sln が見つかりません。");
  }
}
