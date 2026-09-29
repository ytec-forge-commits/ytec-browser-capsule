from __future__ import annotations

import argparse
from pathlib import Path

from reportlab.lib import colors
from reportlab.lib.enums import TA_CENTER, TA_LEFT
from reportlab.lib.pagesizes import A4
from reportlab.lib.styles import ParagraphStyle, getSampleStyleSheet
from reportlab.lib.units import mm
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.platypus import (
    Image,
    KeepTogether,
    PageBreak,
    Paragraph,
    SimpleDocTemplate,
    Spacer,
    Table,
    TableStyle,
)


VERSION = "1.2.1"
PAGE_WIDTH, PAGE_HEIGHT = A4
PRIMARY = colors.HexColor("#6C5CE7")
NAVY = colors.HexColor("#1F3159")
TEAL = colors.HexColor("#24B89A")
INK = colors.HexColor("#17233C")
MUTED = colors.HexColor("#63708A")
LINE = colors.HexColor("#D9DDF0")
PALE = colors.HexColor("#F6F4FF")
WARN = colors.HexColor("#FFF4D8")
WARN_LINE = colors.HexColor("#F0B429")
SAFE = colors.HexColor("#E8F8F2")


def register_fonts() -> tuple[str, str]:
    regular = Path(r"C:\Windows\Fonts\meiryo.ttc")
    bold = Path(r"C:\Windows\Fonts\meiryob.ttc")
    if not regular.exists() or not bold.exists():
        raise FileNotFoundError("Meiryoフォントが見つかりません。")
    pdfmetrics.registerFont(
        TTFont("ManualJP", str(regular), subfontIndex=0)
    )
    pdfmetrics.registerFont(
        TTFont("ManualJP-Bold", str(bold), subfontIndex=0)
    )
    return "ManualJP", "ManualJP-Bold"


def fit_image(path: Path, max_width: float, max_height: float) -> Image:
    from PIL import Image as PillowImage

    with PillowImage.open(path) as source:
        width, height = source.size
    ratio = min(max_width / width, max_height / height)
    return Image(str(path), width=width * ratio, height=height * ratio)


def build_styles(font: str, bold: str) -> dict[str, ParagraphStyle]:
    base = getSampleStyleSheet()
    return {
        "cover_title": ParagraphStyle(
            "CoverTitle",
            parent=base["Title"],
            fontName=bold,
            fontSize=25,
            leading=34,
            textColor=colors.white,
            alignment=TA_CENTER,
            wordWrap="CJK",
        ),
        "cover_subtitle": ParagraphStyle(
            "CoverSubtitle",
            parent=base["BodyText"],
            fontName=font,
            fontSize=12,
            leading=19,
            textColor=colors.white,
            alignment=TA_CENTER,
            wordWrap="CJK",
        ),
        "page_title": ParagraphStyle(
            "PageTitle",
            parent=base["Heading1"],
            fontName=bold,
            fontSize=20,
            leading=27,
            spaceAfter=7 * mm,
            textColor=NAVY,
            wordWrap="CJK",
        ),
        "section": ParagraphStyle(
            "Section",
            parent=base["Heading2"],
            fontName=bold,
            fontSize=13,
            leading=19,
            spaceBefore=3 * mm,
            spaceAfter=2 * mm,
            textColor=PRIMARY,
            wordWrap="CJK",
        ),
        "body": ParagraphStyle(
            "Body",
            parent=base["BodyText"],
            fontName=font,
            fontSize=9.4,
            leading=16,
            spaceAfter=2.2 * mm,
            textColor=INK,
            wordWrap="CJK",
        ),
        "small": ParagraphStyle(
            "Small",
            parent=base["BodyText"],
            fontName=font,
            fontSize=8.2,
            leading=13,
            textColor=MUTED,
            wordWrap="CJK",
        ),
        "step": ParagraphStyle(
            "Step",
            parent=base["BodyText"],
            fontName=font,
            fontSize=9.2,
            leading=15,
            leftIndent=7 * mm,
            firstLineIndent=-7 * mm,
            spaceAfter=1.5 * mm,
            textColor=INK,
            wordWrap="CJK",
        ),
        "box_title": ParagraphStyle(
            "BoxTitle",
            parent=base["BodyText"],
            fontName=bold,
            fontSize=10.5,
            leading=16,
            spaceAfter=1.5 * mm,
            textColor=NAVY,
            wordWrap="CJK",
        ),
        "box_body": ParagraphStyle(
            "BoxBody",
            parent=base["BodyText"],
            fontName=font,
            fontSize=8.9,
            leading=14,
            textColor=INK,
            wordWrap="CJK",
        ),
        "toc": ParagraphStyle(
            "Toc",
            parent=base["BodyText"],
            fontName=font,
            fontSize=10,
            leading=19,
            textColor=INK,
            wordWrap="CJK",
        ),
    }


