using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;
using Ytec.BrowserCapsule.Domain.BrowserDiscovery;

namespace Ytec.BrowserCapsule.Infrastructure.WindowsUserProfiles;

public sealed record WindowsUserProfileCandidate(
    string? SecurityIdentifier,
    string ProfilePath);

public sealed record WindowsUserProfileCatalogResult(
    IReadOnlyList<WindowsUserProfileIdentity> Profiles,
    int SkippedProfileCount);

/// <summary>
/// 権限昇格せず、現在のプロセスから読み取れるローカルユーザープロファイルを列挙します。
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsUserProfileCatalog
{
  private static readonly HashSet<string> ExcludedDirectoryNames =
      new(StringComparer.OrdinalIgnoreCase)
      {
        "All Users",
        "Default",
        "Default User",
        "Public",
        "systemprofile",
        "LocalService",
        "NetworkService",
      };

  private readonly string _currentProfilePath;
  private readonly string _currentUserName;
  private readonly Func<IReadOnlyList<WindowsUserProfileCandidate>>
      _candidateReader;
  private readonly Func<string, bool> _accessProbe;

  public WindowsUserProfileCatalog()
      : this(
          Environment.GetFolderPath(
              Environment.SpecialFolder.UserProfile),
          Environment.UserName,
          ReadRegistryCandidates,
          CanReadProfileDirectory)
  {
  }

  public WindowsUserProfileCatalog(
      string currentProfilePath,
      string currentUserName,
      Func<IReadOnlyList<WindowsUserProfileCandidate>> candidateReader,
      Func<string, bool> accessProbe)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(currentProfilePath);
    ArgumentException.ThrowIfNullOrWhiteSpace(currentUserName);
    ArgumentNullException.ThrowIfNull(candidateReader);
    ArgumentNullException.ThrowIfNull(accessProbe);

    _currentProfilePath = Path.GetFullPath(currentProfilePath);
    _currentUserName = currentUserName;
    _candidateReader = candidateReader;
    _accessProbe = accessProbe;
  }

  public WindowsUserProfileCatalogResult Discover(bool includeOtherUsers)
  {
    var currentSid = ReadCurrentSecurityIdentifier();
    var candidates = new List<WindowsUserProfileCandidate>
    {
      new(currentSid, _currentProfilePath),
    };
    var skipped = 0;

    if (includeOtherUsers)
    {
      try
      {
        candidates.AddRange(_candidateReader());
      }
      catch (Exception exception) when (
          exception is IOException
          or UnauthorizedAccessException
          or System.Security.SecurityException)
      {
        skipped++;
      }
    }

    var profiles = new List<WindowsUserProfileIdentity>();
    var seenPaths = new HashSet<string>(
        StringComparer.OrdinalIgnoreCase);
    foreach (var candidate in candidates)
    {
      if (!TryNormalizeProfilePath(
          candidate.ProfilePath,
          out var profilePath)
          || !seenPaths.Add(profilePath))
      {
        continue;
      }

      var isCurrent = string.Equals(
          profilePath,
          _currentProfilePath,
          StringComparison.OrdinalIgnoreCase);
      if (!isCurrent && !includeOtherUsers)
      {
        continue;
      }

      if (!isCurrent && !_accessProbe(profilePath))
      {
        skipped++;
        continue;
      }

      var displayName = isCurrent
          ? _currentUserName
          : Path.GetFileName(
              profilePath.TrimEnd(Path.DirectorySeparatorChar));
      if (string.IsNullOrWhiteSpace(displayName)
          || ExcludedDirectoryNames.Contains(displayName))
      {
        continue;
      }

      profiles.Add(new WindowsUserProfileIdentity(
          CreateStableId(
              candidate.SecurityIdentifier,
              profilePath),
          displayName,
          profilePath,
          isCurrent));
    }

    return new WindowsUserProfileCatalogResult(
        profiles
            .OrderByDescending(profile => profile.IsCurrentUser)
            .ThenBy(
                profile => profile.DisplayName,
                StringComparer.CurrentCulture)
            .ToArray(),
        skipped);
  }

  public static string CreateStableId(
      string? securityIdentifier,
      string profilePath)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(profilePath);
    var source = string.IsNullOrWhiteSpace(securityIdentifier)
        ? Path.GetFullPath(profilePath)
        : securityIdentifier;
    var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(
        $"ytec-browser-capsule/windows-user/v1/{source}"));
    return Convert.ToHexString(bytes.AsSpan(0, 12)).ToLowerInvariant();
  }

  private static IReadOnlyList<WindowsUserProfileCandidate>
      ReadRegistryCandidates()
  {
    const string profileListPath =
        @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList";
    var result = new List<WindowsUserProfileCandidate>();

    foreach (var view in new[]
             {
               RegistryView.Registry64,
               RegistryView.Registry32,
             })
    {
      try
      {
        using var baseKey = RegistryKey.OpenBaseKey(
            RegistryHive.LocalMachine,
            view);
        using var profileList = baseKey.OpenSubKey(
            profileListPath,
            writable: false);
        if (profileList is null)
        {
          continue;
        }

        foreach (var sid in profileList.GetSubKeyNames())
        {
          using var profileKey = profileList.OpenSubKey(
              sid,
              writable: false);
          if (profileKey?.GetValue(
              "ProfileImagePath",
              null,
              RegistryValueOptions.DoNotExpandEnvironmentNames)
              is not string configuredPath
              || string.IsNullOrWhiteSpace(configuredPath))
          {
            continue;
          }

          result.Add(new WindowsUserProfileCandidate(
              sid,
              Environment.ExpandEnvironmentVariables(
                  configuredPath)));
        }
      }
      catch (Exception exception) when (
          exception is IOException
          or UnauthorizedAccessException
          or System.Security.SecurityException)
      {
        // 読み取れるレジストリービューだけで検出を継続します。
      }
    }

    return result;
  }

  private static string? ReadCurrentSecurityIdentifier()
  {
    try
    {
      using var identity = WindowsIdentity.GetCurrent();
      return identity.User?.Value;
    }
    catch (Exception exception) when (
        exception is UnauthorizedAccessException
        or System.Security.SecurityException)
    {
      return null;
    }
  }

  private static bool CanReadProfileDirectory(string profilePath)
  {
    try
    {
      _ = Directory
          .EnumerateFileSystemEntries(
              profilePath,
              "*",
              SearchOption.TopDirectoryOnly)
          .Take(1)
          .ToArray();
      return true;
    }
    catch (Exception exception) when (
        exception is IOException
        or UnauthorizedAccessException
        or DirectoryNotFoundException
        or System.Security.SecurityException)
    {
      return false;
    }
  }

  private static bool TryNormalizeProfilePath(
      string path,
      out string normalized)
  {
    normalized = string.Empty;
    if (string.IsNullOrWhiteSpace(path)
        || path.StartsWith(@"\\", StringComparison.Ordinal)
        || path.StartsWith(@"\\?\", StringComparison.Ordinal)
        || path.StartsWith(@"\\.\", StringComparison.Ordinal))
    {
      return false;
    }

    try
    {
      normalized = Path.GetFullPath(path);
      if (!Path.IsPathFullyQualified(normalized)
          || !Directory.Exists(normalized)
          || (File.GetAttributes(normalized)
              & FileAttributes.ReparsePoint) != 0)
      {
        normalized = string.Empty;
        return false;
      }

      return true;
    }
    catch (Exception exception) when (
        exception is IOException
        or UnauthorizedAccessException
        or ArgumentException
        or NotSupportedException
        or PathTooLongException)
    {
      normalized = string.Empty;
      return false;
    }
  }
}
