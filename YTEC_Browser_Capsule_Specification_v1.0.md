# Y-TEC Browser Capsule 仕様書

- 文書バージョン: 1.0
- 作成日: 2026-07-23
- 想定読者: Codex、実装担当者、レビュー担当者
- 正式名称: Y-TEC Browser Capsule
- 対象OS: Windows 10 22H2 / Windows 11（MVPは x64）
- 対象ブラウザー: Google Chrome、Microsoft Edge、Mozilla Firefox / Firefox ESR
- ライセンス: Apache License 2.0（2026-08-25のユーザー決定により更新）
- 実装言語: C#
- UI: WPF
- ターゲット: .NET 10 LTS
- 配布形態: 自己完結型のポータブル配布（MVP）。インストーラーは後続フェーズ。

---

## 1. 目的

Windows上のChrome、Edge、Firefoxについて、複数のブラウザープロファイルを検出し、以下を安全にバックアップ・復元できるデスクトップアプリを作成する。

1. 選択したブラウザープロファイルのデータを暗号化してバックアップする。
2. ブックマーク、履歴、設定、拡張機能関連データなどをプロファイル単位で復元する。
3. 保存パスワードはブラウザーの保護機構を回避せず、各ブラウザー公式のCSVエクスポート／インポート操作をアプリが案内・補助する。
4. Chrome、Edge、Firefoxの複数プロファイルを個別に選択・識別・対応付けできるようにする。
5. バックアップファイルが盗難・改ざんされた場合でも、強力な暗号化と完全性検証により内容を保護する。
6. 社内配布を想定し、外部通信・テレメトリー・広告・クラウド依存を持たない設計とする。

本アプリは、認証情報抽出ツールやブラウザー暗号化回避ツールではない。ブラウザーの保存パスワードデータベースを直接復号しないことを最重要要件とする。

---

## 2. 基本方針

### 2.1 パスワードの扱い

Chrome、Edge、Firefoxの保存パスワードは、ユーザー自身が対象プロファイルを開き、ブラウザー標準画面からCSVへエクスポートする。

本アプリは次だけを行う。

- 対象プロファイルでブラウザーを開く。
- パスワードのエクスポート画面を開く。
- 現在のWindowsユーザーだけがアクセスできる一時フォルダーを作成する。
- ユーザーへ一時フォルダーへの保存を案内する。
- 保存されたCSVの形式を最小限検証する。
- CSVを直ちに一時暗号化し、平文CSVを削除する。
- 最終バックアップコンテナへ暗号化した状態で格納する。
- 復元時は一時フォルダーへ必要な時間だけCSVを復号し、ブラウザー標準のインポート画面を開く。
- ユーザーの完了確認後、平文CSVを削除する。

禁止事項:

- Chrome / Edgeの `Login Data` を読み取り、保存パスワードを復号する処理。
- Firefoxの `logins.json` と `key4.db` からパスワードを抽出して平文化する処理。
- Windows DPAPIやブラウザーのApp-Bound Encryptionを回避する処理。
- ブラウザーへコードを注入する処理。
- UI Automationによって本人確認、エクスポート確認、インポート確認を自動承認する処理。
- パスワード、ユーザー名、URLをログやテレメトリーへ出力する処理。

### 2.2 プロファイル本体の扱い

プロファイル本体は、ブラウザーを完全終了した状態でファイル単位に読み取り、圧縮しながら暗号化コンテナへ格納する。

バックアップは次の2目的を区別する。

- **同一環境復旧**: 同じPC・同じWindowsユーザーへ戻す用途。プロファイル全体の復元を許可する。
- **別環境移行**: 別PC、OS再インストール後、別Windowsユーザーへの移行。端末に結び付いたCookie、セッション、暗号化データなどは完全復元を保証しない。パスワードはCSVインポートを正式な復元経路とする。

---

## 3. 対象範囲

### 3.1 MVPで対応するもの

- Chrome Stable
- Edge Stable
- Firefox Stable
- Firefox ESR
- 各ブラウザーの複数プロファイル
- プロファイル単位のバックアップ選択
- プロファイル本体の暗号化バックアップ
- 保存パスワードCSVの手動エクスポート補助
- 保存パスワードCSVの手動インポート補助
- 同一環境へのフル復元
- 別環境へのベストエフォート復元
- 復元前の自動ロールバックバックアップ
- バックアップの完全性検証
- オフライン動作
- 日本語UI
- 英語リソースを追加しやすいローカライズ構造

### 3.2 MVP対象外

- macOS、Linux
- Chrome Beta / Dev / Canary
- Edge Beta / Dev / Canary
- Firefox Beta / Developer Edition / Nightly
- Android、iOS
- クラウド同期機能
- 自動スケジュールバックアップ
- バックグラウンド常駐
- Windowsサービス
- 企業管理サーバー
- ブラウザーの同期アカウント操作
- パスキーの移行保証
- 支払い情報、カード番号のエクスポート
- ブラウザーのマスターパスワード解除
- ブラウザーの認証機構を回避した完全自動パスワード移行
- ブラウザー間のパスワードCSV変換
- ブラウザー間の完全なプロファイル変換

---

## 4. 用語

- **ブラウザーインストール**: Chrome、Edge、Firefoxなどの製品単位。
- **ユーザーデータルート**: ブラウザーが複数プロファイルと共有メタデータを保存するルート。
- **プロファイル**: ブックマーク、履歴、設定、パスワードなどを分離して保持する単位。
- **バックアップセット**: 1回のバックアップ操作で生成される暗号化ファイル。
- **ソースプロファイル**: バックアップ元のプロファイル。
- **ターゲットプロファイル**: 復元先のプロファイル。
- **同一環境ID**: 同一PC・同一Windowsユーザーかを警告判定するための、暗号化マニフェスト内のハッシュ値。
- **ステージングフォルダー**: パスワードCSVなどを短時間だけ扱う、アクセス制御済みの一時フォルダー。