def box(
    title: str,
    body: str,
    styles: dict[str, ParagraphStyle],
    background: colors.Color = PALE,
    border: colors.Color = LINE,
) -> KeepTogether:
    content = [
        Paragraph(title, styles["box_title"]),
        Paragraph(body, styles["box_body"]),
    ]
    table = Table([[content]], colWidths=[169 * mm])
    table.setStyle(
        TableStyle(
            [
                ("BACKGROUND", (0, 0), (-1, -1), background),
                ("BOX", (0, 0), (-1, -1), 0.8, border),
                ("LEFTPADDING", (0, 0), (-1, -1), 5 * mm),
                ("RIGHTPADDING", (0, 0), (-1, -1), 5 * mm),
                ("TOPPADDING", (0, 0), (-1, -1), 4 * mm),
                ("BOTTOMPADDING", (0, 0), (-1, -1), 4 * mm),
            ]
        )
    )
    return KeepTogether([table, Spacer(1, 3 * mm)])


def step(
    number: int | str,
    text: str,
    styles: dict[str, ParagraphStyle],
) -> Paragraph:
    return Paragraph(f"<b>{number}</b>　{text}", styles["step"])


def page_header_footer(
    canvas,
    doc,
    font: str,
    bold: str,
) -> None:
    canvas.saveState()
    page = canvas.getPageNumber()
    if page > 1:
        canvas.setStrokeColor(LINE)
        canvas.setLineWidth(0.5)
        canvas.line(20 * mm, 281 * mm, 190 * mm, 281 * mm)
        canvas.setFont(bold, 7.5)
        canvas.setFillColor(NAVY)
        canvas.drawString(
            20 * mm,
            285 * mm,
            f"Y-TEC Browser Capsule 操作マニュアル {VERSION}",
        )
        canvas.setFont(font, 7.5)
        canvas.setFillColor(MUTED)
        canvas.drawRightString(190 * mm, 12 * mm, f"{page} / 13")
        canvas.drawString(
            20 * mm,
            12 * mm,
            "重要データを扱います。実行前に対象と保存先を確認してください。",
        )
    canvas.restoreState()


