# Skill: Building, running & packaging the WinUI app

How to compile, run, and produce installable outputs (MSIX for the Store, zip for unpackaged). Read this before changing `.csproj`, `.sln`, `.appxmanifest`, packaging/certificate files, CI workflows, or any build script under `Source/`.

---

## Prerequisites (you need these *on the machine*)

- **Windows 10/11** (the app is Windows-only).
- **Visual Studio 2022** (17.x) with the **".NET Desktop Development"** and **"Windows App SDK / WinUI"** workload (required for `msbuild.exe` and the WinUI XAML compiler). The build scripts hardcode `C:\Program Files\Microsoft Visual Studio\2022\Community\...` but you may edit the `$vstudio` variable (Community vs Professional).
- **.NET 9 SDK** pinned by `global.json` (`9.0.102`, `rollForward: latestFeature`).
- **WebView2 Runtime** (bundled with Windows / WinUI on most systems).

## The solution & configurations

Solution: `Source/Desktop/MdcAi.sln`. Five projects:
```
MdcAi                      (WinExe, WinUI shell)      net9.0-windows10.0.19041.0
  └ MdcAi.ChatUI          (Views/VMs/WebView)        net9.0-windows10.0.19041.0
      └ MdcAi.Extensions.WinUI (helpers)             net9.0-windows10.0.19041.0      → WindowsAppSDK
          └ MdcAi.ChatUI.LocalDal (plain lib)        net9.0
  └ MdcAi.OpenAiApi       (plain lib)                net9.0
```

**Configurations** (from the `.sln` + main `.csproj`):

- `Debug`, `Release` → packaged (MSIX) via `Packaged=True`
- `Debug-Unpackaged`, `Release-Unpackaged` → unpackaged (`Packaged` != true)

`Packaged` is computed in `MdcAi.csproj`:
```xml
<Packaged>False</Packaged>
<Packaged Condition="'$(Configuration)' == 'Debug' Or '$(Configuration)' == 'Release'">True</Packaged>
```

Platforms: `x86`, `x64`, `ARM64`.

## Key moves in `MdcAi.csproj` (the shell project)

- `OutputType=WinExe`, `UseWinUI=true`, `Nullable=disable`.
- RuntimeIdentifiers win-x86/x64/arm64; `<Platforms>x86;x64;ARM64</Platforms>`.
- **Packaged** block: `EnableMsixTooling=True`, `WindowsPackageType=MSIX`, `GenerateAppxPackageOnBuild=True`, `AppxBundle=Always`, uses a `PfxFile` + `PackageCertificatePassword` from the repo `build/*.pfx`, and selects the appx manifest by `Release` (`Package.appxmanifest` for release, `Package.Dev.appxmanifest` for non-release).
- **Unpackaged** block: `EnableMsixTooling=False`, `WindowsPackageType=None`, defines `UNPACKAGED` constant (this flips data-folder/WebView paths in code — see below), sets `WindowsAppSDKSelfContained=true`, `PublishReadyToRun` toggled.
- Package identity (`Package.appxmanifest`): `Name=58458BojanSala.MdcAI`, `Version=1.0.2.0` (note the manifest version lags the assembly `1.0.3.0` — bump both consistently when releasing). Capabilities: `rescap:runFullTrust`, `internetClient`.
- `PublishProfile=win-$(Platform).pubxml` per platform.

## `UNPACKAGED` compile symbol — what it changes in the app

Many code paths branch on `#if UNPACKAGED` / `&& (Packaged) != 'True'`. The important ones:

- **App data folder** — `AppServices.GetLocalDataFolder()`: unpackaged → `%LOCALAPPDATA%\MDCAI`; packaged → `ApplicationData.Current.LocalFolder.Path`.
- **Asset loading** — `AppServices.GetAppFile(path)`: unpackaged reads from `AppContext.BaseDirectory\Assets\...`; packaged reads via `ms-appx:///MdcAi.ChatUI/Assets/...`.
- **WebView2 user data folder** (`WEBVIEW2_USER_DATA_FOLDER`) is set in unpackaged builds.

## How to build

### From the CLI (unpackaged, quick dev build)
```
# restore + build the solution (packaged default too heavy; use unpackaged for dev)
msbuild Source/Desktop/MdcAi.sln /restore /p:Configuration=Debug-Unpackaged /p:Platform=x64
```
or just `dotnet build` on a single class periphery project may not run the WinUI XAML compile — **prefer `msbuild`** for the app projects.

