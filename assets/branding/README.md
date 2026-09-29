# ブランド素材

## 正本

- `YtecBrowserCapsule-icon.png`: 1024x1024、RGBA透過PNG。Web掲載用の正本
- `YtecBrowserCapsule-icon-preview.png`: 256px〜16pxの明暗背景確認
- `YtecBrowserCapsule-icon-ai-source.png`: 画像生成時のマゼンタ背景原本
- `YtecBrowserCapsule-icon-transparent-source.png`: クロマキー除去後の原寸原本
- `../../src/Ytec.BrowserCapsule.App/Assets/YtecBrowserCapsule.ico`:
  Windowsアプリへ埋め込む16px〜256pxの複数解像度ICO

## 意匠

3つの色付きドットをChrome、Edge、Firefoxの複数プロファイルに見立て、
下向きの印とカプセル形状で「複数プロファイルを1つの暗号化バックアップへ
まとめる」機能を表します。ブラウザーのロゴ、商標、人物、鍵、南京錠、
パスワード、データベースの意匠は使用していません。

配色:

- 濃紺: `#172B4D`
- 青: `#3267E3`
- 淡い青: `#C8DCFF`
- ミント: `#51C88A`
- アンバー: `#F2A93B`
- 白

## 作成記録

2026-07-26にOpenAIの組み込み画像生成で作成しました。生成原本の外周は
均一な`#FF00FF`とし、ローカルのクロマキー除去後、Lanczos補間で
1024px PNGとWindows用ICOへ変換しました。小サイズは明背景と濃色背景で
目視確認しています。

最終生成指示:

> Y-TEC Browser CapsuleのWindowsデスクトップアプリアイコン。
> 濃紺の角丸正方形の中に、ミント、アンバー、淡い青の3つの丸、
> 太い白の下向き記号、白と青のカプセルを中央配置する。
> 16x16でも認識できる太く単純な形とし、文字、人物、ブラウザーロゴ、
> 商標、鍵、南京錠、パスワード、データベースは使用しない。

これらは本プロジェクト用のオリジナル素材として作成し、リポジトリの
Apache License 2.0と同じ条件で配布します。
