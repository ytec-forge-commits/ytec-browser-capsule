[English](README.en.md)

# Y-TEC Browser Capsule

Chrome、Microsoft Edge、Firefox / Firefox ESRの複数プロファイルを、暗号化バックアップへ保存し、別のWindows PCへ復元するためのオープンソース・デスクトップアプリです。

- 正式バージョン: **1.2.1**
- 2026-09-29再公開候補には、保護対象ファイルを含むバックアップを復元前に拒否する安全修正を含みます。BVB v1/v2・復元キー・承認済みマニュアルの形式は変更しません。
- 対応OS: Windows 10 / 11（64-bit）
- 配布形態: インストール不要の自己完結型ポータブルZIP
- ライセンス: [Apache License 2.0](LICENSE)
- 対応言語: 日本語 / English / システム既定

スマートフォン、Windows 7 / 8 / 8.1、32-bit Windows、macOS、Linuxには対応しません。

## 主な機能

- Chrome Stable、Edge Stable、Firefox Stable / ESRの複数プロファイルを検出
- 明示選択時だけ、現在の権限で読み取れる他Windowsユーザーのプロファイルも検出
- 設定、ブックマーク、履歴、拡張機能、Cookie／サイトデータ、セッション、プロファイル全体を選択
- 複数ブラウザー／複数プロファイルを1つの暗号化`.bvb`へ保存
- バックアップごとに256-bitランダム復元キー`.ybckey`を生成し、文字入力なしで別PCへ復元
- 旧BVB v1は従来のパスフレーズで検証・復元可能
- 通常終了後も残る対象ブラウザープロセスを、本人確認後に限定して終了支援
- ブラウザー公式画面を使う保存パスワードCSVのエクスポート／インポート補助
- CSVの書き込み完了と形式を確認後、一時暗号化して平文削除を試行
- 保存パスワードがないプロファイルは、CSV待機中でもスキップ可能
- 復元元と復元先を1件ずつ明示対応付けし、復元前に暗号化ロールバックを作成
- 暗号タグ、マニフェスト、ファイルSHA-256による作成後・復元前の完全検証
- アプリ内から日英PDF操作マニュアルを表示

## 復元キーを最優先で保護してください

新規バックアップは次の2ファイルで1組です。

- `.bvb`: 暗号化されたバックアップ本体
- `.ybckey`: 復元に必要な秘密のキーファイル

どちらか一方を失うと復元できません。両方を取得した第三者は、バックアップを復元できる可能性があります。可能なら`.bvb`と`.ybckey`を別のドライブまたは承認済み秘密保管場所へ分けてください。

復元キーをソースコード、Issue、スクリーンショット、メール本文、チャット、WordPress、公開共有リンクへ貼り付けないでください。Y-TECやアプリが紛失した復元キーを再発行することはできません。

実装ではバックアップごとに`RandomNumberGenerator`から新しい鍵・salt・nonceを生成します。固定鍵、共有マスターキー、配布物へ埋め込む秘密鍵はありません。公開前CIでは追跡ファイルとGit全履歴に対して、秘密鍵形式、代表的なトークン形式、`.ybckey`、`.pfx`、`.pem`等の混入を検査します。

詳細は[脅威モデル](docs/threat-model.md)、[BVB形式](docs/backup-format.md)、[セキュリティポリシー](SECURITY.md)を参照してください。

## セキュリティ境界

本アプリは次を実装しません。

- 保存パスワードの直接復号
- DPAPIまたはApp-Bound Encryptionの回避
- Chrome / Edgeの認証情報データベース解析
- Firefoxの`logins.json`／`key4.db`からの認証情報抽出
- ブラウザー認証情報データベースへの直接書き込み
- ブラウザーへのコード注入、本人確認、OS／ブラウザー承認の自動操作
- 管理者権限、ACL変更、所有権取得、別ユーザーへの偽装
- テレメトリー、アクセス解析、広告、クラウド同期、自動アップデート
- 平文の集約ZIP

