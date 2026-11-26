# Windows Release x64 build-error correction

Date: 2026-11-21  
Base source: `RunMind-Phase1-Remaining-Gaps-Implementation-2026-11-21.zip`  
Status: **Source-level corrections applied; Windows solution rebuild required**

## Reported failures

The supplied Windows build log reported four compiler errors in the shared `Shared/SystemManager.cs` file, repeated in each of the three host projects (12 reported error instances):

- `CS0103`: `SupportsNewWindowBehavior` was referenced at three call sites but had no method definition.
- `CS0136`: an adapter-local variable named `properties` collided with the enclosing `IPGlobalProperties properties` declaration.

## Corrections applied

1. Added `SupportsNewWindowBehavior(SystemApplicationRecord application)`. It rejects packaged applications, resolves the discovered executable through the existing validated executable resolver, and only returns true for a narrow allowlist of applications with known `--new-window` support: Chrome, Edge, Firefox, Brave, Vivaldi, Opera, and Visual Studio Code. Unknown applications remain limited to reuse/new-process behavior.
2. Renamed the per-adapter `GetIPProperties()` local to `adapterProperties`; the separate `IPGlobalProperties properties` value remains unchanged for host name, domain, and active TCP connection reporting.
3. Added two regression checks to `tests/system-manager-contract.test.cjs` specifically covering the missing helper and local-name collision.
4. Added a changelog entry and this correction report.

## Validation performed here

| Check | Result |
|---|---:|
| Complete Node test suite | **66/66 passed** |
| JavaScript syntax (`node --check`) | **20/20 passed** |
| JSON files | **2/2 parsed** |
| C# compiler availability | Not available |
| Windows Release x64 solution build | Not run in this environment |

The tests include source-contract regression checks for both reported C# errors. They do not compile C#. This environment has no .NET SDK/MSBuild or PowerShell, so the corrected solution must be rebuilt on Windows.

## Next Windows check

From the extracted source root, run:

```powershell
.\Validate-Phase1-Windows.ps1 -Configuration Release -Platform x64
```

The previous errors should no longer occur. Any new diagnostics should be treated as new build findings, not assumed fixed by these source-level checks.
