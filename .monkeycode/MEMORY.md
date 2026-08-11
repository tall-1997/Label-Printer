# User Instruction Memory

This file records user instructions, preferences, and teachings for reference in future interactions.

## Format

### User Instruction Entry
User instruction entries should follow this format:

[User Instruction Summary]
- Date: [YYYY-MM-DD]
- Context: [Mentioned scenario or time]
- Instructions:
  - [Content of user teaching or instruction, described line by line]

### Project Knowledge Entry
Entries discovered by the Agent during task execution should follow this format:

[Project Knowledge Summary]
- Date: [YYYY-MM-DD]
- Context: Discovered by Agent while performing [specific task description]
- Category: [Operations & Deployment|Build Methods|Testing Methods|Troubleshooting & Debugging|Workflow & Collaboration|Environment Configuration]
- Instructions:
  - [Specific knowledge points, described line by line]

## Deduplication Strategy
- Before adding a new entry, check for similar or identical instructions.
- If a duplicate is found, skip the new entry or merge it with the existing one.
- When merging, update the context or date information.
- This helps avoid redundant entries and keeps the memory file tidy.

## Entries

[Windows Release Verification Workflow]
- Date: 2026-08-11
- Context: Discovered by Agent while performing GitHub Actions Windows installer release verification
- Category: Build Methods
- Instructions:
  - For C# Windows installer releases, verify local `HEAD`, `origin/main`, and the release tag peeled commit all point to the same commit before marking the release complete.
  - The release workflow is `.github/workflows/release-csharp.yml`; it publishes `BarTenderPrinter/BarTenderPrinter.csproj` as self-contained `win-x64`, builds `installer/BarTenderPrinter.iss`, and uploads `BarTenderPrinter-Setup-${VERSION}-win-x64.exe` to GitHub Releases.
  - After the workflow succeeds, verify the GitHub Release asset name, size, and SHA-256 digest, then download the asset to a temporary directory and compare the local SHA-256 with the Release asset digest.
  - When using a temporary GitHub CLI token for release work, run `gh auth logout` after verification and remind the user to revoke any token exposed in chat.