保存パスワードは、利用者本人がブラウザー公式UIでCSVへエクスポート／インポートします。本アプリはCSV値、URL、ユーザー名、パスワードを画面やログへ表示せず、インポート成否を推測しません。

## プライバシー

アプリはローカルで動作し、利用者が明示的に要求しない限りネットワーク通信を行いません。設定ファイルには既定バックアップ先と言語設定だけを保存し、復元キー、パスフレーズ、ブラウザーデータ、認証情報は保存しません。

ネットワーク動作に関する表明:

> This program will not transfer any information to other networked systems unless specifically requested by the user or the person installing or operating it.

詳細は[PRIVACY.md](PRIVACY.md)を参照してください。

## ダウンロードと真正性確認

一般配布ZIPは[GitHub Releases](https://github.com/ytec-forge-commits/ytec-browser-capsule/releases)と[Y-TEC Forge](https://ytec.cloudfree.jp/forge/projects/browser-capsule/)で公開します。展開前に、ZIP横の`.sha256`と公開ページのSHA-256が一致することを確認してください。

今回の直接配布版は、承認済みのY-TEC自己署名Authenticode署名を使用し、最終署名とパッケージング後にSHA-256を生成します。署名方針は[CODE_SIGNING.md](CODE_SIGNING.md)を参照してください。商用CAの信頼済み証明書ではないため、SmartScreen警告が消えることは保証しません。証明書を自動登録しません。

Microsoft Store版は未公開です。ForgeまたはGitHub Releasesから手動で更新してください。連絡先: https://ytec.cloudfree.jp/forge/contact/

## 開発

必要環境:

- Windows 10 / 11 x64
- .NET SDK 10.0.302以降の10.0 feature band
- PowerShell 7

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' restore .\YtecBrowserCapsule.sln
& 'C:\Program Files\dotnet\dotnet.exe' build .\YtecBrowserCapsule.sln -c Release --no-restore
& 'C:\Program Files\dotnet\dotnet.exe' test .\YtecBrowserCapsule.sln -c Release --no-build
& 'C:\Program Files\dotnet\dotnet.exe' format .\YtecBrowserCapsule.sln --verify-no-changes --no-restore
& 'C:\Program Files\dotnet\dotnet.exe' package list --project .\YtecBrowserCapsule.sln --vulnerable --include-transitive
.\eng\Test-Secrets.ps1 -IncludeGitHistory
```

自己完結型ポータブルRelease:

```powershell
.\eng\New-PortableRelease.ps1 -Version 1.2.1
.\eng\Test-PortableRelease.ps1 -Version 1.2.1
```

本体`src/`に外部NuGet依存はありません。.NET 10 / WPF標準ライブラリだけを参照します。テスト専用依存とライセンスは[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)に記載しています。

## 文書

- [日本語PDF操作マニュアル](output/pdf/Y-TEC_Browser_Capsule_操作マニュアル_1.2.1.pdf)
- [English PDF User Manual](output/pdf/Y-TEC_Browser_Capsule_User_Manual_1.2.1.pdf)
- [操作マニュアル（Markdown）](docs/operations-manual.md)
- [アーキテクチャ](docs/architecture.md)
- [脅威モデル](docs/threat-model.md)
- [BVB暗号化バックアップ形式](docs/backup-format.md)
- [エラーコード](docs/error-codes.md)
- [手動テスト計画](docs/manual-test-plan.md)
- [Release準備状況](docs/release-readiness.md)
- [コード署名方針](CODE_SIGNING.md)
- [設計判断（ADR）](docs/adr/)

## コントリビューションとライセンス

変更提案は[CONTRIBUTING.md](CONTRIBUTING.md)と[SECURITY.md](SECURITY.md)を先に確認してください。認証回避や保存パスワード直接抽出を追加する提案は受け付けません。

ソースコードは[Apache License 2.0](LICENSE)です。著作権表示は[NOTICE](NOTICE)、自己完結型配布へ同梱する.NET Runtime等の表示は[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)に従います。
