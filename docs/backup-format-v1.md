# BVB暗号化バックアップ形式 v1

## 状態

Phase 2〜5で実装・テスト済みのフォーマットバージョン1です。拡張子は`.bvb`、作成途中は`.bvb.partial`です。Y-TEC Browser Capsuleが1.0へ到達する前でも、互換性を壊す変更ではフォーマットバージョンを変更します。

## 暗号方式

- 暗号: AES-256-GCM
- KDF: PBKDF2-HMAC-SHA256
- Salt: バックアップごとに暗号学的乱数32バイト
- PBKDF2: 作成端末で約750msを目標に校正、最低600,000回、読取上限20,000,000回
- 既定チャンク: 4 MiB（許容64 KiB〜16 MiB）
- nonce: バックアップごとの乱数4バイト + 64-bitチャンク番号
- tag: 16バイト
- AAD: `SHA-256(平文ヘッダー82バイト)` + チャンクフレームヘッダー16バイト
- ファイルハッシュ: SHA-256

パスフレーズは8文字以上とし、長い文章形式を推奨します。UTF-8へ変換した一時バイト列と派生鍵は使用後にゼロ化します。パスフレーズ、派生鍵、平文アーカイブはディスクへ保存しません。

## 数値と文字列

- 特記のない整数はlittle-endian。
- nonceの8バイトチャンク番号だけはbig-endian。
- UUIDはRFC 4122のbig-endianバイト順。
- パスとJSONはBOMなしUTF-8。不正UTF-8は拒否。

## 平文ヘッダー

固定長82バイトです。

| Offset | Size | 内容 |
| ---: | ---: | --- |
| 0 | 8 | Magic `YTECBVB\0` |
| 8 | 2 | フォーマットバージョン（1） |
| 10 | 2 | ヘッダー長（82） |
| 12 | 2 | KDF ID（1 = PBKDF2-HMAC-SHA256） |
| 14 | 2 | Cipher ID（1 = AES-256-GCM） |
| 16 | 32 | Salt |
| 48 | 4 | PBKDF2反復回数 |
| 52 | 4 | 平文チャンクサイズ |
| 56 | 4 | nonce prefix |
| 60 | 16 | Backup ID |
| 76 | 2 | 最小アプリmajor |
| 78 | 2 | 最小アプリminor |
| 80 | 2 | 最小アプリbuild |

PC名、Windowsユーザー表示名、Windowsユーザー安定識別子、プロファイル名、パス、ファイル一覧、コンポーネント、警告、同一環境IDはこのヘッダーへ入れず、暗号化マニフェストだけへ保存します。安定識別子はSIDまたはプロファイルパスの一方向ハッシュで、SIDそのものは保存しません。

## 暗号チャンク

ヘッダー直後に次のフレームを連続して格納します。

```text
uint64 chunkIndex
uint32 plaintextLength
byte   flags        // bit 0 = final
byte[3] reserved    // すべて0
byte[plaintextLength] ciphertext
byte[16] tag
```

- 最初のチャンク番号は0で、1ずつ増加する。
- 非最終チャンクの平文長はヘッダーのチャンクサイズと一致する。
- 最終チャンクは必須で、平文長0を許可する。
- 最終チャンク後のデータを拒否する。
- nonceは`noncePrefix[4] || chunkIndexBigEndian[8]`。
- AADは`headerHash[32] || frameHeader[16]`。

この規則により、欠落、重複、順序変更、長さ／flags変更、末尾切断、末尾追加を拒否します。誤パスフレーズと改ざんは同じ一般エラーとして扱います。

## 暗号化された逐次エントリー領域

標準ZIPは復号読取時のシークを要求し得るため使用しません。圧縮なしの独自逐次コンテナを暗号チャンク内へ格納します。暗号アルゴリズムは独自設計していません。

### 内部ヘッダー

```text
byte[8] magic       // YBVENTR\0
uint16  version     // 1
uint16  reserved    // 0
uint32  entryCount
```

### ファイルエントリー

`entryCount`回、次を繰り返します。

```text
byte    marker      // 0x01
uint32  pathUtf8Length
uint64  contentLength
byte[pathUtf8Length] canonicalRelativePath
byte[contentLength] content
byte[32] sha256
```

入力ストリームは宣言長と一致する必要があります。作成中に短縮・伸長したファイルは失敗扱いにします。

### マニフェストと終端

```text
byte    marker      // 0x02
uint32  manifestJsonLength
byte[manifestJsonLength] manifestJson
byte    endMarker   // 0xFF
```

マニフェストにはFormat Version、Backup ID、作成日時、アプリバージョン、同一環境ID、プロファイル情報、コンポーネント、ファイル相対パス・長さ・SHA-256、CSV状態、警告を格納します。各ファイルは`settings`、`bookmarks`、`history`、`extensions`、`cookies`、`sessions`、`fullProfile`、`passwordCsv`のいずれかのコンポーネント名を持ちます。読取時は逐次エントリーとマニフェストの件数・パス・長さ・ハッシュ・コンポーネントが完全一致することを確認します。

## エントリーパス

全エントリーは`/`区切りの正規相対パスです。次を拒否します。

- 絶対パス、ドライブレター、UNC、`\`区切り
- `.`、`..`、空セグメント
- 末尾のドットまたは空白
- `:`を含むNTFS Alternate Data Stream指定
- 制御文字とWindowsで使用できない文字
- CON、NUL、AUX、PRN、COM1〜9、LPT1〜9
- 大文字小文字だけが異なる衝突
- UTF-8で4096バイト超、1セグメント255文字超

復元時はこの検証に加え、復元ルートと既存リパースポイントを再検証します。

## 上限

- エントリー数: 100,000
- 暗号化マニフェスト平文: 16 MiB
- PBKDF2反復回数: 600,000〜20,000,000
- 暗号チャンク: 64 KiB〜16 MiB
- 個別ファイル長: 64 GiB
- 全ファイル合計: 2 TiB

ファイル内容は128 KiB単位、暗号は設定チャンク単位で処理し、ファイル全体をメモリへ読み込みません。

## 完成手順

1. 完成先と同じディレクトリーの`.bvb.partial`を`CreateNew`で作成。
2. 平文ヘッダーを書き、逐次エントリーを暗号ストリームへ直接書き込む。
3. 最終チャンクとファイルバッファをディスクへflush。
4. `.partial`を先頭から再読取し、全GCM tag、チャンク列、内部構造、ファイルSHA-256、マニフェストを検証。
5. 検証成功後だけ、上書きなしの`File.Move`で`.bvb`へ切り替える。
6. 失敗・キャンセル時は`.partial`を削除し、既存の完成ファイルは変更しない。

## 検証済み展開

- 復元前に全暗号チャンク、全エントリー、マニフェストを最後まで認証する。
- 展開先は空のローカル通常フォルダーに限定し、リパースポイントを拒否する。
- 復元本体では選択コンポーネントのエントリーだけを書き出す。未選択エントリーも暗号・構造・ハッシュ検証は省略しない。
- 保存パスワードCSVはプロファイル本体の復元ステージングへ書き出さず、公式インポート操作を開始した場合だけACL制限領域へ選択展開する。
- 展開後のファイルはマニフェストの宣言長とSHA-256へ一致する。

## バージョン規則

- 読取側はversion 1だけを明示的に受理する。
- 未知の新旧バージョン、KDF ID、Cipher ID、ヘッダー長を推測で開かない。
- 新形式ではMagicを流用し、Format Versionを増やす。
