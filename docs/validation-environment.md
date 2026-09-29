# 専用検証環境

## 歴史資料としての位置付け

以下は2026年7月の旧版・旧VirtualBox環境の記録であり、現行環境の操作手順では
ありません。記載の旧VM操作スクリプトは再公開ソースに同梱しません。
2026-09-29の再公開でWindows 10やクリーンVM試験を実施したという意味では
ありません。現行確認の範囲は[Release準備状況](release-readiness.md)を参照してください。

## 目的

一般配布前の実機ゲートとして、Windows 10 22H2とWindows 11の
クリーンVMで、配布ZIPの起動、実ブラウザープロファイル、
バックアップ、明示復元、アクセシビリティ、オフライン動作を
確認します。

## 境界

- VM、ISO、合成プロファイル、実行証拠は`.validation`配下へ置き、
  ソース管理と配布ZIPへ含めません。
- Windows ISO、Chrome Enterprise MSI、Chrome for Testing、Firefox ESR、
  VirtualBox Guest Additionsは検証専用とし、本アプリと再配布しません。
- VMのネットワークアダプターは`none`とし、検証中は外部通信を
  行いません。
- 動的VDIのセットアップ速度とVirtualBox 7.1の非同期AHCI安定性を
  優先し、SATAのホストI/Oキャッシュを有効、discard連携を無効にします。
  正本データはVMへ置かず、ホスト障害時はVMを作り直します。
- VMにはVirtualBox Guest Additionsだけを入れた状態を
  クリーン基準としてスナップショット化します。
- 検証用Windowsアカウントのランダムパスワードは、
  現在ユーザーとSYSTEMだけが読めるACLのファイルへ保存します。
- VirtualBoxの無人インストール出力は、生成済みパスワードを
  展開する実装のため、標準出力と標準エラーの両方を記録しません。
- 実在サービスのCookie、履歴、URL、ユーザー名、パスワードは
  VMへ持ち込みません。
- 保存パスワードは認証情報DBへ触れず、ブラウザー公式CSVを
  ユーザー本人が操作する場合だけ確認します。

## 作成コマンド

Microsoft公表SHA-256と一致したISOだけを使用します。
スクリプトはVirtualBoxのISO検出結果も照合し、Windows 10／11 Proの
正しいイメージ番号であることを確認してからVMを作成します。

```powershell
pwsh ./eng/validation/New-CleanWindowsVm.ps1 `
  -Target Windows11 `
  -IsoPath <公式ISO> `
  -ExpectedSha256 <Microsoft公表値> `
  -ProductKey <Microsoft公表のWindows Pro GVLK>

pwsh ./eng/validation/Wait-CleanWindowsVm.ps1 `
  -VmName YBC-Win11-25H2-Clean
```

Windows 10は`-Target Windows10`と
`YBC-Win10-22H2-Clean`へ読み替えます。
`ProductKey`には購入済みキーや組織固有キーを渡さず、Microsoftが
公開するWindows 10／11 Pro共通のKMSクライアントセットアップキー
（GVLK）だけを使います。これはインストール用で、検証VMを
ライセンス認証するものではありません。値と利用条件は
[Microsoft公式のKMSクライアントキー一覧](https://learn.microsoft.com/en-us/windows-server/get-started/kms-client-activation-keys)
を正本とします。

検証用資格情報が露出した可能性がある場合は、OS内で権限を迂回して
パスワードだけを交換せず、停止した使い捨てVMを次で再インストール
します。既存VDI、ISO、無人インストール媒体は削除せず切り離して
保全し、新しいVDIと未露出のランダムパスワードへ置き換えます。
無人インストールの標準出力と標準エラーは記録しません。

```powershell
pwsh ./eng/validation/Reinstall-ValidationVm.ps1 `
  -Target Windows10 `
  -IsoPath <公式ISO> `
  -ExpectedSha256 <Microsoft公表値> `
  -ProductKey <Microsoft公表のWindows Pro GVLK>
