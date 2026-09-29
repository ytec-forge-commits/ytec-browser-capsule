using System.Diagnostics;
using Ytec.BrowserCapsule.Domain.BackupContainers;
using Ytec.BrowserCapsule.Domain.BrowserDiscovery;
using Ytec.BrowserCapsule.Domain.ProfileBackups;

namespace Ytec.BrowserCapsule.Application.ProfileBackups;

/// <summary>
/// ブラウザー終了確認、計画固定、暗号化作成を順に実行します。
/// </summary>
public sealed class ProfileBackupService
{
  private readonly Dictionary<string, IBrowserProfileSource> _sources;
  private readonly Dictionary<string, IBrowserBackupPlanner> _planners;
  private readonly IBrowserProcessInspector _processInspector;
  private readonly IEnvironmentFingerprintProvider _fingerprintProvider;
  private readonly IBackupContainerService _containerService;

  public ProfileBackupService(
      IReadOnlyList<IBrowserProfileSource> sources,
      IBrowserProcessInspector processInspector,
      IEnvironmentFingerprintProvider fingerprintProvider,
      IBackupContainerService containerService)
  {
    ArgumentNullException.ThrowIfNull(sources);
    ArgumentNullException.ThrowIfNull(processInspector);
    ArgumentNullException.ThrowIfNull(fingerprintProvider);
    ArgumentNullException.ThrowIfNull(containerService);

    _sources = sources.ToDictionary(
        source => source.BrowserId,
        StringComparer.Ordinal);
    _planners = sources
        .OfType<IBrowserBackupPlanner>()
        .ToDictionary(
            planner => planner.BrowserId,
            StringComparer.Ordinal);
    _processInspector = processInspector;
    _fingerprintProvider = fingerprintProvider;
    _containerService = containerService;
  }

  public Task<ProfileBackupResult> CreateAsync(
      ProfileBackupRequest request,
      ReadOnlyMemory<char> passphrase,
      IProgress<ProfileBackupProgress>? progress,
      CancellationToken cancellationToken)
  {
    return CreateCoreAsync(
        request,
        recoveryKeyFilePath: null,
        passphrase,
        progress,
        cancellationToken);
  }

