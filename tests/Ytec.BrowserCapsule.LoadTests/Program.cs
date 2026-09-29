using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Ytec.BrowserCapsule.Application.ProfileBackups;
using Ytec.BrowserCapsule.Domain.BackupContainers;
using Ytec.BrowserCapsule.Domain.BrowserDiscovery;
using Ytec.BrowserCapsule.Domain.ProfileBackups;
using Ytec.BrowserCapsule.Infrastructure.BackupContainers;

namespace Ytec.BrowserCapsule.LoadTests;

internal static class Program
{
  private const int ManyFilesCount = 100_000;
  private const long FourGiB = 4L * 1024 * 1024 * 1024;
  private const long OneGiB = 1024L * 1024 * 1024;
  private const int MaximumProgressReportsPerSecond = 20;
  private static readonly JsonSerializerOptions ReportJsonOptions = new()
  {
    WriteIndented = true,
  };

  public static async Task<int> Main(string[] args)
  {
    var options = LoadTestOptions.Parse(args);
    Directory.CreateDirectory(options.WorkRoot);
    Directory.CreateDirectory(
        Path.GetDirectoryName(options.ResultPath)
        ?? throw new InvalidOperationException(
            "結果ファイルの親フォルダーがありません。"));

    var startedUtc = DateTimeOffset.UtcNow;
    var results = new List<LoadScenarioResult>();
    foreach (var scenario in options.GetScenarios())
    {
      Console.WriteLine(
          string.Create(
              CultureInfo.InvariantCulture,
              $"START {scenario}"));
      results.Add(await RunScenarioAsync(
          options,
          scenario,
          CancellationToken.None));
    }

    var report = new LoadTestReport(
        startedUtc,
        DateTimeOffset.UtcNow,
        Environment.OSVersion.VersionString,
        Environment.Version.ToString(),
        results);
    var json = JsonSerializer.Serialize(
        report,
        ReportJsonOptions);
    await File.WriteAllTextAsync(options.ResultPath, json);
    Console.WriteLine(
        string.Create(
            CultureInfo.InvariantCulture,
            $"RESULT {options.ResultPath}"));
    return results.All(result => result.Passed) ? 0 : 1;
  }

  private static async Task<LoadScenarioResult> RunScenarioAsync(
      LoadTestOptions options,
      string scenario,
      CancellationToken cancellationToken)
  {
    var runRoot = Path.Combine(
        options.WorkRoot,
        string.Create(
            CultureInfo.InvariantCulture,
            $"run-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}"));
    var sourceRoot = Path.Combine(runRoot, "source");
    Directory.CreateDirectory(sourceRoot);

    try
    {
      var preparationWatch = Stopwatch.StartNew();
      var files = scenario switch
      {
        LoadScenarios.LargeFile =>
            await CreateLargeFilesAsync(sourceRoot, cancellationToken),
        LoadScenarios.ManyFiles =>
            CreateManyFiles(sourceRoot),
        _ => throw new ArgumentOutOfRangeException(
            nameof(scenario),
            scenario,
            "未知の負荷試験です。"),
      };
      preparationWatch.Stop();

      var destinationPath = Path.Combine(runRoot, "load-test.bvb");
      var recoveryKeyPath = Path.Combine(
          runRoot,
          "load-test.ybckey");
      var source = new SyntheticBrowserSource(sourceRoot, files);
      var service = new ProfileBackupService(
          [source],
          new NeverRunningProcessInspector(),
          new SyntheticFingerprintProvider(),
          new BvbBackupContainerService(
              BvbContainerSecurityOptions.CreateMinimumSecureDefault()));
      var progress = new TimestampedProgress();
      var backupWatch = Stopwatch.StartNew();
      var result = await service.CreateAsync(
          new ProfileBackupRequest(
              destinationPath,
              [
                new ProfileBackupSelection(
                    source.Profile,
                    BackupComponent.FullProfile),
              ],
              "1.1.0-load-test"),
          recoveryKeyPath,
          progress,
          cancellationToken);

      backupWatch.Stop();
      var logicalBytes = files.Sum(file => file.Length);
      var manifestFileCount = result.Container.Verification.Manifest
          .Profiles
          .SelectMany(profile => profile.Files)
          .LongCount();
      var maxReportsPerSecond = progress
          .GetMaximumReportsInOneSecond();
      var passed =
          result.Container.Verification.EntryCount == files.Count
          && manifestFileCount == files.Count
          && !File.Exists(destinationPath + ".partial")
          && !File.Exists(recoveryKeyPath + ".partial")
          && File.Exists(destinationPath)
          && File.Exists(recoveryKeyPath)
          && result.Container.Verification.Header.CredentialKind
              == BackupContainerCredentialKind.RecoveryKeyFile
          && progress.Last?.Stage == ProfileBackupStage.Completed
          && maxReportsPerSecond <= MaximumProgressReportsPerSecond;
      var throughput = backupWatch.Elapsed.TotalSeconds > 0
          ? logicalBytes
              / 1024d
              / 1024d
              / backupWatch.Elapsed.TotalSeconds
          : 0;
      var scenarioResult = new LoadScenarioResult(
          scenario,
          passed,
          files.Count,
          logicalBytes,
          result.Container.ContainerBytes,
          result.Container.Verification.PlaintextBytes,
          preparationWatch.Elapsed.TotalSeconds,
          backupWatch.Elapsed.TotalSeconds,
          throughput,
          progress.Count,
          maxReportsPerSecond,
          Process.GetCurrentProcess().PeakWorkingSet64);
      Console.WriteLine(
          string.Create(
              CultureInfo.InvariantCulture,
              $"PASS={passed} entries={files.Count} "
              + $"logicalBytes={logicalBytes} "
              + $"seconds={backupWatch.Elapsed.TotalSeconds:F2} "
              + $"MiBPerSecond={throughput:F2} "
              + $"progressReports={progress.Count} "
              + $"maxReportsPerSecond={maxReportsPerSecond}"));
      return scenarioResult;
    }
    finally
    {
      if (!options.KeepArtifacts)
      {
        DeleteRunDirectory(runRoot, options.WorkRoot);
      }
    }
  }

