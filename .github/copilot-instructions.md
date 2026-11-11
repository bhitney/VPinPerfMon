# Copilot instructions for VPinPerfMon

## Build hygiene: no warnings

- The solution (`VPinPerfMon.slnx`) must build with **0 warnings and 0 errors** in Debug and Release. Treat a new warning as a defect in the change that introduced it.
- After any code change, build the whole solution (`dotnet build VPinPerfMon.slnx -c Debug --nologo`) and check the `Warning(s)` count. Don't stop at the single project you edited, because the console tool, GUI and Setup projects share code and settings.
- Fix the cause of warnings instead of suppressing them. Common issues include:
  - **Possible null reference (CS8602, CS8600, CS8604):** initialize the field or variable before it is used, including before any lambda or event handler that captures it. Or make the type nullable and check it.
  - **Unreachable code (CS0162, CS0219 and similar):** delete the dead code. Do not comment it out.
  - **Unused variables, fields or parameters:** remove them.
  - **Platform-specific APIs (CA1416):** the console tool uses `PerformanceCounter`, so `VPinPerfMon` is declared Windows-only with `[assembly: SupportedOSPlatform("windows")]`. New Windows-only calls are fine, and don't need their own guard.
- Keep `<Nullable>enable</Nullable>` in every project.
- The GitHub Actions run (`.github/workflows`) shows the same compiler warnings as annotations, plus runner notices such as deprecated Node versions. Keep the actions on current major versions, and check that the new tag exists and runs on a supported Node version before bumping it.

## Editing

- Make minimal changes. Match the style of the surrounding code, and don't add comments unless they explain something non-obvious.
- When replacing text in a file, include the whole line or block in the match. Matching only the end of a line has deleted the start of that line before.
- Use `get_errors` on edited files for quick feedback, then run the full solution build before finishing.
