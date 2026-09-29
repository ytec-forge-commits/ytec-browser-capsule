namespace Ytec.BrowserCapsule.Domain.BackupContainers;

/// <summary>
/// バックアップ対象プロファイルの暗号化マニフェスト用情報です。
/// </summary>
public sealed record BackupProfileDescriptor(
    string BackupProfileId,
    string BrowserId,
    string SourceProfileId,
    string DisplayName,
    IReadOnlyList<string> Components,
    BackupPasswordCsvDescriptor? PasswordCsv,
    IReadOnlyList<string> Warnings,
    string? SourceWindowsUserId = null,
    string? SourceWindowsUserDisplayName = null);

/// <summary>
/// 公式エクスポートCSVの有無だけを記録します。
/// </summary>
public sealed record BackupPasswordCsvDescriptor(
    bool Included,
    long RecordCount,
    bool OriginalFileNameStored);

/// <summary>
/// コンテナへストリーミングする1ファイルを表します。
/// </summary>
public sealed record BackupContainerEntrySource(
    string BackupProfileId,
    string EntryPath,
    long Length,
    Func<CancellationToken, ValueTask<Stream>> OpenReadAsync,
    string? Component = null);

/// <summary>
/// 暗号化コンテナ作成要求です。
/// </summary>
public sealed record BackupContainerRequest(
    string DestinationPath,
    string AppVersion,
    string EnvironmentFingerprint,
    IReadOnlyList<BackupProfileDescriptor> Profiles,
    IReadOnlyList<BackupContainerEntrySource> Entries,
    Guid? BackupId = null,
    DateTimeOffset? CreatedUtc = null);

/// <summary>
/// 暗号化マニフェストに記録するファイル情報です。
/// </summary>
public sealed record BackupFileManifest(
    string EntryPath,
    long Length,
    string Sha256,
    string? Component = null);

/// <summary>
/// 暗号化マニフェストに記録するプロファイル情報です。
/// </summary>
public sealed record BackupProfileManifest(
    string BackupProfileId,
    string BrowserId,
    string SourceProfileId,
    string DisplayName,
    IReadOnlyList<string> Components,
    IReadOnlyList<BackupFileManifest> Files,
    BackupPasswordCsvDescriptor? PasswordCsv,
    IReadOnlyList<string> Warnings,
    string? SourceWindowsUserId = null,
    string? SourceWindowsUserDisplayName = null);

/// <summary>
/// コンテナ末尾へ暗号化して格納するマニフェストです。
/// </summary>
public sealed record BackupManifest(
    int FormatVersion,
    Guid BackupId,
    DateTimeOffset CreatedUtc,
    string AppVersion,
    string EnvironmentFingerprint,
    IReadOnlyList<BackupProfileManifest> Profiles);

/// <summary>
/// 平文ヘッダーから公開できる最小限の情報です。
/// </summary>
public sealed record BackupContainerHeaderInfo(
    int FormatVersion,
    Guid BackupId,
    int Pbkdf2Iterations,
    int ChunkSizeBytes,
    Version MinimumAppVersion,
    BackupContainerCredentialKind CredentialKind =
        BackupContainerCredentialKind.LegacyPassphrase);

/// <summary>
/// 検証済み暗号化コンテナの情報です。
/// </summary>
public sealed record BackupContainerVerificationResult(
    BackupContainerHeaderInfo Header,
    BackupManifest Manifest,
    long EntryCount,
    long PlaintextBytes);

/// <summary>
/// 作成と再読み取り検証が完了したバックアップです。
/// </summary>
public sealed record BackupContainerWriteResult(
    string FinalPath,
    long ContainerBytes,
    BackupContainerVerificationResult Verification)
{
  public string? RecoveryKeyPath { get; init; }
}

/// <summary>
/// 安全な一時領域へ展開した検証済みファイルです。
/// </summary>
public sealed record BackupExtractedFile(
    string EntryPath,
    string AbsolutePath,
    long Length,
    string Sha256,
    string? Component);

/// <summary>
/// コンテナ全体の認証と一時展開が完了した結果です。
/// </summary>
public sealed record BackupContainerExtractionResult(
    BackupContainerVerificationResult Verification,
    IReadOnlyList<BackupExtractedFile> Files);
