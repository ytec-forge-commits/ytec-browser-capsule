using Ytec.BrowserCapsule.Application.ProfileBackups;
using Ytec.BrowserCapsule.Domain.BackupContainers;
using Ytec.BrowserCapsule.Domain.BrowserDiscovery;
using Ytec.BrowserCapsule.Domain.ProfileBackups;
using Ytec.BrowserCapsule.Domain.ProfileRestores;

namespace Ytec.BrowserCapsule.Application.ProfileRestores;

/// <summary>
/// 全体検証、暗号化ロールバック、一時展開、安全適用を順に実行します。
/// </summary>
public sealed class ProfileRestoreService
{
  private readonly Dictionary<string, IBrowserProfileSource> _sources;
  private readonly IBrowserProcessInspector _processInspector;
  private readonly IEnvironmentFingerprintProvider _fingerprintProvider;
  private readonly IBackupContainerService _containerService;
  private readonly ProfileBackupService _backupService;
  private readonly IRestoreFileApplier _fileApplier;

  public ProfileRestoreService(
      IReadOnlyList<IBrowserProfileSource> sources,
      IBrowserProcessInspector processInspector,
      IEnvironmentFingerprintProvider fingerprintProvider,
      IBackupContainerService containerService,
      ProfileBackupService backupService,
      IRestoreFileApplier fileApplier)
  {
    ArgumentNullException.ThrowIfNull(sources);
    ArgumentNullException.ThrowIfNull(processInspector);
    ArgumentNullException.ThrowIfNull(fingerprintProvider);
    ArgumentNullException.ThrowIfNull(containerService);
    ArgumentNullException.ThrowIfNull(backupService);
    ArgumentNullException.ThrowIfNull(fileApplier);

    _sources = sources.ToDictionary(
        source => source.BrowserId,
        StringComparer.Ordinal);
    _processInspector = processInspector;
    _fingerprintProvider = fingerprintProvider;
    _containerService = containerService;
    _backupService = backupService;
    _fileApplier = fileApplier;
  }

  public Task<BackupContainerVerificationResult> InspectAsync(
      string containerPath,
      ReadOnlyMemory<char> passphrase,
      CancellationToken cancellationToken)
  {
    return InspectAsync(
        containerPath,
        BackupContainerCredential.FromLegacyPassphrase(passphrase),
        cancellationToken);
  }

  public Task<BackupContainerHeaderInfo> ReadHeaderAsync(
      string containerPath,
      CancellationToken cancellationToken)
  {
    return _containerService.ReadHeaderAsync(
        containerPath,
        cancellationToken);
  }

  public Task<BackupContainerVerificationResult> InspectAsync(
      string containerPath,
      BackupContainerCredential credential,
      CancellationToken cancellationToken)
  {
    return _containerService.VerifyAsync(
        containerPath,
        credential,
        cancellationToken);
  }

  public async Task<IReadOnlyList<PasswordCsvImportFile>>
      ExtractPasswordCsvForImportAsync(
          string containerPath,
          ReadOnlyMemory<char> passphrase,
          IReadOnlySet<string> backupProfileIds,
          string emptySecureStagingDirectory,
          CancellationToken cancellationToken)
  {
    return await ExtractPasswordCsvForImportAsync(
        containerPath,
        BackupContainerCredential.FromLegacyPassphrase(passphrase),
        backupProfileIds,
        emptySecureStagingDirectory,
        cancellationToken).ConfigureAwait(false);
  }

