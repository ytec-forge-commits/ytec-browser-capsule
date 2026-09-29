using System.Text.Json;
using Ytec.BrowserCapsule.Browsers.Common.BrowserDiscovery;
using Ytec.BrowserCapsule.Domain.BrowserDiscovery;

namespace Ytec.BrowserCapsule.Browsers.Chromium.BrowserDiscovery;

/// <summary>
/// ChromiumのLocal Stateまたはプロファイル特徴ファイルから一覧を読み取ります。
/// </summary>
public sealed class ChromiumProfileReader
{
  private static readonly HashSet<string> ExcludedDirectoryNames =
      new(StringComparer.OrdinalIgnoreCase)
      {
        "Guest Profile",
        "System Profile",
      };

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
    var localStatePath = Path.Combine(
        installation.UserDataRoot,
        "Local State");
    if (!File.Exists(localStatePath))
    {
      return [];
    }

    try
    {
      using var stream = new FileStream(
          localStatePath,
          FileMode.Open,
          FileAccess.Read,
          FileShare.ReadWrite | FileShare.Delete);
      using var document = JsonDocument.Parse(
          stream,
          new JsonDocumentOptions
          {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip,
          });

      if (!document.RootElement.TryGetProperty("profile", out var profileNode)
          || !profileNode.TryGetProperty("info_cache", out var infoCache)
          || infoCache.ValueKind is not JsonValueKind.Object)
      {
        return [];
      }

      var results = new List<BrowserProfile>();
      foreach (var entry in infoCache.EnumerateObject())
      {
        cancellationToken.ThrowIfCancellationRequested();
        var directoryName = entry.Name;
        var profilePath = Path.Combine(
            installation.UserDataRoot,
            directoryName);

        if (!CanUseProfileDirectory(
            installation.UserDataRoot,
            directoryName,
            profilePath))
        {
          continue;
        }

        var displayName = ReadDisplayName(entry.Value, directoryName);
        results.Add(CreateProfile(
            installation,
            directoryName,
            displayName,
            profilePath,
            ProfileDiscoveryConfidence.Metadata));
      }

      return results
          .OrderByDescending(profile => profile.IsDefault)
          .ThenBy(profile => profile.DisplayName, StringComparer.CurrentCulture)
          .ToArray();
    }
    catch (Exception exception) when (
        exception is JsonException
        or IOException
        or UnauthorizedAccessException)
    {
      return [];
    }
  }

  private static BrowserProfile[] ReadFallbackProfiles(
      BrowserInstallation installation,
      CancellationToken cancellationToken)
  {
    try
    {
      var profiles = new List<BrowserProfile>();
      foreach (var directory in Directory.EnumerateDirectories(
          installation.UserDataRoot,
          "*",
          SearchOption.TopDirectoryOnly))
      {
        cancellationToken.ThrowIfCancellationRequested();
        var directoryName = Path.GetFileName(directory);

        if (!CanUseProfileDirectory(
            installation.UserDataRoot,
            directoryName,
            directory)
            || !File.Exists(Path.Combine(directory, "Preferences")))
        {
          continue;
        }

        profiles.Add(CreateProfile(
            installation,
            directoryName,
            directoryName,
            directory,
            ProfileDiscoveryConfidence.DirectoryFallback));
      }

      return profiles
          .OrderByDescending(profile => profile.IsDefault)
          .ThenBy(profile => profile.DisplayName, StringComparer.CurrentCulture)
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

  private static bool CanUseProfileDirectory(
      string root,
      string directoryName,
      string profilePath)
  {
    return !string.IsNullOrWhiteSpace(directoryName)
        && !ExcludedDirectoryNames.Contains(directoryName)
        && directoryName.IndexOfAny(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) < 0
        && FileSystemSafety.IsDirectChildDirectory(root, profilePath);
  }

  private static string ReadDisplayName(
      JsonElement profileEntry,
      string fallback)
  {
    if (profileEntry.ValueKind is JsonValueKind.Object
        && profileEntry.TryGetProperty("name", out var name)
        && name.ValueKind is JsonValueKind.String
        && !string.IsNullOrWhiteSpace(name.GetString()))
    {
      return name.GetString()!;
    }

    return fallback;
  }

  private static BrowserProfile CreateProfile(
      BrowserInstallation installation,
      string directoryName,
      string displayName,
      string profilePath,
      ProfileDiscoveryConfidence confidence)
  {
    return new BrowserProfile(
        installation.BrowserId,
        installation.InstallationId,
        $"{installation.InstallationId}:{directoryName}",
        displayName,
        directoryName,
        Path.GetFullPath(profilePath),
        installation.UserDataRoot,
        string.Equals(
            directoryName,
            "Default",
            StringComparison.OrdinalIgnoreCase),
        LastUsedUtc: null,
        installation.Channel,
        confidence,
        installation.WindowsUser);
  }
}
