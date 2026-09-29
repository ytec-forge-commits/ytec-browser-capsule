namespace Ytec.BrowserCapsule.Browsers.Firefox.BrowserDiscovery;

/// <summary>
/// FirefoxのINIファイルに必要な最小限の読み取りモデルです。
/// </summary>
public sealed class IniDocument
{
  private readonly Dictionary<string, Dictionary<string, string>> _sections =
      new(StringComparer.OrdinalIgnoreCase);

  public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>
      Sections => _sections.ToDictionary(
          pair => pair.Key,
          pair => (IReadOnlyDictionary<string, string>)pair.Value,
          StringComparer.OrdinalIgnoreCase);

  public static IniDocument Parse(string content)
  {
    ArgumentNullException.ThrowIfNull(content);

    var document = new IniDocument();
    Dictionary<string, string>? currentSection = null;

    using var reader = new StringReader(content);
    while (reader.ReadLine() is { } rawLine)
    {
      var line = rawLine.Trim();
      if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#'))
      {
        continue;
      }

      if (line.StartsWith('[') && line.EndsWith(']') && line.Length > 2)
      {
        var sectionName = line[1..^1].Trim();
        if (sectionName.Length == 0)
        {
          currentSection = null;
          continue;
        }

        if (!document._sections.TryGetValue(
            sectionName,
            out currentSection))
        {
          currentSection = new Dictionary<string, string>(
              StringComparer.OrdinalIgnoreCase);
          document._sections.Add(sectionName, currentSection);
        }

        continue;
      }

      var separatorIndex = line.IndexOf('=');
      if (currentSection is null || separatorIndex <= 0)
      {
        continue;
      }

      var key = line[..separatorIndex].Trim();
      var value = line[(separatorIndex + 1)..].Trim();
      if (key.Length > 0)
      {
        currentSection[key] = value;
      }
    }

    return document;
  }
}