  public async Task<IReadOnlyList<PasswordCsvImportFile>>
      ExtractPasswordCsvForImportAsync(
          string containerPath,
          BackupContainerCredential credential,
          IReadOnlySet<string> backupProfileIds,
          string emptySecureStagingDirectory,
          CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(backupProfileIds);
    if (backupProfileIds.Count == 0)
    {
      return [];
    }

    var verification = await _containerService.VerifyAsync(
        containerPath,
        credential,
        cancellationToken).ConfigureAwait(false);
    var selectedProfiles = verification.Manifest.Profiles
        .Where(profile =>
            backupProfileIds.Contains(profile.BackupProfileId)
            && profile.PasswordCsv?.Included is true)
        .ToArray();
    ValidateProtectedProfileEntries(selectedProfiles);
    var selectedPaths = selectedProfiles
        .SelectMany(profile => profile.Files)
        .Where(file => string.Equals(
            file.Component,
            "passwordCsv",
            StringComparison.Ordinal))
        .Select(file => file.EntryPath)
        .ToHashSet(StringComparer.Ordinal);
    if (selectedPaths.Count == 0)
    {
      return [];
    }

    var extraction = await _containerService
        .ExtractSelectedVerifiedAsync(
            containerPath,
            credential,
            emptySecureStagingDirectory,
            selectedPaths,
            cancellationToken)
        .ConfigureAwait(false);
    var result = new List<PasswordCsvImportFile>(
        selectedProfiles.Length);
    for (var index = 0; index < selectedProfiles.Length; index++)
    {
      var profile = selectedProfiles[index];
      var manifestFile = profile.Files.Single(file =>
          string.Equals(
              file.Component,
              "passwordCsv",
              StringComparison.Ordinal));
      var extracted = extraction.Files.Single(file =>
          string.Equals(
              file.EntryPath,
              manifestFile.EntryPath,
              StringComparison.Ordinal));
      var safePath = Path.Combine(
          Path.GetFullPath(emptySecureStagingDirectory),
          $"{profile.BrowserId}-passwords-{index + 1:D2}.csv");
      File.Move(extracted.AbsolutePath, safePath, overwrite: false);
      result.Add(new PasswordCsvImportFile(
          profile.BackupProfileId,
          profile.BrowserId,
          safePath,
          profile.PasswordCsv?.RecordCount ?? 0));
    }

    var nestedRoot = Path.Combine(
        Path.GetFullPath(emptySecureStagingDirectory),
        "profiles");
    if (Directory.Exists(nestedRoot)
        && (File.GetAttributes(nestedRoot)
            & FileAttributes.ReparsePoint) == 0)
    {
      Directory.Delete(nestedRoot, recursive: true);
    }

    return result;
  }

  public Task<ProfileRestoreResult> RestoreAsync(
      ProfileRestoreRequest request,
      ReadOnlyMemory<char> passphrase,
      IProgress<ProfileRestoreProgress>? progress,
      CancellationToken cancellationToken)
  {
    return RestoreAsync(
        request,
        BackupContainerCredential.FromLegacyPassphrase(passphrase),
        progress,
        cancellationToken);
  }

