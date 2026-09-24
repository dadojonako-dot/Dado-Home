# Windows pilot

Portable test distribution for Windows 10/11 x64. It contains the published self-contained .NET backend, Vite production assets served by the backend at the same origin, and PostgreSQL Windows binaries. Both services bind to loopback only. There are no credentials in the distribution; the launcher creates them in the local `runtime` directory.

Extract into an ASCII-only path such as `C:/Dado-Home-Pilot`. PostgreSQL 16 initdb with UTF-8 failed when tested under a Cyrillic path; the launcher detects this before creating any settings or database files and asks the user to relocate the folder. Spaces in the path are supported.

## Build

Requirements for the builder only: .NET 8 SDK, Node compatible with the locked Vite version, and extracted PostgreSQL 16 Windows x64 binaries (the `pgsql` folder, including bin/lib/share/doc). Install admin dependencies with `npm ci` in `admin`.

For a complete distribution pass `-VcRedist C:/tools/VC_redist.x64.exe`, downloaded from Microsoft's official `https://aka.ms/vs/17/release/vc_redist.x64.exe`. Its Authenticode signature is checked before copying. A user whose Windows lacks Visual C++ Runtime installs it once from `prerequisites`; developer tools and internet access are not needed to run the kit.

```powershell
./pilot/Build-Windows.ps1 -PostgresRoot C:/tools/pgsql -Output C:/build/Dado-Home-Pilot
```

The destination must be new or empty. The script does not delete/overwrite an existing pilot. It type-checks and builds the panel, publishes a self-contained backend, copies PostgreSQL and the launchers, and includes PostgreSQL's legal notice. Runtime libraries in the published app must remain together.

The backend applies migrations before any hosted services or request handling. `PilotSeed.Initialize` executes only when both `ASPNETCORE_ENVIRONMENT=Pilot` and `Pilot__Enabled=true`. Deterministic IDs make the seed repeatable without replenishing sold stock, resetting passwords, or duplicating users. Outside Pilot the seed is a no-op. HTTPS redirection is disabled only for the loopback pilot; existing deployment behaviour remains unchanged elsewhere.

`runtime/settings.json` holds local ports, DB/JWT keys and initial role passwords. `runtime/Access.txt` is the user's initial access sheet. These files and the entire database/log directory must be excluded from published archives and Git. `Stop.ps1` verifies the stored PID, executable path and process creation time before stopping the backend and stops only this distribution's PostgreSQL data directory.

## Verification

```powershell
$env:PILOT_KIT_ROOT='C:/build/Dado-Home-Pilot'
node tests/pilot.windows.mjs
```

Use a disposable copy: this test performs a sale and return. It checks first startup, assets using the same origin, all role logins, access boundaries, duplicate start, stop/restart persistence and idempotency. The lower-level POS test remains `tests/pos.integration.mjs` with `POS_TEST_CONNECTION` pointing to a separate empty PostgreSQL database.

After testing, create the deliverable from the built binaries and launchers, excluding `runtime`. Include a source ZIP and the Russian quick-start guide. A new copy must create new random credentials and a clean sample catalog.

## Scope

This is a local POS/backend/admin pilot, not a production installer or mobile release. Existing unfinished admin sections remain labelled as such. Card/QR record external terminal confirmation; no payment processor/fiscal integration is simulated as real. Reports use UTC.