---

## 5. 技術構成

### 5.1 ソリューション構成

```text
YtecBrowserCapsule.sln
src/
  Ytec.BrowserCapsule.App/                 # WPF UI、起動処理、DI
  Ytec.BrowserCapsule.Application/         # ユースケース、ワークフロー
  Ytec.BrowserCapsule.Domain/              # モデル、インターフェース、例外
  Ytec.BrowserCapsule.Infrastructure/      # ファイル、暗号、Windows、プロセス
  Ytec.BrowserCapsule.Browsers.Common/     # ブラウザー共通処理
  Ytec.BrowserCapsule.Browsers.Chromium/   # Chrome / Edge共通処理
  Ytec.BrowserCapsule.Browsers.Chrome/
  Ytec.BrowserCapsule.Browsers.Edge/
  Ytec.BrowserCapsule.Browsers.Firefox/
tests/
  Ytec.BrowserCapsule.UnitTests/
  Ytec.BrowserCapsule.IntegrationTests/
  Ytec.BrowserCapsule.SecurityTests/
docs/
  architecture.md
  backup-format.md
  threat-model.md
  manual-test-plan.md
AGENTS.md
LICENSE
THIRD-PARTY-NOTICES.md
README.md
```

### 5.2 依存関係方針

可能な限り.NET標準ライブラリを使用する。

外部依存を追加する場合は、以下を満たすこと。

- MIT、Apache-2.0、BSDなど、社内利用・商用利用・再配布が明確なライセンス。
- メンテナンス状況と脆弱性を確認する。
- 追加理由をADRへ記録する。
- `THIRD-PARTY-NOTICES.md` を更新する。
- 暗号アルゴリズムを独自実装する外部ライブラリは、明確な必要性がない限り導入しない。

### 5.3 実行権限

- `requestedExecutionLevel` は `asInvoker`。
- 管理者権限を要求しない。
- 既定では現在のWindowsユーザーが所有するブラウザープロファイルだけを対象にする。
- ユーザーが「読み取り可能なほかのWindowsユーザーも表示」を明示的に選んだ場合だけ、ローカルのWindowsユーザープロファイル一覧を読み取り専用で確認する。
- 他ユーザー領域は、現在の実行ユーザー権限で読み取れる既知のブラウザーパスだけを対象にする。アクセス不能な領域は権限を変更せずスキップする。
- 他ユーザーのSID、フルパス、表示名を平文ログへ出さない。安定識別子にはSIDまたはプロファイルパスの一方向ハッシュを使う。
- 保存パスワードCSVの公式エクスポート／インポート補助は、現在ログイン中のWindowsユーザーだけを対象にする。
- UAC昇格を使用した認証情報アクセスを実装しない。

---

## 6. ブラウザー検出仕様

### 6.1 共通のプロファイルモデル

```csharp
public sealed record BrowserProfile(
    string BrowserId,
    string InstallationId,
    string ProfileId,
    string DisplayName,
    string ProfileDirectoryName,
    string AbsoluteProfilePath,
    string UserDataRoot,
    bool IsDefault,
    DateTimeOffset? LastUsedUtc,
    BrowserChannel Channel,
    ProfileDiscoveryConfidence Confidence,
    WindowsUserProfileIdentity? WindowsUser);
```

`AbsoluteProfilePath` はUI表示時にユーザー名部分をマスクできるようにする。
他ユーザーのプロファイルは所有Windowsユーザーを画面上で明示し、同名プロファイルの取り違えを防ぐ。

### 6.2 Chrome

標準ユーザーデータルート:

```text
%LOCALAPPDATA%\Google\Chrome\User Data
```

検出順:

1. HKCUのChromeポリシー `UserDataDir` を確認。
2. HKLMのChromeポリシー `UserDataDir` を確認。
3. 環境変数・Chromeで使用される変数を安全に展開。
4. 標準パスを確認。
5. 存在する候補ごとに `Local State` を解析。
6. `profile.info_cache` からプロファイルディレクトリー名と表示名を取得。
7. `Local State` が壊れている場合は、ユーザーデータルート直下で `Preferences` を持つディレクトリーをフォールバック検出。
8. `Default`、`Profile 1` の名前だけに依存しない。
9. `Guest Profile`、`System Profile` は既定で除外し、詳細設定で表示可能にする。
10. シンボリックリンク、ジャンクション、リパースポイントは既定で追跡しない。

プロファイル起動:

```text
chrome.exe --profile-directory="<ProfileDirectoryName>" chrome://password-manager/settings
```

この起動が失敗した場合はChromeを通常起動し、ユーザーへ対象プロファイルへの切り替えを案内する。

### 6.3 Edge

標準ユーザーデータルート:

```text
%LOCALAPPDATA%\Microsoft\Edge\User Data
```

検出順はChromeと同等とし、Edgeポリシー `UserDataDir` とEdgeの `Local State` を使用する。

プロファイル起動はベストエフォートで次を使用する。

```text
msedge.exe --profile-directory="<ProfileDirectoryName>" edge://wallet/passwords
```

パスワード画面URIが将来変更された場合に備え、URIはブラウザーアダプターの設定値とし、失敗時はEdge設定画面を開いて手動手順を表示する。

### 6.4 Firefox

標準設定ルート:

```text
%APPDATA%\Mozilla\Firefox
```

検出順:

1. `%APPDATA%\Mozilla\Firefox\profiles.ini` を解析。
2. 各 `ProfileN` セクションの `Name`、`Path`、`IsRelative`、`Default` を取得。
3. `installs.ini` が存在する場合は既定プロファイル判定の補助に使用。
4. `profiles.ini` が破損または不足している場合は `%APPDATA%\Mozilla\Firefox\Profiles` を走査。
5. `prefs.js` などFirefoxプロファイルの特徴ファイルを持つディレクトリーだけを候補とする。
6. 新旧のFirefoxプロファイル管理UIの差異に依存しない。
7. ルートディレクトリーをバックアップ対象とし、ローカルキャッシュ側は既定で除外する。