### Packaged (Store-ready) build
```
msbuild Source/Desktop/MdcAi.sln /p:Configuration=Release /p:Platform=x64 /t:Publish /p:UapAppxPackageBuildMode=StoreUpload /p:AppxBundle=Always /p:Packaged=True /p:PublishReadyToRun=False
```
Output lands under `Source/Desktop/MdcAi/bin/<platform>/Release/<tfm>/win10-<platform>/AppPackages/…`.

### CI reference
`Source/.github/workflows/dotnet-desktop.yml` builds **`Release-Unpackaged x64`**:
```
msbuild $env:Solution_Name /t:Restore /p:Configuration=Release-Unpackaged
msbuild $env:Solution_Name /p:Configuration=Release-Unpackaged /p:Platform=x64 /t:Publish /p:PublishReadyToRun=False
```
and uploads `Source/Desktop/MdcAi/bin` as an artifact. It uses `setup-dotnet` 6.0.x (stale — the SDK is now 9 in `global.json`), `setup-msbuild`, checkout v3.

## Packaging scripts provided in-repo

- **`Source/Desktop/MdcAi/Pack.ps1`** — builds all three platforms (`x64`,`x86`,`ARM64`) in `Release` (packaged/MSIX Store-upload), renames `.msixsym`→`.appxsym`, strips old uploads, and produces an `.appxupload` per platform.
- **`Source/Desktop/MdcAi/BuildUnpacked.ps1`** — builds all platforms in `Release-Unpackaged` (unpackaged), then zips each `win10-<platform>` output into `MdcAi_<ver>_<platform>.zip`.
- Both call `dotnet restore` then `msbuild /t:Publish` (`Pack.ps1` uses `Release`, `BuildUnpacked.ps1` uses `Release-Unpackaged`).

> `Packaged` config names: from the `.sln` configs, packaged = `Debug`/`Release`, unpackaged = `Debug-Unpackaged`/`Release-Unpackaged`. CI (`dotnet-desktop.yml`) uses `Release-Unpackaged`.

## Signing / certificates

- Dev/Test signing uses `Source/build/WinUI-Gallery-Test.pfx` (password `DevCert123!`) referenced via `PackageCertificateKeyFile`/`PackageCertificatePassword` in the packaged block.
- `MdcAi/MdcAi_TemporaryKey.pfx` is a legacy temp key. The Store upload (`appxupload`) is signed per the Store/Partner Center flow.
- `MdcAi_DevCert.cer` is a dev cert artifact under `Source/build/`.
- The `AppPackages/` folders contain `Add-AppDevPackage.ps1` scripts / `.cer` for local installs.

## Prior `AppPackages` / `BundleArtifacts` (checked in, mostly stale)

- `Source/Desktop/MdcAi/AppPackages/{MdcAi_1.0.2.0_Debug_Test, MdcAi_1.0.2.0_Test}` — old installer deliverables and MSIX/MSIXBUNDLE files.
- `Source/Desktop/MdcAi/BundleArtifacts/` — store-upload staging (`x64.txt` marker).

These are build output committed to the repo historically; don't treat them as source of truth for the version (see the "bump both versions" note above).

## React renderer build (separate step)

The WebView renderer (`React Chat Renderer`) is built with `npm` and its output zipped into `ChatListUI.zip` as a content asset of `MdcAi.ChatUI`. See `Skills/WebViewRenderer` → "Build & packaging of the React app". Do **not** forget to re-zip after changing the renderer.

## Common gotchas

- **Don't bump the Store/assembly version piecemeal**: keep `Package.appxmanifest` `<Identity>Version>` and the `AssemblyVersion` in `*.csproj` (currently 1.0.3.0) in agreement.
- **Rebuild the embedded `Chats.db`** when you change EF schema — see `Skills/Db`.
- **Flags toggles in code by config**: `#if UNPACKAGED` changes storage & asset paths; if you add asset/data code be deliberate about which path you're in.
- The `.vs/` and numerous committed `AppPackages/` / `bin` artifacts in the tree are build output; avoid committing new ones unless it's a real new packaged deliverable.
- WinUI builds are heavy; prefer x64 for local iteration and only cross-compile the others for final packaging. Verify the created exe actually runs (unpackaged appeals to the WebView2 localhost:3431 serving path — see `Skills/WebViewRenderer`).

---

Read next: `Skills/Db` (migrations + embedded DB), `Skills/WebViewRenderer` (rebuild renderer before shipping).

