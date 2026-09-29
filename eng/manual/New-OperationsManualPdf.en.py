from __future__ import annotations

import argparse
from pathlib import Path

from reportlab.lib import colors
from reportlab.lib.enums import TA_CENTER
from reportlab.lib.pagesizes import A4
from reportlab.lib.styles import ParagraphStyle, getSampleStyleSheet
from reportlab.lib.units import mm
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


def fit_image(path: Path, max_width: float, max_height: float) -> Image:
    from PIL import Image as PillowImage

    with PillowImage.open(path) as source:
        width, height = source.size
    ratio = min(max_width / width, max_height / height)
    return Image(str(path), width=width * ratio, height=height * ratio)


def build_styles() -> dict[str, ParagraphStyle]:
    base = getSampleStyleSheet()
    return {
        "cover_title": ParagraphStyle(
            "CoverTitle",
            parent=base["Title"],
            fontName="Helvetica-Bold",
            fontSize=25,
            leading=32,
            textColor=colors.white,
            alignment=TA_CENTER,
        ),
        "cover_subtitle": ParagraphStyle(
            "CoverSubtitle",
            parent=base["BodyText"],
            fontName="Helvetica",
            fontSize=12,
            leading=18,
            textColor=colors.white,
            alignment=TA_CENTER,
        ),
        "page_title": ParagraphStyle(
            "PageTitle",
            parent=base["Heading1"],
            fontName="Helvetica-Bold",
            fontSize=20,
            leading=25,
            spaceAfter=6 * mm,
            textColor=NAVY,
        ),
        "section": ParagraphStyle(
            "Section",
            parent=base["Heading2"],
            fontName="Helvetica-Bold",
            fontSize=12.5,
            leading=17,
            spaceBefore=2.5 * mm,
            spaceAfter=1.5 * mm,
            textColor=PRIMARY,
        ),
        "body": ParagraphStyle(
            "Body",
            parent=base["BodyText"],
            fontName="Helvetica",
            fontSize=9.2,
            leading=14.2,
            spaceAfter=2.2 * mm,
            textColor=INK,
        ),
        "small": ParagraphStyle(
            "Small",
            parent=base["BodyText"],
            fontName="Helvetica",
            fontSize=8.1,
            leading=12,
            textColor=MUTED,
        ),
        "step": ParagraphStyle(
            "Step",
            parent=base["BodyText"],
            fontName="Helvetica",
            fontSize=9,
            leading=14,
            leftIndent=7 * mm,
            firstLineIndent=-7 * mm,
            spaceAfter=1.7 * mm,
            textColor=INK,
        ),
        "box_title": ParagraphStyle(
            "BoxTitle",
            parent=base["BodyText"],
            fontName="Helvetica-Bold",
            fontSize=10.3,
            leading=15,
            spaceAfter=1.5 * mm,
            textColor=NAVY,
        ),
        "box_body": ParagraphStyle(
            "BoxBody",
            parent=base["BodyText"],
            fontName="Helvetica",
            fontSize=8.7,
            leading=13.5,
            textColor=INK,
        ),
        "toc": ParagraphStyle(
            "Toc",
            parent=base["BodyText"],
            fontName="Helvetica",
            fontSize=10,
            leading=18,
            textColor=INK,
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


def step(number: int | str, text: str, styles: dict[str, ParagraphStyle]) -> Paragraph:
    return Paragraph(f"<b>{number}</b>&nbsp;&nbsp;{text}", styles["step"])


def page_header_footer(canvas, doc) -> None:
    canvas.saveState()
    page = canvas.getPageNumber()
    if page > 1:
        canvas.setStrokeColor(LINE)
        canvas.setLineWidth(0.5)
        canvas.line(20 * mm, 281 * mm, 190 * mm, 281 * mm)
        canvas.setFont("Helvetica-Bold", 7.5)
        canvas.setFillColor(NAVY)
        canvas.drawString(
            20 * mm,
            285 * mm,
            f"Y-TEC Browser Capsule User Manual {VERSION}",
        )
        canvas.setFont("Helvetica", 7.5)
        canvas.setFillColor(MUTED)
        canvas.drawRightString(190 * mm, 12 * mm, f"{page} / 13")
        canvas.drawString(
            20 * mm,
            12 * mm,
            "This app handles important data. Check the source and destination before continuing.",
        )
    canvas.restoreState()


def build_manual(repo: Path, output: Path) -> None:
    styles = build_styles()
    screenshots = repo / "assets" / "manual" / "screenshots"
    icon = repo / "assets" / "branding" / "YtecBrowserCapsule-icon.png"
    main_shot = screenshots / "01-main-window-1.2.0-en.png"
    options_shot = screenshots / "02-backup-options-1.2.0-en.png"
    export_shot = screenshots / "04-csv-backup-assistant-1.2.0-en.png"
    import_shot = screenshots / "08-csv-import-assistant-1.2.0-en.png"
    for required in (icon, main_shot, options_shot, export_shot, import_shot):
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
        title=f"Y-TEC Browser Capsule User Manual {VERSION}",
        author="Y-TEC",
        subject="Browser profile migration for Windows 10 and Windows 11",
    )

    story: list = []

    # 1: cover
    cover = Table(
        [
            [Spacer(1, 16 * mm)],
            [fit_image(icon, 40 * mm, 40 * mm)],
            [Spacer(1, 9 * mm)],
            [Paragraph("Y-TEC Browser Capsule", styles["cover_title"])],
            [Paragraph(f"User Manual {VERSION}", styles["cover_title"])],
            [Spacer(1, 6 * mm)],
            [
                Paragraph(
                    "Move selected Chrome, Edge, and Firefox profiles between Windows PCs.<br/>"
                    "A recovery-key file protects each new backup.",
                    styles["cover_subtitle"],
                )
            ],
            [Spacer(1, 12 * mm)],
            [
                Paragraph(
                    "Windows 10 / 11 (64-bit) &nbsp;&nbsp; Portable application",
                    styles["cover_subtitle"],
                )
            ],
            [Spacer(1, 16 * mm)],
        ],
        colWidths=[169 * mm],
    )
    cover.setStyle(
        TableStyle(
            [
                ("BACKGROUND", (0, 0), (-1, -1), NAVY),
                ("VALIGN", (0, 0), (-1, -1), "MIDDLE"),
                ("ALIGN", (0, 0), (-1, -1), "CENTER"),
                ("BOX", (0, 0), (-1, -1), 2, PRIMARY),
                ("LEFTPADDING", (0, 0), (-1, -1), 10 * mm),
                ("RIGHTPADDING", (0, 0), (-1, -1), 10 * mm),
            ]
        )
    )
    story.extend(
        [
            Spacer(1, 14 * mm),
            cover,
            Spacer(1, 11 * mm),
            Paragraph(
                "All screens in this manual use synthetic profiles. No personal browser data, password, or recovery key is shown.",
                styles["small"],
            ),
            Paragraph("Manual revised: 2026-09-29", styles["small"]),
            PageBreak(),
        ]
    )

    # 2: safety and contents
    story.extend(
        [
            Paragraph("Read this first", styles["page_title"]),
            box(
                "Keep both files, but store them separately",
                "Every new backup consists of an encrypted <b>.bvb</b> file and its matching secret "
                "<b>.ybckey</b> recovery-key file. Neither file can restore the data by itself. "
                "Do not email, post, or place the recovery key in a public share.",
                styles,
                WARN,
                WARN_LINE,
            ),
            box(
                "Screenshots contain synthetic data only",
                "The names, sizes, paths, and record counts shown in this manual are examples. "
                "No personal browser profile, password, or recovery key is included.",
                styles,
                SAFE,
                TEAL,
            ),
            Paragraph("Contents", styles["section"]),
            Table(
                [[
                    Paragraph(
                        "3&nbsp;&nbsp;Before you start<br/>4&nbsp;&nbsp;Select profiles<br/>"
                        "5&nbsp;&nbsp;Backup options<br/>6&nbsp;&nbsp;Saved-password CSV<br/>"
                        "7&nbsp;&nbsp;Recovery-key storage<br/>8&nbsp;&nbsp;Verify and restore",
                        styles["toc"],
                    ),
                    Paragraph(
                        "9&nbsp;&nbsp;Import saved-password CSV<br/>10&nbsp;&nbsp;Multiple Windows users<br/>"
                        "11&nbsp;&nbsp;Checks and troubleshooting<br/>12&nbsp;&nbsp;Security and limitations<br/>"
                        "13&nbsp;&nbsp;Support, privacy, and license",
                        styles["toc"],
                    ),
                ]],
                colWidths=[84.5 * mm, 84.5 * mm],
            ),
            PageBreak(),
        ]
    )

    # 3: before start
    story.extend(
        [
            Paragraph("Before you start", styles["page_title"]),
            Paragraph("Supported environment", styles["section"]),
            step("-", "Windows 10 or Windows 11, 64-bit. Windows 7, 8, 8.1, 32-bit Windows, macOS, Linux, and mobile devices are not supported.", styles),
            step("-", "Google Chrome, Microsoft Edge, and Mozilla Firefox profiles that the current Windows account can read.", styles),
            step("-", "No installation, administrator rights, account sign-in, telemetry, or network connection is required by the app.", styles),
            Paragraph("Preparation checklist", styles["section"]),
            step(1, "Confirm the source PC, destination PC, Windows account, browser, and profile names.", styles),
            step(2, "Prepare reliable storage with enough free space for the selected profiles, the .bvb file, and the .ybckey file.", styles),
            step(3, "Close downloads, web forms, and other browser work. The app will ask before helping to close remaining target processes.", styles),
            step(4, "If saved passwords are required, be ready to use the browser's official export/import screen and complete its identity or OS confirmation yourself.", styles),
            step(5, "Keep the previous PC and its backup until the restored profile has been checked on the new PC.", styles),
            box(
                "Language",
                "Open <b>Settings</b> to choose System default, Japanese, or English. Restart the app after changing the language. "
                "The Japanese and English PDF manuals are included in the portable package.",
                styles,
            ),
            box(
                "Do not use live data for practice",
                "For a first run, use a test Windows account and test browser profiles. Keep production and personal recovery keys out of screenshots, issue reports, and chat messages.",
                styles,
                WARN,
                WARN_LINE,
            ),
            PageBreak(),
        ]
    )

    # 4: main screen
    story.extend(
        [
            Paragraph("Select profiles to back up", styles["page_title"]),
            fit_image(main_shot, 169 * mm, 112 * mm),
            Spacer(1, 4 * mm),
            step(1, "Launch <b>YtecBrowserCapsule.exe</b> and review the detected browsers and profiles.", styles),
            step(2, "Select only the profiles that belong in this backup. Check the browser name, display name, internal name, and estimated size.", styles),
            step(3, "Use <b>Show other readable Windows users</b> only when you are authorized to handle their browser data.", styles),
            step(4, "Select <b>Create backup</b> and review the options before choosing any output files.", styles),
            box(
                "No hidden expansion of scope",
                "The app does not elevate privileges, change file ownership, bypass access control, or read profiles that the current Windows account cannot access.",
                styles,
                SAFE,
                TEAL,
            ),
            PageBreak(),
        ]
    )

    # 5: options
    story.extend(
        [
            Paragraph("Choose backup options", styles["page_title"]),
            fit_image(options_shot, 157 * mm, 92 * mm),
            Spacer(1, 4 * mm),
            step(1, "Choose the destination and file name for the encrypted <b>.bvb</b> backup.", styles),
            step(2, "Select the required contents. Profile data is copied from the selected browser profiles.", styles),
            step(3, "Choose saved-password CSV only if you intend to complete the official browser export for each applicable profile.", styles),
            step(4, "Choose the destination for the matching <b>.ybckey</b> file. Read the warning if both files are placed on the same drive.", styles),
            step(5, "Approve browser-close assistance only after your browser work is saved. Wait without suspending the PC.", styles),
            step(6, "On completion, confirm that both output files exist and that the app reports success.", styles),
            box(
                "Recovery keys are generated, not typed",
                "Version 1.2.1 creates a cryptographically random key file for each new backup. There is no fixed application key, shared master key, or passphrase to remember.",
                styles,
                SAFE,
                TEAL,
            ),
            PageBreak(),
        ]
    )

    # 6: export CSV
    story.extend(
        [
            Paragraph("Add saved-password CSV safely", styles["page_title"]),
            fit_image(export_shot, 143 * mm, 111 * mm),
            Spacer(1, 3 * mm),
            step(1, "Confirm the displayed browser and profile, then select <b>Open official screen and wait</b>.", styles),
            step(2, "On the browser's official password-management screen, complete any required identity confirmation and export the CSV to the dedicated folder displayed by the app.", styles),
            step(3, "Wait while the app confirms that writing has finished and checks the required CSV headers. Values are not shown in the UI or log.", styles),
            step(4, "The CSV is immediately placed in temporary encryption and the plaintext file is deleted when possible. Check the displayed record count and deletion status.", styles),
            step(5, "If the profile contains no saved passwords, use <b>Skip this profile</b>; it remains available while the assistant is waiting.", styles),
            box(
                "The browser remains the authority",
                "Y-TEC Browser Capsule does not decrypt stored passwords, parse browser credential databases, bypass DPAPI or App-Bound Encryption, or automate the browser's identity confirmation.",
                styles,
                WARN,
                WARN_LINE,
            ),
            PageBreak(),
        ]
    )

    # 7: key storage and process close
    story.extend(
        [
            Paragraph("Protect the recovery-key file", styles["page_title"]),
            box(
                ".ybckey is a secret",
                "The key file is required to restore its matching .bvb backup. Y-TEC cannot regenerate a lost key. Anyone who obtains both files may be able to restore the protected browser data.",
                styles,
                WARN,
                WARN_LINE,
            ),
            step("GOOD", "Store .bvb on the migration SSD and .ybckey on a different USB drive or approved secret store.", styles),
            step("GOOD", "If both are created together temporarily, verify the copy first, then move the key to a separate protected location.", styles),
            step("AVOID", "Do not paste the key into email, chat, WordPress, issue reports, source code, or a public cloud link.", styles),
            step("AVOID", "Do not rename keys without keeping a reliable association with the matching backup.", styles),
            Paragraph("Browser-close assistance", styles["section"]),
            step(1, "The app first asks you to close the browser normally so it can finish writing profile files.", styles),
            step(2, "With your approval, it requests closure only for matching executable paths in the current Windows session.", styles),
            step(3, "Processes owned by other sessions, with insufficient rights, or from a different executable path are not terminated.", styles),
            box(
                "If a browser will not close",
                "Cancel the backup and check background-app settings, active downloads, another Windows session, or PC access restrictions. Never force-close a browser while important writes are in progress.",
                styles,
            ),
            PageBreak(),
        ]
    )

    # 8: verify and restore
    story.extend(
        [
            Paragraph("Verify and restore a backup", styles["page_title"]),
            Paragraph("Verify before moving or deleting the source", styles["section"]),
            step(1, "Select <b>Verify backup</b>, choose the .bvb file, then choose its matching .ybckey file.", styles),
            step(2, "Wait for authentication, manifest, and SHA-256 checks to complete. A mismatch is a stop condition.", styles),
            step(3, "Keep both files until the restored browser has been checked on the destination PC.", styles),
            Paragraph("Restore to the destination PC", styles["section"]),
            step(1, "Install or open the same browser once to create the destination profile, then close it.", styles),
            step(2, "Copy the matching .bvb and .ybckey files to the destination PC and select <b>Restore from backup</b>.", styles),
            step(3, "Map each source profile to a destination profile of the same browser. Do not rely on display names alone.", styles),
            step(4, "Review the confirmation, complete browser-close assistance, and allow the app to create an encrypted rollback before writing.", styles),
            step(5, "After restore, open the browser and check bookmarks, history, settings, extensions, and required sign-ins.", styles),
            box(
                "Protected values may require reauthentication",
                "OS- and browser-protected data may not work on another PC. Reauthentication or re-registration may be required because this app does not circumvent the browser's encryption model.",
                styles,
                SAFE,
                TEAL,
            ),
            box(
                "Legacy backups",
                "BVB v1 backups are supported for verification and restore only. Their original passphrase may be requested. New backups use BVB v2 and a .ybckey file.",
                styles,
            ),
            PageBreak(),
        ]
    )

    # 9: import CSV
    story.extend(
        [
            Paragraph("Import saved-password CSV", styles["page_title"]),
            fit_image(import_shot, 140 * mm, 101 * mm),
            Spacer(1, 3 * mm),
            step(1, "Confirm the browser and profile displayed by the assistant, then select <b>Open official screen</b>.", styles),
            step(2, "Use <b>Open CSV location</b> to find the temporary file. <b>Copy folder path</b> copies only the parent folder, not the CSV file name.", styles),
            step(3, "On the browser's official screen, select CSV import and complete any required confirmation yourself.", styles),
            step(4, "Return to the app and select <b>Done - delete CSV</b>. Confirm that no plaintext CSV remains in the temporary folder.", styles),
            step(5, "Use <b>Skip and delete CSV</b> if that profile should not be imported.", styles),
            box(
                "Check every profile name",
                "The assistant presents one CSV per source profile. Do not import a file into a different profile merely because the browser name matches.",
                styles,
                WARN,
                WARN_LINE,
            ),
            PageBreak(),
        ]
    )

    # 10: multiple Windows users
    story.extend(
        [
            Paragraph("Multiple Windows users", styles["page_title"]),
            Paragraph(
                "The optional user-profile scan adds only browser profiles that the current Windows account can already read. "
                "The app does not request administrator rights, change ACLs, take ownership, or impersonate another user.",
                styles["body"],
            ),
            step(1, "Obtain authorization from the owner of each Windows account and its browser data.", styles),
            step(2, "Ask the user to close the browser and, if appropriate, sign out of Windows before copying the profile.", styles),
            step(3, "Select <b>Show other readable Windows users</b> and review the owner, browser, profile, and size before selecting it.", styles),
            step(4, "Saved-password CSV must be exported by the relevant user while signed into that Windows account and using the browser's official interface.", styles),
            step(5, "On the destination, restore while signed in as the intended user whenever possible.", styles),
            box(
                "Unreadable means out of scope",
                "If Windows denies access, do not weaken permissions or copy files with an elevated workaround. Ask the administrator or the data owner to perform the migration in an authorized session.",
                styles,
                SAFE,
                TEAL,
            ),
            Paragraph("Recommended migration order", styles["section"]),
            step("A", "Back up and verify one user at a time.", styles),
            step("B", "Label the .bvb and .ybckey pair without exposing secrets.", styles),
            step("C", "Restore and validate before moving to the next user.", styles),
            PageBreak(),
        ]
    )

    # 11: checks and troubleshooting
    story.extend(
        [
            Paragraph("Checks and troubleshooting", styles["page_title"]),
            box("Backup does not start", "Close the browser normally, save active work, then approve close assistance. Check for background processes, another user session, or insufficient access.", styles),
            box("Recovery key mismatch", "Confirm the .bvb and .ybckey were created together. Do not try unrelated keys at random. A lost key cannot be reissued by the app or Y-TEC.", styles),
            box("CSV is not detected", "Export directly into the dedicated folder shown by the assistant, wait until the browser has finished writing, and then retry detection.", styles),
            box("Plaintext CSV cannot be deleted", "Close the CSV in the browser, spreadsheet, preview, or antivirus tool. Then delete the exact file named by the warning and verify the folder is empty.", styles),
            box("Restore fails", "Do not launch the browser or delete the destination profile, rollback .bvb, or rollback .ybckey. Record the error code and ask for support.", styles, WARN, WARN_LINE),
            Paragraph("Safe information to include in a support report", styles["section"]),
            step("-", "App version, Windows version, browser name, error code, and the action that failed.", styles),
            step("-", "Never send saved passwords, CSV contents, recovery keys, personal profile paths, or the backup itself unless a trusted support process explicitly requires it.", styles),
            PageBreak(),
        ]
    )

    # 12: security
    story.extend(
        [
            Paragraph("Security, privacy, and limitations", styles["page_title"]),
            Paragraph("Security design", styles["section"]),
            step("-", "New BVB v2 backups use AES-256-GCM with a newly generated recovery key. Salts and nonces are generated with the operating system cryptographic random-number generator.", styles),
            step("-", "The recovery key is never compiled into the app, source repository, portable package, manual, or Forge page.", styles),
            step("-", "Restore verifies the authenticated container, manifest, and file hashes before writing. Existing destination data is protected by an encrypted rollback.", styles),
            step("-", "The app operates locally and does not include telemetry, analytics, advertising, cloud sync, or an update client.", styles),
            Paragraph("Explicitly not implemented", styles["section"]),
            step("-", "Direct decryption of saved passwords or cookies.", styles),
            step("-", "DPAPI or App-Bound Encryption bypass.", styles),
            step("-", "Parsing or direct modification of browser credential databases.", styles),
            step("-", "Automation of browser/OS identity confirmation, account sign-in, or password injection.", styles),
            box(
                "Authenticode status",
                "Direct releases use Y-TEC self-signed Authenticode signatures, not a commercially trusted CA certificate. SmartScreen warnings may still appear. Check the official source, signature, and published SHA-256 before running the app. Certificates are not installed automatically.",
                styles,
                WARN,
                WARN_LINE,
            ),
            PageBreak(),
        ]
    )

    # 13: support/legal
    story.extend(
        [
            Paragraph("Support, privacy, and license", styles["page_title"]),
            Paragraph("Official project", styles["section"]),
            Paragraph(
                'Public page and downloads:<br/>'
                '<link href="https://ytec.cloudfree.jp/forge/en/projects/browser-capsule/" color="#6C5CE7">https://ytec.cloudfree.jp/forge/en/projects/browser-capsule/</link><br/>'
                'Source code and security reporting:<br/>'
                '<link href="https://github.com/ytec-forge-commits/ytec-browser-capsule" color="#6C5CE7">https://github.com/ytec-forge-commits/ytec-browser-capsule</link><br/>'
                'Contact:<br/>'
                '<link href="https://ytec.cloudfree.jp/forge/contact/" color="#6C5CE7">https://ytec.cloudfree.jp/forge/contact/</link>',
                styles["body"],
            ),
            Paragraph("Privacy statement", styles["section"]),
            box(
                "Network behavior",
                "This program will not transfer any information to other networked systems unless specifically requested by the user or the person installing or operating it.",
                styles,
                SAFE,
                TEAL,
            ),
            Paragraph("License", styles["section"]),
            Paragraph(
                "Y-TEC Browser Capsule is open-source software under the Apache License 2.0. See <b>LICENSE</b>, "
                "<b>NOTICE</b>, and <b>THIRD-PARTY-NOTICES.md</b> in the source repository and portable package.",
                styles["body"],
            ),
            Paragraph("Final migration checklist", styles["section"]),
            step(1, "The .bvb and matching .ybckey both exist and verification passes.", styles),
            step(2, "The key is stored separately from the backup and is not in a public or shared location.", styles),
            step(3, "The restored profile opens and its important non-password data has been checked.", styles),
            step(4, "Saved-password CSV import is complete and no plaintext CSV remains.", styles),
            step(5, "Rollback files and the source PC remain available until acceptance is complete.", styles),
            step(6, "Before deleting old data, verify the new PC again with the intended user.", styles),
            Spacer(1, 7 * mm),
            Paragraph(
                f"Y-TEC Browser Capsule User Manual {VERSION} / Manual revised 2026-09-29",
                styles["small"],
            ),
        ]
    )

    doc.build(
        story,
        onFirstPage=lambda canvas, document: page_header_footer(canvas, document),
        onLaterPages=lambda canvas, document: page_header_footer(canvas, document),
    )


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "--repo",
        type=Path,
        default=Path(__file__).resolve().parents[2],
    )
    parser.add_argument(
        "--output",
        type=Path,
        default=None,
    )
    args = parser.parse_args()
    output = args.output or (
        args.repo / "output" / "pdf" / f"Y-TEC_Browser_Capsule_User_Manual_{VERSION}.pdf"
    )
    build_manual(args.repo, output)
    print(output)


if __name__ == "__main__":
    main()