  public async Task<ProfileRestoreResult> RestoreAsync(
      ProfileRestoreRequest request,
      BackupContainerCredential credential,
      IProgress<ProfileRestoreProgress>? progress,
      CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(request);
    ValidateBasicRequest(request);
    progress?.Report(new ProfileRestoreProgress(
        ProfileRestoreStage.Inspecting,
        0,
        0,
        ProfileDisplayName: null));
    var sourceVerification = await _containerService.VerifyAsync(
        request.ContainerPath,
        credential,
        cancellationToken).ConfigureAwait(false);
    ValidateMappings(request.Mappings, sourceVerification.Manifest);
    ValidateProtectedProfileEntries(sourceVerification.Manifest.Profiles.Where(profile =>
        request.Mappings.Any(mapping => mapping.BackupProfileId == profile.BackupProfileId)));

    var warnings = new List<ProfileRestoreWarning>();
    var environmentMatched = string.Equals(
        sourceVerification.Manifest.EnvironmentFingerprint,
        _fingerprintProvider.GetCurrentFingerprint(),
        StringComparison.Ordinal);
    var windowsUsersMatched = WindowsUsersMatch(
        request.Mappings,
        sourceVerification.Manifest);
    var effectiveMode = request.RequestedMode;
    if ((!environmentMatched || !windowsUsersMatched)
        && request.RequestedMode == ProfileRestoreMode.SameEnvironment
        && !request.AllowFullRestoreAcrossEnvironment)
    {
      effectiveMode = ProfileRestoreMode.Migration;
      warnings.Add(new ProfileRestoreWarning(
          "environment-mismatch-mode-changed",
          !environmentMatched
              ? "環境IDが異なるため、別環境移行へ切り替えました。"
              : "Windowsユーザーの対応が異なるため、別環境移行へ切り替えました。"));
    }
    else if (!environmentMatched
        && request.AllowFullRestoreAcrossEnvironment)
    {
      warnings.Add(new ProfileRestoreWarning(
          "environment-mismatch-full-restore",
          "環境IDが異なる状態でフル復元が明示承認されています。Cookie、セッション、パスキーは復元を保証できません。"));
    }

    if (effectiveMode == ProfileRestoreMode.Migration)
    {
      warnings.Add(new ProfileRestoreWarning(
          "migration-login-warning",
          "別環境ではCookie、ログインセッション、パスキーを保証できません。復元後に再ログインしてください。"));
    }

    var selectedEntryPaths = SelectRestoreEntryPaths(
        sourceVerification.Manifest,
        request.Mappings);
    if (selectedEntryPaths.Count == 0)
    {
      if (!HasSelectedPasswordCsv(
          sourceVerification.Manifest,
          request.Mappings))
      {
        throw new RestorePlanException(
            "選択した復元対象にファイルがありません。");
      }

      warnings.Add(new ProfileRestoreWarning(
          "password-csv-import-required",
          "保存パスワードCSVはプロファイルへ直接書き込みません。公式インポート画面から取り込んでください。"));
      progress?.Report(new ProfileRestoreProgress(
          ProfileRestoreStage.Completed,
          0,
          0,
          ProfileDisplayName: null));
      return new ProfileRestoreResult(
          effectiveMode,
          environmentMatched,
          RollbackPath: null,
          RestoredFileCount: 0,
          warnings,
          sourceVerification);
    }

    var processChecks = await BuildProcessChecksAsync(
        request.Mappings,
        cancellationToken).ConfigureAwait(false);
    await EnsureBrowsersClosedAsync(
        processChecks,
        cancellationToken).ConfigureAwait(false);

    var rollbackDirectory = CreateValidatedLocalDirectory(
        request.RollbackDirectory);
    var rollbackPath = Path.Combine(
        rollbackDirectory,
        $"YBC-Rollback-{DateTime.Now:yyyyMMdd-HHmmss}-"
        + $"{Guid.NewGuid():N}.bvb");
    var rollbackKeyPath = Path.ChangeExtension(
        rollbackPath,
        ".ybckey");
    progress?.Report(new ProfileRestoreProgress(
        ProfileRestoreStage.CreatingRollback,
        0,
        0,
        ProfileDisplayName: null));
    var rollbackResult = await _backupService.CreateAsync(
        new ProfileBackupRequest(
            rollbackPath,
            request.Mappings.Select(mapping =>
                new ProfileBackupSelection(
                    mapping.TargetProfile,
                    BackupComponent.FullProfile))
                .ToArray(),
            request.AppVersion),
        rollbackKeyPath,
        progress: null,
        cancellationToken).ConfigureAwait(false);

    var stagingDirectory = CreateStagingDirectory(
        request.Mappings,
        "restore");
    string? rollbackStagingDirectory = null;
    List<RestoreTargetPath> createdTargetFiles = [];
    var applyStarted = false;
    try
    {
      progress?.Report(new ProfileRestoreProgress(
          ProfileRestoreStage.Extracting,
          0,
          0,
          ProfileDisplayName: null));
      var extraction = await _containerService
          .ExtractSelectedVerifiedAsync(
              request.ContainerPath,
              credential,
              stagingDirectory,
              selectedEntryPaths,
              cancellationToken)
          .ConfigureAwait(false);
      var operations = BuildOperations(
          extraction,
          request.Mappings,
          warnings);
      if (operations.Count == 0)
      {
        throw new RestorePlanException(
            "選択した復元対象にファイルがありません。");
      }

      createdTargetFiles = FindTargetsAbsentBeforeRestore(operations);
      applyStarted = true;
      await _fileApplier.ApplyAsync(
          operations,
          token => EnsureBrowsersClosedAsync(processChecks, token),
          progress,
          cancellationToken).ConfigureAwait(false);
      progress?.Report(new ProfileRestoreProgress(
          ProfileRestoreStage.Verifying,
          operations.Count,
          operations.Count,
          ProfileDisplayName: null));

      progress?.Report(new ProfileRestoreProgress(
          ProfileRestoreStage.Completed,
          operations.Count,
          operations.Count,
          ProfileDisplayName: null));
      return new ProfileRestoreResult(
          effectiveMode,
          environmentMatched,
          rollbackResult.Container.FinalPath,
          operations.Count,
          warnings,
          sourceVerification)
      {
        RollbackRecoveryKeyPath =
            rollbackResult.Container.RecoveryKeyPath,
      };
    }
    catch (Exception restoreFailure) when (
        applyStarted
        && restoreFailure is not RestoreFailedRollbackFailedException)
    {
      progress?.Report(new ProfileRestoreProgress(
          ProfileRestoreStage.RollingBack,
          0,
          0,
          ProfileDisplayName: null));
      try
      {
        rollbackStagingDirectory = CreateStagingDirectory(
            request.Mappings,
            "rollback");
        var rollbackExtraction =
            await _containerService.ExtractVerifiedAsync(
                rollbackResult.Container.FinalPath,
                BackupContainerCredential.FromRecoveryKeyFile(
                    rollbackKeyPath),
                rollbackStagingDirectory,
                CancellationToken.None).ConfigureAwait(false);
        var rollbackMappings = BuildRollbackMappings(
            rollbackExtraction.Verification.Manifest,
            request.Mappings);
        var rollbackWarnings = new List<ProfileRestoreWarning>();
        var rollbackOperations = BuildOperations(
            rollbackExtraction,
            rollbackMappings,
            rollbackWarnings);
        await _fileApplier.ApplyAsync(
            rollbackOperations,
            token => EnsureBrowsersClosedAsync(processChecks, token),
            progress: null,
            CancellationToken.None).ConfigureAwait(false);
        DeleteTargetsCreatedByFailedRestore(createdTargetFiles);
      }
      catch (Exception rollbackFailure)
      {
        throw new RestoreFailedRollbackFailedException(
            restoreFailure,
            rollbackFailure);
      }

      throw new RestoreFailedRolledBackException(restoreFailure);
    }
    finally
    {
      TryDeleteGeneratedStaging(stagingDirectory);
      if (rollbackStagingDirectory is not null)
      {
        TryDeleteGeneratedStaging(rollbackStagingDirectory);
      }
    }
  }