```

Windows 11は`-Target Windows11`へ読み替えます。

Windows 10 22H2は互換性確認対象です。Microsoftによる無償の
セキュリティ更新は2025年10月14日に終了しているため、
公開後の通常利用環境として推奨する意味ではありません。

## 合格証拠

- ISO名、SHA-256、OSエディション、バージョン、ビルド
- UEFI、Secure Boot、TPM状態（Windows 11）
- ネットワークアダプター無効
- 配布ZIPのSHA-256と展開後ファイルの一致
- Chrome for Testing公式JSONの取得URL、固定バージョン、
  ZIPのSHA-256（検証専用、再配布なし）
- UAC昇格なしの起動
- バックアップID、対象ブラウザー数、対象プロファイル数、
  ファイル件数、論理バイト数、完全検証結果
- 復元元と復元先を識別する合成ID、復元件数、内容ハッシュ
- 100%／150% DPI、高コントラスト、キーボード、ナレーターの
  実施結果

値を含むCSV、完全な実プロファイルパス、認証情報は証拠へ
記録しません。

## 実施結果（2026-07-24〜2026-07-25）

- Windows 10 Pro 22H2 build 19045.2965と、Windows 11 Pro 25H2
  build 26200.8037のクリーン基準に合格しました。
- 両VMともNICは`none`、クリップボードとドラッグ＆ドロップは
  無効です。Win11はSecure BootとTPM 2.0も確認しました。
- Win10で実ブラウザーが作成した合成6プロファイルを1つの`.bvb`へ
  フルバックアップし、Win11へ移行復元しました。CSVや実在サービスの
  認証情報は使用していません。
- 復元後とブラウザー起動後の2回、6つのマーカーSHA-256が一致し、
  暗号化ロールバック1件を確認しました。
- Win11で150% DPI、論理1280x720相当、高コントラスト、Tabフォーカスを
  確認し、終了後は100% DPIと高コントラスト無効へ戻しました。
- Win10の同一環境で6プロファイルを変更後にフル復元し、全マーカーの
  SHA-256一致と暗号化ロールバック1件を確認しました。
- Chrome Stable 150.0.7871.187をNICなしで導入し、現在ユーザーの
  2プロファイルを実起動しました。
- 無効化した検証用ローカルユーザーに合成Chrome 2プロファイルを作成し、
  YbcTestへ読み取り・実行権限だけを付与しました。通常権限のアプリで
  検出し、対象2件だけの暗号化バックアップと完全検証に成功しました。
- Win10のChrome Stableで、本人操作による合成ダミー1件の公式CSV
  エクスポート／インポートを確認しました。CSV専用バックアップは
  1,202バイト、SHA-256は
  `2ea522f0aaa1fa54bbc6380ab323cbf745a3646971045ac67d4e70609096597a`で、
  アプリ内完全検証後と公式インポート完了後の一時平文CSVは0件でした。
- Windowsナレーターは本人が実聴し、主要ラベル、選択状態、警告、
  ボタン名をすべて判別可能と確認しました。
- 公式CSV試験中だけ検証VMのホスト→ゲスト共有クリップボードを
  検証用認証情報の受け渡しに使用し、試験後に無効へ戻しました。
- 2026-07-24のVM受け入れ用ZIPのSHA-256
  `c7a77f890d12d844ab7a06c2820e4e5a79b135481384dcfaa9ac2d1310038e7b`を
  両VMで照合・展開し、同一EXE SHA-256、FileVersion `0.9.0.0`、
  `NotSigned`、MainWindow生成を確認しました。最終ZIPの値は配布物に
  同梱する`.sha256`を正本とします。
- 基準、配布物、移行、DPI、ハイコントラストの機械証拠は
  `.validation/evidence`と`.validation/tmp/win11-output`に保存し、
  ソース管理と配布ZIPから除外しています。

VirtualBox 7.1.4のGuestControlからWin10／11上の一部プロセスを
待機すると、実プロセスの起動成功とは別に`0xC0000374`または
`0xC0000005`で終了する相性問題がありました。検証スクリプトは
VMコンソールから起動し、結果JSONだけをGuestControlでコピーする方式を
併用しています。アプリ本体の復元処理、マーカー照合、暗号化ロールバック、
Chrome Stable本体には同エラーは発生していません。
