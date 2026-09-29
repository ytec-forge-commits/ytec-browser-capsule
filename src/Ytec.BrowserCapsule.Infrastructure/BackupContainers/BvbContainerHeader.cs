using Ytec.BrowserCapsule.Domain.BackupContainers;

namespace Ytec.BrowserCapsule.Infrastructure.BackupContainers;

internal sealed record BvbContainerHeader(
    ushort FormatVersion,
    BackupContainerCredentialKind CredentialKind,
    byte[] KeyDescriptor,
    int Pbkdf2Iterations,
    int ChunkSizeBytes,
    byte[] NoncePrefix,
    Guid BackupId,
    Version MinimumAppVersion)
{
  public BackupContainerHeaderInfo ToPublicInfo()
  {
    return new BackupContainerHeaderInfo(
        FormatVersion,
        BackupId,
        Pbkdf2Iterations,
        ChunkSizeBytes,
        MinimumAppVersion,
        CredentialKind);
  }
}