  private async Task<IReadOnlyList<BrowserProcessCheck>>
      BuildProcessChecksAsync(
          IReadOnlyList<ProfileRestoreMapping> mappings,
          CancellationToken cancellationToken)
  {
    var checks = new List<BrowserProcessCheck>();
    foreach (var browserGroup in mappings.GroupBy(
        mapping => mapping.TargetProfile.BrowserId))
    {
      if (!_sources.TryGetValue(browserGroup.Key, out var source))
      {
        throw new RestorePlanException(
            "復元先ブラウザーの検出機能がありません。");
      }

      var installations = await source
          .DiscoverInstallationsAsync(cancellationToken)
          .ConfigureAwait(false);
      foreach (var installationId in browserGroup
          .Select(mapping => mapping.TargetProfile.InstallationId)
          .Distinct(StringComparer.Ordinal))
      {
        var installation = installations.FirstOrDefault(candidate =>
            string.Equals(
                candidate.InstallationId,
                installationId,
                StringComparison.Ordinal))
            ?? throw new RestorePlanException(
                $"{source.DisplayName}の場所が変わっています。再検出してください。");
        checks.Add(new BrowserProcessCheck(
            source.DisplayName,
            source.GetProcessMatchRules(installation)));
      }
    }

    return checks;
  }