プロファイル起動:

```text
firefox.exe -P "<ProfileName>" -no-remote about:logins
```

名前による起動に失敗する場合は通常起動し、`about:profiles` またはFirefoxのプロファイルメニューから対象を開く手順を表示する。

---

## 7. ブラウザーアダプター

```csharp
public interface IBrowserAdapter
{
    string BrowserId { get; }
    string DisplayName { get; }

    Task<IReadOnlyList<BrowserInstallation>> DiscoverInstallationsAsync(
        CancellationToken cancellationToken);

    Task<IReadOnlyList<BrowserProfile>> DiscoverProfilesAsync(
        BrowserInstallation installation,
        CancellationToken cancellationToken);

    IReadOnlyList<ProcessMatchRule> GetProcessMatchRules();

    BackupPlan BuildBackupPlan(
        BrowserProfile profile,
        BackupComponentSelection selection);

    RestorePlan BuildRestorePlan(
        BrowserProfile source,
        BrowserProfile target,
        RestoreMode mode,
        BackupComponentSelection selection);

    Task<LaunchResult> OpenPasswordExportPageAsync(
        BrowserProfile profile,
        CancellationToken cancellationToken);

    Task<LaunchResult> OpenPasswordImportPageAsync(
        BrowserProfile profile,
        CancellationToken cancellationToken);

    PasswordCsvValidationResult ValidatePasswordCsv(
        Stream csvStream);
}
```

ブラウザー固有のパス、除外ルール、CSV検証、起動URIをUIやワークフローへ直接書かない。

---

## 8. バックアップ対象

### 8.1 選択できるコンポーネント

- プロファイル設定
- ブックマーク／お気に入り
- 履歴
- 拡張機能と拡張機能設定
- Cookie／サイトデータ
- セッション情報
- 保存パスワードCSV
- プロファイル全体（詳細設定）

初期選択:

- 設定: ON
- ブックマーク: ON
- 履歴: ON
- 拡張機能: ON
- Cookie／サイトデータ: ON。ただし別環境では復元保証外と表示。
- セッション: OFF
- パスワードCSV: ON
- キャッシュ: OFF

### 8.2 既定除外

共通:

- キャッシュ
- GPUキャッシュ
- コードキャッシュ
- クラッシュダンプ
- 一時ダウンロード
- ロックファイル
- ソケット
- ログ
- 更新用一時ファイル
- リパースポイント
- 別ボリュームへのリンク

Chromium系の例:

```text
Cache/
Code Cache/
GPUCache/
ShaderCache/
GrShaderCache/
GraphiteDawnCache/
Crashpad/
BrowserMetrics/
SingletonCookie
SingletonLock
SingletonSocket
*.tmp
```

Firefoxの例:

```text
cache2/
startupCache/
shader-cache/
crashes/
minidumps/
parent.lock
lock
*.tmp
```

除外ルールはコード内に散在させず、アダプター単位のテスト可能な設定として保持する。

### 8.3 読み取りエラー

- 1ファイルの読み取り失敗でバックアップ全体を成功扱いにしない。
- 重要ファイルと任意ファイルを区別する。
- 重要ファイル失敗時はバックアップを失敗させる。
- 任意ファイル失敗時は「警告付き成功」とし、マニフェストへ記録する。
- アクセス拒否やファイル変更を無限リトライしない。
- 最大3回、指数バックオフで再試行する。
- ブラウザーが起動している場合は再試行より先に終了を要求する。

---

## 9. パスワードCSVワークフロー

### 9.1 ステージングフォルダー

場所:

```text
%LOCALAPPDATA%\Y-TEC\BrowserCapsule\Staging\<SessionGuid>\
```

要件:

- ディレクトリー継承を無効化。
- 現在のWindowsユーザーSIDとSYSTEMだけにフルコントロールを許可。
- `Everyone`、`Users`、`Authenticated Users` のアクセスを付与しない。
- ランダムなGUIDを使用。
- 起動時に前回クラッシュで残ったステージングを検出し、内容を開かず削除を試みる。
- リパースポイントではないことを確認する。
- UNCパスを使用しない。

### 9.2 エクスポート手順

選択したプロファイルごとに次を繰り返す。

1. ステージングフォルダーを作成。
2. 対象プロファイルのパスワード設定画面を開く。
3. ブラウザー名とプロファイル名を大きく表示。
4. エクスポート先としてステージングフォルダーを指定するよう案内。
5. `FileSystemWatcher` だけに依存せず、短時間のポーリングと組み合わせてCSVを検出。
6. ファイル書き込み完了を、サイズが一定期間変化しないことと排他的オープン可否で確認。
7. CSVを読み取り、ヘッダーだけを検証。
8. CSV全体をログやUIへ表示しない。
9. CSVを一時暗号化ファイルへストリーム暗号化。
10. 平文CSVを通常削除。
11. 削除できない場合は明確な警告を表示し、パスをユーザーへ示す。
12. SSD上の完全消去を保証する表現を使用しない。
13. 一時暗号化キーはプロセスメモリだけに保持し、完了後 `CryptographicOperations.ZeroMemory` で消去。
14. クラッシュ後に一時暗号化ファイルが残っても、キーがないため復号不能とする。

### 9.3 CSV検証

CSVは書き換えず、元形式を保持する。

- UTF-8 BOMあり／なしを許容。
- RFC 4180相当の引用符と改行を処理できること。
- 大文字小文字を区別せず必須列を確認。
- Chrome / Edgeは最低限 `url`、`username`、`password` 相当列を要求。
- Firefoxは最低限 `url`、`username`、`password` 相当列を要求し、追加列を許容。
- 空のCSVはユーザーへ確認し、明示的に許可された場合だけ保存。
- レコードの値をログへ出さない。
- CSVインジェクション対策として値を表計算ソフトへ渡したり変換したりしない。
- CSVのインポート互換性を壊さないため、正規化や列の並べ替えを行わない。

