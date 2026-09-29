namespace Ytec.BrowserCapsule.Domain.BackupContainers;

/// <summary>
/// 暗号化バックアップの作成と完全性検証を提供します。
/// </summary>
public interface IBackupContainerService
{
  Task<BackupContainerHeaderInfo> ReadHeaderAsync(
      string containerPath,
      CancellationToken cancellationToken);

  Task<BackupContainerWriteResult> CreateAsync(
      BackupContainerRequest request,
      string recoveryKeyFilePath,
      CancellationToken cancellationToken);

  Task<BackupContainerWriteResult> CreateAsync(
      BackupContainerRequest request,
      ReadOnlyMemory<char> passphrase,
      CancellationToken cancellationToken);

  Task<BackupContainerVerificationResult> VerifyAsync(
      string containerPath,
      BackupContainerCredential credential,
      CancellationToken cancellationToken);

  Task<BackupContainerVerificationResult> VerifyAsync(
      string containerPath,
      ReadOnlyMemory<char> passphrase,
      CancellationToken cancellationToken);

  Task<BackupContainerExtractionResult> ExtractVerifiedAsync(
      string containerPath,
      BackupContainerCredential credential,
      string emptyDestinationDirectory,
      CancellationToken cancellationToken);

  Task<BackupContainerExtractionResult> ExtractVerifiedAsync(
      string containerPath,
      ReadOnlyMemory<char> passphrase,
      string emptyDestinationDirectory,
      CancellationToken cancellationToken);

  Task<BackupContainerExtractionResult> ExtractSelectedVerifiedAsync(
      string containerPath,
      BackupContainerCredential credential,
      string emptyDestinationDirectory,
      IReadOnlySet<string> selectedEntryPaths,
      CancellationToken cancellationToken);

  Task<BackupContainerExtractionResult> ExtractSelectedVerifiedAsync(
      string containerPath,
      ReadOnlyMemory<char> passphrase,
      string emptyDestinationDirectory,
      IReadOnlySet<string> selectedEntryPaths,
      CancellationToken cancellationToken);
}
