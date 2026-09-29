using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using Ytec.BrowserCapsule.Domain.Settings;

namespace Ytec.BrowserCapsule.App.Localization;

/// <summary>
/// 日本語を原文として、画面上の文字列を選択言語へ変換します。
/// </summary>
internal static partial class UiText
{
  private static readonly ReadOnlyDictionary<string, string>
      EnglishTranslations = CreateEnglishTranslations();
  private static readonly KeyValuePair<string, string>[]
      EnglishReplacementSegments = EnglishTranslations
          .Where(pair => ContainsJapanese(pair.Key))
          .OrderByDescending(pair => pair.Key.Length)
          .ToArray();
  private static readonly ConditionalWeakTable<DependencyObject,
      HashSet<DependencyProperty>> WatchedProperties = new();
  private static readonly ConditionalWeakTable<Window, object>
      WindowLoadedHooks = new();
  private static bool _classHandlersRegistered;

  public static bool IsEnglish { get; private set; }

  public static string ManualFileName => IsEnglish
      ? "Operation Manual.pdf"
      : "操作マニュアル.pdf";

  public static void Initialize(AppLanguage language)
  {
    var selectedCulture = ResolveCulture(language);
    CultureInfo.CurrentUICulture = selectedCulture;
    IsEnglish = selectedCulture.TwoLetterISOLanguageName.Equals(
        "en",
        StringComparison.OrdinalIgnoreCase);

    if (!_classHandlersRegistered)
    {
      EventManager.RegisterClassHandler(
          typeof(FrameworkElement),
          FrameworkElement.LoadedEvent,
          new RoutedEventHandler(OnElementLoaded),
          handledEventsToo: true);
      EventManager.RegisterClassHandler(
          typeof(FrameworkContentElement),
          FrameworkContentElement.LoadedEvent,
          new RoutedEventHandler(OnElementLoaded),
          handledEventsToo: true);
      _classHandlersRegistered = true;
    }
  }

  public static string T(string source)
  {
    if (!IsEnglish || string.IsNullOrEmpty(source))
    {
      return source;
    }

    if (EnglishTranslations.TryGetValue(source, out var exact))
    {
      return exact;
    }

    var dynamicTranslation = TranslateDynamicText(source);
    if (dynamicTranslation is not null)
    {
      return dynamicTranslation;
    }

    var translated = source;
    foreach (var segment in EnglishReplacementSegments)
    {
      translated = translated.Replace(
          segment.Key,
          segment.Value,
          StringComparison.Ordinal);
    }

    return translated;
  }

  public static string F(
      string sourceFormat,
      params object?[] arguments)
  {
    return string.Format(
        CultureInfo.CurrentCulture,
        T(sourceFormat),
        arguments);
  }

  public static void Apply(Window window)
  {
    if (!IsEnglish)
    {
      return;
    }

    TranslateTree(window);
    if (WindowLoadedHooks.TryGetValue(window, out _))
    {
      return;
    }

    WindowLoadedHooks.Add(window, new object());
    window.Loaded += (_, _) => TranslateTree(window);
  }

  private static CultureInfo ResolveCulture(AppLanguage language)
  {
    return language switch
    {
      AppLanguage.Japanese => CultureInfo.GetCultureInfo("ja-JP"),
      AppLanguage.English => CultureInfo.GetCultureInfo("en-US"),
      _ when CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals(
          "ja",
          StringComparison.OrdinalIgnoreCase) =>
          CultureInfo.GetCultureInfo("ja-JP"),
      _ => CultureInfo.GetCultureInfo("en-US"),
    };
  }

  private static void OnElementLoaded(
      object sender,
      RoutedEventArgs e)
  {
    if (!IsEnglish || sender is not DependencyObject element)
    {
      return;
    }

    switch (element)
    {
      case Window:
        TranslateAndWatch(element, Window.TitleProperty);
        break;
      case TextBlock:
        TranslateAndWatch(element, TextBlock.TextProperty);
        break;
      case HeaderedContentControl:
        TranslateAndWatch(element, HeaderedContentControl.HeaderProperty);
        break;
      case ContentControl:
        TranslateAndWatch(element, ContentControl.ContentProperty);
        break;
      case Run:
        TranslateAndWatch(element, Run.TextProperty);
        break;
    }

    TranslateAndWatch(element, AutomationProperties.NameProperty);
    TranslateAndWatch(element, AutomationProperties.HelpTextProperty);
    TranslateAndWatch(element, ToolTipService.ToolTipProperty);
  }

  private static void TranslateTree(DependencyObject root)
  {
    var pending = new Stack<DependencyObject>();
    var visited = new HashSet<DependencyObject>(
        ReferenceEqualityComparer.Instance);
    pending.Push(root);

    while (pending.Count > 0)
    {
      var current = pending.Pop();
      if (!visited.Add(current))
      {
        continue;
      }

      OnElementLoaded(current, new RoutedEventArgs());
      foreach (var child in LogicalTreeHelper.GetChildren(current))
      {
        if (child is DependencyObject dependencyChild)
        {
          pending.Push(dependencyChild);
        }
      }

      if (current is not Visual && current is not System.Windows.Media.Media3D.Visual3D)
      {
        continue;
      }

      var childCount = VisualTreeHelper.GetChildrenCount(current);
      for (var index = 0; index < childCount; index++)
      {
        pending.Push(VisualTreeHelper.GetChild(current, index));
      }
    }
  }