  private static async Task<IReadOnlyList<BackupPlanFile>>
      CreateLargeFilesAsync(
          string sourceRoot,
          CancellationToken cancellationToken)
  {
    var definitions = new[]
    {
      (Name: "over-4-gib.bin", EntryPath: "p/a", Length: FourGiB + 1),
      (Name: "padding.bin", EntryPath: "p/b", Length: OneGiB),
    };
    var files = new List<BackupPlanFile>(definitions.Length);
    foreach (var definition in definitions)
    {
      cancellationToken.ThrowIfCancellationRequested();
      var path = Path.Combine(sourceRoot, definition.Name);
      await using (var stream = new FileStream(
          path,
          FileMode.CreateNew,
          FileAccess.Write,
          FileShare.None,
          bufferSize: 4096,
          FileOptions.Asynchronous | FileOptions.SequentialScan))
      {
        stream.SetLength(definition.Length);
        await stream.FlushAsync(cancellationToken);
      }

      files.Add(CreatePlanFile(
          path,
          definition.EntryPath,
          definition.Length));
    }

    return files;
  }

  private static List<BackupPlanFile> CreateManyFiles(
      string sourceRoot)
  {
    var files = new List<BackupPlanFile>(ManyFilesCount);
    for (var index = 0; index < ManyFilesCount; index++)
    {
      var name = ToBase36(index)
          .PadLeft(4, '0');
      var path = Path.Combine(sourceRoot, name);
      using (File.Create(path))
      {
      }

      files.Add(CreatePlanFile(
          path,
          string.Create(
              CultureInfo.InvariantCulture,
              $"p/{name}"),
          length: 0));
    }

    return files;
  }

  private static string ToBase36(int value)
  {
    const string digits = "0123456789abcdefghijklmnopqrstuvwxyz";
    Span<char> buffer = stackalloc char[8];
    var position = buffer.Length;
    do
    {
      buffer[--position] = digits[value % digits.Length];
      value /= digits.Length;
    }
    while (value > 0);

    return new string(buffer[position..]);
  }

  private static BackupPlanFile CreatePlanFile(
      string sourcePath,
      string entryPath,
      long length)
  {
    var lastWriteUtc = new DateTimeOffset(
        File.GetLastWriteTimeUtc(sourcePath),
        TimeSpan.Zero);
    return new BackupPlanFile(
        sourcePath,
        entryPath,
        length,
        lastWriteUtc,
        BackupComponent.FullProfile,
        IsRequired: true);
  }

