# AGENTS.md

## Project goal

Build Y-TEC Browser Capsule, a Windows desktop application that safely backs up and restores multiple profiles from Chrome, Edge, and Firefox.

The app must use each browser's official CSV export/import UI for saved passwords. It must never decrypt browser password databases or bypass browser or Windows credential protections.

Read `YTEC_Browser_Capsule_Specification_v1.0.md` before changing code.

## Non-negotiable security rules

- Do not read or decrypt Chrome or Edge `Login Data`.
- Do not use DPAPI or App-Bound Encryption bypass techniques.
- Do not extract Firefox passwords from `logins.json` and `key4.db`.
- Do not inject code into browsers.
- Do not automate approval of password export/import or OS authentication prompts.
- Do not log passwords, usernames, URLs, cookies, CSV rows, passphrases, keys, raw SIDs, machine names, or full user paths.
- Do not create a plaintext aggregate ZIP or plaintext profile archive.
- Do not follow or restore reparse points, symlinks, or junctions.
- Do not write outside a validated restore target.
- Do not restore directly over an existing profile without a verified rollback backup.
- Do not add telemetry or network communication.
- Do not request administrator privileges.

If a requested change conflicts with these rules, stop and explain the conflict instead of implementing it.

## Technology

- C#
- WPF
- .NET 10 LTS
- Nullable enabled
- Treat warnings as errors
- Prefer the .NET standard library
- Target Windows x64 for MVP
- Use AES-256-GCM
- Use PBKDF2-HMAC-SHA256 with stored, calibratable work factor
- Stream large files; never load a whole profile or backup into memory

## Product identity and UI

- The official display name is `Y-TEC Browser Capsule`.
- The executable name is `YtecBrowserCapsule.exe`.
- This is a Windows 10 / 11 x64 desktop application. Do not add mobile, web, macOS, or Linux variants.
- Design for people who are not familiar with backup tools: use plain Japanese and plain English, one clear primary action, short explanations, and confirmation before destructive operations.
- Keep Japanese and English UI text, accessibility names, and PDF manuals aligned. Language changes are applied after restart so one workflow never mixes languages.
- Keep the visual style light and approachable with restrained accent colors and rounded surfaces. Avoid a dense, gray, enterprise-tool appearance.
- Do not sacrifice contrast, keyboard operation, or accessibility for decoration.

## Settings and publication safety

- `settings.json` schema v2 stores only the default backup directory and UI language. Reading schema v1 must preserve the existing backup directory and migrate language to `SystemDefault`.
- Never commit `.ybckey`, `.bvb`, saved-password CSV, certificate private keys, signing keys, tokens, or live browser profiles. Direct releases use the existing approved Y-TEC self-signing boundary; never generate, move, export, or reconfigure its private key as part of a routine release, and never store it in GitHub Secrets, the workspace, logs, or artifacts. Do not initiate a SignPath application or reapplication without a new explicit owner request.
- Before a public push or release, run `eng/Test-Secrets.ps1 -IncludeGitHistory` and scan the completed portable ZIP separately. A current-tree scan alone is not sufficient.

## Architecture

Keep domain and application logic independent of WPF.

Browser-specific behavior must be implemented through browser adapters:

- Chrome
- Edge
- Firefox

Do not scatter browser paths, process names, exclusions, CSV rules, or settings-page URIs through UI code.

## Dependencies

Before adding a production dependency:

1. Explain why the standard library is insufficient.
2. State the package license.
3. Confirm commercial use and redistribution are allowed.
4. Add or update `THIRD-PARTY-NOTICES.md`.
5. Add tests for the behavior introduced by the dependency.

Prefer MIT, Apache-2.0, or BSD dependencies.

## Workflow

Work phase by phase according to the specification.

For every task:

1. Inspect the current repository and relevant tests.
2. State a short implementation plan.
3. Make the smallest coherent change.
4. Add or update tests.
5. Run formatting, build, and relevant tests.
6. Review the diff for security and privacy regressions.
7. Report commands run, results, limitations, and remaining work.

Do not claim completion if tests were not run.

## Required commands

Use the repository's actual scripts when they exist. Until then, use:

```powershell
dotnet restore
dotnet build --configuration Release
dotnet test --configuration Release
```

## Testing priorities

Security-critical tests are mandatory:

- Wrong passphrase
- Header, ciphertext, and tag tampering
- Truncated and reordered chunks
- Nonce uniqueness
- Path traversal
- Absolute paths and UNC paths
- NTFS alternate data streams
- Windows reserved names
- Reparse points
- Restore target escape
- Rollback failure
- Browser starts during restore
- CSV content never appears in logs

Use synthetic test credentials only.

## Destructive operations

Before changing profile files:

- Verify the browser is closed.
- Show a restore plan.
- Create and verify a rollback backup.
- Restore into a temporary location.
- Validate hashes and paths.
- Replace the target only after validation.

## Documentation

Update these documents when behavior changes:

- `README.md`
- `docs/architecture.md`
- `docs/backup-format.md`
- `docs/threat-model.md`
- `docs/manual-test-plan.md`
- This `AGENTS.md` when a recurring project rule is discovered

## Definition of done

A change is done only when:

- It matches the specification.
- It has tests appropriate to its risk.
- Release build succeeds.
- Relevant tests succeed.
- No sensitive values appear in logs or fixtures.
- Security-sensitive assumptions are documented.
- The final response clearly identifies anything not verified.