  private static void TranslateAndWatch(
      DependencyObject element,
      DependencyProperty property)
  {
    if (BindingOperations.IsDataBound(element, property))
    {
      return;
    }

    TranslateProperty(element, property);

    var properties = WatchedProperties.GetOrCreateValue(element);
    if (!properties.Add(property))
    {
      return;
    }

    var descriptor = DependencyPropertyDescriptor.FromProperty(
        property,
        element.GetType());
    descriptor?.AddValueChanged(
        element,
        (_, _) => TranslateProperty(element, property));
  }

  private static void TranslateProperty(
      DependencyObject element,
      DependencyProperty property)
  {
    if (element.GetValue(property) is not string source
        || !ContainsJapanese(source))
    {
      return;
    }

    var translated = T(source);
    if (!string.Equals(source, translated, StringComparison.Ordinal))
    {
      element.SetCurrentValue(property, translated);
    }
  }

  private static string? TranslateDynamicText(string source)
  {
    Match match;
    if ((match = SelectedSummaryRegex().Match(source)).Success)
    {
      return $"{match.Groups[1].Value} selected · "
          + $"Approx. {match.Groups[2].Value} total";
    }

    if ((match = BrowserProfileCountRegex().Match(source)).Success)
    {
      return $"Found {match.Groups[1].Value} browsers and "
          + $"{match.Groups[2].Value} profiles";
    }

    if ((match = ProgressWithPercentRegex().Match(source)).Success)
    {
      return $"{match.Groups[1].Value} / {match.Groups[2].Value} files · "
          + $"{match.Groups[3].Value}%";
    }

    if ((match = ProgressFilesRegex().Match(source)).Success)
    {
      return $"{match.Groups[1].Value} / {match.Groups[2].Value} files";
    }

    if ((match = WizardImportProgressRegex().Match(source)).Success)
    {
      return $"{match.Groups[1].Value} / {match.Groups[2].Value} — "
          + "Import one profile at a time using the browser's official UI.";
    }

    if ((match = WizardExportProgressRegex().Match(source)).Success)
    {
      return $"{match.Groups[1].Value} / {match.Groups[2].Value} — "
          + "Continue one profile at a time to avoid mixing up CSV files.";
    }

    if ((match = CsvRecordCountRegex().Match(source)).Success)
    {
      return $"CSV records: {match.Groups[1].Value} "
          + "(values are not displayed)";
    }

    if ((match = WindowsUsersReadRegex().Match(source)).Success)
    {
      return $"Checked {match.Groups[1].Value} readable Windows user areas.";
    }

    if ((match = WindowsUsersSkippedRegex().Match(source)).Success)
    {
      return $"Skipped {match.Groups[1].Value} without changing permissions.";
    }

    if ((match = BrowserRunningCountRegex().Match(source)).Success)
    {
      return $"{match.Groups[1].Value} browser types are running.";
    }

    if ((match = SafelySkippedCountRegex().Match(source)).Success)
    {
      return $"Safely skipped {match.Groups[1].Value}.";
    }

    if ((match = FileCountRegex().Match(source)).Success)
    {
      return $"{match.Groups[1].Value} files";
    }

    if ((match = VersionRegex().Match(source)).Success)
    {
      return $"Version {match.Groups[1].Value}";
    }

    if ((match = BrowserCloseRegex().Match(source)).Success)
    {
      return $"Close {match.Groups[1].Value}, then try again.";
    }

    return null;
  }

  private static bool ContainsJapanese(string value)
  {
    return JapaneseTextRegex().IsMatch(value);
  }

