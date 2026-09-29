# Contributing / コントリビューション

Y-TEC Browser Capsuleへの提案ありがとうございます。変更前に[SECURITY.md](SECURITY.md)と[AGENTS.md](AGENTS.md)を確認してください。

## Pull request checklist

- 変更目的と利用者への影響を説明する
- 実データではなく合成プロファイルと合成CSVだけで検証する
- 保存パスワード直接復号、DPAPI回避、App-Bound Encryption回避、認証情報DB解析を追加しない
- 新しい依存関係が必要な場合は、理由、代替案、ライセンス、配布物への影響を記載する
- UI文言を追加した場合は日本語・英語・アクセシビリティ名をそろえる
- 保存形式を変更する場合は、版番号、旧形式読込、失敗時復旧、回帰テストを含める
- Releaseビルド、全テスト、format、脆弱性確認、秘密情報・鍵スキャンを実行する
- 復元キー、証明書、トークン、個人情報、実ブラウザーデータをcommitしない

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' restore .\YtecBrowserCapsule.sln
& 'C:\Program Files\dotnet\dotnet.exe' build .\YtecBrowserCapsule.sln -c Release --no-restore
& 'C:\Program Files\dotnet\dotnet.exe' test .\YtecBrowserCapsule.sln -c Release --no-build
& 'C:\Program Files\dotnet\dotnet.exe' format .\YtecBrowserCapsule.sln --verify-no-changes --no-restore
& 'C:\Program Files\dotnet\dotnet.exe' package list --project .\YtecBrowserCapsule.sln --vulnerable --include-transitive
.\eng\Test-Secrets.ps1 -IncludeGitHistory
```

English contributions are welcome. Please preserve the Japanese UI and documentation alongside English changes. Never include live browser profiles, password exports, recovery keys, signing keys, tokens, or personal information in a pull request.
