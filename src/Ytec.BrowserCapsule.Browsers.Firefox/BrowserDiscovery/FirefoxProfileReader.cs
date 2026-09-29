using System.Text.RegularExpressions;
using Ytec.BrowserCapsule.Browsers.Common.BrowserDiscovery;
using Ytec.BrowserCapsule.Domain.BrowserDiscovery;

namespace Ytec.BrowserCapsule.Browsers.Firefox.BrowserDiscovery;

/// <summary>
/// Firefoxのprofiles.iniとinstalls.iniからプロファイルを検出します。
/// </summary>
public sealed partial class FirefoxProfileReader
{
  public static IReadOnlyList<BrowserProfile> Read(
      BrowserInstallation installation,
      CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(installation);
    cancellationToken.ThrowIfCancellationRequested();

    var metadataProfiles = TryReadMetadataProfiles(
        installation,
        cancellationToken);

    return metadataProfiles.Length > 0
        ? metadataProfiles
        : ReadFallbackProfiles(installation, cancellationToken);
  }

  private static BrowserProfile[] TryReadMetadataProfiles(
      BrowserInstallation installation,
      CancellationToken cancellationToken)
  {
    var profilesIniPath = Path.Combine(
        installation.UserDataRoot,
        "profiles.ini");
    if (!File.Exists(profilesIniPath))
    {
      return [];
    }

    try
    {
      var profilesDocument = IniDocument.Parse(
          File.ReadAllText(profilesIniPath));
      var installDefaults = ReadInstallDefaults(installation.UserDataRoot);
      var profiles = new List<BrowserProfile>();

      foreach (var section in profilesDocument.Sections)
      {
        cancellationToken.ThrowIfCancellationRequested();
        if (!ProfileSectionRegex().IsMatch(section.Key)
            || !section.Value.TryGetValue("Path", out var configuredPath))
        {
          continue;
        }

        var isRelative = !section.Value.TryGetValue(
            "IsRelative",
            out var relativeValue)
            || string.Equals(relativeValue, "1", StringComparison.Ordinal);
        var profilePath = ResolveProfilePath(
            installation.UserDataRoot,
            configuredPath,
            isRelative);

        if (profilePath is null
            || !Directory.Exists(profilePath)
            || FileSystemSafety.IsReparsePoint(profilePath)
            || !File.Exists(Path.Combine(profilePath, "prefs.js")))
        {
          continue;
        }

        var displayName = section.Value.TryGetValue("Name", out var name)
            && !string.IsNullOrWhiteSpace(name)
            ? name
            : Path.GetFileName(profilePath);
        var isDefault = section.Value.TryGetValue("Default", out var defaultValue)
            && string.Equals(defaultValue, "1", StringComparison.Ordinal);
        isDefault |= installDefaults.Contains(profilePath);

        profiles.Add(CreateProfile(
            installation,
            section.Key,
            displayName,
            profilePath,
            isDefault,
            ProfileDiscoveryConfidence.Metadata));
      }

      return profiles
          .OrderByDescending(profile => profile.IsDefault)
          .ThenBy(profile => profile.DisplayName, StringComparer.CurrentCulture)
          .ToArray();
    }
    catch (Exception exception) when (
        exception is IOException
        or UnauthorizedAccessException
        or ArgumentException
        or NotSupportedException)
    {
      return [];
    }
  }

  private static HashSet<string> ReadInstallDefaults(string settingsRoot)
  {
    var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var installsIniPath = Path.Combine(settingsRoot, "installs.ini");
    if (!File.Exists(installsIniPath))
    {
      return result;
    }

    try
    {
      var document = IniDocument.Parse(File.ReadAllText(installsIniPath));
      foreach (var section in document.Sections.Values)
      {
        if (!section.TryGetValue("Default", out var configuredPath))
        {
          continue;
        }

        var resolved = ResolveProfilePath(
            settingsRoot,
            configuredPath,
            isRelative: !Path.IsPathFullyQualified(configuredPath));
        if (resolved is not null)
        {
          result.Add(resolved);
        }
      }
    }
    catch (Exception exception) when (
        exception is IOException
        or UnauthorizedAccessException
        or ArgumentException
        or NotSupportedException)
    {
      // profiles.iniだけで検出を継続します。
    }

    return result;
  }

  private static BrowserProfile[] ReadFallbackProfiles(
      BrowserInstallation installation,
      CancellationToken cancellationToken)
  {
    var profilesRoot = Path.Combine(
        installation.UserDataRoot,
        "Profiles");
    if (!Directory.Exists(profilesRoot)
        || FileSystemSafety.IsReparsePoint(profilesRoot))
    {
      return [];
    }

    try
    {
      var profiles = new List<BrowserProfile>();
      foreach (var directory in Directory.EnumerateDirectories(
          profilesRoot,
          "*",
          SearchOption.TopDirectoryOnly))
      {
        cancellationToken.ThrowIfCancellationRequested();
        if (!FileSystemSafety.IsDirectChildDirectory(profilesRoot, directory)
            || !File.Exists(Path.Combine(directory, "prefs.js")))
        {
          continue;
        }

        var directoryName = Path.GetFileName(directory);
        profiles.Add(CreateProfile(
            installation,
            $"fallback:{directoryName}",
            directoryName,
            directory,
            isDefault: false,
            ProfileDiscoveryConfidence.DirectoryFallback));
      }

      return profiles
          .OrderBy(profile => profile.DisplayName, StringComparer.CurrentCulture)
          .ToArray();
    }
    catch (Exception exception) when (
        exception is IOException
        or UnauthorizedAccessException
        or DirectoryNotFoundException)
    {
      return [];
    }
  }

  private static string? ResolveProfilePath(
      string settingsRoot,
      string configuredPath,
      bool isRelative)
  {
    if (string.IsNullOrWhiteSpace(configuredPath))
    {
      return null;
    }

    var normalized = configuredPath.Replace(
        Path.AltDirectorySeparatorChar,
        Path.DirectorySeparatorChar);

    try
    {
      if (isRelative)
      {
        var combined = Path.GetFullPath(Path.Combine(settingsRoot, normalized));
        return FileSystemSafety.IsWithinRoot(settingsRoot, combined)
            ? combined
            : null;
      }

      if (!Path.IsPathFullyQualified(normalized)
          || normalized.StartsWith(
              new string(Path.DirectorySeparatorChar, 2),
              StringComparison.Ordinal))
      {
        return null;
      }

      return Path.GetFullPath(normalized);
    }
    catch (Exception exception) when (
        exception is ArgumentException
        or NotSupportedException
        or PathTooLongException)
    {
      return null;
    }
  }

  private static BrowserProfile CreateProfile(
      BrowserInstallation installation,
      string profileKey,
      string displayName,
      string profilePath,
      bool isDefault,
      ProfileDiscoveryConfidence confidence)
  {
    return new BrowserProfile(
        installation.BrowserId,
        installation.InstallationId,
        $"{installation.InstallationId}:{profileKey}",
        displayName,
        Path.GetFileName(profilePath),
        Path.GetFullPath(profilePath),
        installation.UserDataRoot,
        isDefault,
        LastUsedUtc: null,
        installation.Channel,
        confidence,
        installation.WindowsUser);
  }

  [GeneratedRegex(
      @"^Profile\d+$",
      RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
  private static partial Regex ProfileSectionRegex();
}