  private static void DeleteRunDirectory(
      string runRoot,
      string workRoot)
  {
    var normalizedRoot = Path.GetFullPath(workRoot)
        .TrimEnd(Path.DirectorySeparatorChar)
        + Path.DirectorySeparatorChar;
    var normalizedRun = Path.GetFullPath(runRoot);
    if (!normalizedRun.StartsWith(
        normalizedRoot,
        StringComparison.OrdinalIgnoreCase)
        || !Path.GetFileName(normalizedRun).StartsWith(
            "run-",
            StringComparison.Ordinal))
    {
      throw new InvalidOperationException(
          "負荷試験の一時フォルダー範囲を確認できません。");
    }

    if (Directory.Exists(normalizedRun))
    {
      Directory.Delete(normalizedRun, recursive: true);
    }
  }

  private sealed class SyntheticBrowserSource
      : IBrowserProfileSource, IBrowserBackupPlanner
  {
    private readonly BrowserInstallation _installation;
    private readonly IReadOnlyList<BackupPlanFile> _files;

    public SyntheticBrowserSource(
        string sourceRoot,
        IReadOnlyList<BackupPlanFile> files)
    {
      _files = files;
      _installation = new BrowserInstallation(
          BrowserId,
          "load-installation",
          DisplayName,
          sourceRoot,
          ExecutablePath: null,
          BrowserChannel.Stable);
      Profile = new BrowserProfile(
          BrowserId,
          _installation.InstallationId,
          "load-profile",
          "合成負荷プロファイル",
          Path.GetFileName(sourceRoot),
          sourceRoot,
          sourceRoot,
          IsDefault: true,
          LastUsedUtc: null,
          BrowserChannel.Stable,
          ProfileDiscoveryConfidence.DirectoryFallback);
    }

    public string BrowserId => "load-test";

    public string DisplayName => "合成負荷ブラウザー";

    public BrowserProfile Profile { get; }

    public Task<IReadOnlyList<BrowserInstallation>>
        DiscoverInstallationsAsync(
            CancellationToken cancellationToken)
    {
      cancellationToken.ThrowIfCancellationRequested();
      return Task.FromResult<IReadOnlyList<BrowserInstallation>>(
          [_installation]);
    }

    public Task<IReadOnlyList<BrowserProfile>>
        DiscoverProfilesAsync(
            BrowserInstallation installation,
            CancellationToken cancellationToken)
    {
      cancellationToken.ThrowIfCancellationRequested();
      return Task.FromResult<IReadOnlyList<BrowserProfile>>(
          [Profile]);
    }

    public IReadOnlyList<ProcessMatchRule> GetProcessMatchRules(
        BrowserInstallation installation)
    {
      return
      [
        new ProcessMatchRule(
            "YtecBrowserCapsuleLoadTestNeverRunning",
            []),
      ];
    }

    public Task<BrowserBackupPlan> BuildBackupPlanAsync(
        BrowserProfile profile,
        string backupProfileId,
        BackupComponent selection,
        CancellationToken cancellationToken)
    {
      cancellationToken.ThrowIfCancellationRequested();
      if (!string.Equals(
          profile.ProfileId,
          Profile.ProfileId,
          StringComparison.Ordinal))
      {
        throw new InvalidOperationException(
            "負荷試験プロファイルが一致しません。");
      }

      return Task.FromResult(new BrowserBackupPlan(
          Profile,
          backupProfileId,
          selection,
          _files,
          [],
          _files.Sum(file => file.Length)));
    }
  }

  private sealed class NeverRunningProcessInspector
      : IBrowserProcessInspector
  {
    public Task<bool> IsRunningAsync(
        IReadOnlyList<ProcessMatchRule> rules,
        CancellationToken cancellationToken)
    {
      cancellationToken.ThrowIfCancellationRequested();
      return Task.FromResult(false);
    }
  }

  private sealed class SyntheticFingerprintProvider
      : IEnvironmentFingerprintProvider
  {
    public string GetCurrentFingerprint()
    {
      return "load-test-synthetic-environment";
    }
  }