### 9.4 復元手順

1. 暗号化バックアップから対象CSVを安全なステージングへ復号。
2. ACLを再確認。
3. 対象プロファイルのインポート画面を開く。
4. ユーザーへCSVを選択してインポートするよう案内。
5. アプリはインポート完了を推測しない。
6. ユーザーが「インポート完了」を押した後にCSVを削除。
7. キャンセル時も削除を試みる。
8. 削除失敗時は警告し、残存パスを表示する。
9. 別プロファイルのCSVを誤って選ばないよう、ファイル名にブラウザー名とプロファイル表示名の安全な短縮値を含める。
10. ファイル名にメールアドレスなどの個人情報を含めない。

---

## 10. ブラウザー終了確認

### 10.1 原則

プロファイル本体のバックアップ／復元時は対象ブラウザーの全プロセスが終了していることを要求する。

### 10.2 動作

- 対象実行ファイル名と実行パスを照合する。
- WebView2など、Edgeと同名に見える別用途プロセスを誤判定しない。
- 「ブラウザーを終了してください」ボタンを表示。
- ウィンドウへ通常の終了要求を送る機能は提供可能。
- 既定では強制終了しない。
- 詳細設定の「強制終了」はMVP対象外。
- 終了待ちにはタイムアウトを設定する。
- 終了後、ロックファイルが解放されたことを再確認する。

---

## 11. 暗号化バックアップ形式

### 11.1 拡張子

```text
.bvb
```

途中ファイル:

```text
.bvb.partial
```

完成後にアトミックにリネームする。

### 11.2 暗号方針

- 暗号: AES-256-GCM
- KDF: PBKDF2-HMAC-SHA256
- Salt: 32バイト、暗号学的乱数
- PBKDF2反復回数: バックアップ作成環境で約750msを目標に校正し、最低600,000回
- GCM nonce: バックアップ単位の4バイト乱数プレフィックス + 8バイトの単調増加チャンク番号
- GCM tag: 16バイト
- チャンクサイズ: 4 MiBを既定値
- 各チャンクのAAD: ヘッダーハッシュ、チャンク番号、平文長
- パスフレーズを失った場合のバックドアや復旧手段を実装しない。
- 同じ鍵とnonceの組み合わせを絶対に再利用しない。
- 認証失敗時は復号データを利用しない。

### 11.3 ストリーミング構造

平文ZIPを一時ファイルへ作成しない。

```text
Files -> ZipArchive -> ChunkedAesGcmEncryptingStream -> .bvb.partial
```

復元時:

```text
.bvb -> ChunkedAesGcmDecryptingStream -> ZipArchive -> validated restore target
```

`ZipArchive` へ渡せるシーク可能性が必要な場合は、暗号化ストリームの設計を先に検証する。標準 `ZipArchiveMode.Create` は書き込み時に非シークストリームを扱える構成とする。読み取り側でシークが必要になる場合は、次のどちらかをADRで選定する。

1. 暗号化チャンク索引を持ち、復号ストリームへシーク機能を実装する。
2. ZIPではなく、独自の逐次エントリーコンテナを実装する。

セキュリティと実装単純性を優先し、独自逐次コンテナを採用する場合でも、暗号アルゴリズム自体は独自設計しない。

### 11.4 コンテナヘッダー

ヘッダーに平文で保存してよい情報だけを含める。

- Magic
- フォーマットバージョン
- KDF識別子
- 暗号識別子
- Salt
- 反復回数
- チャンクサイズ
- Nonce prefix
- Backup ID
- 最小アプリバージョン
- ヘッダー長

次は暗号化マニフェスト内にのみ保存する。

- PC名
- Windowsユーザー表示名
- Windowsユーザー安定識別子（SIDまたはプロファイルパスの一方向ハッシュ）
- プロファイル名
- パス
- ブラウザーバージョン
- ファイル一覧
- コンポーネント一覧
- エラー／警告
- 同一環境ID

### 11.5 エントリー形式

すべてのエントリーは相対パス。

禁止:

- 絶対パス
- ドライブレター
- UNC
- `..`
- 空のセグメント
- 末尾のドット／空白を利用した曖昧パス
- NTFS Alternate Data Stream指定
- デバイス名（CON、NUL、AUXなど）
- リパースポイントの復元
- 大文字小文字差だけで衝突する複数エントリー

各ファイルについてSHA-256を計算し、暗号化マニフェストへ保存する。

### 11.6 マニフェスト例

```json
{
  "formatVersion": 1,
  "backupId": "uuid",
  "createdUtc": "2026-07-23T00:00:00Z",
  "appVersion": "1.0.0",
  "environmentFingerprint": "sha256",
  "profiles": [
    {
      "browserId": "chrome",
      "sourceProfileId": "Profile 1",
      "displayName": "Work",
      "components": [
        "bookmarks",
        "history",
        "settings",
        "extensions",
        "cookies",
        "passwordCsv"
      ],
      "files": [
        {
          "entryPath": "profiles/chrome/uuid/profile/Bookmarks",
          "length": 12345,
          "sha256": "..."
        }
      ],
      "passwordCsv": {
        "included": true,
        "recordCount": 123,
        "originalFileNameStored": false
      },
      "warnings": []
    }
  ]
}
```

---

## 12. パスフレーズ

- 最低8文字。長い文章形式を推奨する。
- 文字種類の強制はしない。
- 長いパスフレーズを推奨する。
- 確認入力を要求する。
- 貼り付けを許可する。
- クリップボードを自動消去しない。
- パスフレーズを設定ファイルへ保存しない。
- `string` で長時間保持せず、可能な範囲で `char[]` / `byte[]` を使用。
- 派生鍵、パスフレーズバイト列、一時鍵を使用後にゼロ化。
- 強度表示はローカル計算だけで行う。
- ネットワークへ送信しない。
- Caps Lock状態を表示する。
- パスフレーズ紛失時は復旧不能であることを作成前に明示する。

