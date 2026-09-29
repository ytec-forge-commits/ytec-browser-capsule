# Authenticode署名: Y-TEC自己署名の直接配布

## 現在の状態

運用正本は[CODE_SIGNING.md](../CODE_SIGNING.md)です。今回の1.2.1再公開版は、
承認済みの既存Y-TEC自己署名を使用します。署名提供元への新規申請や再申請、
秘密鍵の生成・移動・export、アカウント設定変更は行いません。

## 秘密鍵の保護

- 既存の承認済み署名境界だけを利用し、秘密鍵を取り出さない。
- PFX、PFXパスワード、PEM、秘密鍵、署名認証情報をWorkspace、repository、
  GitHub Secrets、ログ、CI Artifact、配布ZIPへ保存・出力しない。
- 公開用CERを提供する場合は公開鍵だけを含み、証明書を自動登録しない。

## 配布工程

1. 固定した現在版ソースと依存関係を識別し、build・test・秘密情報検査を行います。
2. 新しい日英マニュアルと署名前のポータブル配布フォルダーを生成します。
3. 既存の承認済み署名境界で最終Y-TEC EXE・DLLを署名します。
4. 署名の完全性、承認済み署名者との一致、期限、検証結果を確認します。
5. 署名済みファイルを変更せず最終ZIPとSHA-256を生成し、ZIP・PDFを別途検査します。
6. 公開承認後にGitHub ReleasesとForgeへ同じ最終成果物を配置し、再取得して照合します。

`New-PortableRelease.ps1`単体の出力は署名前の中間成果物であり、公式配布物ではありません。
`Complete-AuthenticodeRelease.ps1`は既存補助スクリプトです。使用前に選択した
プロバイダ、署名対象、検証条件、出力先との整合を確認してください。
スクリプト名だけで全EXE・DLLの署名完了とみなしてはいけません。

## 利用者の確認

正式なForgeページまたはGitHub Releasesから取得し、公開SHA-256と照合します。

```powershell
Get-FileHash .\YtecBrowserCapsule-1.2.1-win-x64.zip -Algorithm SHA256
Get-AuthenticodeSignature .\YtecBrowserCapsule.exe
```

自己署名は商用CAの標準信頼済み証明書ではありません。証明書を信頼していない
環境でのルート非信頼と、署名の破損・改ざん・署名者不一致を区別します。
署名が存在するだけで安全性は保証されず、SmartScreen警告が消えるとも保証しません。
利用者の信頼設定を自動変更しません。秘密鍵は配布物・文書へ含めません。

## チャネルと更新

Microsoft Store版は未公開です。現在はポータブル用途の直接配布のみを扱い、
ForgeまたはGitHub Releasesから新しいZIPを別フォルダーへ展開して手動更新します。
独自Updater、Store設定、MSIX化、保存データ形式の変更は今回の対象外です。
