# Fortnite Loot Tracker

A Windows desktop app that counts chests searched or broken in saved Fortnite replays. It also tracks Sprite chests, ammo boxes and other containers, shows per-match history and recorded playtime, and includes a Pirate Token odds calculator.

## Run on Windows

Extract the Windows x64 release ZIP and open `Fortnite Loot Tracker.exe`. Enable replay recording in Fortnite and keep the tracker open while playing. It checks your Windows account's Fortnite replay folder and updates when a recording finishes. You can choose another replay folder in Settings.

**Teammate tracking is experimental.** In Playtime & Squad, save a teammate's replay name and double-click it to see shared matches. The app counts chest actions it can attribute to that teammate from your recording. Counts may be incomplete. “Not recorded” means no actions could be attributed from the available replay data.

## Build from source

Use the .NET 10 SDK on Windows:

```powershell
dotnet publish Source/FortniteLootTracker.csproj -c Release -r win-x64 --self-contained true -o App -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false
```

Your replay files, saved names, preferences and match history stay on your PC. The app reads replays from disk and stores its own history under `%LOCALAPPDATA%\FortniteLootTracker`. It does not upload them.

This is an unofficial fan-made app. Third-party notices are included in [PARSER-LICENSE.txt](PARSER-LICENSE.txt), [PHOSPHOR-LICENSE.txt](PHOSPHOR-LICENSE.txt), [FONT-LICENSE.txt](FONT-LICENSE.txt) and [DOTNET-NOTICES.txt](DOTNET-NOTICES.txt).