将来機能として、ユーザーが印刷・保管できるランダムなリカバリーキーを検討できるが、MVPには含めない。

---

## 13. バックアップ処理

### 13.1 フロー

1. 単一起動Mutexを取得。
2. ブラウザーとプロファイルを検出。
3. ユーザーが対象とコンポーネントを選択。
4. 保存先、推定容量、空き容量を確認。
5. パスワードCSVを選択した各プロファイルについてエクスポート補助。
6. 対象ブラウザーの終了を確認。
7. バックアップ計画を固定し、途中で対象ファイル一覧を無制限に増やさない。
8. `.partial` へ暗号化ストリーミング書き込み。
9. 各ファイルのSHA-256を計算。
10. 暗号化マニフェストを書き込む。
11. コンテナを閉じる。
12. 読み取り検証を実行。
13. チャンク認証とファイルハッシュを検証。
14. `.partial` を完成ファイルへアトミックリネーム。
15. 一時データを削除。
16. 成功、警告、失敗を表示。

### 13.2 キャンセル

- ファイル単位または暗号チャンク単位でキャンセルを確認。
- キャンセル後は `.partial` を削除。
- 削除失敗時は再起動後清掃対象として登録。
- 完成済みバックアップをキャンセル処理で削除しない。

### 13.3 進捗

- 全体進捗
- 現在のブラウザー
- 現在のプロファイル
- 現在のコンポーネント
- 処理済みファイル数
- 処理済みバイト数
- 推定残り時間は精度が低い場合に表示しない
- パスワードや具体的な閲覧URLを含むファイル内容は表示しない

---

## 14. 復元モード

### 14.1 同一環境フル復元

条件:

- 暗号化マニフェストの同一環境IDが一致。
- 対象ブラウザーが終了。
- ユーザーが上書きを明示承認。
- 復元前ロールバックが成功。

動作:

1. ターゲットプロファイルを選択。
2. 現在のターゲットを暗号化ロールバックバックアップへ保存。
3. ターゲットディレクトリー内を安全に置換。
4. 共有ルートファイルを復元する場合はブラウザー固有の許可リストだけを使用。
5. 復元後にファイルハッシュと主要ファイルの存在を確認。
6. ブラウザーを起動する前に完了画面を表示。
7. ユーザーがブラウザーを起動して確認。
8. 問題がある場合はロールバックを実行できる。

### 14.2 別環境移行

既定モード。

- ターゲット側で空の新規プロファイルをブラウザー標準UIから作成してもらう。
- 本アプリが `Local State` などを直接編集して新規Chromiumプロファイルを登録しない。
- Firefoxも、可能な限りFirefox標準プロファイル管理からターゲットを作成してもらう。
- ソースとターゲットをユーザーが明示的に対応付ける。
- 既存ターゲットのロールバックを必須とする。
- 端末依存データは復元保証外と明示する。
- 保存パスワードはCSVインポートを正式経路とする。
- Cookie、ログインセッション、パスキーは復元できない可能性が高いことを表示する。
- 復元後に再ログインが必要であることを表示する。

### 14.3 コンポーネント復元

ユーザーはプロファイルごとに次を選択できる。

- ブックマーク
- 履歴
- 設定
- 拡張機能
- Cookie／サイトデータ
- セッション
- パスワードCSV
- 全体

依存関係があるファイル群はアダプター側で原子的なグループとして扱い、片方だけ復元しない。

---

## 15. 復元の安全対策

- バックアップ全体の認証に成功するまで書き込みを開始しない設計を優先。
- 大容量バックアップで事前全検証が難しい場合は、少なくともヘッダー、マニフェスト、対象エントリー認証後に一時復元先へ書き込む。
- 直接本番プロファイルへ展開せず、同一ボリュームの一時ディレクトリーへ展開。
- 検証後にディレクトリー交換または安全なファイル置換を行う。
- Path Traversalを拒否。
- シンボリックリンク／ジャンクションを復元しない。
- 復元先配下に既存リパースポイントがある場合は停止。
- ZIP bomb相当を防ぐため、マニフェストの総展開容量、ファイル数、個別ファイルサイズへ上限を適用。
- バックアップ内の宣言容量と実データ量の不一致を拒否。
- 復元中にブラウザーが起動した場合は停止。
- ロールバックがない破壊的上書きを許可しない。
- ロールバックバックアップも暗号化する。
- 復元完了後、ロールバックの自動削除は行わず、ユーザーへ保持・削除を選択させる。

---

## 16. 同一環境ID

目的は復元可否の強制ではなく警告。

候補入力:

- Windows MachineGuid
- 現在のWindowsユーザーSID
- アプリ固有の固定ドメイン文字列

これらを連結してSHA-256を計算し、結果だけを暗号化マニフェストへ保存する。

- 生のMachineGuidやSIDを平文ヘッダーへ保存しない。
- IDが一致しなくても復元を完全禁止しない。
- 不一致時は「別環境移行」へ既定切り替え。
- ユーザーが詳細設定からフル復元を選ぶ場合は二段階確認する。

---

## 17. UI仕様

### 17.1 ホーム

ボタン:

- 新しいバックアップ
- バックアップから復元
- バックアップを検証
- 設定
- このアプリについて

### 17.2 プロファイル選択

ツリー例:

```text
[✓] Google Chrome
    [✓] 仕事用 (Profile 1)
    [ ] 個人用 (Default)
[✓] Microsoft Edge
    [✓] Default
[ ] Mozilla Firefox
    [ ] default-release
```

表示情報:

