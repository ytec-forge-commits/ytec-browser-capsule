# Changelog

このプロジェクトは[Semantic Versioning](https://semver.org/)を使用します。

## [1.2.1] - 2026-08-25

### Changed

- プロジェクト本体のライセンスをMIT LicenseからApache License 2.0へ変更
- Apache License 2.0の再配布条件に合わせて`NOTICE`をソースと配布ZIPへ追加
- アプリ内表示、日英README、日英PDF、Forge掲載を新ライセンスへ統一

### Security

- v1.2.0のタグ・配布物・SHA-256は履歴として変更せず、v1.2.1を独立した配布物として作成

## [1.2.0] - 2026-08-25

### Added

- 日本語・英語・システム既定のUI言語設定
- English PDF User Manualと日本語1.2.0 PDF操作マニュアル
- 日英README、プライバシー、セキュリティ、コントリビューション、コード署名方針
- 公開前に追跡ファイルとGit全履歴を確認する秘密情報・鍵スキャン

### Changed

- 設定schemaをv2へ更新し、v1の既定バックアップ先を維持したまま言語設定を追加
- GitHub repositoryを`ytec-forge-commits/ytec-browser-capsule`へ移管
- 保存パスワードCSV補助画面の英語表示幅と完了ボタン文言を調整

### Security

- 復元キー、証明書秘密鍵、トークン、平文CSV、暗号化バックアップをGitへ含めない規則を強化
- 固定鍵を使用せず、バックアップごとの暗号学的乱数生成をCIで確認

## [1.1.1] - 2026-07-27

- 保存パスワードがないプロファイルをCSV待機中でもスキップ可能に修正

## [1.1.0] - 2026-07-27

- BVB v2復元キーファイル、ブラウザー終了支援、公式CSV自動待機、UI更新

## [1.0.0] - 2026-07-26

- 初回一般配布
