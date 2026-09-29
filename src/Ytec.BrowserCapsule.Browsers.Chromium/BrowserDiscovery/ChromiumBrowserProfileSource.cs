using System.Runtime.Versioning;
using Ytec.BrowserCapsule.Browsers.Chromium.ProfileBackups;
using Ytec.BrowserCapsule.Browsers.Common.BrowserDiscovery;
using Ytec.BrowserCapsule.Browsers.Common.ProfileBackups;
using Ytec.BrowserCapsule.Domain.BrowserDiscovery;
using Ytec.BrowserCapsule.Domain.ProfileBackups;

namespace Ytec.BrowserCapsule.Browsers.Chromium.BrowserDiscovery;

/// <summary>
/// ChromeとEdgeで共通する検出処理を提供します。
/// </summary>
[SupportedOSPlatform("windows")]
public abstract class ChromiumBrowserProfileSource :
    IBrowserProfileSource,
  IBrowserBackupPlanner
{
  private readonly ChromiumBrowserConfiguration _configuration;
  private readonly UserScope[] _userScopes;
  private readonly FileSystemBackupPlanBuilder _backupPlanBuilder =
      new(ChromiumBackupRules.Create());

  protected ChromiumBrowserProfileSource(
      ChromiumBrowserConfiguration configuration,
      IChromiumPolicyValueReader? policyReader = null,
      SafePathVariableExpander? pathExpander = null)
  {
    ArgumentNullException.ThrowIfNull(configuration);
    _configuration = configuration;
    var expander = pathExpander
        ?? SafePathVariableExpander.CreateForCurrentUser();
    _userScopes =
    [
      new UserScope(
          Owner: null,
          new ChromiumInstallationLocator(
              configuration,
              policyReader ?? new WindowsChromiumPolicyValueReader(),
              expander),
          expander),
    ];
  }

  protected ChromiumBrowserProfileSource(
      ChromiumBrowserConfiguration configuration,
      IReadOnlyList<WindowsUserProfileIdentity> windowsUsers)
  {
    ArgumentNullException.ThrowIfNull(configuration);
    ArgumentNullException.ThrowIfNull(windowsUsers);
    if (windowsUsers.Count == 0)
    {
      throw new ArgumentException(
          "Windowsユーザープロファイルを1件以上指定してください。",
          nameof(windowsUsers));
    }

    _configuration = configuration;
    var policyReader = new WindowsChromiumPolicyValueReader();
    _userScopes = windowsUsers.Select(owner =>
    {
      var expander =
          SafePathVariableExpander.CreateForWindowsUser(owner);
      var scopePolicyReader = owner.IsCurrentUser
          ? (IChromiumPolicyValueReader)policyReader
          : new LocalMachineOnlyPolicyValueReader(policyReader);
      return new UserScope(
          owner,
          new ChromiumInstallationLocator(
              configuration,
              scopePolicyReader,
              expander),
          expander);
    }).ToArray();
  }

  public string BrowserId => _configuration.BrowserId;

  public string DisplayName => _configuration.DisplayName;

  public Task<IReadOnlyList<BrowserInstallation>> DiscoverInstallationsAsync(
      CancellationToken cancellationToken)
  {
    return Task.Run(
        () =>
        {
          cancellationToken.ThrowIfCancellationRequested();
          var result = new List<BrowserInstallation>();
          var seenRoots = new HashSet<string>(
              StringComparer.OrdinalIgnoreCase);
          foreach (var scope in _userScopes)
          {
            cancellationToken.ThrowIfCancellationRequested();
            var installations = scope.Locator.Locate();
            for (var index = 0; index < installations.Count; index++)
            {
              var installation = installations[index];
              if (!seenRoots.Add(installation.UserDataRoot))
              {
                continue;
              }

              result.Add(scope.Owner is null
                  ? installation
                  : installation with
                  {
                    InstallationId =
                        $"{_configuration.BrowserId}-stable-"
                        + $"{scope.Owner.StableId}-{index + 1}",
                    WindowsUser = scope.Owner,
                  });
            }
          }

          return (IReadOnlyList<BrowserInstallation>)result;
        },
        cancellationToken);
  }

  public Task<IReadOnlyList<BrowserProfile>> DiscoverProfilesAsync(
      BrowserInstallation installation,
      CancellationToken cancellationToken)
  {
    return Task.Run(
        () => ChromiumProfileReader.Read(installation, cancellationToken),
        cancellationToken);
  }

  public IReadOnlyList<ProcessMatchRule> GetProcessMatchRules(
      BrowserInstallation installation)
  {
    var pathExpander = _userScopes.FirstOrDefault(scope =>
        string.Equals(
            scope.Owner?.StableId,
            installation.WindowsUser?.StableId,
            StringComparison.Ordinal))?.PathExpander
        ?? _userScopes[0].PathExpander;
    var expectedPaths = _configuration.ExecutableCandidates
        .Append(installation.ExecutablePath)
        .Where(path => !string.IsNullOrWhiteSpace(path))
        .Select(path => pathExpander.TryExpandAbsolutePath(path))
        .Where(path => path is not null)
        .Select(path => path!)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    return [new ProcessMatchRule(_configuration.ProcessName, expectedPaths)];
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

  private sealed record UserScope(
      WindowsUserProfileIdentity? Owner,
      ChromiumInstallationLocator Locator,
      SafePathVariableExpander PathExpander);

  private sealed class LocalMachineOnlyPolicyValueReader(
      IChromiumPolicyValueReader inner) : IChromiumPolicyValueReader
  {
    public string? ReadCurrentUser(string policySubKey)
    {
      return null;
    }

    public string? ReadLocalMachine(string policySubKey)
    {
      return inner.ReadLocalMachine(policySubKey);
    }
  }
}
