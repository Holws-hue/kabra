# Kabra Online 2D Android port — Intersect .339 V48

Source: official Intersect commit 76885bcd5b12bd47a69aaad44b682fd93957e458.

Build experiment: GitHub Actions installs .NET 8 Android workload, clones pinned Intersect engine, applies Android package guards, then runs `dotnet restore` and `dotnet build` against the actual Intersect.Client.Core project reference.

This is NOT a playable APK. The original IntersectGame and its desktop startup need Android lifecycle adaptation. No login, character selection, map or movement was tested. No live V48 server changes.

GitHub workflow is triggered by a push to the isolated `kabra-2d-android-port-339` branch; a failed build should be diagnosed from its actual job logs, never invented.
