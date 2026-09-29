using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;
using Ytec.BrowserCapsule.Domain.BrowserDiscovery;

namespace Ytec.BrowserCapsule.Infrastructure.BrowserDiscovery;

/// <summary>
/// プロセス名と実行ファイルの完全パスを照合して実行状態を確認します。
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsBrowserProcessInspector :
    IBrowserProcessInspector,
    IBrowserProcessController
{
  public Task<bool> IsRunningAsync(
      IReadOnlyList<ProcessMatchRule> rules,
      CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(rules);

    return Task.Run(
        () => IsRunning(rules, cancellationToken),
        cancellationToken);
  }

  public static bool ExecutablePathMatches(
      string? actualPath,
      IReadOnlyList<string> expectedPaths)
  {
    if (string.IsNullOrWhiteSpace(actualPath))
    {
      return false;
    }

    string normalizedActual;
    try
    {
      normalizedActual = Path.GetFullPath(actualPath);
    }
    catch (Exception exception) when (
        exception is ArgumentException
        or NotSupportedException
        or PathTooLongException)
    {
      return false;
    }

    foreach (var expectedPath in expectedPaths)
    {
      try
      {
        if (Path.IsPathFullyQualified(expectedPath)
            && string.Equals(
                normalizedActual,
                Path.GetFullPath(expectedPath),
                StringComparison.OrdinalIgnoreCase))
        {
          return true;
        }
      }
      catch (Exception exception) when (
          exception is ArgumentException
          or NotSupportedException
          or PathTooLongException)
      {
        // 不正な候補だけを無視し、残りの候補を確認します。
      }
    }

    return false;
  }

  public Task<BrowserProcessCloseResult> RequestCloseAsync(
      IReadOnlyList<ProcessMatchRule> rules,
      TimeSpan waitTimeout,
      CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(rules);
    return Task.Run(
        async () => await CloseProcessesAsync(
            rules,
            waitTimeout,
            force: false,
            cancellationToken).ConfigureAwait(false),
        cancellationToken);
  }

  public Task<BrowserProcessCloseResult> TerminateRemainingAsync(
      IReadOnlyList<ProcessMatchRule> rules,
      TimeSpan waitTimeout,
      CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(rules);
    return Task.Run(
        async () => await CloseProcessesAsync(
            rules,
            waitTimeout,
            force: true,
            cancellationToken).ConfigureAwait(false),
        cancellationToken);
  }

  private static async Task<BrowserProcessCloseResult>
      CloseProcessesAsync(
          IReadOnlyList<ProcessMatchRule> rules,
          TimeSpan waitTimeout,
          bool force,
          CancellationToken cancellationToken)
  {
    ArgumentOutOfRangeException.ThrowIfLessThan(
        waitTimeout,
        TimeSpan.Zero);

    var processes = FindControllableProcesses(
        rules,
        cancellationToken);
    var requested = 0;
    foreach (var process in processes)
    {
      try
      {
        cancellationToken.ThrowIfCancellationRequested();
        if (force)
        {
          process.Kill(entireProcessTree: true);
          requested++;
        }
        else if (process.CloseMainWindow())
        {
          requested++;
        }
      }
      catch (Exception exception) when (
          exception is Win32Exception
          or InvalidOperationException
          or NotSupportedException)
      {
        // 終了済み、または通常権限で操作できないプロセスは残存確認へ回します。
      }
    }

    var deadline = DateTime.UtcNow + waitTimeout;
    while (DateTime.UtcNow < deadline)
    {
      cancellationToken.ThrowIfCancellationRequested();
      if (processes.All(HasExited))
      {
        break;
      }

      await Task.Delay(
          TimeSpan.FromMilliseconds(150),
          cancellationToken).ConfigureAwait(false);
    }

    var exited = processes.Count(HasExited);
    foreach (var process in processes)
    {
      process.Dispose();
    }

    var remaining = FindControllableProcesses(
        rules,
        cancellationToken);
    try
    {
      return new BrowserProcessCloseResult(
          processes.Count,
          requested,
          exited,
          remaining.Count);
    }
    finally
    {
      foreach (var process in remaining)
      {
        process.Dispose();
      }
    }
  }

  private static List<Process> FindControllableProcesses(
      IReadOnlyList<ProcessMatchRule> rules,
      CancellationToken cancellationToken)
  {
    var currentSessionId = Process.GetCurrentProcess().SessionId;
    var result = new Dictionary<int, Process>();
    foreach (var rule in rules)
    {
      cancellationToken.ThrowIfCancellationRequested();
      if (rule.ExpectedExecutablePaths.Count == 0)
      {
        continue;
      }

      foreach (var process in Process.GetProcessesByName(rule.ProcessName))
      {
        try
        {
          cancellationToken.ThrowIfCancellationRequested();
          if (process.SessionId == currentSessionId
              && ExecutablePathMatches(
                  process.MainModule?.FileName,
                  rule.ExpectedExecutablePaths)
              && result.TryAdd(process.Id, process))
          {
            continue;
          }
        }
        catch (Exception exception) when (
            exception is Win32Exception
            or InvalidOperationException
            or NotSupportedException)
        {
          // 完全パスと現在セッションを確認できないプロセスは操作しません。
        }

        process.Dispose();
      }
    }

    return result.Values.ToList();
  }

  private static bool HasExited(Process process)
  {
    try
    {
      return process.HasExited;
    }
    catch (InvalidOperationException)
    {
      return true;
    }
  }

  private static bool IsRunning(
      IReadOnlyList<ProcessMatchRule> rules,
      CancellationToken cancellationToken)
  {
    foreach (var rule in rules)
    {
      cancellationToken.ThrowIfCancellationRequested();
      if (rule.ExpectedExecutablePaths.Count == 0)
      {
        continue;
      }

      foreach (var process in Process.GetProcessesByName(rule.ProcessName))
      {
        using (process)
        {
          cancellationToken.ThrowIfCancellationRequested();
          try
          {
            if (ExecutablePathMatches(
                process.MainModule?.FileName,
                rule.ExpectedExecutablePaths))
            {
              return true;
            }
          }
          catch (Exception exception) when (
              exception is Win32Exception
              or InvalidOperationException
              or NotSupportedException)
          {
            // 別Windowsユーザーの同名プロセスはパスを読めない場合があります。
            // 不整合なバックアップを避けるため、安全側で実行中として扱います。
            return true;
          }
        }
      }
    }

    return false;
  }
}