  private async Task EnsureBrowsersClosedAsync(
      IReadOnlyList<BrowserProcessCheck> checks,
      CancellationToken cancellationToken)
  {
    foreach (var check in checks)
    {
      if (await _processInspector.IsRunningAsync(
          check.Rules,
          cancellationToken).ConfigureAwait(false))
      {
        throw new BrowserMustBeClosedException(check.DisplayName);
      }
    }
  }

  private static List<RestoreFileOperation> BuildOperations(
      BackupContainerExtractionResult extraction,
      IReadOnlyList<ProfileRestoreMapping> mappings,
      List<ProfileRestoreWarning> warnings)
  {
    var filesByPath = extraction.Files.ToDictionary(
        file => file.EntryPath,
        StringComparer.Ordinal);
    var operations = new List<RestoreFileOperation>();
    var passwordCsvPresent = false;

    foreach (var mapping in mappings)
    {
      var sourceProfile = extraction.Verification.Manifest.Profiles
          .Single(profile => string.Equals(
              profile.BackupProfileId,
              mapping.BackupProfileId,
              StringComparison.Ordinal));
      ValidateProtectedProfileEntries([sourceProfile]);
      var profilePrefix = string.Join(
          '/',
          "profiles",
          sourceProfile.BrowserId,
          sourceProfile.BackupProfileId,
          "profile") + "/";
      var passwordPrefix = string.Join(
          '/',
          "profiles",
          sourceProfile.BrowserId,
          sourceProfile.BackupProfileId,
          "passwords") + "/";

      foreach (var manifestFile in sourceProfile.Files)
      {
        if (manifestFile.EntryPath.StartsWith(
            passwordPrefix,
            StringComparison.Ordinal))
        {
          passwordCsvPresent |=
              (mapping.Components & BackupComponent.PasswordCsv) != 0;
          continue;
        }

        if (!manifestFile.EntryPath.StartsWith(
            profilePrefix,
            StringComparison.Ordinal))
        {
          throw new RestorePlanException(
              "バックアップ内のプロファイルパスを解釈できません。");
        }

        if (!ShouldRestore(
            manifestFile.Component,
            mapping.Components))
        {
          continue;
        }

        var relativeTargetPath =
            manifestFile.EntryPath[profilePrefix.Length..];
        var extracted = filesByPath[manifestFile.EntryPath];
        operations.Add(new RestoreFileOperation(
            extracted.AbsolutePath,
            mapping.TargetProfile.AbsoluteProfilePath,
            relativeTargetPath,
            manifestFile.Length,
            manifestFile.Sha256,
            manifestFile.Component,
            mapping.TargetProfile.DisplayName)
        {
          BrowserId = mapping.TargetProfile.BrowserId,
        });
      }
    }

    if (passwordCsvPresent)
    {
      warnings.Add(new ProfileRestoreWarning(
          "password-csv-import-required",
          "保存パスワードCSVはプロファイルへ直接書き込みません。復元完了後に公式インポート画面から取り込んでください。"));
    }

    return operations;
  }

  private static HashSet<string> SelectRestoreEntryPaths(
      BackupManifest manifest,
      IReadOnlyList<ProfileRestoreMapping> mappings)
  {
    var result = new HashSet<string>(StringComparer.Ordinal);
    var sourceById = manifest.Profiles.ToDictionary(
        profile => profile.BackupProfileId,
        StringComparer.Ordinal);
    foreach (var mapping in mappings)
    {
      var sourceProfile = sourceById[mapping.BackupProfileId];
      foreach (var file in sourceProfile.Files)
      {
        if (string.Equals(
            file.Component,
            "passwordCsv",
            StringComparison.Ordinal))
        {
          continue;
        }

        if (ShouldRestore(file.Component, mapping.Components))
        {
          result.Add(file.EntryPath);
        }
      }
    }

    return result;
  }

