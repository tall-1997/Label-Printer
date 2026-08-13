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
