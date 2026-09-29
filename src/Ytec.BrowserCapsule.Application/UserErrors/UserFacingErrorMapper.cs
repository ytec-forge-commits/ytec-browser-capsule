using Ytec.BrowserCapsule.Domain.BackupContainers;
using Ytec.BrowserCapsule.Domain.PasswordCsv;
using Ytec.BrowserCapsule.Domain.ProfileBackups;
using Ytec.BrowserCapsule.Domain.ProfileRestores;

namespace Ytec.BrowserCapsule.Application.UserErrors;

public enum UserOperation
{
  Backup = 0,
  Verification = 1,
  Restore = 2,
  PasswordCsv = 3,
}

/// <summary>
/// 技術例外や秘密を含み得るパスを画面へ出さないエラー表示です。
/// </summary>
public sealed record UserFacingError(
    string Code,
    string Message,
    string SuggestedAction);

/// <summary>
/// 例外を、安定したコードと日本語の対処方法へ変換します。
/// </summary>
public static class UserFacingErrorMapper
{
  public static UserFacingError Map(
      Exception exception,
      UserOperation operation)
  {
    ArgumentNullException.ThrowIfNull(exception);

    return exception switch
    {
      BrowserMustBeClosedException browser =>
          new UserFacingError(
              "YBC-PROC-001",
              browser.Message,
              "アプリの終了支援を使うか、対象ブラウザーを完全に終了して再試行してください。"),
      BackupContainerIntegrityException =>
          new UserFacingError(
              "YBC-CRYPTO-001",
              "復元キーまたは旧パスフレーズが違うか、バックアップが破損・改ざんされています。",
              "同じバックアップ用の復元キーか確認し、元の2ファイルをコピーし直してください。"),
      InvalidBackupEntryPathException =>
          new UserFacingError(
              "YBC-REST-001",
              "バックアップ内に安全でないファイルパスがあります。",
              "このバックアップは復元せず、信頼できる別のバックアップを使用してください。"),
      RestoreFailedRollbackFailedException =>
          new UserFacingError(
              "YBC-REST-004",
              "復元と自動ロールバックの両方を完了できませんでした。",
              "ブラウザーを起動せず、表示されたロールバックフォルダーを保持してください。"),
      RestoreFailedRolledBackException =>
          new UserFacingError(
              "YBC-REST-003",
              "復元を完了できなかったため、復元前の状態へ戻しました。",
              "ブラウザーを起動して元の状態を確認し、ロールバックを保持してください。"),
      RestorePlanException =>
          new UserFacingError(
              "YBC-REST-002",
              "選択した復元先へ安全に復元できません。",
              "復元元と同じブラウザーのプロファイルを選び直してください。"),
      PasswordCsvValidationException =>
          new UserFacingError(
              "YBC-CSV-002",
              "CSVの形式を安全に確認できません。",
              "ブラウザー公式画面からCSVをもう一度エクスポートしてください。"),
      BackupPlanException =>
          new UserFacingError(
              "YBC-BACK-001",
              "プロファイルのバックアップ内容を安全に固定できません。",
              "ブラウザーを終了し、プロファイルを再検出してから試してください。"),
      BackupContainerException =>
          new UserFacingError(
              "YBC-CRYPTO-003",
              "このバックアップ形式を安全に処理できません。",
              "対応バージョンで作成したバックアップか確認してください。"),
      UnauthorizedAccessException =>
          new UserFacingError(
              "YBC-IO-002",
              "ファイルまたはフォルダーへアクセスできません。",
              "書き込み可能なローカルフォルダーを選び直してください。"),
      IOException ioException when ioException.Message.Contains(
          "空き容量",
          StringComparison.Ordinal) =>
          new UserFacingError(
              "YBC-IO-001",
              "保存先の空き容量が不足しています。",
              "空き容量を増やすか、別のローカルドライブを選んでください。"),
      IOException =>
          new UserFacingError(
              "YBC-IO-003",
              "ファイルの読み書きを完了できませんでした。",
              "保存先の接続と空き容量を確認し、もう一度お試しください。"),
      _ => CreateUnexpected(operation),
    };
  }

  private static UserFacingError CreateUnexpected(
      UserOperation operation)
  {
    var operationText = operation switch
    {
      UserOperation.Backup => "バックアップ",
      UserOperation.Verification => "検証",
      UserOperation.Restore => "復元",
      UserOperation.PasswordCsv => "CSVの処理",
      _ => "処理",
    };
    return new UserFacingError(
        "YBC-UNEXPECTED-001",
        $"{operationText}中に予期しない問題が発生しました。",
        "ブラウザーと本アプリを終了し、もう一度お試しください。");
  }
}