  private static void ValidateProtectedProfileEntries(
      IEnumerable<BackupProfileManifest> profiles)
  {
    foreach (var profile in profiles)
    {
      var prefix = $"profiles/{profile.BrowserId}/{profile.BackupProfileId}/";
      foreach (var file in profile.Files)
      {
        if (!file.EntryPath.StartsWith(prefix, StringComparison.Ordinal))
        {
          throw new RestorePlanException("バックアップ内のプロファイルパスを解釈できません。");
        }

        // component metadata is supplied by the archive author, not a permission check.
        ProtectedBrowserFilePolicy.ValidateRestorePath(profile.BrowserId, file.EntryPath[prefix.Length..]);
      }
    }
  }

  private static bool HasSelectedPasswordCsv(
      BackupManifest manifest,
      IReadOnlyList<ProfileRestoreMapping> mappings)
  {
    var sourceById = manifest.Profiles.ToDictionary(
        profile => profile.BackupProfileId,
        StringComparer.Ordinal);
    return mappings.Any(mapping =>
        (mapping.Components & BackupComponent.PasswordCsv) != 0
        && sourceById[mapping.BackupProfileId].PasswordCsv?.Included
            is true);
  }

  private static List<RestoreTargetPath> FindTargetsAbsentBeforeRestore(
      IReadOnlyList<RestoreFileOperation> operations)
  {
    return operations
        .Select(operation => new RestoreTargetPath(
            Path.GetFullPath(operation.TargetRoot),
            GetValidatedTargetPath(
                operation.TargetRoot,
                operation.RelativeTargetPath)))
        .Where(target => !File.Exists(target.AbsolutePath))
        .ToList();
  }

  private static void DeleteTargetsCreatedByFailedRestore(
      IReadOnlyList<RestoreTargetPath> targets)
  {
    foreach (var target in targets)
    {
      if (!File.Exists(target.AbsolutePath))
      {
        continue;
      }

      if ((File.GetAttributes(target.AbsolutePath)
          & FileAttributes.ReparsePoint) != 0)
      {
        throw new RestorePlanException(
            "ロールバック対象にリンクまたはジャンクションがあります。");
      }

      File.Delete(target.AbsolutePath);
      var parent = Path.GetDirectoryName(target.AbsolutePath);
      while (parent is not null
          && !string.Equals(
              parent,
              target.TargetRoot,
              StringComparison.OrdinalIgnoreCase)
          && Directory.Exists(parent)
          && (File.GetAttributes(parent)
              & FileAttributes.ReparsePoint) == 0
          && !Directory.EnumerateFileSystemEntries(parent).Any())
      {
        Directory.Delete(parent);
        parent = Path.GetDirectoryName(parent);
      }
    }
  }

  private static string GetValidatedTargetPath(
      string targetRoot,
      string relativeTargetPath)
  {
    var fullRoot = Path.GetFullPath(targetRoot);
    var normalized = BackupEntryPathValidator.Validate(
        relativeTargetPath.Replace(
            Path.DirectorySeparatorChar,
            '/'));
    var fullTarget = Path.GetFullPath(
        Path.Combine(
            fullRoot,
            normalized.Replace(
                '/',
                Path.DirectorySeparatorChar)));
    var rootWithSeparator = fullRoot.EndsWith(
        Path.DirectorySeparatorChar)
        ? fullRoot
        : fullRoot + Path.DirectorySeparatorChar;
    if (!fullTarget.StartsWith(
        rootWithSeparator,
        StringComparison.OrdinalIgnoreCase))
    {
      throw new RestorePlanException(
          "復元先プロファイルの外へ書き込もうとしました。");
    }

    return fullTarget;
  }