  private sealed class TimestampedProgress
      : IProgress<ProfileBackupProgress>
  {
    private readonly object _gate = new();
    private readonly List<ProgressSample> _samples = [];

    public int Count
    {
      get
      {
        lock (_gate)
        {
          return _samples.Count;
        }
      }
    }

    public ProfileBackupProgress? Last
    {
      get
      {
        lock (_gate)
        {
          return _samples.Count == 0
              ? null
              : _samples[^1].Progress;
        }
      }
    }

    public void Report(ProfileBackupProgress value)
    {
      lock (_gate)
      {
        _samples.Add(new ProgressSample(
            Stopwatch.GetTimestamp(),
            value));
      }
    }

    public int GetMaximumReportsInOneSecond()
    {
      lock (_gate)
      {
        var left = 0;
        var maximum = 0;
        for (var right = 0; right < _samples.Count; right++)
        {
          while (left < right
              && _samples[right].Timestamp
                  - _samples[left].Timestamp
                  >= Stopwatch.Frequency)
          {
            left++;
          }

          maximum = Math.Max(maximum, right - left + 1);
        }

        return maximum;
      }
    }
  }

  private sealed record ProgressSample(
      long Timestamp,
      ProfileBackupProgress Progress);

  private sealed record LoadTestReport(
      DateTimeOffset StartedUtc,
      DateTimeOffset CompletedUtc,
      string OperatingSystem,
      string DotNetRuntime,
      IReadOnlyList<LoadScenarioResult> Scenarios);

  private sealed record LoadScenarioResult(
      string Scenario,
      bool Passed,
      int EntryCount,
      long LogicalBytes,
      long ContainerBytes,
      long VerifiedPlaintextBytes,
      double PreparationSeconds,
      double BackupAndSelfVerificationSeconds,
      double LogicalMiBPerSecond,
      int ProgressReportCount,
      int MaximumProgressReportsInOneSecond,
      long PeakWorkingSetBytes);

  private sealed record LoadTestOptions(
      string WorkRoot,
      string ResultPath,
      string Scenario,
      bool KeepArtifacts)
  {
    public static LoadTestOptions Parse(string[] args)
    {
      string? workRoot = null;
      string? resultPath = null;
      var scenario = LoadScenarios.All;
      var keepArtifacts = false;
      for (var index = 0; index < args.Length; index++)
      {
        switch (args[index])
        {
          case "--work-root":
            workRoot = ReadValue(args, ref index);
            break;
          case "--result":
            resultPath = ReadValue(args, ref index);
            break;
          case "--scenario":
            scenario = ReadValue(args, ref index);
            break;
          case "--keep":
            keepArtifacts = true;
            break;
          default:
            throw new ArgumentException(
                $"未知の引数です: {args[index]}");
        }
      }

      ArgumentException.ThrowIfNullOrWhiteSpace(workRoot);
      ArgumentException.ThrowIfNullOrWhiteSpace(resultPath);
      var normalizedWorkRoot = Path.GetFullPath(workRoot);
      if (!Path.IsPathFullyQualified(workRoot)
          || !string.Equals(
              Path.GetFileName(normalizedWorkRoot),
              "YtecBrowserCapsule-LoadTests",
              StringComparison.Ordinal))
      {
        throw new ArgumentException(
            "作業ルートは末尾がYtecBrowserCapsule-LoadTestsの"
            + "絶対パスである必要があります。");
      }

      var normalizedResultPath = Path.GetFullPath(resultPath);
      if (!Path.IsPathFullyQualified(resultPath)
          || !string.Equals(
              Path.GetExtension(normalizedResultPath),
              ".json",
              StringComparison.OrdinalIgnoreCase))
      {
        throw new ArgumentException(
            "結果は絶対パスのJSONファイルを指定してください。");
      }

      if (scenario is not (
          LoadScenarios.All
          or LoadScenarios.LargeFile
          or LoadScenarios.ManyFiles))
      {
        throw new ArgumentException(
            $"未知の負荷試験です: {scenario}");
      }

      return new LoadTestOptions(
          normalizedWorkRoot,
          normalizedResultPath,
          scenario,
          keepArtifacts);
    }

    public IReadOnlyList<string> GetScenarios()
    {
      return Scenario == LoadScenarios.All
          ? [LoadScenarios.LargeFile, LoadScenarios.ManyFiles]
          : [Scenario];
    }

    private static string ReadValue(
        string[] args,
      ref int index)
    {
      index++;
      if (index >= args.Length)
      {
        throw new ArgumentException(
            "引数の値がありません。");
      }

      return args[index];
    }
  }

  private static class LoadScenarios
  {
    public const string All = "all";
    public const string LargeFile = "large-file";
    public const string ManyFiles = "many-files";
  }
}
