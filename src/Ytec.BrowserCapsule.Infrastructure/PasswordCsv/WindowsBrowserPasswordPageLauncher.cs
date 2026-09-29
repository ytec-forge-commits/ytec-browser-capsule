using System.Diagnostics;
using System.Runtime.Versioning;
using Ytec.BrowserCapsule.Domain.BrowserDiscovery;
using Ytec.BrowserCapsule.Domain.PasswordCsv;

namespace Ytec.BrowserCapsule.Infrastructure.PasswordCsv;

public sealed record BrowserPasswordPageLaunchRequest(
    string ExecutablePath,
    IReadOnlyList<string> Arguments);

/// <summary>
/// ブラウザー内部を解析せず、標準のパスワード管理画面だけを起動します。
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsBrowserPasswordPageLauncher :
    IBrowserPasswordPageLauncher
{
  private readonly Dictionary<string, IBrowserProfileSource> _sources;

  public WindowsBrowserPasswordPageLauncher(
      IReadOnlyList<IBrowserProfileSource> sources)
  {
    ArgumentNullException.ThrowIfNull(sources);
    _sources = sources.ToDictionary(
        source => source.BrowserId,
        StringComparer.Ordinal);
  }

  public Task OpenExportPageAsync(
      BrowserProfile profile,
      CancellationToken cancellationToken)
  {
    return OpenAsync(profile, cancellationToken);
  }

  public Task OpenImportPageAsync(
      BrowserProfile profile,
      CancellationToken cancellationToken)
  {
    return OpenAsync(profile, cancellationToken);
  }

  public static BrowserPasswordPageLaunchRequest BuildLaunchRequest(
      BrowserProfile profile,
      BrowserInstallation installation)
  {
    ArgumentNullException.ThrowIfNull(profile);
    ArgumentNullException.ThrowIfNull(installation);
    if (profile.WindowsUser?.IsCurrentUser is false)
    {
      throw new InvalidOperationException(
          "保存パスワードの公式画面は、対象のWindowsユーザーでサインインして開いてください。");
    }

    if (!string.Equals(
        profile.BrowserId,
        installation.BrowserId,
        StringComparison.Ordinal)
        || string.IsNullOrWhiteSpace(installation.ExecutablePath))
    {
      throw new InvalidOperationException(
          "対象プロファイルのブラウザー実行ファイルが見つかりません。");
    }

    IReadOnlyList<string> arguments = profile.BrowserId switch
    {
      "chrome" =>
      [
        $"--profile-directory={profile.ProfileDirectoryName}",
        "chrome://password-manager/settings",
      ],
      "edge" =>
      [
        $"--profile-directory={profile.ProfileDirectoryName}",
        "edge://wallet/passwords",
      ],
      "firefox" =>
      [
        "-profile",
        profile.AbsoluteProfilePath,
        "about:logins",
      ],
      _ => throw new NotSupportedException(
          "このブラウザーの公式パスワード画面には対応していません。"),
    };

    return new BrowserPasswordPageLaunchRequest(
        Path.GetFullPath(installation.ExecutablePath),
        arguments);
  }

  private async Task OpenAsync(
      BrowserProfile profile,
      CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(profile);
    cancellationToken.ThrowIfCancellationRequested();
    if (!_sources.TryGetValue(profile.BrowserId, out var source))
    {
      throw new NotSupportedException(
          "このブラウザーの起動補助には対応していません。");
    }

    var installations = await source
        .DiscoverInstallationsAsync(cancellationToken)
        .ConfigureAwait(false);
    var installation = installations.FirstOrDefault(candidate =>
        string.Equals(
            candidate.InstallationId,
            profile.InstallationId,
            StringComparison.Ordinal))
        ?? throw new InvalidOperationException(
            "ブラウザーの場所が変わっています。再検出してください。");
    var request = BuildLaunchRequest(profile, installation);
    if (!File.Exists(request.ExecutablePath))
    {
      throw new FileNotFoundException(
          "ブラウザーの実行ファイルが見つかりません。");
    }

    var startInfo = new ProcessStartInfo
    {
      FileName = request.ExecutablePath,
      UseShellExecute = false,
      WorkingDirectory =
          Path.GetDirectoryName(request.ExecutablePath) ?? string.Empty,
    };
    foreach (var argument in request.Arguments)
    {
      startInfo.ArgumentList.Add(argument);
    }

    _ = Process.Start(startInfo)
        ?? throw new InvalidOperationException(
            "ブラウザーを起動できませんでした。");
  }
}
