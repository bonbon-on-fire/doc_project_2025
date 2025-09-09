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

## 6) Auto‑fix catalog (built‑in + popular analyzers)

These rules have reliable code fixes and work well with **Fix‑All** (VS/VS Code) and `dotnet format`. Start with these, keep them at `warning` (or `error`) once the repo is clean.

### 6.1 Built‑in IDE (code style) rules — strong auto‑fixers

**Formatting & imports**

* **IDE0055** — Format document
* **IDE0005** — Remove unnecessary usings
* **IDE0065** — Using directives placement (file‑scoped or inside namespace)

**Braces & blocks**

* **IDE0011** — Add braces to control statements
* **IDE0063** — Use simple `using` statement

**Initializers & patterns**

* **IDE0017** — Use object initializer
* **IDE0028** — Use collection initializer
* **IDE0066** — Convert `switch` statement to expression (when safe)

**Simplifications**

* **IDE0004** — Remove unnecessary cast
* **IDE0031** — Use null‑propagation (`?.`)
* **IDE0032** — Use auto‑property
* **IDE0037** — Use inferred member name
* **IDE0040** — Add accessibility modifiers
* **IDE0057** — Use range operator (`..`)
* **IDE0058** — Expression value is never used (remove)
* **IDE0060** — Remove unused parameter (when no referenced)
* **IDE0062** — Make local function `static`
* **IDE0090** — Use target‑typed `new`

> **Expanded:** Many more IDE rules have reliable code fixes. After your baseline is clean, consider elevating these to `warning`/`error` (keep subjective ones like naming, `var`/explicit type, and wrapping at `suggestion`). Common fixable rules include:

* **Names & qualifiers**

  * IDE0001 — Simplify names
  * IDE0003 — Remove unnecessary `this`/`Me`
  * IDE0049 — Use language keywords instead of framework types

* **Null/typeof/name**

  * IDE0031 — Use null‑propagation (`?.`)
  * IDE0041 — Use `is null` check
  * IDE0082 — Convert `typeof(T).Name` to `nameof(T)`

* **Expression‑bodied/throw**

  * IDE0016 — Use `throw` expression
  * IDE0021–IDE0027, IDE0053, IDE0061 — Use expression‑bodied members (constructors, methods, operators, properties, indexers, accessors, lambdas, local functions)

* **Coalescing & patterns**

  * IDE0029/IDE0030 — Use coalesce / coalesce assignment (`??`, `??=`)
  * IDE0019/IDE0020/IDE0038 — Use pattern matching instead of `as`/`is` + cast/null checks
  * IDE0170 — Simplify property patterns