  private static bool ShouldRestore(
      string? component,
      BackupComponent selectedComponents)
  {
    if ((selectedComponents & BackupComponent.FullProfile) != 0)
    {
      return !string.Equals(
          component,
          "passwordCsv",
          StringComparison.Ordinal);
    }

    if (component is null)
    {
      throw new RestorePlanException(
          "この開発版バックアップにはコンポーネント情報がありません。全体復元を選択してください。");
    }

    var requiredFlag = component switch
    {
      "settings" => BackupComponent.Settings,
      "bookmarks" => BackupComponent.Bookmarks,
      "history" => BackupComponent.History,
      "extensions" => BackupComponent.Extensions,
      "cookies" => BackupComponent.CookiesAndSiteData,
      "sessions" => BackupComponent.Sessions,
      "fullProfile" => BackupComponent.FullProfile,
      "passwordCsv" => BackupComponent.PasswordCsv,
      _ => throw new RestorePlanException(
          "バックアップに未対応のコンポーネントがあります。"),
    };
    return (selectedComponents & requiredFlag) != 0;
  }

  private static ProfileRestoreMapping[]
      BuildRollbackMappings(
          BackupManifest rollbackManifest,
          IReadOnlyList<ProfileRestoreMapping> originalMappings)
  {
    return rollbackManifest.Profiles.Select(source =>
    {
      var target = originalMappings.Single(mapping =>
          string.Equals(
              mapping.TargetProfile.ProfileId,
              source.SourceProfileId,
              StringComparison.Ordinal));
      return new ProfileRestoreMapping(
          source.BackupProfileId,
          target.TargetProfile,
          BackupComponent.FullProfile);
    }).ToArray();
  }

  private static void ValidateBasicRequest(ProfileRestoreRequest request)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(request.ContainerPath);
    ArgumentException.ThrowIfNullOrWhiteSpace(request.RollbackDirectory);
    ArgumentException.ThrowIfNullOrWhiteSpace(request.AppVersion);
    if (request.Mappings.Count == 0)
    {
      throw new RestorePlanException(
          "復元元と復元先を1件以上対応付けてください。");
    }

