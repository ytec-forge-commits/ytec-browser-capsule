## 概要 / Summary


## 変更内容 / Changes


## セキュリティ境界 / Security boundary

- [ ] 復元キー、証明書、トークン、実ブラウザーデータ、個人情報を含まない
- [ ] 保存パスワード直接復号、DPAPI/App-Bound Encryption回避、認証情報DB解析を追加していない
- [ ] 日英UIとアクセシビリティ文言を確認した（UI変更時）

## 検証 / Verification

- [ ] Release build
- [ ] All tests
- [ ] `dotnet format --verify-no-changes`
- [ ] NuGet vulnerability check
- [ ] `eng/Test-Secrets.ps1 -IncludeGitHistory`

## 影響と残課題 / Impact and remaining work