* **Collections**

  * IDE0028 — Use collection initializers/expressions
  * IDE0301/IDE0305 — Prefer collection expressions (`[]`, `[..]`) where applicable (C# 12+)

* **Indices, ranges & slices**

  * IDE0056 — Use index operator (`^`)
  * IDE0057 — Use range operator (`..`)

* **Deconstruction**

  * IDE0042 — Deconstruct variable declarations

* **Conditionals & parentheses**

  * IDE0045/IDE0046 — Use conditional expression for assignment/return
  * IDE0047/IDE0048 — Parentheses preferences (remove/add for clarity)

* **Fields & modifiers**

  * IDE0040 — Add accessibility modifiers
  * IDE0044 — Add `readonly` modifier
  * IDE0036 — Order modifiers

* **Cleanup & unused**

  * IDE0051 — Remove unused private member
  * IDE0052 — Remove unread private member
  * IDE0059 — Unnecessary assignment of a value
  * IDE0058 — Expression value is never used

* **Switches & patterns**

  * IDE0010 — Add missing cases to `switch`
  * IDE0066 — Convert `switch` statement to expression

* **Language simplifications**

  * IDE0004 — Remove unnecessary cast
  * IDE0090 — Use target‑typed `new`

Full list can be found at: [https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/style-rules/](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/style-rules/)

> Many of these default to `suggestion`; use `--severity info` with `dotnet format` to apply them in bulk. Some are IDE‑only (show in the editor, not build) but still fixable via Quick Fix or `dotnet format`. For a complete reference, see the .NET code‑style rule index and individual rule pages.

**Bulk‑fix examples**

```bash
# all style fixes at >= warn (use --severity info to include suggestions)
dotnet format style --severity warn
# specific IDE rules
dotnet format style --diagnostics IDE0005,IDE0055,IDE0011 --severity info
```

### 6.2 Built‑in Code Quality rules (CAxxxx) — fixable & usually safe

Performance‑oriented fixes that are broadly safe to apply (still review public API surface changes):

* **CA1829** — Use `Length`/`Count` property instead of `Enumerable.Count` on arrays/collections
* **CA1836** — Prefer `IsEmpty` over `Count` when available
* **CA1834** — Prefer `StringBuilder.Append(char)` for single‑char strings
* **CA1847** — Use `char` literal for single‑character lookup (`"x"` → `'x'`)
* **CA1837** — Prefer `Environment.ProcessId` over `Process.GetCurrentProcess().Id`
* **CA1802** — Use `const` for compile‑time constants (⚠️ changing public fields can be a breaking change)

> Use `dotnet format analyzers ...` to apply CA fixes that have code‑fix providers. For the **full, up‑to‑date list of CA rules**, see Microsoft’s official index: [https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/](https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/) (each rule page notes if a **Code fix** is available). Commonly fixable examples include **CA1829** (use `Length`/`Count`), **CA1836** (prefer `IsEmpty`), **CA1834** (`StringBuilder.Append(char)`), **CA1847** (use `'char'` literal), **CA1837** (`Environment.ProcessId`), **CA1802** (use `const`), **CA1826** (use property instead of LINQ), **CA1860** (avoid `Enumerable.Any()` where a count/length exists). Keep potentially breaking API changes (e.g., **CA1822** *Mark members static*) gated behind manual review or restricted scopes.

**Bulk‑fix examples**

```bash
# include CA rules that have code fixes
dotnet format analyzers --severity warn
# target specific rules
dotnet format analyzers --diagnostics CA1829,CA1836,CA1834,CA1847 --severity info
```

### 6.3 StyleCop.Analyzers (optional)

Adds style rules with many automatic fixes.

**Install**

```xml
<!-- Directory.Build.props or per‑project -->
<ItemGroup>
  <PackageReference Include="StyleCop.Analyzers" Version="1.*" PrivateAssets="all" />
</ItemGroup>
```

**Run fixes**

* Many SA rules surface Quick Fixes (Fix‑All in doc/project). Use VS/VS Code Quick Fix or `dotnet format analyzers` to apply available code fixes.

### 6.4 Roslynator (optional, lots of fixes)

Hundreds of analyzers & code fixes beyond the built‑ins.

**Install CLI**

```bash
dotnet tool install -g roslynator.dotnet.cli
```

**Use**

```bash
# analyze & fix entire solution (uses analyzers referenced by your projects)
roslynator fix path/to/YourSolution.sln
# or project
eroslynator fix path/to/Project.csproj
```

> You can scope with `--diagnostics RCSxxxx` or `--include` paths. Prefer committing in small batches.

### 6.5 Meziantou.Analyzer (optional)

Practical performance, security, and correctness rules; many have code fixes.

**Install**

```xml
<ItemGroup>
  <PackageReference Include="Meziantou.Analyzer" Version="2.*" PrivateAssets="all" />
</ItemGroup>
```

**Apply fixes**

* Use Quick Fix/ Fix‑All in the editor, or run `dotnet format analyzers`.

### 6.6 What *not* to auto‑fix blindly

Keep these at `suggestion` or review individually:

* **CA1062** (validate arguments) — can add boilerplate/null checks indiscriminately
* **CA1848** (LoggerMessage) — requires refactoring to the LoggerMessage pattern
* **CA2007** (ConfigureAwait) — policy dependent; library vs app
* **CA1822** (make member static) — may break virtual/override or public APIs

---

## 7) “Make it fail” options

* Repository‑wide hammer: `TreatWarningsAsErrors=true` (or pass `-warnaserror` in CI).
* Targeted: set only specific rules to `error` in `.editorconfig` (preferred once baseline is clean).

---

## 8) Troubleshooting

* **I don’t see IDE rules in build output** → Ensure `<EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>` and rule severity ≥ `warning` in `.editorconfig`.
* **`dotnet format`**\*\* didn’t change anything\*\* → Check severities; by default it applies warn/error. Add `--severity info` to include info‑level fixes.
* **VS Code isn’t fixing on save** → Confirm `.vscode/settings.json` is in the workspace, the C# extension is active, and auto‑save behavior matches your `"explicit"/"always"` choice.
* **Legacy projects** → Prefer SDK‑style; otherwise add analyzers via NuGet.

---

## 9) Team policy suggestion (sane defaults)

1. Run `dotnet format --severity info` once to sanitize the repo.
2. Keep `IDE0005`, `IDE0055`, `IDE0065`, `IDE0044` at `warning` (or `error`).
3. Enforce `dotnet format --verify-no-changes` in CI.
4. Enable VS Code `formatOnSave` + `fixAll`.

That’s it—warnings surface in build, the easy ones auto‑fix, and CI guards drift.

---

## 10) Rule cookbook: IDE0011 — Add braces (warn + auto‑fix)

Make missing braces show up as **warnings** in `dotnet build` and enable bulk fixes.

**MSBuild (once per repo)**

```xml
<!-- Directory.Build.props or a .csproj -->
<PropertyGroup>
  <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
</PropertyGroup>
```

**.editorconfig**

```ini
[*.cs]
# Prefer braces on control statements and warn when missing
csharp_prefer_braces = true:warning       # or: when_multiline:warning
# Force build-time severity for the specific rule
dotnet_diagnostic.IDE0011.severity = warning
```

**Auto-fix (CLI)**

```bash
dotnet format style --diagnostics IDE0011
```

This applies the Roslyn code fix to add braces where safe.

**Auto-fix (VS Code)**
The existing settings in this playbook (`formatOnSave` + `source.fixAll`) will apply the "Add braces" fix on save when available.
