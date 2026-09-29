[日本語](README.md)

# Y-TEC Browser Capsule

Y-TEC Browser Capsule is an open-source Windows desktop app for backing up selected Google Chrome, Microsoft Edge, and Mozilla Firefox profiles into an encrypted container and restoring them on another PC.

- Current version: **1.2.1**
- The 2026-09-29 republication candidate rejects backups containing protected credential files before restoration. BVB v1/v2, recovery-key formats, and the approved manuals remain unchanged.
- Supported OS: Windows 10 / 11, 64-bit
- Distribution: self-contained portable ZIP; no installer required
- License: [Apache License 2.0](LICENSE)
- UI languages: English / Japanese / System default

Mobile devices, Windows 7 / 8 / 8.1, 32-bit Windows, macOS, and Linux are not supported.

## Highlights

- Discovers multiple Chrome, Edge, Firefox, and Firefox ESR profiles.
- Optionally includes profiles of other Windows users only when the current account can already read them.
- Selectively backs up settings, bookmarks, history, extensions, site data, sessions, or complete profiles.
- Streams multiple selected profiles into one authenticated `.bvb` container.
- Generates a new 256-bit `.ybckey` recovery-key file for every backup; no passphrase typing is required for new backups.
- Verifies authentication tags, the encrypted manifest, and every file SHA-256.
- Helps close only confirmed target-browser processes after normal exit has been attempted.
- Guides the user through the browser's official saved-password CSV export and import screens.
- Temporarily encrypts a completed CSV and attempts immediate plaintext deletion.
- Lets a no-password profile be skipped while the CSV assistant is waiting.
- Requires an explicit source-to-destination mapping and creates an encrypted rollback before restore.
- Includes Japanese and English PDF manuals that can be opened from the app.

## Protect the recovery key

Every new backup is a pair:

- `.bvb`: the encrypted backup container
- `.ybckey`: the secret recovery-key file required to restore it

Losing either file makes restoration impossible. Anyone who obtains both files may be able to restore the browser data. Store them on separate protected devices or in separate approved locations whenever possible.

Never paste a recovery key into source code, issues, screenshots, email, chat, WordPress, or a public share. Neither Y-TEC nor the app can regenerate a lost key.

The implementation obtains a fresh key, salts, and nonces from the operating system cryptographic random-number generator for each backup. There is no fixed application key, shared master key, or secret signing key embedded in source or release files. Publication CI checks current tracked files and the complete Git history for private-key formats, representative token formats, `.ybckey`, `.pfx`, `.pem`, and similar secret-bearing files.

See the [threat model](docs/threat-model.md), [BVB format](docs/backup-format.md), and [security policy](SECURITY.md).

## Security boundary

The project deliberately does not implement:

- direct decryption of stored passwords;
- DPAPI or App-Bound Encryption bypass;
- parsing or direct modification of browser credential databases;
- extraction from Firefox `logins.json` or `key4.db`;
- code injection, automated identity confirmation, or automated OS/browser approval;
- administrator elevation, ACL changes, ownership takeover, or user impersonation;
- telemetry, analytics, advertising, cloud sync, or an automatic update client;
- a plaintext aggregate ZIP.

Saved passwords are exported and imported by the user through the browser's official UI. The app does not display or log CSV values, URLs, usernames, or passwords, and it does not claim to know whether an official import succeeded.

## Privacy

The app works locally. Its settings file stores only the default backup directory and language choice, never recovery keys, passphrases, browser data, or credentials.

> This program will not transfer any information to other networked systems unless specifically requested by the user or the person installing or operating it.

See [PRIVACY.md](PRIVACY.md).

## Downloads and authenticity

Official portable ZIP files are published through [GitHub Releases](https://github.com/ytec-forge-commits/ytec-browser-capsule/releases) and [Y-TEC Forge](https://ytec.cloudfree.jp/forge/en/projects/browser-capsule/). Before extracting a ZIP, compare its SHA-256 with the adjacent `.sha256` file and the official download page.

The republished direct release uses approved Y-TEC self-signed Authenticode signatures. SHA-256 values are generated after final signing and packaging. See [CODE_SIGNING.md](CODE_SIGNING.md). This is not a commercially trusted CA certificate and does not guarantee removal of SmartScreen warnings. Certificates are not installed automatically.

No Microsoft Store version is currently available. Update manually from Forge or GitHub Releases. Contact: https://ytec.cloudfree.jp/forge/contact/

## Build and test

Requirements:

- Windows 10 / 11 x64
- .NET SDK 10.0.302 or a later .NET 10.0 feature band
- PowerShell 7

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' restore .\YtecBrowserCapsule.sln
& 'C:\Program Files\dotnet\dotnet.exe' build .\YtecBrowserCapsule.sln -c Release --no-restore
& 'C:\Program Files\dotnet\dotnet.exe' test .\YtecBrowserCapsule.sln -c Release --no-build
& 'C:\Program Files\dotnet\dotnet.exe' format .\YtecBrowserCapsule.sln --verify-no-changes --no-restore
& 'C:\Program Files\dotnet\dotnet.exe' package list --project .\YtecBrowserCapsule.sln --vulnerable --include-transitive
.\eng\Test-Secrets.ps1 -IncludeGitHistory
```

Create and verify the self-contained portable release:

```powershell
.\eng\New-PortableRelease.ps1 -Version 1.2.1
.\eng\Test-PortableRelease.ps1 -Version 1.2.1
```

Production projects under `src/` use only .NET 10 and WPF framework libraries; they have no third-party NuGet runtime dependency. Development and test dependencies are listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## Documentation

- [English PDF User Manual](output/pdf/Y-TEC_Browser_Capsule_User_Manual_1.2.1.pdf)
- [Japanese PDF User Manual](output/pdf/Y-TEC_Browser_Capsule_操作マニュアル_1.2.1.pdf)
- [Architecture](docs/architecture.md)
- [Threat model](docs/threat-model.md)
- [BVB container format](docs/backup-format.md)
- [Error codes](docs/error-codes.md)
- [Manual test plan](docs/manual-test-plan.md)
- [Release readiness](docs/release-readiness.md)
- [Code-signing policy](CODE_SIGNING.md)
- [Architecture decisions](docs/adr/)

## Contributing and license

Read [CONTRIBUTING.md](CONTRIBUTING.md) and [SECURITY.md](SECURITY.md) before proposing a change. Proposals that add credential decryption or authentication bypass are out of scope.

The source code is licensed under the [Apache License 2.0](LICENSE). Copyright attribution is in [NOTICE](NOTICE), and runtime and test notices are in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
