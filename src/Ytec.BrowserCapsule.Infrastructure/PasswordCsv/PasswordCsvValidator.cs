using System.Text;
using Ytec.BrowserCapsule.Domain.PasswordCsv;

namespace Ytec.BrowserCapsule.Infrastructure.PasswordCsv;

/// <summary>
/// CSV本文を保持せず、RFC 4180相当の構造と必須ヘッダーだけを検証します。
/// </summary>
public sealed class PasswordCsvValidator
{
  private const long MaximumCsvBytes = 1024L * 1024 * 1024;
  private static readonly string[] RequiredHeaders =
      ["url", "username", "password"];
  private static readonly UTF8Encoding StrictUtf8 =
      new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

  public static async Task<PasswordCsvValidationResult> ValidateAsync(
      string csvPath,
      bool allowEmpty,
      CancellationToken cancellationToken)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(csvPath);
    var fullPath = Path.GetFullPath(csvPath);
    var fileInfo = new FileInfo(fullPath);
    fileInfo.Refresh();
    if (!fileInfo.Exists
        || fileInfo.Length > MaximumCsvBytes
        || (fileInfo.Attributes & FileAttributes.ReparsePoint) != 0)
    {
      throw new PasswordCsvValidationException(
          "CSVを安全に読み取れないか、サイズ上限を超えています。");
    }

    try
    {
      await using var input = new FileStream(
          fullPath,
          FileMode.Open,
          FileAccess.Read,
          FileShare.Read,
          bufferSize: 64 * 1024,
          FileOptions.Asynchronous | FileOptions.SequentialScan);
      using var reader = new StreamReader(
          input,
          StrictUtf8,
          detectEncodingFromByteOrderMarks: true,
          bufferSize: 64 * 1024,
          leaveOpen: false);
      var result = await ParseAsync(
          reader,
          fileInfo.Length,
          cancellationToken).ConfigureAwait(false);

      if (result.HeaderNames.Count == 0)
      {
        if (allowEmpty && fileInfo.Length == 0)
        {
          return result;
        }

        throw new PasswordCsvValidationException(
            "CSVが空です。空のCSVを保存する場合は明示的な確認が必要です。");
      }

      var headerSet = result.HeaderNames.ToHashSet(
          StringComparer.OrdinalIgnoreCase);
      if (RequiredHeaders.Any(required => !headerSet.Contains(required)))
      {
        throw new PasswordCsvValidationException(
            "公式パスワードCSVに必要な列を確認できません。");
      }

      if (result.RecordCount == 0 && !allowEmpty)
      {
        throw new PasswordCsvValidationException(
            "CSVに保存パスワードのレコードがありません。空のCSVを保存する場合は明示的な確認が必要です。");
      }

      return result;
    }
    catch (DecoderFallbackException exception)
    {
      throw new PasswordCsvValidationException(
          "CSVは有効なUTF-8ではありません。",
          exception);
    }
    catch (IOException exception)
    {
      throw new PasswordCsvValidationException(
          "CSVを読み取れません。",
          exception);
    }
  }

  private static async Task<PasswordCsvValidationResult> ParseAsync(
      TextReader reader,
      long fileBytes,
      CancellationToken cancellationToken)
  {
    var headerNames = new List<string>();
    var headerField = new StringBuilder();
    var inQuotes = false;
    var quotePending = false;
    var afterClosingQuote = false;
    var atFieldStart = true;
    var sawAnyCharacter = false;
    var currentRecordHasContent = false;
    var headerComplete = false;
    long recordCount = 0;
    var buffer = new char[4096];

    while (true)
    {
      cancellationToken.ThrowIfCancellationRequested();
      var read = await reader.ReadAsync(
          buffer.AsMemory(),
          cancellationToken).ConfigureAwait(false);
      if (read == 0)
      {
        break;
      }

      for (var index = 0; index < read; index++)
      {
        var character = buffer[index];
        sawAnyCharacter = true;

        if (quotePending)
        {
          if (character == '"')
          {
            if (!headerComplete)
            {
              headerField.Append('"');
            }

            quotePending = false;
            currentRecordHasContent = true;
            continue;
          }

          quotePending = false;
          inQuotes = false;
          afterClosingQuote = true;
        }

        if (inQuotes)
        {
          if (character == '"')
          {
            quotePending = true;
            continue;
          }

          if (!headerComplete)
          {
            headerField.Append(character);
          }

          currentRecordHasContent = true;
          continue;
        }

        if (afterClosingQuote
            && character is not ',' and not '\r' and not '\n')
        {
          throw new PasswordCsvValidationException(
              "CSVの引用符構造が不正です。");
        }

        switch (character)
        {
          case '"' when atFieldStart:
            inQuotes = true;
            atFieldStart = false;
            currentRecordHasContent = true;
            break;
          case '"':
            throw new PasswordCsvValidationException(
                "CSVの引用符構造が不正です。");
          case ',':
            if (!headerComplete)
            {
              headerNames.Add(headerField.ToString());
              headerField.Clear();
            }

            atFieldStart = true;
            afterClosingQuote = false;
            currentRecordHasContent = true;
            break;
          case '\r':
          case '\n':
            if (character == '\r')
            {
              var next = index + 1 < read ? buffer[index + 1] : '\0';
              if (next == '\n')
              {
                index++;
              }
            }

            CompleteRecord(
                headerNames,
                headerField,
                ref headerComplete,
                ref recordCount,
                currentRecordHasContent || !atFieldStart);
            atFieldStart = true;
            afterClosingQuote = false;
            currentRecordHasContent = false;
            break;
          default:
            if (!headerComplete)
            {
              headerField.Append(character);
            }

            atFieldStart = false;
            currentRecordHasContent = true;
            break;
        }
      }
    }

    if (inQuotes && !quotePending)
    {
      throw new PasswordCsvValidationException(
          "CSVの引用符が閉じられていません。");
    }

    if (sawAnyCharacter
        && (currentRecordHasContent || headerField.Length > 0))
    {
      CompleteRecord(
          headerNames,
          headerField,
          ref headerComplete,
          ref recordCount,
          hasContent: true);
    }

    return new PasswordCsvValidationResult(
        headerNames,
        recordCount,
        fileBytes);
  }

  private static void CompleteRecord(
      List<string> headerNames,
      StringBuilder headerField,
      ref bool headerComplete,
      ref long recordCount,
      bool hasContent)
  {
    if (!headerComplete)
    {
      if (!hasContent && headerField.Length == 0 && headerNames.Count == 0)
      {
        return;
      }

      headerNames.Add(headerField.ToString());
      headerField.Clear();
      headerComplete = true;
      return;
    }

    if (hasContent)
    {
      recordCount = checked(recordCount + 1);
    }
  }
}
