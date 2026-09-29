using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Ytec.BrowserCapsule.Infrastructure.BackupContainers;

internal static class RecoveryKeyFileCodec
{
  public const int KeyLength = 32;
  public const int FileLength = 92;
  private const ushort CurrentVersion = 1;
  private static ReadOnlySpan<byte> Magic =>
      [0x59, 0x54, 0x45, 0x43, 0x4B, 0x45, 0x59, 0x00];

  public static byte[] Encode(Guid backupId, ReadOnlySpan<byte> key)
  {
    if (key.Length != KeyLength)
    {
      throw new ArgumentException(
          "復元キーは32バイトである必要があります。",
          nameof(key));
    }

    var bytes = new byte[FileLength];
    Magic.CopyTo(bytes);
    BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(8, 2), CurrentVersion);
    BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(10, 2), FileLength);
    if (!backupId.TryWriteBytes(
        bytes.AsSpan(12, 16),
        bigEndian: true,
        out var written)
        || written != 16)
    {
      throw new InvalidOperationException(
          "Backup IDを復元キーファイルへ書き込めません。");
    }

    key.CopyTo(bytes.AsSpan(28, KeyLength));
    SHA256.HashData(bytes.AsSpan(0, 60), bytes.AsSpan(60, 32));
    return bytes;
  }

  public static RecoveryKeyFile Decode(ReadOnlySpan<byte> bytes)
  {
    if (bytes.Length != FileLength
        || !bytes[..Magic.Length].SequenceEqual(Magic)
        || BinaryPrimitives.ReadUInt16LittleEndian(bytes[8..10])
            != CurrentVersion
        || BinaryPrimitives.ReadUInt16LittleEndian(bytes[10..12])
            != FileLength)
    {
      throw new CryptographicException(
          "復元キーファイルの形式が不正です。");
    }

    Span<byte> checksum = stackalloc byte[32];
    SHA256.HashData(bytes[..60], checksum);
    if (!CryptographicOperations.FixedTimeEquals(checksum, bytes[60..92]))
    {
      throw new CryptographicException(
          "復元キーファイルの完全性を確認できません。");
    }

    return new RecoveryKeyFile(
        new Guid(bytes[12..28], bigEndian: true),
        bytes[28..60].ToArray());
  }

  internal sealed record RecoveryKeyFile(Guid BackupId, byte[] Key);
}