- ブラウザー名
- プロファイル表示名
- 内部ディレクトリー名
- 最終更新日時
- 推定容量
- 既定プロファイル
- 検出信頼度
- パスは折りたたみ式の詳細欄でのみ表示し、ユーザー名を既定でマスク

### 17.3 バックアップ設定

- コンポーネント選択
- 保存先
- ファイル名
- パスフレーズ
- パスフレーズ確認
- 作成後の完全検証（既定ON）
- キャッシュを含める（詳細設定、既定OFF）
- 別環境移行用の注意説明

### 17.4 パスワードエクスポートアシスタント

プロファイルごとにページを分ける。

表示:

- ブラウザー名
- プロファイル名
- ステップ番号
- 「ブラウザーでエクスポート画面を開く」
- エクスポート先フォルダーを開く
- 保存先パスをコピー
- CSV検出状態
- レコード件数のみ
- スキップ
- 再試行

パスワードやURLをプレビューしない。

### 17.5 復元対応付け

```text
バックアップ元                    復元先
Chrome / 仕事用        ->        Chrome / 新しい仕事用
Edge / Default         ->        Edge / Default
Firefox / old-work     ->        Firefox / work-restored
```

- 自動推測は候補表示まで。
- 実際の確定はユーザーが行う。
- 同名だけで自動確定しない。
- ブラウザーをまたぐ対応付けは不可。
- 1つのターゲットへ複数ソースを割り当てない。

### 17.6 結果画面

- 成功／警告付き成功／失敗
- バックアップファイル
- 対象プロファイル数
- 保存容量
- 検証結果
- 平文CSV残存の有無
- 警告一覧
- プライバシー保護済みログの保存
- 復元時はロールバック場所

---

## 18. 設定

MVPで提供:

- UI言語
- 既定バックアップ保存先
- 完全検証の既定値
- キャッシュ除外
- ログレベル
- 古いステージングの自動清掃
- パス表示のマスク

提供しない:

- パスフレーズ保存
- 自動ブラウザー強制終了
- テレメトリー
- クラウドアップロード
- 自動更新

更新確認を将来追加する場合も、既定OFFまたは管理者が明示設定できる設計とする。

---

## 19. ログとプライバシー

ログへ含めてよいもの:

- アプリバージョン
- 操作種別
- ブラウザーID
- 匿名化したプロファイルID
- ファイル件数
- 合計容量
- エラーコード
- スタックトレース（秘密値を含まないことを確認）

ログへ含めないもの:

- パスワード
- CSVレコード
- URL
- ユーザー名
- メールアドレス
- Cookie
- フォームデータ
- ブックマークタイトル
- PC名
- Windowsユーザー名
- SID
- 完全なプロファイルパス
- パスフレーズ
- 派生鍵
- 暗号nonce以外の秘密値

パスをログへ出す必要がある場合は、固定ソルトを使わない一時ハッシュか、`%USERPROFILE%` 置換済みの相対パスにする。

---

## 20. エラー処理

主要エラーコード例:

```text
BV-DISC-001  Browser installation not found
BV-DISC-002  Profile metadata invalid
BV-PROC-001  Browser is still running
BV-CSV-001   Password CSV not found
BV-CSV-002   Password CSV schema not recognized
BV-CSV-003   Plaintext CSV could not be deleted
BV-CRYPTO-001 Wrong passphrase or corrupted backup
BV-CRYPTO-002 Backup authentication failed
BV-CRYPTO-003 Unsupported backup format
BV-IO-001    Insufficient disk space
BV-IO-002    File access denied
BV-IO-003    Source changed during backup
BV-REST-001  Unsafe restore path
BV-REST-002  Restore target contains a reparse point
BV-REST-003  Rollback backup failed
BV-REST-004  Browser started during restore
```

UIには技術例外をそのまま表示せず、エラーコード、分かりやすい説明、再試行方法を表示する。

---

## 21. セキュリティ脅威モデル

### 21.1 保護対象

- 保存パスワードCSV
- ブックマーク
- 閲覧履歴
- Cookie
- セッション情報
- 拡張機能設定
- 個人情報を含むプロファイルファイル
- バックアップパスフレーズ

### 21.2 想定脅威

- バックアップファイルの盗難
- バックアップファイルの改ざん
- 悪意あるバックアップによるPath Traversal
- シンボリックリンクを利用した任意ファイル上書き
- ローカルの別ユーザーによる平文CSV閲覧
- クラッシュ後に残る一時ファイル
- ログからの情報漏えい
- 復元先の誤選択
- ブラウザー起動中の不整合バックアップ
- オフライン辞書攻撃
- 巨大ファイル、極端なファイル数、圧縮爆弾によるDoS
- 破損バックアップの一部復元
- プロファイル名などを利用したファイル名注入

### 21.3 非対象

- 管理者権限またはカーネル権限を持つ攻撃者
- アプリ実行中のメモリを自由に読み取れる攻撃者
- キーロガー
- ブラウザー自体が侵害済みの環境
- ユーザーが弱いパスフレーズを選択した場合の完全保護
- SSD上で削除済み平文の物理的完全消去

---

## 22. 性能・品質要件

- バックアップ中の常用メモリ: 300 MiB以下を目標。
- ファイル全体をメモリへ読み込まない。
- 4 GiB超の単一ファイルを処理可能。
- 100,000ファイル以上を処理可能。
- Unicode、長いパスを処理可能。
- Windows長いパス設定の有無を考慮。
- 5 GiBのプロファイルバックアップでUIが応答し続ける。
- 全I/Oは非同期またはバックグラウンドスレッドで行う。
- UIスレッドでハッシュ、圧縮、暗号化を行わない。
- キャンセル可能。
- 進捗通知をスロットリングし、UIを過負荷にしない。
- バックアップ完成は検証後のみ成功扱い。
- 既存ファイルを同名で上書きする場合は明示確認。
- `.partial` から完成ファイルへの切り替えは可能な限りアトミック。

---

## 23. テスト仕様

### 23.1 単体テスト

