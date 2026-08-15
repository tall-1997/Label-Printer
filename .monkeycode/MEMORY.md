# User Instruction Memory

This file records user instructions, preferences, and teachings for reference in future interactions.

## Entries

[Linux cross-platform build]
- Date: 2026-08-13
- Context: Discovered by Agent while validating the BarTender preview optimization
- Category: Build Methods
- Instructions:
  - Run .NET CLI with `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1` because the current Linux environment lacks ICU.
  - Add `-p:EnableWindowsTargeting=true` when building the `net8.0-windows` projects on Linux.
  - Use a Windows runner for xUnit execution because the WinForms test host requires `Microsoft.WindowsDesktop.App`.
  - Run test builds and application publishing serially because both operations write the application output directory.

[Avoid MonkeyCode AI integrations]
- Date: 2026-08-14
- Context: User-defined collaboration preference
- Instructions:
  - Do not use MonkeyCode AI related tools, services, branding, or generated integrations during project work.
  - Use local development tools, existing project scripts, and standard Git/GitHub workflows.