    ValidateLocalPathSyntax(request.RollbackDirectory);
    var roots = request.Mappings
        .Select(mapping =>
            Path.GetPathRoot(
                ValidateLocalPathSyntax(
                    mapping.TargetProfile.AbsoluteProfilePath)))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();
    if (roots.Length != 1)
    {
      throw new RestorePlanException(
          "異なるドライブのプロファイルは1回の復元にまとめられません。");
    }
  }

  private static void ValidateMappings(
      IReadOnlyList<ProfileRestoreMapping> mappings,
      BackupManifest manifest)
  {
    if (mappings
        .GroupBy(mapping => mapping.BackupProfileId, StringComparer.Ordinal)
        .Any(group => group.Count() > 1)
        || mappings
            .GroupBy(
                mapping => mapping.TargetProfile.ProfileId,
                StringComparer.Ordinal)
            .Any(group => group.Count() > 1))
    {
      throw new RestorePlanException(
          "復元元または復元先が重複しています。");
    }

    var sourceById = manifest.Profiles.ToDictionary(
        profile => profile.BackupProfileId,
        StringComparer.Ordinal);
    foreach (var mapping in mappings)
    {
      if (!sourceById.TryGetValue(
          mapping.BackupProfileId,
          out var source)
          || !string.Equals(
              source.BrowserId,
              mapping.TargetProfile.BrowserId,
              StringComparison.Ordinal)
          || mapping.Components == BackupComponent.None
          || !Directory.Exists(
              mapping.TargetProfile.AbsoluteProfilePath))
      {
        throw new RestorePlanException(
            "復元元と復元先の対応が不正です。ブラウザーをまたぐ復元はできません。");
      }
    }
  }

  private static bool WindowsUsersMatch(
      IReadOnlyList<ProfileRestoreMapping> mappings,
      BackupManifest manifest)
  {
    var sourceById = manifest.Profiles.ToDictionary(
        profile => profile.BackupProfileId,
        StringComparer.Ordinal);
    return mappings.All(mapping =>
    {
      var sourceWindowsUserId =
          sourceById[mapping.BackupProfileId].SourceWindowsUserId;
      var targetWindowsUserId =
          mapping.TargetProfile.WindowsUser?.StableId;
      return string.IsNullOrWhiteSpace(sourceWindowsUserId)
          || string.Equals(
              sourceWindowsUserId,
              targetWindowsUserId,
              StringComparison.Ordinal);
    });
  }

  private static string CreateStagingDirectory(
      IReadOnlyList<ProfileRestoreMapping> mappings,
      string purpose)
  {
    var parent = Path.GetDirectoryName(
        Path.GetFullPath(
            mappings[0].TargetProfile.AbsoluteProfilePath))
        ?? throw new RestorePlanException(
            "復元先と同じドライブに一時領域を作成できません。");
    var stagingPath = Path.Combine(
        parent,
        $".ytec-browser-capsule-{purpose}-{Guid.NewGuid():N}");
    Directory.CreateDirectory(stagingPath);
    if ((File.GetAttributes(stagingPath)
        & FileAttributes.ReparsePoint) != 0)
    {
      throw new RestorePlanException(
          "安全な一時領域を作成できません。");
    }

    return stagingPath;
  }

  private static string CreateValidatedLocalDirectory(
      string directoryPath)
  {
    var fullPath = ValidateLocalPathSyntax(directoryPath);
    EnsureNoExistingReparsePoint(fullPath);
    Directory.CreateDirectory(fullPath);
    EnsureNoExistingReparsePoint(fullPath);
    return fullPath;
  }

  private static string ValidateLocalPathSyntax(string path)
  {
    try
    {
      if (!Path.IsPathFullyQualified(path)
          || path.StartsWith(
              new string(Path.DirectorySeparatorChar, 2),
              StringComparison.Ordinal))
      {
        throw new RestorePlanException(
            "復元先とロールバック先にはローカルの絶対パスを指定してください。");
      }

      var fullPath = Path.GetFullPath(path);
      if (fullPath.StartsWith(
          new string(Path.DirectorySeparatorChar, 2),
          StringComparison.Ordinal)
          || string.IsNullOrEmpty(Path.GetPathRoot(fullPath)))
      {
        throw new RestorePlanException(
            "復元先とロールバック先にはローカルの絶対パスを指定してください。");
      }

      return fullPath;
    }
    catch (Exception exception) when (
        exception is ArgumentException
        or NotSupportedException
        or PathTooLongException)
    {
      throw new RestorePlanException(
          "復元先とロールバック先にはローカルの絶対パスを指定してください。",
          exception);
    }
  }

  private static void EnsureNoExistingReparsePoint(string fullPath)
  {
    var root = Path.GetPathRoot(fullPath)
        ?? throw new RestorePlanException(
            "ローカルパスのルートを確認できません。");
    var current = root;
    var relative = fullPath[root.Length..];
    foreach (var segment in relative.Split(
        [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
        StringSplitOptions.RemoveEmptyEntries))
    {
      current = Path.Combine(current, segment);
      if (!Directory.Exists(current) && !File.Exists(current))
      {
        continue;
      }

      if ((File.GetAttributes(current)
          & FileAttributes.ReparsePoint) != 0)
      {
        throw new RestorePlanException(
            "復元先またはロールバック先にリンクやジャンクションがあります。");
      }
    }
  }

  private static void TryDeleteGeneratedStaging(string stagingPath)
  {
    try
    {
      var name = Path.GetFileName(stagingPath);
      if (name.StartsWith(
          ".ytec-browser-capsule-",
          StringComparison.Ordinal)
          && Directory.Exists(stagingPath)
          && (File.GetAttributes(stagingPath)
              & FileAttributes.ReparsePoint) == 0)
      {
        Directory.Delete(stagingPath, recursive: true);
      }
    }
    catch (Exception exception) when (
        exception is IOException
        or UnauthorizedAccessException)
    {
      // 平文一時領域の残存は呼び出し側の警告・起動時清掃で扱います。
    }
  }

  private sealed record BrowserProcessCheck(
      string DisplayName,
      IReadOnlyList<ProcessMatchRule> Rules);

  private sealed record RestoreTargetPath(
      string TargetRoot,
      string AbsolutePath);
}