プロファイル検出:

- Chrome `Local State` の複数プロファイル。
- Edge `Local State` の複数プロファイル。
- 表示名に日本語、絵文字、引用符を含む。
- `Default` がない構成。
- `Profile 10` など二桁番号。
- `Local State` 破損時のフォールバック。
- Chrome / EdgeのポリシーでUserDataDirが変更された構成。
- Firefox `profiles.ini` の相対パス／絶対パス。
- Firefoxの複数既定候補。
- Firefoxの破損INI。
- リパースポイント除外。

CSV:

- Chrome形式。
- Edge形式。
- Firefox形式。
- BOMあり／なし。
- 引用符内改行。
- カンマを含む値。
- 空ファイル。
- 必須列不足。
- 追加列。
- 巨大CSV。
- 不正UTF-8。
- CSV内容がログへ出ない。

暗号:

- 正しいパスフレーズでラウンドトリップ。
- 誤ったパスフレーズ。
- ヘッダー改ざん。
- 暗号文改ざん。
- GCMタグ改ざん。
- チャンク欠落。
- チャンク順序変更。
- 末尾切断。
- 重複チャンク。
- 空バックアップ。
- 大容量ストリーム。
- キャンセル。
- nonce一意性。

パス安全性:

- `../`
- 絶対パス
- UNC
- ADS
- 予約デバイス名
- 大文字小文字衝突
- 末尾ドット／空白
- 極端な長さ
- リパースポイント
- 復元先外へのリンク

### 23.2 結合テスト

- サンプルChromeプロファイル2件のバックアップ／復元。
- サンプルEdgeプロファイル2件。
- サンプルFirefoxプロファイル2件。
- 3ブラウザー混在バックアップ。
- 途中ファイル読み取り失敗。
- 空き容量不足。
- バックアップ先が取り外される。
- ブラウザーが途中起動。
- 復元前ロールバック。
- 復元失敗後のロールバック。
- 旧フォーマット読取。
- 不明な新フォーマット拒否。
- ステージングACL確認。
- クラッシュ後清掃。

### 23.3 手動受け入れテスト

各ブラウザーの現行安定版で次を確認。

1. プロファイルを2件以上作成。
2. 各プロファイルへ異なるブックマーク、履歴、拡張機能、テスト用パスワードを用意。
3. アプリが表示名と内部ディレクトリーを正しく識別。
4. 各プロファイルの公式画面を正しく開く。
5. CSVを誤ったプロファイルへ紐付けない。
6. バックアップ後に元プロファイルを変更。
7. 同一環境復元で選択データが戻る。
8. 新規Windowsユーザーまたは検証VMで別環境移行。
9. パスワードCSVを公式UIでインポート。
10. 平文CSVが残存していないことを確認。
11. Cookieやセッションが移らない場合に、仕様どおり警告されている。
12. 改ざんバックアップが復元されない。
13. 復元前ロールバックで元状態へ戻せる。

テスト用の認証情報だけを使用し、実在サービスの本番パスワードを開発・CIで使用しない。

---

## 24. CI

- `dotnet restore`
- `dotnet build --configuration Release`
- `dotnet test --configuration Release`
- 警告を原則エラー扱い。
- Nullable有効。
- TreatWarningsAsErrors有効。ただし既知の生成コードは個別対応。
- フォーマットチェック。
- 依存パッケージ脆弱性チェック。
- 秘密情報スキャン。
- Release成果物のSHA-256生成。
- Windows x64上で統合テスト。
- UIを必要とする手動テストはCIから分離。

CIログにもプロファイルやCSVの実データを出さない。

---

## 25. コーディング規約

- C#最新安定機能を使用可能。ただし可読性を優先。
- Nullable Reference Typesを有効化。
- すべてのI/O APIはCancellationTokenを受け取る。
- 長時間処理は進捗インターフェースを持つ。
- 例外を握りつぶさない。
- ドメイン例外とI/O例外を分離。
- ファイルパス比較はWindowsの大文字小文字非区別を考慮。
- ユーザー入力を直接パスへ連結しない。
- 暗号鍵をフィールドへ長期間保持しない。
- 秘密を持つ型の `ToString()` を実装しない。
- データモデルをログへ構造化出力する際、秘密フィールドを明示除外。
- ブラウザー固有処理はアダプターに隔離。
- 破壊的処理にはDry Run相当の計画表示を設ける。
- 公開APIへXMLコメントを付ける。
- セキュリティ判断にはテストと短いコメントを付ける。
- 「なぜ必要か」が分からない暗号・パス処理を導入しない。

---

## 26. バージョニング

- アプリ: Semantic Versioning。
- バックアップフォーマット: 独立した整数バージョン。
- 読み取り側はサポート済み旧バージョンを明示。
- 新しい未知フォーマットを推測で開かない。
- フォーマット変更時は `docs/backup-format.md` を更新。
- 暗号パラメーターはヘッダーへ保存。
- KDF反復回数は将来増加できる。
- 旧バックアップを新パラメーターへ再暗号化する機能は将来検討。

---

## 27. 実装フェーズ

### Phase 0: リポジトリーと設計

- ソリューション作成
- プロジェクト分割
- AGENTS.md
- README
- Apache License 2.0 `LICENSE`および`NOTICE`
- threat-model.md
- backup-format.mdの初版
- CI
- テスト基盤

完了条件:

- Releaseビルド成功
- テスト成功
- 外部依存一覧が明確
- セキュリティ禁止事項がリポジトリーに記録

### Phase 1: ブラウザー／プロファイル検出

- Chrome検出
- Edge検出
- Firefox検出
- 複数プロファイルUI
- 容量推定
- 実行中プロセス検出

完了条件:

- テストフィクスチャと実ブラウザーで複数プロファイルを正しく列挙
- 破損メタデータでクラッシュしない
- 他ユーザー領域を探索しない

### Phase 2: 暗号化コンテナ

