# Goal

Make style/analyzer rules from `.editorconfig` show up in `dotnet build`, and auto‑fix as many as possible—both locally (VS Code) and in CI.

---

## 0) Quick start — copy/paste

**Project/Repo settings**

```xml
<!-- Directory.Build.props or your .csproj -->
<Project>
  <PropertyGroup>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
    <EnableNETAnalyzers>true</EnableNETAnalyzers>
    <AnalysisLevel>latest</AnalysisLevel> <!-- or a specific level: 8.0, 9.0 -->
    <!-- Optional: fail build on any warning (or set specific rules to error in .editorconfig) -->
    <!-- <TreatWarningsAsErrors>true</TreatWarningsAsErrors> -->
  </PropertyGroup>
</Project>
```

**.editorconfig (baseline severities)**

```ini
# top of repo
root = true

[*.cs]
# make analyzers participate in build and be visible
# (warning/error are enforced; suggestion/silent are not)
dotnet_analyzer_diagnostic.severity = warning

# optional: just style category as warnings
# dotnet_analyzer_diagnostic.category-Style.severity = warning

# per‑rule examples (tune as needed)
dotnet_diagnostic.IDE0005.severity = warning   # remove unnecessary usings
dotnet_diagnostic.IDE0055.severity = warning   # format document
dotnet_diagnostic.CA1822.severity = warning    # mark members as static when possible
```

**VS Code — fix on save** (`.vscode/settings.json`)

```json
{
  "[csharp]": {
    "editor.formatOnSave": true,
    "editor.codeActionsOnSave": {
      "source.organizeImports": "explicit",
      "source.fixAll": "explicit"
    }
  }
}
```

> Use `"explicit"` to trigger only on manual saves; switch to `"always"` if you also want auto‑save to fix.

**One‑shot fixer (CLI)**

```bash
# run from the solution directory
# Fix style + analyzer issues respecting .editorconfig

dotnet format                      # default: applies warn+error fixes
# include info‑level fixes too
dotnet format --severity info
# target specific buckets or rules
 dotnet format style --diagnostics IDE0005,IDE0055 --severity info
 dotnet format analyzers --diagnostics CA1822 --severity warn
# CI mode: fail if changes would be made
 dotnet format --verify-no-changes
```

---

## 1) One‑time repo cleanup

1. Commit your current state.
2. Run `dotnet format --severity info` at the solution root.
3. Review the diff, commit.
4. (Optional) Add stricter severities in `.editorconfig` and repeat.

This establishes a clean baseline so future PRs don’t carry legacy noise.

---

## 2) Day‑to‑day local workflow (VS Code)

* **On save**: formatting, remove unused usings, and many IDE/CA fixes apply automatically via `"source.fixAll"` and `"source.organizeImports"`.
* **On demand**: place cursor on a diagnostic → **Quick Fix** (Ctrl/Cmd+.) → **Fix all occurrences in file** (when available).

If fixes aren’t triggering:

* Confirm the C# extension is enabled.
* Check that the file is part of a loaded solution/project.
* Ensure rule severity ≥ `warning` in `.editorconfig` if you want it visible during build; the fixer itself may still be offered in the editor even if `suggestion`.

---

## 3) Make rules participate in build

* `EnforceCodeStyleInBuild=true` → IDE00xx rules show up in `dotnet build`.
* `EnableNETAnalyzers=true` + `AnalysisLevel=latest` → built‑in CA\*\*\*\* rules run with the latest defaults.
* For **old non‑SDK** or legacy .NET Framework projects, consider migrating to SDK‑style or add the `Microsoft.CodeAnalysis.NetAnalyzers` NuGet package to get analyzers at build.

---

## 4) CI recipes

### GitHub Actions

```yaml
name: ci
on: [push, pull_request]
jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0.x'  # bump as needed
      - run: dotnet --info
      - run: dotnet restore
      - run: dotnet build --no-restore --configuration Release
      - run: dotnet test --no-build --configuration Release
      - name: Enforce formatting & analyzers
        run: dotnet format --verify-no-changes --severity info
```

### Azure Pipelines

```yaml
trigger:
- main

pool:
  vmImage: 'ubuntu-latest'

steps:
- task: UseDotNet@2
  inputs:
    version: '8.0.x'

- script: dotnet restore
- script: dotnet build --no-restore --configuration Release
- script: dotnet test --no-build --configuration Release
- script: dotnet format --verify-no-changes --severity info
  displayName: 'Enforce formatting & analyzers'
```

**Tip:** If you don’t want `info`‑level changes to block CI, drop `--severity info`.

---

## 5) Pre‑commit hooks (optional but effective)

**POSIX (.git/hooks/pre-commit)**

```bash
#!/usr/bin/env bash
set -euo pipefail
files=$(git diff --cached --name-only --diff-filter=ACM | grep -E '\\.cs$' || true)
if [ -n "$files" ]; then
  echo "Running dotnet format on staged C# files..."
  dotnet format --include $files --severity info
  # re-stage potentially modified files
  echo "$files" | xargs git add
fi
```

Make it executable: `chmod +x .git/hooks/pre-commit`

**Windows PowerShell (.git\hooks\pre-commit.ps1)**

```powershell
$ErrorActionPreference = 'Stop'
$files = git diff --cached --name-only --diff-filter=ACM | Where-Object { $_ -match '\\.cs$' }
if ($files) {
  Write-Host 'Running dotnet format on staged C# files...'
  dotnet format --include $files --severity info
  $files | ForEach-Object { git add $_ }
}
```

> Note: `--include` accepts files/dirs relative to the working directory. Run the hook at the repo root for best results.

---

## 6) Common rules with reliable auto‑fixers (examples)

These have solid Fix‑All support and are good candidates to keep at `warning` or `error`:

* **IDE0055** — Format document
* **IDE0005** — Remove unnecessary usings
* **IDE0065** — Using directives placement
* **IDE0044** — Make field readonly
* **IDE0017** — Use object initializer
* **IDE0028** — Use collection initializer
* **IDE0066** — Convert switch statement to expression
* **CA1822** — Member can be static

> Many more have fixers; if a rule is noisy without a reliable fix, keep it at `suggestion` until the codebase is clean.

---

## 7) “Make it fail” options

* Repository‑wide hammer: `TreatWarningsAsErrors=true` (or pass `-warnaserror` in CI).
* Targeted: set only specific rules to `error` in `.editorconfig` (preferred once baseline is clean).

---

## 8) Troubleshooting

* **I don’t see IDE rules in build output** → Ensure `<EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>` and rule severity ≥ `warning` in `.editorconfig`.
* **`dotnet format` didn’t change anything** → Check severities; by default it applies warn/error. Add `--severity info` to include info‑level fixes.
* **VS Code isn’t fixing on save** → Confirm `.vscode/settings.json` is in the workspace, the C# extension is active, and auto‑save behavior matches your `"explicit"/"always"` choice.
* **Legacy projects** → Prefer SDK‑style; otherwise add analyzers via NuGet.

---

## 9) Team policy suggestion (sane defaults)

1. Run `dotnet format --severity info` once to sanitize the repo.
2. Keep `IDE0005`, `IDE0055`, `IDE0065`, `IDE0044` at `warning` (or `error`).
3. Enforce `dotnet format --verify-no-changes` in CI.
4. Enable VS Code `formatOnSave` + `fixAll`.

That’s it—warnings surface in build, the easy ones auto‑fix, and CI guards drift.
