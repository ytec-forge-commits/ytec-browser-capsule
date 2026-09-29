namespace Ytec.BrowserCapsule.IntegrationTests.ProfileBackups;

internal sealed class TemporaryDirectory : IDisposable
{
  public TemporaryDirectory()
  {
    Path = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(),
        "YtecBrowserCapsule.IntegrationTests",
        Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(Path);
  }

  public string Path { get; }

  public string CreateDirectory(params string[] segments)
  {
    var path = segments.Aggregate(Path, System.IO.Path.Combine);
    Directory.CreateDirectory(path);
    return path;
  }

  public string WriteFile(
      string relativePath,
      string content)
  {
    var path = System.IO.Path.Combine(Path, relativePath);
    Directory.CreateDirectory(
        System.IO.Path.GetDirectoryName(path)
        ?? throw new InvalidOperationException("親フォルダーがありません。"));
    File.WriteAllText(path, content);
    return path;
  }

  public string GetPath(string relativePath)
  {
    return System.IO.Path.Combine(Path, relativePath);
  }

  public void Dispose()
  {
    var expectedRoot = System.IO.Path.GetFullPath(
        System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "YtecBrowserCapsule.IntegrationTests"));
    var actualPath = System.IO.Path.GetFullPath(Path);

    if (actualPath.StartsWith(
        expectedRoot + System.IO.Path.DirectorySeparatorChar,
        StringComparison.OrdinalIgnoreCase)
        && Directory.Exists(actualPath))
    {
      Directory.Delete(actualPath, recursive: true);
    }
  }
}
