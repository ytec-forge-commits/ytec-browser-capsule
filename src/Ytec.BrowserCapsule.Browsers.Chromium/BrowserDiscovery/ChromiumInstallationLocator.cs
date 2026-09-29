using System.Runtime.Versioning;
using Ytec.BrowserCapsule.Browsers.Common.BrowserDiscovery;
using Ytec.BrowserCapsule.Domain.BrowserDiscovery;

namespace Ytec.BrowserCapsule.Browsers.Chromium.BrowserDiscovery;

/// <summary>
/// ポリシーと標準パスから現在のユーザーのUser Dataルートを列挙します。
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ChromiumInstallationLocator
{
  private readonly ChromiumBrowserConfiguration _configuration;
  private readonly IChromiumPolicyValueReader _policyReader;
  private readonly SafePathVariableExpander _pathExpander;

  public ChromiumInstallationLocator(
      ChromiumBrowserConfiguration configuration,
      IChromiumPolicyValueReader policyReader,
      SafePathVariableExpander pathExpander)
  {
    ArgumentNullException.ThrowIfNull(configuration);
    ArgumentNullException.ThrowIfNull(policyReader);
    ArgumentNullException.ThrowIfNull(pathExpander);

    _configuration = configuration;
    _policyReader = policyReader;
    _pathExpander = pathExpander;
  }

  public IReadOnlyList<BrowserInstallation> Locate()
  {
    var candidates = new[]
    {
      _policyReader.ReadCurrentUser(_configuration.PolicySubKey),
      _policyReader.ReadLocalMachine(_configuration.PolicySubKey),
      _configuration.StandardUserDataRoot,
    };

    var roots = candidates
        .Select(_pathExpander.TryExpandAbsolutePath)
        .Where(path => path is not null && Directory.Exists(path))
        .Select(path => Path.GetFullPath(path!))
        .Where(path => !FileSystemSafety.IsReparsePoint(path))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    var executablePath = _configuration.ExecutableCandidates
        .Select(_pathExpander.TryExpandAbsolutePath)
        .FirstOrDefault(path => path is not null && File.Exists(path));

    return roots
        .Select((root, index) => new BrowserInstallation(
            _configuration.BrowserId,
            $"{_configuration.BrowserId}-stable-{index + 1}",
            _configuration.DisplayName,
            root,
            executablePath,
            _configuration.Channel))
        .ToArray();
  }
}
