# Kabra Online 2D — Android port (Intersect 0.8.0.339 / V48)

**Experimental source port. NOT a confirmed playable APK.** All changes are isolated to branch `kabra-2d-android-port-339`. Windows client, production V48 server and Crónicas del Abismo are untouched.

## What is wired

- Android `MainActivity` extracts the client resources from Android assets to app-private writable storage.
- AndroidBridge enters the **real** Intersect `Bootstrapper.Start` => `ClientContext` => `MonoGameRunner` => `IntersectGame` pipeline, and attaches MonoGame's Android View.
- AndroidTouchInput merges virtual joystick directions with the original `MonoInput` keyboard path (left/bottom area), plus a pointer substitute to tap the Gwen login/character-selection GUI elsewhere.
- Patches of official commit `76885bcd5b12bd47a69aaad44b682fd93957e458` are applied to the runner checkout **only** during CI; Windows source in GitHub remains unchanged.
- The workflow stages actual `resources/` from this repo but forces `Host=127.0.0.1`, `Port=1`, `UpdateUrl=""` in the packaged test config to **prevent connections to the production server**. Those values deliberately will not connect from a phone.

## Status / blockers

1. No verified green CI build or successful APK publication yet. Compilation errors must be read from the GitHub Actions job itself.
2. Android native API compatibility for MonoGame and Intersect remains unverified; `IPlatformRunner` changes may require additional adaptation.
3. Resource loading on Android remains unverified, even though resources are packaged and copied at runtime.
4. The Android client handshake public key and any V48-specific protocol/DLL changes must be compared with the Windows V48 binary before testing connection. **Do not use a newly generated public key against V48 without verification.**
5. No emulator/device or **isolated** copy of the V48 server is connected. No login, character select, map, touch movement, or PC coexistence has been tested.

## GitHub Actions

[Open workflow runs](https://github.com/Holws-hue/kabra/actions/workflows/kabra-android.yml).

CI triggers on each push to this branch; `workflow_dispatch` is also declared. If GitHub Actions is disabled, repo Settings → Actions → General → Actions permissions → Allow actions; an owner must approve this setting. Manual Run workflow may not appear for a workflow which is not present on the default branch: **do not merge this experimental workflow into main just to expose the button**. Check push runs first.

Do not distribute an APK until the full login/map/multiplayer checklist is verified.

## Safe V48 staging test (no production changes)

1. Copy server **files and databases** into a separate staging machine or isolated directory (not the live V48 installation). Back up before any change. The copy must use separate databases and a separate listen port, and must not bind the live port.
2. Ensure the copy is only reachable over your test LAN or restricted test VPN. Create test accounts there; never use production credentials.
3. Build a new APK against the test Host/Port in the **staged** Android `resources/config.json` before packaging. Current CI intentionally sets loopback/port 1; this requires a staging-specific build change, not a live-server edit.
4. Connect a Windows V48 client also pointed at the same staging copy. Confirm login, character selection, map entry, movement, and another PC player on-screen.
5. Keep production and Android test build's private keys/credentials out of GitHub. The handshake public key should match your staging server; do not commit private keys.

If the build fails, inspect the **first failing Actions step and first compiler error**, not speculative error codes.
