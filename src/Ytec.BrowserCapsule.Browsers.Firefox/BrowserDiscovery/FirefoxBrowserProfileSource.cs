using System.Runtime.Versioning;
using Ytec.BrowserCapsule.Browsers.Common.BrowserDiscovery;
using Ytec.BrowserCapsule.Browsers.Common.ProfileBackups;
using Ytec.BrowserCapsule.Browsers.Firefox.ProfileBackups;
using Ytec.BrowserCapsule.Domain.BrowserDiscovery;
using Ytec.BrowserCapsule.Domain.ProfileBackups;

namespace Ytec.BrowserCapsule.Browsers.Firefox.BrowserDiscovery;

/// <summary>
/// Mozilla Firefox Stable / ESRのプロファイルを検出します。
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class FirefoxBrowserProfileSource :
    IBrowserProfileSource,
    IBrowserBackupPlanner
{
  private readonly IReadOnlyList<UserScope> _userScopes;
  private readonly IReadOnlyList<string> _executableCandidates;
  private readonly FileSystemBackupPlanBuilder _backupPlanBuilder =
      new(FirefoxBackupRules.Create());

  public FirefoxBrowserProfileSource()
      : this(
          Path.Combine(
              Environment.GetFolderPath(
                  Environment.SpecialFolder.ApplicationData),
              "Mozilla",
              "Firefox"),
          CreateExecutableCandidates())
  {
  }

  public FirefoxBrowserProfileSource(
      string settingsRoot,
      IReadOnlyList<string> executableCandidates)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(settingsRoot);
    ArgumentNullException.ThrowIfNull(executableCandidates);

    _userScopes =
    [
      new UserScope(
          Path.GetFullPath(settingsRoot),
          Owner: null,
          InstallationId: "firefox-stable-or-esr-1"),
    ];
    _executableCandidates = executableCandidates;
  }

  public FirefoxBrowserProfileSource(
      IReadOnlyList<WindowsUserProfileIdentity> windowsUsers)
  {
    ArgumentNullException.ThrowIfNull(windowsUsers);
    if (windowsUsers.Count == 0)
    {
      throw new ArgumentException(
          "Windowsユーザープロファイルを1件以上指定してください。",
          nameof(windowsUsers));
    }

    _userScopes = windowsUsers.Select(owner =>
    {
      var roamingRoot = owner.IsCurrentUser
          ? Environment.GetFolderPath(
              Environment.SpecialFolder.ApplicationData)
          : Path.Combine(
              owner.ProfilePath,
              "AppData",
              "Roaming");
      return new UserScope(
          Path.Combine(roamingRoot, "Mozilla", "Firefox"),
          owner,
          $"firefox-stable-or-esr-{owner.StableId}");
    }).ToArray();
    _executableCandidates = CreateExecutableCandidates()
        .Concat(windowsUsers.Select(owner =>
        {
          var localRoot = owner.IsCurrentUser
              ? Environment.GetFolderPath(
                  Environment.SpecialFolder.LocalApplicationData)
              : Path.Combine(
                  owner.ProfilePath,
                  "AppData",
                  "Local");
          return Path.Combine(
              localRoot,
              "Mozilla Firefox",
              "firefox.exe");
        }))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();
  }

  public string BrowserId => "firefox";

  public string DisplayName => "Mozilla Firefox";

  public Task<IReadOnlyList<BrowserInstallation>> DiscoverInstallationsAsync(
      CancellationToken cancellationToken)
  {
    return Task.Run<IReadOnlyList<BrowserInstallation>>(
        () =>
        {
          cancellationToken.ThrowIfCancellationRequested();
          var executablePath = _executableCandidates.FirstOrDefault(
              File.Exists);
          var installations = new List<BrowserInstallation>();
          foreach (var scope in _userScopes)
          {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Directory.Exists(scope.SettingsRoot)
                || FileSystemSafety.IsReparsePoint(
                    scope.SettingsRoot))
            {
              continue;
            }

            installations.Add(new BrowserInstallation(
                BrowserId,
                scope.InstallationId,
                DisplayName,
                scope.SettingsRoot,
                executablePath,
                BrowserChannel.StableOrEsr,
                scope.Owner));
          }

          return installations;
        },
        cancellationToken);
  }

  public Task<IReadOnlyList<BrowserProfile>> DiscoverProfilesAsync(
      BrowserInstallation installation,
      CancellationToken cancellationToken)
  {
    return Task.Run<IReadOnlyList<BrowserProfile>>(
        () => FirefoxProfileReader.Read(installation, cancellationToken),
        cancellationToken);
  }

  public IReadOnlyList<ProcessMatchRule> GetProcessMatchRules(
      BrowserInstallation installation)
  {
    var paths = _executableCandidates
        .Append(installation.ExecutablePath)
        .Where(path => !string.IsNullOrWhiteSpace(path))
        .Select(path => path!)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    return [new ProcessMatchRule("firefox", paths)];
  }

  public Task<BrowserBackupPlan> BuildBackupPlanAsync(
      BrowserProfile profile,
      string backupProfileId,
      BackupComponent selection,
      CancellationToken cancellationToken)
  {
    if (!string.Equals(
        profile.BrowserId,
        BrowserId,
        StringComparison.Ordinal))
    {
      throw new ArgumentException(
          "別ブラウザーのプロファイルは計画できません。",
          nameof(profile));
    }

    return _backupPlanBuilder.BuildAsync(
        profile,
        backupProfileId,
        selection,
        cancellationToken);
  }

  private static IReadOnlyList<string> CreateExecutableCandidates()
  {
    return
    [
      Path.Combine(
          Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
          "Mozilla Firefox",
          "firefox.exe"),
      Path.Combine(
          Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
          "Mozilla Firefox",
          "firefox.exe"),
    ];
  }

  private sealed record UserScope(
      string SettingsRoot,
      WindowsUserProfileIdentity? Owner,
      string InstallationId);
}