def build_manual(repo: Path, output: Path) -> None:
    font, bold = register_fonts()
    styles = build_styles(font, bold)
    screenshots = repo / "assets" / "manual" / "screenshots"
    icon = repo / "assets" / "branding" / "YtecBrowserCapsule-icon.png"
    main_shot = screenshots / "01-main-window-1.2.0-ja.png"
    options_shot = screenshots / "02-backup-options-1.2.0-ja.png"
    export_shot = screenshots / "04-csv-backup-assistant-1.2.0-ja.png"
    import_shot = screenshots / "08-csv-import-assistant-1.2.0-ja.png"
    for required in (
        icon,
        main_shot,
        options_shot,
        export_shot,
        import_shot,
    ):
        if not required.exists():
            raise FileNotFoundError(required)

    output.parent.mkdir(parents=True, exist_ok=True)
    doc = SimpleDocTemplate(
        str(output),
        pagesize=A4,
        leftMargin=20 * mm,
        rightMargin=20 * mm,
        topMargin=20 * mm,
        bottomMargin=20 * mm,
        title=f"Y-TEC Browser Capsule 操作マニュアル {VERSION}",
        author="Y-TEC",
        subject="Windows 10 / 11向けブラウザープロファイル移行アプリ",
    )
    story = []

    # 1: cover
    cover_icon = fit_image(icon, 42 * mm, 42 * mm)
    cover_panel = Table(
        [
            [Spacer(1, 13 * mm)],
            [cover_icon],
            [Spacer(1, 8 * mm)],
            [
                Paragraph(
                    "Y-TEC Browser Capsule",
                    styles["cover_title"],
                )
            ],
            [
                Paragraph(
                    f"操作マニュアル {VERSION}",
                    styles["cover_title"],
                )
            ],
            [Spacer(1, 5 * mm)],
            [
                Paragraph(
                    "Chrome・Microsoft Edge・Firefoxのプロファイルを<br/>"
                    "安全にバックアップし、別のWindows PCへ移行するためのガイド",
                    styles["cover_subtitle"],
                )
            ],
            [Spacer(1, 12 * mm)],
            [
                Paragraph(
                    "対応：Windows 10 / 11（64-bit）　配布形態：ポータブル",
                    styles["cover_subtitle"],
                )
            ],
            [Spacer(1, 13 * mm)],
        ],
        colWidths=[170 * mm],
        rowHeights=None,
    )
    cover_panel.setStyle(
        TableStyle(
            [
                ("BACKGROUND", (0, 0), (-1, -1), NAVY),
                ("ALIGN", (0, 0), (-1, -1), "CENTER"),
                ("VALIGN", (0, 0), (-1, -1), "MIDDLE"),
                ("BOX", (0, 0), (-1, -1), 2, PRIMARY),
                ("LEFTPADDING", (0, 0), (-1, -1), 10 * mm),
                ("RIGHTPADDING", (0, 0), (-1, -1), 10 * mm),
            ]
        )
    )
    story.extend(
        [
            Spacer(1, 14 * mm),
            cover_panel,
            Spacer(1, 13 * mm),
            Paragraph(
                "このマニュアルの画面は説明用の合成プロファイルです。"
                "個人のブラウザーデータや保存パスワードは掲載していません。",
                styles["small"],
            ),
            Paragraph("マニュアル改訂：2026-09-29", styles["small"]),
            PageBreak(),
        ]
    )

    # 2: safety and contents
    story.extend(
        [
            Paragraph("最初にお読みください", styles["page_title"]),
            box(
                "最重要：2つのファイルがそろって初めて復元できます",
                "バックアップ本体 <b>.bvb</b> と復元キーファイル "
                "<b>.ybckey</b> を作成します。どちらか一方を紛失すると復元できません。"
                "盗難時の安全性を高めるため、可能なら別々のUSBメモリや保管場所へ分けてください。"
                "復元キーを他人へ送信したり、公開場所へ置いたりしないでください。",
                styles,
                WARN,
                WARN_LINE,
            ),
            Paragraph("このアプリが行うこと", styles["section"]),
            Paragraph(
                "Chrome、Microsoft Edge、Firefox / Firefox ESRのプロファイルを、"
                "AES-256-GCMで暗号化した単一バックアップへ保存します。"
                "ネットワーク接続、インストール、管理者権限は不要です。",
                styles["body"],
            ),
            Paragraph("保存パスワードの扱い", styles["section"]),
            Paragraph(
                "ブラウザー内部の保存パスワードは直接読みません。ブラウザー公式画面で"
                "本人がCSVをエクスポート／インポートします。アプリは専用フォルダーへ"
                "保存されたCSVを検出し、形式確認後すぐ一時暗号化して平文削除を試みます。",
                styles["body"],
            ),
            Paragraph("目次", styles["section"]),
            Table(
                [
                    [
                        Paragraph(
                            "3　画面と起動<br/>"
                            "4　バックアップ<br/>"
                            "5　復元キーの保管<br/>"
                            "6　ブラウザー終了支援<br/>"
                            "7　保存パスワードCSV",
                            styles["toc"],
                        ),
                        Paragraph(
                            "8　完全検証<br/>"
                            "9　別PCへ復元<br/>"
                            "10　保存パスワードCSV復元<br/>"
                            "11　複数Windowsユーザー<br/>"
                            "12　トラブル対応<br/>"
                            "13　安全境界・ライセンス",
                            styles["toc"],
                        ),
                    ]
                ],
                colWidths=[84.5 * mm, 84.5 * mm],
                style=TableStyle(
                    [
                        ("BACKGROUND", (0, 0), (-1, -1), PALE),
                        ("BOX", (0, 0), (-1, -1), 0.6, LINE),
                        ("INNERGRID", (0, 0), (-1, -1), 0.4, LINE),
                        ("VALIGN", (0, 0), (-1, -1), "TOP"),
                        ("LEFTPADDING", (0, 0), (-1, -1), 5 * mm),
                        ("TOPPADDING", (0, 0), (-1, -1), 4 * mm),
                        ("BOTTOMPADDING", (0, 0), (-1, -1), 4 * mm),
                    ]
                ),
            ),
            PageBreak(),
        ]
    )

    # 3: launch and main
    story.extend(
        [
            Paragraph("起動とメイン画面", styles["page_title"]),
            step(1, "配布ZIPをローカルの空フォルダーへすべて展開します。ZIP内から直接起動しないでください。", styles),
            step(2, "<b>YtecBrowserCapsule.exe</b> を起動します。SmartScreenが表示されたら、配布元と公開SHA-256を先に確認します。", styles),
            step(3, "検出されたブラウザー、プロファイル名、概算容量を確認し、対象だけにチェックを付けます。", styles),
            Spacer(1, 2 * mm),
            fit_image(main_shot, 169 * mm, 102 * mm),
            Spacer(1, 3 * mm),
            box(
                "迷ったときは「操作マニュアル」",
                "右上の「操作マニュアル」で、アプリに同梱されたこのPDFをいつでも開けます。"
                "「このアプリについて」では版数・安全境界・Apache License 2.0を確認できます。",
                styles,
                SAFE,
                TEAL,
            ),
            PageBreak(),
        ]
    )

    # 4: backup
    story.extend(
        [
            Paragraph("バックアップを作成する", styles["page_title"]),
            step(1, "対象プロファイルを選び、「バックアップを作成」を押します。", styles),
            step(2, "バックアップ本体の保存先と <b>.bvb</b> ファイル名を指定します。", styles),
            step(3, "保存内容を選び、「復元キーも必ず保管する」を確認します。", styles),
            fit_image(options_shot, 112 * mm, 102 * mm),
            Spacer(1, 2 * mm),
            step(4, "復元キーファイル <b>.ybckey</b> の保存先を指定します。同じフォルダー／ドライブの場合は警告を読みます。", styles),
            step(5, "ブラウザー終了支援を確認し、処理完了までPCをスリープさせず待ちます。", styles),
            step(6, "完了画面に表示された <b>.bvb</b> と <b>.ybckey</b> の両方が存在することを確認します。", styles),
            box(
                "保存パスワードを含めない場合",
                "ブックマーク・履歴・拡張機能・設定などのプロファイル本体だけを選べます。"
                "別PCではCookie、セッション、パスキー等がそのまま使えない場合があります。",
                styles,
            ),
            PageBreak(),
        ]
    )

    # 5: recovery key
    story.extend(
        [
            Paragraph("復元キーを安全に保管する", styles["page_title"]),
            box(
                ".ybckey は「合鍵」ではなく復元に必要な秘密です",
                "本体だけでは復元できず、復元キーだけでもデータはありません。"
                "しかし両方を入手した人はバックアップを開けるため、セットで公開・送信しないでください。",
                styles,
                WARN,
                WARN_LINE,
            ),
            Paragraph("おすすめの保管例", styles["section"]),
            step("A", ".bvb は移行用SSD、.ybckey は別のUSBメモリへ保存する。", styles),
            step("B", ".bvb は暗号化済み外付けディスク、.ybckey は自分だけが利用できる保護された別の保管場所へ保存する。", styles),
            step("C", "一時的に同じ場所へ作成した場合、コピー確認後に復元キーだけ別の場所へ移す。", styles),
            Paragraph("してはいけないこと", styles["section"]),
            step("×", "復元キーをメール本文、チャット、公開共有リンク、WordPress等へ貼り付ける。", styles),
            step("×", ".bvb だけを移動して、元PCの復元キーを消してしまう。", styles),
            step("×", "どの .bvb と組になるか確認せず、復元キーの名前を付け替える。", styles),
            Paragraph("取り違え防止", styles["section"]),
            Paragraph(
                "復元時はバックアップ内部IDと復元キーのID・ハッシュを照合します。"
                "別のキー、改ざんされたキー、破損したキーは拒否されます。"
                "アプリにも解除用バックドアはありません。",
                styles["body"],
            ),
            box(
                "旧1.0.0以前のバックアップ",
                "BVB v1は読み取り・復元のみ対応します。旧形式を選んだ場合だけ従来の"
                "パスフレーズ入力が表示されます。1.1.0以降の新規バックアップはBVB v2です。",
                styles,
                SAFE,
                TEAL,
            ),
            PageBreak(),
        ]
    )

    # 6: close helper
    story.extend(
        [
            Paragraph("ブラウザー終了支援", styles["page_title"]),
            Paragraph(
                "プロファイルを安全にコピーするには、対象ブラウザーのバックグラウンドプロセスも"
                "終了している必要があります。アプリはバックアップ／復元の直前に確認します。",
                styles["body"],
            ),
            Paragraph("確認の流れ", styles["section"]),
            step(1, "編集中のフォーム、ダウンロード、未保存のタブ内容を利用者が保存します。", styles),
            step(2, "案内を確認して通常終了を承認すると、アプリが対象ブラウザーへ終了要求を送ります。", styles),
            step(3, "バックグラウンドに残った場合だけ、データ損失の警告を表示します。", styles),
            step(4, "強制終了を承認するか、中止して自分で終了・再検出するかを選びます。", styles),
            box(
                "アプリが終了する範囲",
                "検出済みブラウザーの<b>正確な実行ファイルパス</b>と<b>現在のWindowsセッション</b>"
                "に一致するプロセスだけです。他のユーザーセッション、別パスの同名EXE、"
                "権限不足のプロセスは終了しません。ブラウザー以外は対象にしません。",
                styles,
                SAFE,
                TEAL,
            ),
            Paragraph("うまく終了できない場合", styles["section"]),
            Paragraph(
                "処理を中止し、ブラウザーの設定で「バックグラウンドアプリの実行を続行」を"
                "一時的に無効化するか、タスクマネージャーで対象を確認してください。"
                "別ユーザーのプロセスや権限の問題は、対象ユーザーまたはPCの管理者へ相談してください。",
                styles["body"],
            ),
            box(
                "強制終了は最後の手段",
                "書き込み中のブラウザーを強制終了すると、直前の操作やダウンロードが失われる"
                "可能性があります。警告画面で対象を読み、問題がなければ承認してください。",
                styles,
                WARN,
                WARN_LINE,
            ),
            PageBreak(),
        ]
    )

    # 7: export CSV
    story.extend(
        [
            Paragraph("保存パスワードCSVを安全に追加", styles["page_title"]),
            fit_image(export_shot, 121 * mm, 113 * mm),
            Spacer(1, 2 * mm),
            step(1, "保存内容で「保存パスワードCSV」を選びます。", styles),
            step(2, "表示中のブラウザーとプロファイル名を確認し、「標準画面を開いて待機」を押します。", styles),
            step(3, "ブラウザー公式画面で本人確認し、表示された専用フォルダーへCSVを保存します。", styles),
            step(4, "保存パスワードがないプロファイルは、待機中でも「このプロファイルはスキップ」を押して次へ進めます。", styles),
            step(5, "アプリが書き込み完了と必須ヘッダーを確認し、直ちに一時暗号化します。", styles),
            step(6, "件数と平文削除済み表示を確認して次のプロファイルへ進みます。", styles),
            box(
                "自動化しない部分",
                "本人確認、OS／ブラウザーの承認、CSVエクスポート操作は自動化しません。"
                "CSVの値は画面やログへ表示せず、認証情報データベースも解析しません。",
                styles,
                SAFE,
                TEAL,
            ),
            PageBreak(),
        ]
    )

    # 8: verification
    story.extend(
        [
            Paragraph("バックアップを完全検証する", styles["page_title"]),
            Paragraph(
                "別PCへ運ぶ前と、コピー後の両方で完全検証を実行してください。"
                "ファイルが存在するだけでは、破損・取り違えを検出できません。",
                styles["body"],
            ),
            step(1, "「バックアップを検証」を押し、対象の .bvb を選びます。", styles),
            step(2, "対応する .ybckey を選びます。", styles),
            step(3, "暗号タグ、マニフェスト、全ファイルのSHA-256検証完了を待ちます。", styles),
            step(4, "プロファイル数、ファイル数、警告の有無を確認します。", styles),
            box(
                "検証に失敗したら",
                "別の復元キーを手当たり次第に試さず、同じ日時・名前で作成した組か確認します。"
                "コピー前の元ファイルが残っていれば、元とコピー先のSHA-256も比較してください。"
                "破損した媒体は上書きせず保全します。",
                styles,
                WARN,
                WARN_LINE,
            ),
            Paragraph("配布ZIPのSHA-256確認例", styles["section"]),
            box(
                "PowerShell",
                "<font name=\"Courier\">Get-FileHash "
                f".\\YtecBrowserCapsule-{VERSION}-win-x64.zip "
                "-Algorithm SHA256</font><br/>"
                "表示値をY-TEC Forgeの公開ページまたはGitHub Releasesの値と照合してください。",
                styles,
            ),
            Paragraph("検証と復元は別操作です", styles["section"]),
            Paragraph(
                "完全検証は復元先プロファイルへ書き込みません。"
                "重要なバックアップは、移行前に検証だけを先に行うと安全です。",
                styles["body"],
            ),
            PageBreak(),
        ]
    )

    # 9: restore
    story.extend(
        [
            Paragraph("別のWindows PCへ復元する", styles["page_title"]),
            step(1, "新しいPCで対象ブラウザーを一度起動し、復元先プロファイルを作成してから終了します。", styles),
            step(2, ".bvb と .ybckey を新しいPCへコピーし、「バックアップから復元」を押します。", styles),
            step(3, "バックアップ元ごとに、同じ種類のブラウザーの復元先を明示選択します。", styles),
            step(4, "別PC・別Windowsユーザーの場合は「別環境移行」を選び、警告を確認します。", styles),
            step(5, "ブラウザー終了支援を完了すると、現在の復元先を暗号化ロールバックへ保存してから復元します。", styles),
            step(6, "完了後にブラウザーを起動し、ブックマーク・履歴・設定・拡張機能を確認します。", styles),
            box(
                "ロールバックも2ファイルです",
                "復元先の元状態はローカルのRollbacksへ <b>.bvb</b> と <b>.ybckey</b> で保存されます。"
                "動作確認が終わるまで両方を削除しないでください。別々の場所へ保管するのが安全です。",
                styles,
                SAFE,
                TEAL,
            ),
            Paragraph("別PCで引き継げないことがあるもの", styles["section"]),
            Paragraph(
                "Cookie、ログインセッション、パスキー、OSや端末へ結び付くデータは、"
                "ブラウザーやサービス側の保護により再ログイン・再登録が必要な場合があります。"
                "これは暗号化回避を行わない安全設計によるものです。",
                styles["body"],
            ),
            box(
                "復元先を取り違えない",
                "表示名だけで判断せず、ブラウザー名・内部プロファイル名・用途を確認します。"
                "複数プロファイルを同じ復元先へ重複指定することはできません。",
                styles,
                WARN,
                WARN_LINE,
            ),
            PageBreak(),
        ]
    )

    # 10: import CSV
    story.extend(
        [
            Paragraph("保存パスワードCSVを公式画面から戻す", styles["page_title"]),
            fit_image(import_shot, 121 * mm, 99 * mm),
            Spacer(1, 2 * mm),
            step(1, "復元対応付けで「保存パスワードCSV（公式取込）」を選びます。", styles),
            step(2, "対象ブラウザーとプロファイル名を確認し、「標準画面を開く」を押します。", styles),
            step(3, "「CSVの場所を開く」で一時CSVを確認します。必要なら「フォルダーパスをコピー」を使います。", styles),
            step(4, "ブラウザー公式画面でCSVを選び、本人確認とインポートを完了します。", styles),
            step(5, "アプリへ戻り「インポート完了・削除」を押します。", styles),
            box(
                "コピーされるのはフォルダーだけ",
                "クリップボードへ入るのはCSVの親フォルダーです。CSVファイル名は含めないため、"
                "貼り付けただけでCSV関連アプリが起動する問題を防ぎます。",
                styles,
                SAFE,
                TEAL,
            ),
            PageBreak(),
        ]
    )

    # 11: multi-user
    story.extend(
        [
            Paragraph("複数のWindowsユーザー", styles["page_title"]),
            Paragraph(
                "メイン画面の「読み取り可能なほかのWindowsユーザーも表示」は既定でOFFです。"
                "ONにすると、現在の権限で読み取れるプロファイル本体だけを追加検出します。",
                styles["body"],
            ),
            box(
                "権限を広げません",
                "管理者権限の取得、ACL変更、所有権取得、別ユーザーとしての実行は行いません。"
                "読めないプロファイルはスキップし、現在のWindowsセッション以外のブラウザー"
                "プロセスは終了しません。",
                styles,
                SAFE,
                TEAL,
            ),
            Paragraph("安全な進め方", styles["section"]),
            step(1, "対象ユーザーがブラウザーを終了し、必要ならWindowsからサインアウトします。", styles),
            step(2, "現在のユーザーで読み取り可能な範囲を検出し、Windowsユーザー名とプロファイル名を確認します。", styles),
            step(3, "保存パスワードCSVは、対象ユーザー本人がそのユーザーでサインインして公式画面を操作します。", styles),
            step(4, "復元先では原則として対象ユーザーでサインインし、同じブラウザーのプロファイルへ復元します。", styles),
            Paragraph("表示されない場合", styles["section"]),
            Paragraph(
                "PCの設定やアクセス権により、他ユーザーのプロファイルを読めないことがあります。"
                "アプリで権限を回避せず、ユーザーごとにサインインして個別バックアップするか、"
                "対象ユーザーまたはPCの管理者へ相談してください。",
                styles["body"],
            ),
            box(
                "実データを試験材料にしない",
                "初回運用では、合成データを入れた検証用プロファイルでバックアップ・検証・復元を"
                "一巡してから本番データを扱ってください。",
                styles,
                WARN,
                WARN_LINE,
            ),
            PageBreak(),
        ]
    )

    # 12: troubleshooting
    story.extend(
        [
            Paragraph("困ったときの確認", styles["page_title"]),
            box(
                "ブラウザーが終了しない",
                "未保存内容を保存し、通常終了支援を再実行します。残る場合だけ強制終了を承認します。"
                "別ユーザー・権限不足・管理対象プロセスは手動確認または管理者相談が必要です。",
                styles,
            ),
            box(
                "復元キーが違う／見つからない",
                "同じ日時・名前で作った .bvb と .ybckey の組を確認します。"
                "紛失した復元キーをアプリやY-TECが再発行することはできません。",
                styles,
            ),
            box(
                "CSVを自動検出しない",
                "画面に表示された専用フォルダー直下へCSV形式で保存し、書き込み完了後に再試行します。"
                "Excel等で開いたままなら閉じます。",
                styles,
            ),
            box(
                "平文CSVを削除できない",
                "警告に表示されたファイルを、ブラウザーや表計算ソフトで閉じてから手動削除します。"
                "削除確認まではPCやフォルダーを共有しないでください。",
                styles,
                WARN,
                WARN_LINE,
            ),
            box(
                "復元または自動ロールバックに失敗した",
                "ブラウザーを起動せず、対象プロファイル、ロールバック .bvb、"
                "ロールバック .ybckey を変更・削除しないでください。エラーコードを控えて相談します。",
                styles,
                WARN,
                WARN_LINE,
            ),
            Paragraph("エラー報告に含めてよい情報", styles["section"]),
            Paragraph(
                "アプリ版数、Windows版、ブラウザー名、エラーコード、発生操作。"
                "保存パスワード、CSV内容、復元キー、個人のプロファイルパスは送らないでください。",
                styles["body"],
            ),
            PageBreak(),
        ]
    )

    # 13: boundaries/license
    story.extend(
        [
            Paragraph("安全境界・更新・ライセンス", styles["page_title"]),
            Paragraph("対応環境", styles["section"]),
            Paragraph(
                "Windows 10 / 11（64-bit）、Chrome Stable、Microsoft Edge Stable、"
                "Firefox / Firefox ESR。スマートフォン、macOS、Linux、Windows 7 / 8 / 8.1、"
                "32-bit Windowsは対象外です。",
                styles["body"],
            ),
            Paragraph("意図的に実装しない機能", styles["section"]),
            step("—", "保存パスワードの直接復号、DPAPI回避、App-Bound Encryption回避。", styles),
            step("—", "ブラウザー認証情報データベースの解析・直接書き込み。", styles),
            step("—", "本人確認やOS／ブラウザー承認の自動操作。", styles),
            step("—", "クラウド同期、テレメトリー、アクセス解析、自動更新。", styles),
            Paragraph("更新版を入れ替える", styles["section"]),
            Paragraph(
                "新しい配布ZIPを別の空フォルダーへ展開し、公開SHA-256を確認してから起動します。"
                "古いフォルダーを削除する前に、手元の .bvb / .ybckey / ロールバックが"
                "配布フォルダー外へ保管されていることを確認してください。",
                styles["body"],
            ),
            Paragraph("署名とライセンス", styles["section"]),
            Paragraph(
                "直接配布版ではY-TEC自己署名のAuthenticode署名を使用します。"
                "商用CAの信頼済み証明書ではなく、SmartScreen警告が出る場合があります。"
                "配布元、署名、公開SHA-256を確認してください。証明書の自動登録は行いません。"
                "本ソフトウェア本体はApache License 2.0です。"
                "第三者コンポーネントの表示は同梱の THIRD-PARTY-NOTICES.md を参照してください。",
                styles["body"],
            ),
            Paragraph("プライバシー", styles["section"]),
            Paragraph(
                "本アプリは、利用者またはインストール・操作する人が明示的に要求しない限り、"
                "他のネットワークシステムへ情報を送信しません。",
                styles["body"],
            ),
            box(
                "公開ページ",
                "最新版、SHA-256、注意事項：<br/>"
                '<link href="https://ytec.cloudfree.jp/forge/projects/browser-capsule/" color="#6C5CE7">https://ytec.cloudfree.jp/forge/projects/browser-capsule/</link><br/><br/>'
                "ソースコード：<br/>"
                '<link href="https://github.com/ytec-forge-commits/ytec-browser-capsule" color="#6C5CE7">https://github.com/ytec-forge-commits/ytec-browser-capsule</link><br/><br/>'
                "連絡先：<br/>"
                '<link href="https://ytec.cloudfree.jp/forge/contact/" color="#6C5CE7">https://ytec.cloudfree.jp/forge/contact/</link>',
                styles,
                SAFE,
                TEAL,
            ),
            Paragraph(
                "Copyright © 2026 Y-TEC. Apache License 2.0.",
                styles["small"],
            ),
        ]
    )

    doc.build(
        story,
        onFirstPage=lambda canvas, document: page_header_footer(
            canvas, document, font, bold
        ),
        onLaterPages=lambda canvas, document: page_header_footer(
            canvas, document, font, bold
        ),
    )


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--repo", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    build_manual(args.repo.resolve(), args.output.resolve())


if __name__ == "__main__":
    main()
