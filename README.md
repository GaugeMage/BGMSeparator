# BGM Separator (FFXIV / Dalamud plugin)

Routes **only** Final Fantasy XIV's background music to a separate audio device, so a
streamer can keep the game's music in local recordings / VODs while excluding it from the
live stream (where it would be a copyright problem).

This is the "record-with-music, stream-without-music" workflow described in the reference
screenshots (the DankTankXIV clip use-case).

---

## Is this actually possible as *just* a plugin?

Short answer: **yes, the useful version is** — and this repo is a working scaffold of it.
The "perfect" version is not realistically a plugin. Here's the honest breakdown.

### What this plugin does (feasible, implemented here)
FFXIV does not expose a per-channel audio-device picker, and it mixes BGM + SFX + voice into
one output before it hits Windows. So instead of trying to split the game's live mix, the
plugin **reproduces the BGM itself**:

1. **Detects the current song.** It reads the game's BGM "scene" list every frame to know
   exactly which BGM id is playing (the same technique the
   [Orchestrion](https://github.com/perchbirdd/OrchestrionPlugin) plugin uses). Read-only —
   it never overwrites the game's music.
2. **Decodes the game's own music file.** Each BGM id maps to a `.scd` file in the game data
   (`music/...`). Those contain Ogg Vorbis, which Lumina (shipped with Dalamud) decodes,
   including the `LOOPSTART` / `LOOPEND` loop points.
3. **Plays it out on a device you choose** (e.g. a virtual audio cable) via WASAPI, looping
   the same way the game does, with a short crossfade on song changes.
4. **Mutes the game's own BGM** (`IsSndBgm`) while active, so the game's normal output — the
   one OBS captures as Desktop Audio — has no music in it.

Net result in OBS: your **Desktop Audio** source has game SFX/voice but no music; a
**separate source** (the virtual cable) carries only the music. Put that music source on the
**recording** track but not the **streaming** track, and you're done.

### What it does *not* do (and why)
- **It is not a tap of the game's real audio mix.** It's a parallel, sample-accurate copy
  of the same track. For 95% of BGM (zones, most fights) this is indistinguishable. Where it
  can drift from the game:
  - dynamically **layered** combat music (some fights fade extra stems in/out),
  - abrupt in-engine **transitions / stingers** (victory fanfares, cutscene hits),
  - situational one-shots.
  The plugin reacts to song-id changes, but it can't perfectly mirror the game's internal
  crossfade timing on those edge cases.
- **The in-game per-channel "Sound Device 1 / Sound Device 2" mock-up is out of scope.** That
  would require hooking and re-routing FFXIV's proprietary audio engine's BGM sub-mix to a
  second WASAPI endpoint. It's deep reverse-engineering, breaks on patches, and no public
  plugin does it. Not a realistic v1.

If you want zero virtual-cable setup and a literal engine-level split, that's a much larger
R&D project (plugin **and** likely a native audio shim), not this.

---

## Install (for users)

This plugin is not in the official Dalamud repo — it installs from this custom repo.

> **The GitHub repo must be public for this to work.** Dalamud fetches custom repos over
> anonymous HTTPS with no login, so a *private* repo's `repo.json` and release zip can't be
> reached. If you want to keep the source private, instead hand people the `latest.zip`
> (see "Manual install" below).

### Option A — Custom repo (recommended, auto-updates)
1. In-game: **Dalamud Settings** (`/xlsettings`) → **Experimental** tab.
2. Under **Custom Plugin Repositories**, paste:
   ```
   https://raw.githubusercontent.com/GaugeMage/BGMSeparator/main/repo.json
   ```
3. Click **+**, then **Save and Close**.
4. Open the **Plugin Installer** (`/xlplugins`), search **BGM Separator**, and install.

### Option B — Manual install (works even if the repo is private)
1. Download `latest.zip` from the [Releases page](https://github.com/GaugeMage/BGMSeparator/releases/latest).
2. Extract it into a new folder, e.g. `%AppData%\XIVLauncher\devPlugins\BgmSeparator`.
3. In **Dalamud Settings** → make sure dev plugins load that folder, then enable it in the
   Plugin Installer's **Dev Tools → Installed Dev Plugins**.

## Requirements
- [XIVLauncher / Dalamud](https://goatcorp.github.io/) (this is a Dalamud plugin).
- A virtual audio device is recommended so OBS can grab the music cleanly and you can still
  monitor it. Free option: [VB-CABLE](https://vb-audio.com/Cable/). (You mentioned you
  already have virtual audio devices — any WASAPI render endpoint works.)

## Build (for developers)
```
dotnet build BgmSeparator/BgmSeparator.csproj -c Release
```
Requires the .NET 10 SDK and the Dalamud dev libraries, which the `Dalamud.NET.Sdk` resolves
from your local Dalamud install (`%AppData%\XIVLauncher\addon\Hooks\dev`). The Release build
produces `BgmSeparator/bin/x64/Release/BgmSeparator/latest.zip` plus a manifest, and a loose
build you can point a Dalamud dev-plugin path at.

## Releasing (for the maintainer)
Releases are automated by [`.github/workflows/release.yml`](.github/workflows/release.yml).
To cut a release:
1. Bump `<Version>` in `BgmSeparator/BgmSeparator.csproj`.
2. Commit, then tag and push:
   ```
   git tag v0.1.0
   git push origin v0.1.0
   ```
3. The workflow downloads Dalamud, builds, attaches `latest.zip` to a GitHub Release, and
   regenerates/commits `repo.json` so its version and API level stay in sync.

Users on the custom repo then get the update automatically (the download links always point
at the *latest* release). You can also regenerate `repo.json` locally after a Release build
with `pwsh ./scripts/generate-repo.ps1`.

## In-game usage
1. `/bgmsep` to open settings.
2. Pick your **Output device** (your virtual cable).
3. Leave **"Mute the game's own BGM while active"** on.
4. Adjust volume / crossfade to taste.

### Commands
| Command | What it does |
| --- | --- |
| `/bgmsep` | Opens the settings window. |
| `/bgmsep toggle` | Flips the separated output on/off. |
| `/bgmsep on` / `/bgmsep off` | Forces it on or off. |
| `/bgmsep diag` | Starts/stops local BGM state recording. |

**Binding a key to the toggle:** FFXIV can't bind plugin commands directly, so make a macro
containing `/bgmsep toggle`, drag it onto a hotbar slot, and bind that slot. Handy for
swapping back to the game's own BGM mid-fight (e.g. a final phase) without opening the menu.

Turning it **off** stops the separate output *and* restores the game's BGM mute to whatever
you had before, so the music comes back through your normal desktop audio immediately.

## OBS routing
1. Add an **Audio Input Capture** (or Application Audio Capture) source for your virtual
   cable → call it `XIV BGM`.
2. Keep your normal **Desktop Audio** for game SFX/voice.
3. OBS → **Advanced Audio Properties**: for `XIV BGM`, enable it on the **recording** track
   and disable it on the **streaming** track (or vice-versa).
4. To hear the music yourself, set the `XIV BGM` source's **Audio Monitoring** to
   "Monitor and Output".

---

## Project layout
```
repo.json                          Dalamud custom-repo plugin master (points at latest release)
scripts/generate-repo.ps1          regenerates repo.json from the built manifest
.github/workflows/release.yml      tag -> build -> release + repo.json sync
BgmSeparator/
  Plugin.cs                        entry point, command + window wiring
  Services.cs                      Dalamud service injection
  Configuration.cs                 saved settings
  Bgm/
    BgmScene.cs                    game BGM-scene struct layout
    BgmAddressResolver.cs          sig-scan for the scene list
    BgmWatcher.cs                  per-frame current-song detection + event
  Audio/
    ScdBgmLoader.cs                BGM id -> .scd path -> raw bytes
    ScdOggExtractor.cs             SCD -> Ogg Vorbis (fixes the bundled-Lumina GetAudio bug)
    VorbisLoopSampleProvider.cs    Ogg decode + LOOPSTART/LOOPEND looping
    AudioEngine.cs                 WASAPI output + device list + mixer
    BgmPlaybackCoordinator.cs      song-change -> load -> crossfade; in-game mute
  Windows/
    ConfigWindow.cs                settings UI
```

## Credits
- BGM detection technique, scene-list signature, and `BgmScene` struct layout are based on
  [Orchestrion](https://github.com/perchbirdd/OrchestrionPlugin).
- SCD/Ogg struct layouts and the descramble table are from
  [Lumina](https://github.com/NotAdam/Lumina).

## License
MIT — see [LICENSE](LICENSE).

## Status / caveats
This is a first implementation scaffold. The BGM-detection signature, the `BgmScene` struct
offsets, and the Lumina `ScdFile` / `BGM` sheet APIs are all based on current public sources,
but **it needs to be built and tested against the live game and your Dalamud version** —
signatures and sheet columns occasionally shift between patches. NAudio's shared-mode WASAPI
output converts the internal 48 kHz stereo mix to whatever your chosen device expects.
