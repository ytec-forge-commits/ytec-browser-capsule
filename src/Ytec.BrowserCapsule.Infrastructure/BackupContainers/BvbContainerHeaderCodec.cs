using System.Buffers.Binary;
using System.Security.Cryptography;
using Ytec.BrowserCapsule.Domain.BackupContainers;

namespace Ytec.BrowserCapsule.Infrastructure.BackupContainers;

internal static class BvbContainerHeaderCodec
{
  public const ushort CurrentFormatVersion = 2;
  public const ushort LegacyFormatVersion = 1;
  public const int HeaderLength = 82;
  private const ushort Pbkdf2HmacSha256Identifier = 1;
  private const ushort RecoveryKeyFileIdentifier = 2;
  private const ushort Aes256GcmIdentifier = 1;

  private static ReadOnlySpan<byte> Magic =>
      [0x59, 0x54, 0x45, 0x43, 0x42, 0x56, 0x42, 0x00];

  public static byte[] CreateLegacyV1(
      Guid backupId,
      BvbContainerSecurityOptions options,
      out BvbContainerHeader header)
  {
    options.Validate();

    var salt = RandomNumberGenerator.GetBytes(32);
    var noncePrefix = RandomNumberGenerator.GetBytes(4);
    header = new BvbContainerHeader(
        LegacyFormatVersion,
        BackupContainerCredentialKind.LegacyPassphrase,
        salt,
        options.Pbkdf2Iterations,
        options.ChunkSizeBytes,
        noncePrefix,
        backupId,
        options.MinimumAppVersion);

    return Encode(header);
  }

  public static byte[] CreateV2(
      Guid backupId,
      ReadOnlySpan<byte> recoveryKeyIdentifier,
      BvbContainerSecurityOptions options,
      out BvbContainerHeader header)
  {
    options.Validate();
    if (recoveryKeyIdentifier.Length != 32)
    {
      throw new ArgumentException(
          "復元キー識別子は32バイトである必要があります。",
          nameof(recoveryKeyIdentifier));
    }

    var noncePrefix = RandomNumberGenerator.GetBytes(4);
    header = new BvbContainerHeader(
        CurrentFormatVersion,
        BackupContainerCredentialKind.RecoveryKeyFile,
        recoveryKeyIdentifier.ToArray(),
        Pbkdf2Iterations: 0,
        options.ChunkSizeBytes,
        noncePrefix,
        backupId,
        options.MinimumAppVersion);
    return Encode(header);
  }

  public static BvbContainerHeader Decode(ReadOnlySpan<byte> bytes)
  {
    if (bytes.Length != HeaderLength
        || !bytes[..Magic.Length].SequenceEqual(Magic))
    {
      throw new BackupContainerIntegrityException();
    }

    var formatVersion = BinaryPrimitives.ReadUInt16LittleEndian(
        bytes[8..10]);
    var headerLength = BinaryPrimitives.ReadUInt16LittleEndian(
        bytes[10..12]);
    var kdfIdentifier = BinaryPrimitives.ReadUInt16LittleEndian(
        bytes[12..14]);
    var cipherIdentifier = BinaryPrimitives.ReadUInt16LittleEndian(
        bytes[14..16]);
    var iterations = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
        bytes[48..52]));
    var chunkSize = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
        bytes[52..56]));

    var credentialKind = (formatVersion, kdfIdentifier) switch
    {
      (LegacyFormatVersion, Pbkdf2HmacSha256Identifier) =>
          BackupContainerCredentialKind.LegacyPassphrase,
      (CurrentFormatVersion, RecoveryKeyFileIdentifier) =>
          BackupContainerCredentialKind.RecoveryKeyFile,
      _ => throw new BackupContainerIntegrityException(),
    };

    if (headerLength != HeaderLength
        || cipherIdentifier != Aes256GcmIdentifier
        || (credentialKind == BackupContainerCredentialKind.LegacyPassphrase
            && iterations is
                < BvbContainerSecurityOptions.MinimumPbkdf2Iterations
                or > BvbContainerSecurityOptions.MaximumPbkdf2Iterations)
        || (credentialKind == BackupContainerCredentialKind.RecoveryKeyFile
            && iterations != 0)
        || chunkSize is < BvbContainerSecurityOptions.MinimumChunkSizeBytes
            or > BvbContainerSecurityOptions.MaximumChunkSizeBytes)
    {
      throw new BackupContainerIntegrityException();
    }

    var backupId = new Guid(bytes[60..76], bigEndian: true);
    var minimumVersion = new Version(
        BinaryPrimitives.ReadUInt16LittleEndian(bytes[76..78]),
        BinaryPrimitives.ReadUInt16LittleEndian(bytes[78..80]),
        BinaryPrimitives.ReadUInt16LittleEndian(bytes[80..82]));

    return new BvbContainerHeader(
        formatVersion,
        credentialKind,
        bytes[16..48].ToArray(),
        iterations,
        chunkSize,
        bytes[56..60].ToArray(),
        backupId,
        minimumVersion);
  }

  public static byte[] ComputeHash(ReadOnlySpan<byte> encodedHeader)
  {
    return SHA256.HashData(encodedHeader);
  }

  private static byte[] Encode(BvbContainerHeader header)
  {
    var bytes = new byte[HeaderLength];
    Magic.CopyTo(bytes);
    BinaryPrimitives.WriteUInt16LittleEndian(
        bytes.AsSpan(8, 2),
        header.FormatVersion);
    BinaryPrimitives.WriteUInt16LittleEndian(
        bytes.AsSpan(10, 2),
        HeaderLength);
    BinaryPrimitives.WriteUInt16LittleEndian(
        bytes.AsSpan(12, 2),
        header.CredentialKind switch
        {
          BackupContainerCredentialKind.LegacyPassphrase =>
              Pbkdf2HmacSha256Identifier,
          BackupContainerCredentialKind.RecoveryKeyFile =>
              RecoveryKeyFileIdentifier,
          _ => throw new InvalidOperationException(
              "未対応の資格情報方式です。"),
        });
    BinaryPrimitives.WriteUInt16LittleEndian(
        bytes.AsSpan(14, 2),
        Aes256GcmIdentifier);
    if (header.KeyDescriptor.Length != 32)
    {
      throw new InvalidOperationException(
          "キー記述子は32バイトである必要があります。");
    }

    header.KeyDescriptor.CopyTo(bytes, 16);
    BinaryPrimitives.WriteUInt32LittleEndian(
        bytes.AsSpan(48, 4),
        checked((uint)header.Pbkdf2Iterations));
    BinaryPrimitives.WriteUInt32LittleEndian(
        bytes.AsSpan(52, 4),
        checked((uint)header.ChunkSizeBytes));
    header.NoncePrefix.CopyTo(bytes, 56);
    if (!header.BackupId.TryWriteBytes(
        bytes.AsSpan(60, 16),
        bigEndian: true,
        out var guidBytesWritten)
        || guidBytesWritten != 16)
    {
      throw new InvalidOperationException(
          "Backup IDをヘッダーへ書き込めません。");
    }

    BinaryPrimitives.WriteUInt16LittleEndian(
        bytes.AsSpan(76, 2),
        checked((ushort)header.MinimumAppVersion.Major));
    BinaryPrimitives.WriteUInt16LittleEndian(
        bytes.AsSpan(78, 2),
        checked((ushort)header.MinimumAppVersion.Minor));
    BinaryPrimitives.WriteUInt16LittleEndian(
        bytes.AsSpan(80, 2),
        checked((ushort)Math.Max(0, header.MinimumAppVersion.Build)));
    return bytes;
  }
}