- KDF
- チャンクAES-GCM
- エントリーコンテナ
- マニフェスト
- ハッシュ
- 完全性検証
- `.partial` とアトミック完成
- キャンセル

完了条件:

- 改ざん、切断、誤パスフレーズを拒否
- 大容量をストリーミング処理
- 平文アーカイブを作成しない

### Phase 3: プロファイルバックアップ

- 除外ルール
- バックアップ計画
- 進捗
- 警告
- 3ブラウザー混在
- バックアップ検証

完了条件:

- 実プロファイルのバックアップを作成・検証
- ブラウザー起動中は開始しない
- キャッシュを既定除外

### Phase 4: パスワードCSVアシスタント

- ステージングACL
- プロファイル別ブラウザー起動
- CSV検出
- ヘッダー検証
- 一時暗号化
- 平文削除
- 復元時の一時復号
- 公式インポート画面起動

完了条件:

- パスワード値をログへ出さない
- 複数プロファイルを取り違えない
- キャンセル・クラッシュ清掃が動作
- ブラウザー保護を回避するコードが存在しない

### Phase 5: 復元とロールバック

- バックアップ読取
- 対応付けUI
- 同一環境ID
- 一時展開
- 安全検証
- ロールバック
- コンポーネント復元
- パスワードインポートアシスタント

完了条件:

- Path Traversalテスト成功
- 復元失敗時に元状態へ戻せる
- 別環境では警告と安全な既定値
- ブラウザー起動中に書き込まない

### Phase 6: ハードニングとリリース

- セキュリティレビュー
- 依存ライセンスレビュー
- アクセシビリティ
- 日本語表現
- エラーコード
- 手動テスト
- ポータブルRelease生成
- ハッシュ生成
- 操作マニュアル

---

## 28. MVP受け入れ条件

以下をすべて満たしたときMVP完成とする。

1. Chrome、Edge、Firefoxを検出できる。
2. 各ブラウザーで2件以上のプロファイルを検出・選択できる。
3. 1つの `.bvb` に複数ブラウザー・複数プロファイルを保存できる。
4. バックアップ全体がパスフレーズで暗号化される。
5. 誤パスフレーズ、改ざん、切断を確実に拒否する。
6. 平文ZIPや平文プロファイル一式を一時作成しない。
7. 保存パスワードを直接復号するコードがない。
8. 各プロファイルの公式パスワードエクスポート画面を開けるか、失敗時に明確な手順を表示する。
9. CSVを短時間で一時暗号化し、平文削除を試みる。
10. 複数プロファイルのCSVを取り違えない。
11. 同一環境へのフル復元ができる。
12. 別環境移行でターゲットプロファイルへ復元できる。
13. 保存パスワードを公式CSVインポートで復元できる。
14. 復元前ロールバックを必須にできる。
15. Path Traversal、リパースポイント、改ざんバックアップを拒否する。
16. ブラウザー起動中のバックアップ／復元を拒否する。
17. ログへ認証情報、URL、CSV内容を出力しない。
18. オフラインで動作する。
19. テレメトリーを送信しない。
20. Releaseビルド、テスト、手動受け入れテストが完了している。

---

## 29. Codexへの実装上の指示

- いきなり全機能を一括実装しない。
- Phase 0から順に、小さなレビュー可能な変更へ分割する。
- 各フェーズ開始時に実装計画と変更予定ファイルを示す。
- 各フェーズ終了時にビルド、テスト、自己レビューを行う。
- 不明なブラウザー内部仕様を推測で固定しない。
- 公式資料または実ファイルの観察結果を、テストフィクスチャとADRへ残す。
- ブラウザーのパスワード保護を回避する提案が必要になった場合は実装を停止し、公式CSV方式へ戻す。
- 暗号化コンテナはUIより先にテスト主導で実装する。
- 復元処理はバックアップ処理より厳しい入力検証を行う。
- 復元時は常にロールバック可能性を優先する。
- 外部パッケージを追加する前に、標準ライブラリで実現できない理由とライセンスを提示する。
- セキュリティ上重要なコードのテストを省略しない。
- 完成を宣言する前に、本仕様のMVP受け入れ条件をチェックリスト形式で報告する。

---

## 30. 公式資料

実装時は最新内容を再確認すること。

- Chrome: パスワードのインポート／エクスポート  
  https://support.google.com/chrome/answer/13068232

- Chrome: 複数プロファイル管理  
  https://support.google.com/chrome/answer/2364824

- Chromium: User Data Directory  
  https://www.chromium.org/user-experience/user-data-directory/

- Chrome Enterprise: UserDataDirポリシー  
  https://chromeenterprise.google/policies/user-data-dir/

- Microsoft Edge: お気に入り・パスワードのインポート  
  https://support.microsoft.com/en-us/edge/import-your-favorites-and-passwords-in-microsoft-edge

- Microsoft Edge: パスワードのエクスポート  
  https://support.microsoft.com/en-us/edge/export-passwords-in-microsoft-edge

- Microsoft Edge: User Data Directory  
  https://learn.microsoft.com/en-us/deployedge/edge-learnmore-create-user-directory-vars

- Mozilla Firefox: ログイン情報のエクスポート  
  https://support.mozilla.org/en-US/kb/export-login-data-firefox

- Mozilla Firefox: ログイン情報のインポート  
  https://support.mozilla.org/en-US/kb/import-login-data-file

- Mozilla Firefox: プロファイル管理  
  https://support.mozilla.org/en-US/kb/profile-management

- Mozilla Firefox: プロファイルの手動バックアップ／復元  
  https://support.mozilla.org/en-US/kb/back-and-restore-information-firefox-profiles

- .NETサポートポリシー  
  https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core

- Codex: AGENTS.md  
  https://developers.openai.com/codex/agent-configuration/agents-md

- Codex: Best practices  
  https://developers.openai.com/codex/learn/best-practices