  private static ReadOnlyDictionary<string, string>
      CreateEnglishTranslations()
  {
    return new ReadOnlyDictionary<string, string>(
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
          ["↻  再検出"] = "↻  Re-scan",
          ["0件を選択"] = "0 profiles selected",
          ["3つのブラウザーを、かんたんに別PCへ"] =
              "Move three browsers to another PC with ease",
          ["3つのブラウザーを、ひとつのカプセルへ"] =
              "Three browsers, one secure capsule",
          ["Chrome、Edge、Firefoxを一度起動してプロファイルを作成したあと、「再検出」を押してください。"] =
              "Start Chrome, Edge, or Firefox once to create a profile, then select Re-scan.",
          ["Chrome、Microsoft Edge、Firefoxの複数プロファイルを、専用の復元キーファイルで守る暗号化バックアップへまとめます。"] =
              "Combine multiple Chrome, Microsoft Edge, and Firefox profiles into an encrypted backup protected by a dedicated recovery-key file.",
          ["Cookie、セッション、パスキーは保証せず、復元後の再ログインを前提にします。"] =
              "Cookies, sessions, and passkeys are not guaranteed; expect to sign in again after restoring.",
          ["Cookie／サイトデータ"] = "Cookies / site data",
          ["Cookie／サイトデータ（別PCでは復元保証外）"] =
              "Cookies / site data (not guaranteed on another PC)",
          ["CSVの一時保存先"] = "Temporary CSV location",
          ["CSVの取り込み"] = "Import CSV",
          ["CSVの場所を開く"] = "Open CSV location",
          ["CSVの保存先"] = "CSV save location",
          ["CSVを保存したら取り込みを開始してください。"] =
              "After saving the CSV, start the import.",
          ["CSV検出の進み具合"] = "CSV detection progress",
          ["CSV検出を再試行"] = "Retry CSV detection",
          ["Y-TEC Browser Capsuleについて"] = "About Y-TEC Browser Capsule",
          ["Y-TEC Browser Capsuleの設定"] = "Y-TEC Browser Capsule settings",
          ["インポートする一時CSVのパス"] = "Temporary CSV path to import",
          ["インポート完了・削除"] = "Done - delete CSV",
          ["インポート後に下の完了ボタンを押すと、一時CSVを削除します。"] =
              "After importing, select the completion button below to delete the temporary CSV.",
          ["インポート後に完了ボタンを押すと、一時CSVを削除します。"] =
              "After importing, select the completion button to delete the temporary CSV.",
          ["オフライン動作"] = "Works offline",
          ["キャッシュを除外"] = "Exclude caches",
          ["キャンセル"] = "Cancel",
          ["このアプリについて"] = "About this app",
          ["このバックアップにない項目は選べません。"] =
              "Items not present in this backup cannot be selected.",
          ["このバックアップは別のPCまたはWindowsユーザーで作成された可能性があります。"] =
              "This backup may have been created on another PC or by another Windows user.",
          ["このバックアップ元を復元する"] = "Restore this backup source",
          ["このプロファイルはスキップ"] = "Skip this profile",
          ["この画面を閉じる"] = "Close this window",
          ["これは旧BVB v1バックアップです。作成時に設定したパスフレーズを入力してください。パスフレーズは保存しません。"] =
              "This is a legacy BVB v1 backup. Enter the passphrase used when it was created. The passphrase is not saved.",
          ["すべて選択"] = "Select all",
          ["スキップしてCSVを削除"] = "Skip and delete CSV",
          ["セッション"] = "Sessions",
          ["セッション情報（通常は選択不要）"] =
              "Session data (normally unnecessary)",
          ["テレメトリーなし"] = "No telemetry",
          ["バージョンを確認しています"] = "Checking version",
          ["パスフレーズ"] = "Passphrase",
          ["バックアップから復元"] = "Restore from backup",
          ["バックアップのパスフレーズ"] = "Backup passphrase",
          ["バックアップの準備"] = "Prepare backup",
          ["バックアップまたは復元の進み具合"] = "Backup or restore progress",
          ["バックアップを開く"] = "Open backup",
          ["バックアップを検証"] = "Verify backup",
          ["バックアップを作成"] = "Create backup",
          ["バックアップ元"] = "Backup source",
          ["バックアップ先と、安全のため固定している動作を確認します。"] =
              "Review the backup location and the safety behavior that is always enforced.",
          ["フォルダーパスをコピー"] = "Copy folder path",
          ["ブックマーク"] = "Bookmarks",
          ["ブックマーク／お気に入り"] = "Bookmarks / favorites",
          ["ブラウザーの保護は回避せず、標準画面から保存したCSVだけを短時間取り込みます。"] =
              "Browser protection is never bypassed. Only a CSV saved from the browser's official UI is handled briefly.",
          ["ブラウザーの保存パスワードを直接復号しません。各ブラウザーの公式画面で、ご本人がCSVのエクスポート／インポートを行います。"] =
              "The app never decrypts saved browser passwords. You export and import CSV files yourself through each browser's official UI.",
          ["ブラウザーを確認しています…"] = "Checking browsers…",
          ["ブラウザーを再検出"] = "Re-scan browsers",
          ["ブラウザー設定"] = "Browser settings",
          ["フルパスや保存内容はこの画面に表示しません。"] =
              "Full paths and saved content are not shown on this screen.",
          ["プロファイルが見つかりませんでした"] = "No profiles found",
          ["プロファイルと容量を確認しています…"] =
              "Checking profiles and sizes…",
          ["プロファイルのフルパスを画面に表示しない"] =
              "Do not show full profile paths on screen",
          ["プロファイルの対応付け"] = "Profile mapping",
          ["プロファイルの内容は変更しません。"] =
              "Profile contents will not be changed.",
          ["プロファイルを選ぶだけ。復元キーもアプリが自動で作ります。"] =
              "Select profiles and the app creates the recovery key automatically.",
          ["プロファイル検出の進み具合"] = "Profile discovery progress",
          ["プロファイル全体（保存パスワードCSVは別選択）"] =
              "Entire profile (saved-password CSV selected separately)",
          ["ライセンスと表示"] = "License and notices",
          ["ロールバック後に復元"] = "Back up current state, then restore",
          ["安全に開く"] = "Open safely",
          ["安全のため固定している動作"] = "Always-on safety behavior",
          ["暗号化バックアップを開く"] = "Open encrypted backup",
          ["画面付きPDFマニュアルを開く"] = "Open illustrated PDF manual",
          ["画面付きの操作マニュアルを既定のPDF閲覧アプリで開きます。"] =
              "Open the illustrated user manual in your default PDF viewer.",
          ["拡張機能"] = "Extensions",
          ["拡張機能と設定"] = "Extensions and settings",
          ["環境IDの不一致と端末依存データの非保証を理解しました"] =
              "I understand the environment ID mismatch and that device-bound data is not guaranteed",
          ["管理者権限を使わず、現在のユーザーから読み取れるブラウザープロファイルだけを追加します。"] =
              "Without administrator privileges, include only browser profiles readable by the current user.",
          ["既定"] = "Default",
          ["既定のバックアップ先"] = "Default backup location",
          ["既定のバックアップ先を選ぶ"] = "Choose default backup location",
          ["既定のバックアップ先を未指定に戻す"] =
              "Clear default backup location",
          ["古いCSV一時領域を起動時に清掃"] =
              "Clean old temporary CSV areas at startup",
          ["作成後に完全検証"] = "Fully verify after creation",
          ["次へ"] = "Next",
          ["実行中"] = "Running",
          ["実行中のブラウザーは、確認後にアプリから安全に終了できます。"] =
              "After confirmation, the app can safely close running browsers.",
          ["準備しています…"] = "Preparing…",
          ["詳細: プロファイル全体（保存パスワードCSVは別選択）"] =
              "Details: Entire profile (saved-password CSV selected separately)",
          ["推定検出"] = "Estimated",
          ["設定"] = "Settings",
          ["設定を保存"] = "Save settings",
          ["設定を保存せず閉じる"] = "Close without saving",
          ["選ぶ"] = "Choose",
          ["選択を解除"] = "Clear selection",
          ["操作マニュアル"] = "User manual",
          ["中止"] = "Stop",
          ["同一環境として復元"] = "Restore as the same environment",
          ["同名でも自動確定しません。ブラウザーごとに、復元先を明示的に選んでください。"] =
              "Matching names are not accepted automatically. Explicitly choose a destination for each browser.",
          ["読み取り可能なほかのWindowsユーザーも表示"] =
              "Show other readable Windows users",
          ["標準のパスワード管理画面で「CSVからインポート」を選び、下の一時CSVを指定してください。本アプリはインポート成否を推測しません。"] =
              "In the browser's password manager, choose Import from CSV and select the temporary CSV below. The app does not guess whether import succeeded.",
          ["標準画面を開いて待機"] = "Open official UI and wait",
          ["標準画面を開く"] = "Open official UI",
          ["標準画面を開くと自動で待機します。書き込み完了を確認し、形式検証後すぐ暗号化して平文削除を試みます。検出されない場合だけ下の再試行を使ってください。"] =
              "After opening the official UI, the app waits automatically. It confirms writing is complete, validates the format, encrypts the CSV immediately, and tries to delete the plaintext. Use Retry only if the CSV is not detected.",
          ["復元キーはバックアップを開くための秘密情報です。紛失すると復元できません。盗難対策を高める場合は、完成後にバックアップとは別のUSBメモリ等へ保管してください。"] =
              "The recovery key is secret information required to open the backup. If it is lost, the backup cannot be restored. For stronger theft protection, store it separately from the backup, such as on another USB drive.",
          ["復元キーファイル方式"] = "Recovery-key file",
          ["復元キーも必ず保管することを確認しました"] =
              "I understand that the recovery key must also be kept",
          ["復元する内容（選択した全プロファイルに適用）"] =
              "Content to restore (applies to every selected profile)",
          ["復元モード"] = "Restore mode",
          ["復元元と復元先を確認"] = "Review source and destination",
          ["復元先の対応付け"] = "Map restore destinations",
          ["復元先プロファイル"] = "Destination profile",
          ["文字入力は不要です。アプリがバックアップ専用の「.ybckey」を自動生成します。別PCではバックアップと復元キーの両方を選ぶだけで復元できます。"] =
              "No typing is required. The app automatically creates a dedicated .ybckey file. On another PC, select both the backup and its recovery key to restore.",
          ["閉じる"] = "Close",
          ["別環境移行（安全な既定値）"] =
              "Move to another environment (safe default)",
          ["保存"] = "Save",
          ["保存する内容"] = "Content to save",
          ["保存する内容を選びます。バックアップと復元キーファイルは次の手順で自動作成します。"] =
              "Choose what to save. The encrypted backup and recovery-key file are created automatically in the next step.",
          ["保存パスワードCSV（公式画面でエクスポート）"] =
              "Saved-password CSV (export through official UI)",
          ["保存パスワードCSV（公式取込）"] =
              "Saved-password CSV (official import)",
          ["保存パスワードCSVアシスタント"] =
              "Saved-password CSV assistant",
          ["保存パスワードCSVを安全に追加"] =
              "Add a saved-password CSV safely",
          ["保存パスワードの安全な扱い"] =
              "Safe handling of saved passwords",
          ["保存パスワードの公式インポート"] =
              "Official saved-password import",
          ["保存パスワードを公式画面から取り込む"] =
              "Import saved passwords through the official UI",
          ["保存先パスをコピー"] = "Copy save path",
          ["保存先フォルダーを開く"] = "Open save folder",
          ["本アプリのソースコードはApache License 2.0です。配布フォルダーのLICENSE、NOTICE、THIRD-PARTY-NOTICES.mdに、利用条件と表示を収録しています。"] =
              "This app's source code is available under the Apache License 2.0. LICENSE, NOTICE, and THIRD-PARTY-NOTICES.md in the distribution contain the terms and notices.",
          ["本アプリは保存パスワードを直接読みません。開いたブラウザー標準画面でCSVをエクスポートし、下の専用フォルダーへ保存してください。"] =
              "The app never reads saved passwords directly. Export a CSV through the browser's official UI and save it in the dedicated folder below.",
          ["未指定に戻す"] = "Clear",
          ["未指定の場合は、保存時に前回の場所またはWindowsの既定場所を表示します。ネットワーク先は設定できません。"] =
              "If unset, saving starts at the previous or Windows default location. Network locations cannot be configured.",
          ["履歴"] = "History",
          ["表示言語"] = "Display language",
          ["Windowsの表示言語（推奨）"] =
              "Use Windows display language (recommended)",
          ["日本語"] = "Japanese",
          ["言語の変更はアプリを再起動すると反映されます。"] =
              "Language changes take effect after restarting the app.",
          ["表示言語は、アプリを再起動すると切り替わります。"] =
              "The display language will change after you restart the app.",
          ["再起動後に言語を変更します"] =
              "Language will change after restart",
          ["内部名: {0}"] = "Internal name: {0}",
          ["{0}件"] = "{0} profiles",
          ["{0}ファイル"] = "{0} files",
          ["{0:N0}ファイル"] = "{0:N0} files",
          ["バージョン {0}"] = "Version {0}",

          ["CSVに保存パスワードがありません。空のCSVとしてバックアップへ含めますか？"] =
              "The CSV contains no saved passwords. Include it in the backup as an empty CSV?",
          ["CSVの書き込み完了を確認しています。保存後、最大30秒待ちます…"] =
              "Checking that the CSV has finished writing. Waiting up to 30 seconds after it is saved…",
          ["CSVの待機を中止しました。"] = "Stopped waiting for the CSV.",
          ["CSVの保存を待っています。ブラウザー標準画面でエクスポートし、表示された専用フォルダーへ保存してください。"] =
              "Waiting for the CSV. Export it through the browser's official UI and save it in the dedicated folder shown.",
          ["CSVを確認できませんでした。専用フォルダー直下へ保存してから再試行してください。"] =
              "The CSV could not be confirmed. Save it directly in the dedicated folder, then retry.",
          ["CSVを自動検出しています。保存後、そのままお待ちください…"] =
              "Detecting the CSV automatically. After saving, please wait…",
          ["CSVを保存したら取り込みを開始してください。CSVの値は画面やログへ表示しません。"] =
              "After saving the CSV, start the import. CSV values are never shown on screen or written to logs.",
          ["PDFを開けるアプリが設定されているか確認してください。"] =
              "Check that an app for opening PDF files is configured.",
          ["Y-TEC Browser Capsule バックアップ (*.bvb)|*.bvb"] =
              "Y-TEC Browser Capsule backup (*.bvb)|*.bvb",
          ["Y-TEC Browser Capsule 復元キー (*.ybckey)|*.ybckey"] =
              "Y-TEC Browser Capsule recovery key (*.ybckey)|*.ybckey",
          ["Y-TEC Browser Capsuleはすでに開いています。"] =
              "Y-TEC Browser Capsule is already running.",
          ["この時点ではブラウザープロファイルを変更しません。"] =
              "Browser profiles are not changed at this stage.",
          ["この場所へ作成しますか？"] = "Create the files here?",
          ["しばらく待ってから再検出してください。ブラウザーの内容は変更されていません。"] =
              "Wait a moment, then re-scan. Browser contents have not been changed.",
          ["すでに起動しています"] = "Already running",
          ["スクリーンショットモードではCSVを暗号化しません。"] =
              "CSV files are not encrypted in screenshot mode.",
          ["パスフレーズを入力してください。"] = "Enter the passphrase.",
          ["バックアップが完成しました"] = "Backup complete",
          ["バックアップと復元キーが同じフォルダーです。\n\n"] =
              "The backup and recovery key are in the same folder.\n\n",
          ["バックアップは正常です"] = "The backup is valid",
          ["バックアップは変更していません。"] =
              "The backup has not been changed.",
          ["バックアップへ"] = "Add to backup",
          ["バックアップへ含める内容がありません。"] =
              "There is no content to include in the backup.",
          ["バックアップを開けません"] = "Cannot open backup",
          ["バックアップを開始できません"] = "Cannot start backup",
          ["バックアップを確認できません"] = "Cannot verify backup",
          ["バックアップを完全検証しています…"] =
              "Fully verifying the backup…",
          ["バックアップを検証しています…"] = "Verifying the backup…",
          ["バックアップを作成できませんでした"] =
              "The backup could not be created",
          ["バックアップを中止しました"] = "Backup canceled",
          ["バックアップ完了"] = "Backup complete",
          ["バックアップ失敗"] = "Backup failed",
          ["バックアップ先は保存時に選択します。"] =
              "Choose the backup location when saving.",
          ["バックアップ専用の復元キーを安全な乱数から生成します。"] =
              "A dedicated recovery key is generated using cryptographically secure random data.",
          ["バックアップ内容を確認しています…"] =
              "Reviewing backup contents…",
          ["ブラウザープロファイルは変更されていません。"] =
              "Browser profiles have not been changed.",
          ["ブラウザーを開けません"] = "Cannot open browser",
          ["ブラウザーを確認できませんでした"] =
              "Browsers could not be checked",
          ["ブラウザーを起動せず、そのままお待ちください。"] =
              "Do not start the browser; please wait.",
          ["ブラウザーを終了してください"] = "Close the browser",
          ["ブラウザーを終了しますか？"] = "Close the browser?",
          ["プロファイルへ直接書き込まず、CSV移行手順を完了しました。"] =
              "The CSV migration steps were completed without writing directly to the profile.",
          ["プロファイルを復元しています…"] = "Restoring profiles…",
          ["ほかのWindowsユーザーへの保存パスワードCSV取込は、このログイン状態では実行しません。対象ユーザーでサインインして公式インポートを行ってください。"] =
              "Saved-password CSV files for other Windows users are not imported in this sign-in session. Sign in as the target user and use the browser's official import.",
          ["ロールバックと復元キーも別々の場所へ保管してください。\n\n"] =
              "Store the rollback backup and its recovery key separately as well.\n\n",
          ["安全な一時領域へ検証展開しています…"] =
              "Extracting to a safe temporary area for verification…",
          ["安全に中止しています。少しお待ちください。"] =
              "Stopping safely. Please wait.",
          ["暗号タグ、ファイルハッシュ、マニフェストを再検証しています。"] =
              "Rechecking authentication tags, file hashes, and the manifest.",
          ["暗号タグ、ファイルハッシュ、マニフェストを最後まで確認します。"] =
              "Checking authentication tags, file hashes, and the manifest from start to finish.",
          ["暗号化バックアップと復元キーを作成しました。\n\n"] =
              "Created the encrypted backup and recovery key.\n\n",
          ["暗号化バックアップの完全性を確認しました。\n\n"] =
              "Verified the integrity of the encrypted backup.\n\n",
          ["暗号化バックアップの保存先"] = "Encrypted backup location",
          ["暗号化バックアップを作成しています…"] =
              "Creating encrypted backup…",
          ["暗号設定をこのPC向けに調整しています…"] =
              "Tuning encryption settings for this PC…",
          ["一時CSVがあるフォルダーのパスをコピーしました。"] =
              "Copied the path of the folder containing the temporary CSV.",
          ["完成ファイルは作成していません。途中ファイルの削除を試みました。"] =
              "No completed file was created. The app attempted to delete partial files.",
          ["完成前の安全確認をしています…"] =
              "Running final safety checks…",
          ["環境ID不一致の注意を確認してください。"] =
              "Acknowledge the environment ID mismatch warning.",
          ["既定のバックアップ先を選択"] =
              "Choose default backup location",
          ["緊急停止"] = "Emergency stop",
          ["空のCSV"] = "Empty CSV",
          ["警告はありません。"] = "There are no warnings.",
          ["検出結果は読み取り専用です。"] =
              "Discovery results are read-only.",
          ["検証するバックアップ"] = "Backup to verify",
          ["検証に使う復元キー"] = "Recovery key for verification",
          ["検証を中止しました"] = "Verification canceled",
          ["検証完了"] = "Verification complete",
          ["検証失敗"] = "Verification failed",
          ["元のプロファイルは変更しません。"] =
              "Original profiles will not be changed.",
          ["公式の配布ZIPを空のフォルダーへすべて展開し直してください。"] =
              "Extract the complete official distribution ZIP again into an empty folder.",
          ["最終確認"] = "Final confirmation",
          ["残っているブラウザーを強制終了しますか？"] =
              "Force-close the remaining browser processes?",
          ["次回から指定したローカルフォルダーを最初に表示します。"] =
              "The selected local folder will be shown first next time.",
          ["処理しています…"] = "Working…",
          ["書き込み開始後の場合は自動ロールバックを試みています。"] =
              "If writing had started, the app is attempting an automatic rollback.",
          ["推定容量"] = "Estimated size",
          ["設定の保存に失敗しました"] = "Failed to save settings",
          ["設定ファイルの読み取りと容量見積もりだけを行います。"] =
              "Only settings files are read and sizes are estimated.",
          ["設定を読み込めなかったため、既定値で起動しました。"] =
              "Settings could not be loaded, so defaults were used.",
          ["設定を保存しました"] = "Settings saved",
          ["設定を保存できませんでした。フォルダーの書き込み権限を確認してください。"] =
              "Settings could not be saved. Check write permission for the settings folder.",
          ["選択したすべてのバックアップ元に復元先を指定してください。"] =
              "Choose a restore destination for every selected backup source.",
          ["前回のCSV一時領域を清掃できませんでした。CSV補助を使う前に再起動してください。"] =
              "The previous temporary CSV area could not be cleaned. Restart before using CSV assistance.",
          ["操作マニュアルが見つかりません。"] = "The user manual was not found.",
          ["操作マニュアルを開けません"] = "Cannot open user manual",
          ["操作マニュアルを開けませんでした。"] =
              "The user manual could not be opened.",
          ["操作マニュアル用の合成プロファイルを表示しています。実データは読み取っていません。"] =
              "Showing synthetic profiles for manual screenshots. No real data was read.",
          ["存在するローカルフォルダーを選んでください。"] =
              "Choose an existing local folder.",
          ["対象のプロファイルは0件です"] = "There are no target profiles",
          ["対象ブラウザーが実行中です。\n\n"] =
              "A target browser is running.\n\n",
          ["対象ブラウザーが実行中です。ブラウザーの内容は変更していません。"] =
              "A target browser is running. Browser contents have not been changed.",
          ["対象ブラウザーが実行中です。プロファイルは変更していません。"] =
              "A target browser is running. Profiles have not been changed.",
          ["対象ブラウザーが終了していることを確認しました。復元前の状態を暗号化ロールバックへ保存してから復元します。"] =
              "The target browser is closed. The app will save the current state to an encrypted rollback backup before restoring.",
          ["対象ブラウザーだけを強制終了します。"] =
              "Only the target browser processes will be force-closed.",
          ["通常終了後もバックグラウンドプロセスが残っています。\n\n"] =
              "Background processes remain after a normal close.\n\n",
          ["読み取れない項目を除く推定容量"] =
              "Estimated size excluding unreadable items",
          ["復元が完了しました"] = "Restore complete",
          ["復元が完了しました。ブラウザーを起動して内容を確認してください。\n\n"] =
              "Restore is complete. Start the browser and verify the result.\n\n",
          ["復元キーの保管場所"] = "Recovery-key location",
          ["復元キーファイルの保存先"] = "Recovery-key file location",
          ["復元するバックアップ"] = "Backup to restore",
          ["復元するプロファイルを1件以上選んでください。"] =
              "Select at least one profile to restore.",
          ["復元する内容を1つ以上選んでください。"] =
              "Select at least one item to restore.",
          ["復元できませんでした"] = "Restore could not be completed",
          ["復元とロールバックに失敗しました"] =
              "Restore and rollback both failed",
          ["復元に使う復元キー"] = "Recovery key for restore",
          ["復元をキャンセルしました"] = "Restore canceled",
          ["復元を開始できません"] = "Cannot start restore",
          ["復元を中止しました"] = "Restore canceled",
          ["復元完了"] = "Restore complete",
          ["復元結果を確認しています…"] = "Verifying restored data…",
          ["復元失敗"] = "Restore failed",
          ["復元失敗・ロールバック済み"] =
              "Restore failed — rollback completed",
          ["復元前にバックアップを検証しています…"] =
              "Verifying the backup before restore…",
          ["復元前の安全確認"] = "Pre-restore safety check",
          ["復元前の状態へ戻しています…"] =
              "Rolling back to the pre-restore state…",
          ["復元前の状態へ戻しました"] =
              "Rolled back to the pre-restore state",
          ["復元前ロールバックを暗号化しています…"] =
              "Encrypting the pre-restore rollback…",
          ["平文CSVが残っています"] = "A plaintext CSV remains",
          ["平文CSVの削除が必要です"] = "Plaintext CSV must be deleted",
          ["別PCへの移動は簡単ですが、フォルダーごと盗まれた場合の保護は弱くなります。"] =
              "Moving to another PC is easy, but protection is weaker if the entire folder is stolen.",
          ["別PCへ移すときは両方が必要です。"] =
              "Both files are required when moving to another PC.",
          ["別環境へ同一環境モードで復元します。端末依存データは動作しない可能性があります。本当に続けますか？"] =
              "You are restoring to another environment using same-environment mode. Device-bound data may not work. Continue?",
          ["保存する内容を1つ以上選んでください。"] =
              "Select at least one item to save.",
          ["保存パスワードCSVの取込"] = "Saved-password CSV import",
          ["保存パスワードCSVの対象"] = "Saved-password CSV targets",
          ["保存パスワードCSVは、現在ログイン中のWindowsユーザー分だけ公式画面から取得できます。ほかのWindowsユーザー分は、そのユーザーでサインインして別途バックアップしてください。"] =
              "Saved-password CSV files can be obtained through the official UI only for the currently signed-in Windows user. Sign in as each other user and back them up separately.",
          ["保存先パスをクリップボードへコピーしました。"] =
              "Copied the save path to the clipboard.",
          ["未保存の入力は失われる可能性があります。"] =
              "Unsaved input may be lost.",
          ["未保存の入力を保存してから進めてください。"] =
              "Save any unsaved input before continuing.",

          ["アプリの終了支援を使うか、対象ブラウザーを完全に終了して再試行してください。"] =
              "Use the app's close assistance, or fully close the target browser and try again.",
          ["復元キーまたは旧パスフレーズが違うか、バックアップが破損・改ざんされています。"] =
              "The recovery key or legacy passphrase is incorrect, or the backup is damaged or has been tampered with.",
          ["同じバックアップ用の復元キーか確認し、元の2ファイルをコピーし直してください。"] =
              "Confirm that the recovery key belongs to this backup, then copy the original two files again.",
          ["バックアップ内に安全でないファイルパスがあります。"] =
              "The backup contains an unsafe file path.",
          ["このバックアップは復元せず、信頼できる別のバックアップを使用してください。"] =
              "Do not restore this backup. Use another backup from a trusted source.",
          ["復元と自動ロールバックの両方を完了できませんでした。"] =
              "Neither the restore nor automatic rollback could be completed.",
          ["ブラウザーを起動せず、表示されたロールバックフォルダーを保持してください。"] =
              "Do not start the browser. Keep the displayed rollback folder.",
          ["復元を完了できなかったため、復元前の状態へ戻しました。"] =
              "Restore could not be completed, so the pre-restore state was restored.",
          ["ブラウザーを起動して元の状態を確認し、ロールバックを保持してください。"] =
              "Start the browser to confirm the original state and keep the rollback backup.",
          ["選択した復元先へ安全に復元できません。"] =
              "The selected destination cannot be restored safely.",
          ["復元元と同じブラウザーのプロファイルを選び直してください。"] =
              "Choose a destination profile from the same browser as the backup source.",
          ["CSVの形式を安全に確認できません。"] =
              "The CSV format cannot be validated safely.",
          ["ブラウザー公式画面からCSVをもう一度エクスポートしてください。"] =
              "Export the CSV again through the browser's official UI.",
          ["プロファイルのバックアップ内容を安全に固定できません。"] =
              "The profile backup contents cannot be fixed safely.",
          ["ブラウザーを終了し、プロファイルを再検出してから試してください。"] =
              "Close the browser, re-scan profiles, and try again.",
          ["このバックアップ形式を安全に処理できません。"] =
              "This backup format cannot be processed safely.",
          ["対応バージョンで作成したバックアップか確認してください。"] =
              "Confirm that the backup was created by a supported version.",
          ["ファイルまたはフォルダーへアクセスできません。"] =
              "A file or folder cannot be accessed.",
          ["書き込み可能なローカルフォルダーを選び直してください。"] =
              "Choose a writable local folder.",
          ["保存先の空き容量が不足しています。"] =
              "The destination does not have enough free space.",
          ["空き容量を増やすか、別のローカルドライブを選んでください。"] =
              "Free up space or choose another local drive.",
          ["ファイルの読み書きを完了できませんでした。"] =
              "File reading or writing could not be completed.",
          ["保存先の接続と空き容量を確認し、もう一度お試しください。"] =
              "Check the destination connection and free space, then try again.",
          ["ブラウザーと本アプリを終了し、もう一度お試しください。"] =
              "Close the browser and this app, then try again.",
          ["対処方法"] = "What to do",
          ["バックアップ中に予期しない問題が発生しました。"] =
              "An unexpected problem occurred during backup.",
          ["検証中に予期しない問題が発生しました。"] =
              "An unexpected problem occurred during verification.",
          ["復元中に予期しない問題が発生しました。"] =
              "An unexpected problem occurred during restore.",
          ["CSVの処理中に予期しない問題が発生しました。"] =
              "An unexpected problem occurred while processing the CSV.",
          ["処理中に予期しない問題が発生しました。"] =
              "An unexpected problem occurred.",
          ["既定保存先にはローカルの完全修飾パスを指定してください。"] =
              "Specify a fully qualified local path for the default backup location.",
          ["ネットワークパスとデバイスパスは既定保存先にできません。"] =
              "Network and device paths cannot be used as the default backup location.",
        });
  }

  [GeneratedRegex("[一-龯ぁ-んァ-ヶ]")]
  private static partial Regex JapaneseTextRegex();

  [GeneratedRegex(@"^([\d,]+)件を選択・合計 約(.+)$")]
  private static partial Regex SelectedSummaryRegex();

  [GeneratedRegex(@"^([\d,]+)ブラウザー、([\d,]+)プロファイルを検出しました$")]
  private static partial Regex BrowserProfileCountRegex();

  [GeneratedRegex(@"^([\d,]+) / ([\d,]+)ファイル・([\d,]+)%$")]
  private static partial Regex ProgressWithPercentRegex();

  [GeneratedRegex(@"^([\d,]+) / ([\d,]+)ファイル$")]
  private static partial Regex ProgressFilesRegex();

  [GeneratedRegex(@"^([\d,]+) / ([\d,]+) — プロファイルごとに1件ずつ公式インポートします。$")]
  private static partial Regex WizardImportProgressRegex();

  [GeneratedRegex(@"^([\d,]+) / ([\d,]+) — プロファイルごとにCSVを取り違えないよう、1件ずつ進めます。$")]
  private static partial Regex WizardExportProgressRegex();

  [GeneratedRegex(@"^CSVのレコード数: ([\d,]+)件（値は表示しません）$")]
  private static partial Regex CsvRecordCountRegex();

  [GeneratedRegex(@"^([\d,]+)件のWindowsユーザー領域を確認しました。$")]
  private static partial Regex WindowsUsersReadRegex();

  [GeneratedRegex(@"^([\d,]+)件は権限を変えず読み飛ばしました。$")]
  private static partial Regex WindowsUsersSkippedRegex();

  [GeneratedRegex(@"^([\d,]+)種類のブラウザーが実行中です。$")]
  private static partial Regex BrowserRunningCountRegex();

  [GeneratedRegex(@"^([\d,]+)件は安全に読み飛ばしました。$")]
  private static partial Regex SafelySkippedCountRegex();

  [GeneratedRegex(@"^([\d,]+)ファイル$")]
  private static partial Regex FileCountRegex();

  [GeneratedRegex(@"^バージョン (.+)$")]
  private static partial Regex VersionRegex();

  [GeneratedRegex(@"^(.+)を終了してから、もう一度お試しください。$")]
  private static partial Regex BrowserCloseRegex();
}
