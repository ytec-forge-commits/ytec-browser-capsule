# Code signing policy / コード署名方針

## Current status

The republished direct release of version 1.2.1 uses the existing approved Y-TEC self-signed Authenticode provider, as approved by the owner on 2026-09-29. SignPath is not the current provider. No application or reapplication is part of this release.

今回の1.2.1再公開版は、2026-09-29の承認に基づき、既存のY-TEC自己署名Authenticode署名を使用します。SignPathは現在の署名提供元ではなく、申請・再申請は今回の作業に含みません。

Self-signing is not commercial CA trust and does not guarantee removal of SmartScreen warnings. No certificate is installed automatically. SHA-256 proves file identity, not publisher trust or application safety.

自己署名は商用CAによる標準信頼ではありません。SmartScreen警告の解消を保証せず、証明書を自動登録しません。SHA-256はファイルの同一性確認であり、署名や安全性の代替ではありません。

## Key custody / 鍵の保管

- 既存の承認済み署名境界だけを利用し、秘密鍵の生成、移動、複製、export、署名経路の再設定を行いません。
- 秘密鍵、秘密鍵パスワード、署名認証情報をWorkspace、GitHub repository、GitHub Secrets、ログ、CI Artifact、ポータブルZIPへ保存・出力しません。
- 公開用CERを提供する場合は公開鍵だけを含み、利用者の証明書ストアへ自動登録しません。
- CIの未署名ビルドをそのまま公式配布物として公開しません。最終配布物を管理されたローカルRelease工程で署名・検証します。
- 署名前後の成果物を区別し、元ソース版、署名者一致、署名の完全性、検証結果、最終SHA-256を記録します。
- `.pfx`、`.p12`、`.pem`、`.key`、`.snk`等をcommitまたはReleaseへ含めません。

## Trusted release flow / 信頼済みリリース手順

1. 固定した現在版ソースからReleaseビルド、テスト、秘密情報・権利検査を行う。旧履歴、旧ZIP、実ブラウザーデータを持ち込まない。
2. 新しい日英マニュアルを生成し、自己完結型の署名前配布フォルダーを生成する。
3. 既存の承認済み署名境界で、署名可能な最終Y-TEC EXE・DLLを署名する。第三者の既存署名を無断で置換しない。
4. 全署名の完全性と承認済み署名者の一致を確認する。未署名、改ざん、期限外、署名者不一致、想定外の検証結果は公開を停止する。自己署名由来のルート非信頼と改ざんを区別し、許容条件と実際の検証結果をRelease検証記録へ残す。信頼設定は変更しない。
5. 署名後のバイナリを変更せず、最終ZIP、展開後`SHA256SUMS.txt`、最終ZIPの`.sha256`を生成・照合する。ZIPとPDFを個別に秘密情報検査する。
6. ユーザー確認後に、新しい公開ソースと同じ版の検証済み成果物をGitHub ReleaseとY-TEC Forgeへ公開し、ダウンロード後のSHA-256を確認する。

## Incident response / インシデント対応

署名の不正利用、Artifact取り違え、アカウント侵害が疑われる場合は公開を停止し、影響するReleaseを明示して所有者へ報告します。鍵の失効・再発行、アカウント設定や認証情報の変更は別途明示承認を得て行います。秘密鍵を取得・移送・exportして調査する運用は行いません。
