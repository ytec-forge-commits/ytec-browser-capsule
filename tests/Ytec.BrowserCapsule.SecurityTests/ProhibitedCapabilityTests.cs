using System.Xml.Linq;

namespace Ytec.BrowserCapsule.SecurityTests;

public sealed class ProhibitedCapabilityTests
{
  private static readonly string[] ForbiddenSourceFragments =
  [
      "CryptUnprotectData",
        "ProtectedData.Unprotect",
        "app_bound_encryption",
        "SELECT password_value",
        "Login Data",
        "key4.db",
        "logins.json",
    ];

  [Fact]
  public void ProductionSourceContainsNoProhibitedCredentialExtractionFragments()
  {
    var sourceRoot = Path.Combine(FindSolutionRoot(), "src");
    var sourceFiles = Directory
        .EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories)
        .Where(path => !IsGeneratedPath(path))
        .Where(path => IsTextSource(path));

    var findings = new List<string>();

    foreach (var sourceFile in sourceFiles)
    {
      var content = File.ReadAllText(sourceFile);

      foreach (var fragment in ForbiddenSourceFragments)
      {
        if (content.Contains(fragment, StringComparison.OrdinalIgnoreCase))
        {
          findings.Add($"{Path.GetFileName(sourceFile)}: {fragment}");
        }
      }
    }

    Assert.Empty(findings);
  }

  [Fact]
  public void ProductionProjectsHaveNoExternalPackageReferences()
  {
    var sourceRoot = Path.Combine(FindSolutionRoot(), "src");
    var packageReferences = Directory
        .EnumerateFiles(sourceRoot, "*.csproj", SearchOption.AllDirectories)
        .SelectMany(projectPath => XDocument
            .Load(projectPath)
            .Descendants("PackageReference")
            .Select(reference =>
                $"{Path.GetFileName(projectPath)}: {reference.Attribute("Include")?.Value}"))
        .ToArray();

    Assert.Empty(packageReferences);
  }

  private static bool IsGeneratedPath(string path)
  {
    var separator = Path.DirectorySeparatorChar;
    return path.Contains($"{separator}bin{separator}", StringComparison.OrdinalIgnoreCase)
        || path.Contains($"{separator}obj{separator}", StringComparison.OrdinalIgnoreCase);
  }

  private static bool IsTextSource(string path)
  {
    return Path.GetExtension(path) is ".cs" or ".csproj" or ".xaml" or ".manifest";
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
