# Third-Party Notices

## 配布アプリ本体

`1.2.1`時点で、`src/`配下の本体プロジェクトに外部NuGetパッケージはありません。.NET 10およびWPFの標準ライブラリだけを参照しています。

自己完結型配布にはMicrosoft .NET RuntimeとWindows Desktop Runtimeの再頒布可能コードが含まれます。配布スクリプトは、使用したSDKに付属する`LICENSE.txt`と`ThirdPartyNotices.txt`を、それぞれ`DOTNET-LICENSE.txt`と`DOTNET-THIRD-PARTY-NOTICES.txt`として配布物へ同梱します。これらのファイルを削除せず、内容を配布時点のSDKから再生成してください。

## テスト専用NuGetパッケージ

これらは開発・CIでのみ使用し、アプリ本体へ同梱しません。直接参照のバージョンは`Directory.Packages.props`を正本とします。

| パッケージ | バージョン | ライセンス | 用途 |
| --- | --- | --- | --- |
| Microsoft.NET.Test.Sdk | 17.14.1 | MIT | `dotnet test`とテストホスト |
| xunit | 2.9.3 | Apache-2.0 | テスト記述とアサーション |
| xunit.runner.visualstudio | 3.1.4 | Apache-2.0 | VSTestからxUnitテストを検出・実行 |

2026-07-24に復元結果のNuSpecを確認した推移的依存は次のとおりです。すべて開発・CI専用です。

| 推移的パッケージ | バージョン | ライセンス |
| --- | --- | --- |
| Microsoft.CodeCoverage | 17.14.1 | MIT |
| Microsoft.TestPlatform.ObjectModel | 17.14.1 | MIT |
| Microsoft.TestPlatform.TestHost | 17.14.1 | MIT |
| Newtonsoft.Json | 13.0.3 | MIT |
| xunit.abstractions | 2.0.3 | Apache-2.0（NuSpecはプロジェクトのライセンスURLを参照） |
| xunit.analyzers | 1.18.0 | Apache-2.0 |
| xunit.assert | 2.9.3 | Apache-2.0 |
| xunit.core | 2.9.3 | Apache-2.0 |
| xunit.extensibility.core | 2.9.3 | Apache-2.0 |
| xunit.extensibility.execution | 2.9.3 | Apache-2.0 |

実際の復元結果は`dotnet package list --project YtecBrowserCapsule.sln --include-transitive`で再確認できます。`src/`へ新しいPackageReferenceを追加する場合は、一般配布へ含まれるライセンスとnoticeを事前に更新します。

## CI専用GitHub Actions

これらはCI実行時だけ使用し、配布アプリへ同梱しません。

| Action | メジャーバージョン | ライセンス | 用途 |
| --- | --- | --- | --- |
| actions/checkout | v7 | MIT | ソース取得 |
| actions/setup-dotnet | v6 | MIT | .NET SDK準備 |
| actions/upload-artifact | v7 | MIT | Release成果物とSHA-256一覧の保存 |
| github/codeql-action | v4 | MIT | C#の静的セキュリティ解析 |

workflowでは各Actionのレビュー済みタグに対応する完全長commit SHAへ固定します。各依存の著作権表示とライセンス本文は、それぞれの配布元を正本とします。本ファイルは依存関係の存在と利用目的を明示するための一覧です。

## ローカル受け入れ試験だけで使う外部ソフトウェア

次のソフトウェアは`.validation`配下の隔離された試験環境だけで使い、
本アプリのソース、ポータブルRelease、一般配布ZIPへ含めません。

| ソフトウェア | 用途 | 配布方針 |
| --- | --- | --- |
| Oracle VirtualBox / Guest Additions | 旧版のWindows 10／11クリーンVM検証（歴史資料） | アプリと再配布しない。現行VM手順ではない |
| Microsoft Windows 10／11 ISO | OS互換性試験 | Microsoft公式取得物をローカル利用し、再配布しない |
| Google Chrome Enterprise MSI | 署名・配布元確認 | Google公式取得物を検証VMへ入れるだけで、再配布しない |
| Google Chrome for Testing | 実Chromeプロファイル試験 | Google公式のテスト専用取得物を検証VMへ展開するだけで、再配布しない |
| Mozilla Firefox ESR | 実ブラウザープロファイル試験 | Mozilla公式取得物を検証VMへ入れるだけで、再配布しない |

これらはアプリの実行時依存ではありません。検証用ダウンロードの
SHA-256とAuthenticode署名はローカル証拠として確認しますが、
第三者インストーラー自体はRelease成果物へコピーしません。

Chrome for Testingは、通常利用者向けChromeを一般配布物へ組み込む
目的ではなく、ネットワークを切ったクリーンVMで実プロファイルを
作る受け入れ試験だけに使います。取得URLとバージョンは
[Chrome for Testing公式JSON](https://googlechromelabs.github.io/chrome-for-testing/last-known-good-versions-with-downloads.json)
から確認し、ダウンロード後のSHA-256を検証証拠へ固定します。