  public Task<ProfileBackupResult> CreateAsync(
      ProfileBackupRequest request,
      string recoveryKeyFilePath,
      IProgress<ProfileBackupProgress>? progress,
      CancellationToken cancellationToken)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(recoveryKeyFilePath);
    return CreateCoreAsync(
        request,
        recoveryKeyFilePath,
        ReadOnlyMemory<char>.Empty,
        progress,
        cancellationToken);
  }

  private async Task<ProfileBackupResult> CreateCoreAsync(
      ProfileBackupRequest request,
      string? recoveryKeyFilePath,
      ReadOnlyMemory<char> legacyPassphrase,
      IProgress<ProfileBackupProgress>? progress,
      CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(request);
    if (request.Profiles.Count == 0)
    {
      throw new ArgumentException(
          "バックアップするプロファイルを選んでください。",
          nameof(request));
    }

    var duplicateProfile = request.Profiles
        .GroupBy(selection => selection.Profile.ProfileId)
        .FirstOrDefault(group => group.Count() > 1);
    if (duplicateProfile is not null)
    {
      throw new ArgumentException(
          "同じプロファイルが重複して選択されています。",
          nameof(request));
    }

    var passwordCsvFiles = request.PasswordCsvFiles ?? [];
    var selectedProfileIds = request.Profiles
        .Select(selection => selection.Profile.ProfileId)
        .ToHashSet(StringComparer.Ordinal);
    if (passwordCsvFiles
        .GroupBy(csv => csv.SourceProfileId, StringComparer.Ordinal)
        .Any(group => group.Count() > 1)
        || passwordCsvFiles.Any(
            csv => !selectedProfileIds.Contains(csv.SourceProfileId)))
    {
      throw new ArgumentException(
          "パスワードCSVと選択プロファイルの対応が不正です。",
          nameof(request));
    }

    progress?.Report(new ProfileBackupProgress(
        ProfileBackupStage.Planning,
        0,
        0,
        0,
        0,
        BrowserId: null,
        ProfileDisplayName: null));

    var profileDataSelections = request.Profiles
        .Where(selection =>
            (selection.Components & ~BackupComponent.PasswordCsv)
            != BackupComponent.None)
        .ToArray();
    var installations = await EnsureBrowsersClosedAsync(
        profileDataSelections,
        cancellationToken).ConfigureAwait(false);
    var plans = new List<BrowserBackupPlan>(request.Profiles.Count);

    foreach (var selection in request.Profiles)
    {
      cancellationToken.ThrowIfCancellationRequested();
      if (!_planners.TryGetValue(
          selection.Profile.BrowserId,
          out var planner))
      {
        throw new BackupPlanException(
            "対応するブラウザーのバックアップ規則がありません。");
      }

      var plan = await planner.BuildBackupPlanAsync(
          selection.Profile,
          Guid.NewGuid().ToString("N"),
          selection.Components,
          cancellationToken).ConfigureAwait(false);
      plans.Add(plan);
    }

    EnsureFreeSpace(
        request.DestinationPath,
        plans,
        passwordCsvFiles.Sum(csv =>
            csv.ProtectedCsv.PlaintextLength));
    await RecheckBrowsersClosedAsync(
        installations,
        cancellationToken).ConfigureAwait(false);

    var totalFiles = plans.Sum(plan => plan.Files.Count);
    totalFiles = checked(totalFiles + passwordCsvFiles.Count);
    var totalBytes = checked(
        plans.Sum(plan => plan.TotalBytes)
        + passwordCsvFiles.Sum(csv => csv.ProtectedCsv.PlaintextLength));
    var progressState = new BackupProgressState();
    var entries = new List<BackupContainerEntrySource>(totalFiles);
    var planBySourceProfileId = plans.ToDictionary(
        plan => plan.Profile.ProfileId,
        StringComparer.Ordinal);

    foreach (var plan in plans)
    {
      foreach (var file in plan.Files)
      {
        var capturedPlan = plan;
        var capturedFile = file;
        entries.Add(new BackupContainerEntrySource(
            plan.BackupProfileId,
            file.EntryPath,
            file.Length,
            _ => ValueTask.FromResult<Stream>(
                OpenProgressStream(
                    capturedPlan,
                    capturedFile,
                    totalFiles,
                    totalBytes,
                    progress,
                    progressState)),
            GetComponentName(file.Component)));
      }
    }

    foreach (var csv in passwordCsvFiles)
    {
      var plan = planBySourceProfileId[csv.SourceProfileId];
      var capturedCsv = csv.ProtectedCsv;
      var capturedPlan = plan;
      entries.Add(new BackupContainerEntrySource(
          plan.BackupProfileId,
          string.Join(
              '/',
              "profiles",
              plan.Profile.BrowserId,
              plan.BackupProfileId,
              "passwords",
              "passwords.csv"),
          capturedCsv.PlaintextLength,
          async cancellation =>
          {
            var stream = await capturedCsv
                .OpenReadAsync(cancellation)
                .ConfigureAwait(false);
            return new ProgressReadStream(
                stream,
                bytesRead =>
                {
                  var currentBytes = Interlocked.Add(
                      ref progressState.ProcessedBytes,
                      bytesRead);
                  if (progressState.ShouldReport())
                  {
                    progress?.Report(new ProfileBackupProgress(
                        ProfileBackupStage.Writing,
                        Volatile.Read(ref progressState.CompletedFiles),
                        totalFiles,
                        currentBytes,
                        totalBytes,
                        capturedPlan.Profile.BrowserId,
                        capturedPlan.Profile.DisplayName));
                  }
                },
                () =>
                {
                  var currentFiles = Interlocked.Increment(
                      ref progressState.CompletedFiles);
                  if (currentFiles == totalFiles
                      || progressState.ShouldReport())
                  {
                    progress?.Report(new ProfileBackupProgress(
                        currentFiles == totalFiles
                            ? ProfileBackupStage.Verifying
                            : ProfileBackupStage.Writing,
                        currentFiles,
                        totalFiles,
                        Volatile.Read(ref progressState.ProcessedBytes),
                        totalBytes,
                        capturedPlan.Profile.BrowserId,
                        capturedPlan.Profile.DisplayName));
                  }
                });
          },
          "passwordCsv"));
    }

    progress?.Report(new ProfileBackupProgress(
        ProfileBackupStage.Writing,
        0,
        totalFiles,
        0,
        totalBytes,
        BrowserId: null,
        ProfileDisplayName: null));

    var containerRequest = new BackupContainerRequest(
        request.DestinationPath,
        request.AppVersion,
        _fingerprintProvider.GetCurrentFingerprint(),
        plans.Select(plan => new BackupProfileDescriptor(
            plan.BackupProfileId,
            plan.Profile.BrowserId,
            plan.Profile.ProfileId,
            plan.Profile.DisplayName,
            GetComponentNames(plan.SelectedComponents),
            passwordCsvFiles
                .FirstOrDefault(csv => string.Equals(
                    csv.SourceProfileId,
                    plan.Profile.ProfileId,
                    StringComparison.Ordinal))
                ?.ProtectedCsv is { } csv
                ? new BackupPasswordCsvDescriptor(
                    Included: true,
                    csv.Validation.RecordCount,
                    OriginalFileNameStored: false)
                : null,
            plan.Warnings
                .Select(warning => warning.Message)
                .Concat(passwordCsvFiles
                    .Where(csv => string.Equals(
                        csv.SourceProfileId,
                        plan.Profile.ProfileId,
                        StringComparison.Ordinal)
                        && csv.ProtectedCsv.ResidualPlaintextPath is not null)
                    .Select(csv =>
                        $"平文CSVを削除できませんでした: {csv.ProtectedCsv.ResidualPlaintextPath}"))
                .ToArray(),
            plan.Profile.WindowsUser?.StableId,
            plan.Profile.WindowsUser?.DisplayName))
        .ToArray(),
        entries);
    var containerResult = recoveryKeyFilePath is null
        ? await _containerService.CreateAsync(
            containerRequest,
            legacyPassphrase,
            cancellationToken).ConfigureAwait(false)
        : await _containerService.CreateAsync(
            containerRequest,
            recoveryKeyFilePath,
            cancellationToken).ConfigureAwait(false);

    progress?.Report(new ProfileBackupProgress(
        ProfileBackupStage.Completed,
        totalFiles,
        totalFiles,
        totalBytes,
        totalBytes,
        BrowserId: null,
        ProfileDisplayName: null));

    return new ProfileBackupResult(
        containerResult,
        plans.SelectMany(plan => plan.Warnings)
            .Concat(passwordCsvFiles
                .Where(csv =>
                    csv.ProtectedCsv.ResidualPlaintextPath is not null)
                .Select(csv => new BackupPlanWarning(
                    "plaintext-csv-remains",
                    $"平文CSVを削除できませんでした: {csv.ProtectedCsv.ResidualPlaintextPath}")))
            .ToArray());
  }

  private async Task<Dictionary<string, BrowserInstallation>>
      EnsureBrowsersClosedAsync(
          IReadOnlyList<ProfileBackupSelection> selections,
          CancellationToken cancellationToken)
  {
    var result = new Dictionary<string, BrowserInstallation>(
        StringComparer.Ordinal);

    foreach (var browserGroup in selections.GroupBy(
        selection => selection.Profile.BrowserId))
    {
      if (!_sources.TryGetValue(browserGroup.Key, out var source))
      {
        throw new BackupPlanException(
            "対応するブラウザー検出機能がありません。");
      }

      var discovered = await source
          .DiscoverInstallationsAsync(cancellationToken)
          .ConfigureAwait(false);
      foreach (var selection in browserGroup)
      {
        var installation = discovered.FirstOrDefault(
            candidate => string.Equals(
                candidate.InstallationId,
                selection.Profile.InstallationId,
                StringComparison.Ordinal));
        if (installation is null)
        {
          throw new BackupPlanException(
              $"{source.DisplayName}の設定場所が変更されています。再検出してください。");
        }

        if (await _processInspector.IsRunningAsync(
            source.GetProcessMatchRules(installation),
            cancellationToken).ConfigureAwait(false))
        {
          throw new BrowserMustBeClosedException(source.DisplayName);
        }

        result[installation.InstallationId] = installation;
      }
    }

    return result;
  }

  private async Task RecheckBrowsersClosedAsync(
      IReadOnlyDictionary<string, BrowserInstallation> installations,
      CancellationToken cancellationToken)
  {
    foreach (var installation in installations.Values)
    {
      var source = _sources[installation.BrowserId];
      if (await _processInspector.IsRunningAsync(
          source.GetProcessMatchRules(installation),
          cancellationToken).ConfigureAwait(false))
      {
        throw new BrowserMustBeClosedException(source.DisplayName);
      }
    }
  }

  private static ProgressReadStream OpenProgressStream(
      BrowserBackupPlan plan,
      BackupPlanFile file,
      int totalFiles,
      long totalBytes,
      IProgress<ProfileBackupProgress>? progress,
      BackupProgressState progressState)
  {
    var fileInfo = new FileInfo(file.SourcePath);
    fileInfo.Refresh();
    var lastWriteUtc = new DateTimeOffset(
        fileInfo.LastWriteTimeUtc,
        TimeSpan.Zero);
    if (!fileInfo.Exists
        || fileInfo.Length != file.Length
        || lastWriteUtc != file.LastWriteUtc)
    {
      throw new BackupPlanException(
          $"「{plan.Profile.DisplayName}」のファイルが計画後に変更されました。");
    }

    var stream = new FileStream(
        file.SourcePath,
        FileMode.Open,
        FileAccess.Read,
        FileShare.Read,
        bufferSize: 128 * 1024,
        FileOptions.Asynchronous | FileOptions.SequentialScan);

    return new ProgressReadStream(
        stream,
        bytesRead =>
        {
          var currentBytes = Interlocked.Add(
              ref progressState.ProcessedBytes,
              bytesRead);
          if (progressState.ShouldReport())
          {
            progress?.Report(new ProfileBackupProgress(
                ProfileBackupStage.Writing,
                Volatile.Read(ref progressState.CompletedFiles),
                totalFiles,
                currentBytes,
                totalBytes,
                plan.Profile.BrowserId,
                plan.Profile.DisplayName));
          }
        },
      () =>
      {
        var currentFiles = Interlocked.Increment(
            ref progressState.CompletedFiles);
        if (currentFiles == totalFiles
            || progressState.ShouldReport())
        {
          progress?.Report(new ProfileBackupProgress(
              currentFiles == totalFiles
                  ? ProfileBackupStage.Verifying
                  : ProfileBackupStage.Writing,
              currentFiles,
              totalFiles,
              Volatile.Read(ref progressState.ProcessedBytes),
              totalBytes,
              plan.Profile.BrowserId,
              plan.Profile.DisplayName));
        }
      });
  }

  private static List<string> GetComponentNames(
      BackupComponent selection)
  {
    var names = new List<string>();
    foreach (var component in Enum.GetValues<BackupComponent>())
    {
      if (component is BackupComponent.None
          || (selection & component) == 0)
      {
        continue;
      }

      names.Add(GetComponentName(component));
    }

    return names;
  }

  private static string GetComponentName(BackupComponent component)
  {
    return component switch
    {
      BackupComponent.Settings => "settings",
      BackupComponent.Bookmarks => "bookmarks",
      BackupComponent.History => "history",
      BackupComponent.Extensions => "extensions",
      BackupComponent.CookiesAndSiteData => "cookies",
      BackupComponent.Sessions => "sessions",
      BackupComponent.PasswordCsv => "passwordCsv",
      BackupComponent.FullProfile => "fullProfile",
      _ => throw new InvalidOperationException(
          "未定義のバックアップ項目です。"),
    };
  }

  private static void EnsureFreeSpace(
      string destinationPath,
      IEnumerable<BrowserBackupPlan> plans,
      long additionalBytes)
  {
    var fullPath = Path.GetFullPath(destinationPath);
    var root = Path.GetPathRoot(fullPath);
    if (string.IsNullOrWhiteSpace(root)
        || root.StartsWith(
            new string(Path.DirectorySeparatorChar, 2),
            StringComparison.Ordinal))
    {
      return;
    }

    var requiredBytes = checked(
        plans.Sum(plan => plan.TotalBytes) + additionalBytes);
    var safetyMargin = Math.Max(64L * 1024 * 1024, requiredBytes / 20);
    var drive = new DriveInfo(root);
    if (drive.IsReady
        && drive.AvailableFreeSpace < requiredBytes + safetyMargin)
    {
      throw new IOException(
          "バックアップ先の空き容量が不足しています。");
    }
  }

  private sealed class BackupProgressState
  {
    private static readonly long ReportInterval =
        Stopwatch.Frequency / 10;
    private long _lastReportTimestamp;

    public long ProcessedBytes;

    public int CompletedFiles;

    public bool ShouldReport()
    {
      var now = Stopwatch.GetTimestamp();
      var previous = Volatile.Read(ref _lastReportTimestamp);
      return now - previous >= ReportInterval
          && Interlocked.CompareExchange(
              ref _lastReportTimestamp,
              now,
              previous) == previous;
    }
  }
}
