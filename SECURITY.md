# Security Policy / セキュリティポリシー

## Supported version / サポート対象

| Version | Supported |
| --- | --- |
| 1.2.x | Yes |
| 1.1.x and earlier | Security fixes are not guaranteed |

## Report a vulnerability / 脆弱性の報告

公開Issueには、復元キー、バックアップ、保存パスワードCSV、認証情報、実プロファイルパス、個人情報、悪用手順を投稿しないでください。

GitHubの[Private vulnerability reporting](https://github.com/ytec-forge-commits/ytec-browser-capsule/security/advisories/new)を使用し、次の情報だけを安全な範囲で共有してください。

- 影響するバージョン
- Windowsとブラウザーの版
- 再現条件と期待動作
- 秘密値を除去した最小限のログまたは合成データによる再現
- 想定される影響

Do not post recovery keys, backups, saved-password CSV files, credentials, real profile paths, personal data, or weaponized instructions in a public issue. Use [GitHub private vulnerability reporting](https://github.com/ytec-forge-commits/ytec-browser-capsule/security/advisories/new) and provide only sanitized, minimal reproduction details.

## Security boundary / 受け付けない変更

次を追加する変更は、このプロジェクトの対象外です。

- 保存パスワードやCookieの直接復号
- DPAPIまたはApp-Bound Encryptionの回避
- ブラウザー認証情報データベースの解析または直接書き込み
- 本人確認、OS／ブラウザー承認、サインインの自動化
- 管理者権限、ACL回避、所有権取得、別ユーザー偽装
- 復元キー、証明書秘密鍵、トークン等のソースまたは配布物への埋め込み

## Coordinated handling / 対応方針

報告を受領した場合は、影響範囲と再現性を確認し、必要に応じて修正版、回避策、Security Advisoryを準備します。秘密情報が誤って公開された場合は、値を再掲せず、ただちに失効・ローテーションと履歴除去の要否を判断します。
