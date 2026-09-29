namespace Ytec.BrowserCapsule.SecurityTests.BackupContainers;

internal sealed class TemporaryDirectory : IDisposable
{
  public TemporaryDirectory()
  {
    Path = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(),
        "YtecBrowserCapsule.SecurityTests",
        Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(Path);
  }

  public string Path { get; }

  public string GetPath(string fileName)
  {
    return System.IO.Path.Combine(Path, fileName);
  }

  public void Dispose()
  {
    var expectedRoot = System.IO.Path.GetFullPath(
        System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "YtecBrowserCapsule.SecurityTests"));
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
