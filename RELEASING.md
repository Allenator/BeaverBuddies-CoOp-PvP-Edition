# Releasing this fork

This fork's releases are versioned `<upstream's version>-allenator.N` (for example `1.1.0-1.10.0-allenator.8`) and go out by hand as GitHub releases, each a zip of BeaverBuddies and the Game Modes mod it was built and tested with. Upstream's `Tools/Package/release.sh` is for upstream's releases and refuses this fork's versions.

## Before

- **Tests:** `Tools/Tests/fast.sh`, then the full scenario suite, `Tools/Tests/run.sh` with no names (about two hours, with no game running and the Steam client logged on; see [`Tools/Tests/README.md`](Tools/Tests/README.md)).
- **No test code in the build:** the test harness is its own DLL, `BeaverBuddies.TestHarness.dll`, which only test runs install. A release build of BeaverBuddies never makes it.

## The version and the changelog

1. **The csproj:** set `<Version>` in `BeaverBuddies/BeaverBuddies.csproj`. The mod reports this version and matches players by it (with the build), and the build writes it to `version.txt` beside the manifest.
2. **The manifest:** `"Version"` in `BeaverBuddies/manifest.json` stays at `<game>-<upstream's major>.<minor>.0` (for example `1.1.0-1.10.0`) and changes only when a merge brings a new upstream minor version, because the game warns about every save whose mod version differs from the manifest's. The Game Modes entry in its `RequiredMods` names the oldest Game Modes the code works with.
3. **The changelog:** add an entry at the top of `BeaverBuddies/changelog.txt`, in the file's format (`v<version>`, then `<version>`, then `* ` lines). The game shows the top three entries. When the release is built on a new upstream version, its first line says so: "Built on 1.1.0-1.10.0, below. Install the Game Modes that comes with this build, not the Steam Workshop's."
4. **Commit** as `Release <version>`, with the changes as a list in the body.

## Build and zip

1. **Build into a scratch folder**, never the Mods folder you play from:
   - Game Modes at the version the zip will carry: check out [timberborn-game-modes](https://github.com/FezVrasta/timberborn-game-modes) at its tag `v<version>` and run `dotnet build -c Release -p:DocumentsPath=<scratch>/` in its `GameModes` folder.
   - BeaverBuddies: `dotnet build -c Release -p:DocumentsPath=<scratch>/` in `BeaverBuddies/`.
2. **Zip** from `<scratch>/Timberborn/Mods`: `zip -r -X BeaverBuddies-<version>-with-GameModes-<its version>.zip BeaverBuddies GameModes -x GameModes/workshop_data.json -x '*.DS_Store'`.
3. **Check the zip:**
   - compare `unzip -Z1` of it with the previous release's zip, and account for every file added or gone;
   - no `BeaverBuddies.TestHarness.dll`;
   - `BeaverBuddies/version-1.1/version.txt` says the version;
   - note its SHA-256 (`shasum -a 256`).

## Publish

1. Push `master`.
2. Tag the release commit with an annotated tag and push it: `git tag -a v<version> -m "BeaverBuddies Co-Op/PvP Edition <version>"`, then `git push origin v<version>`.
3. Create the release as a pre-release: `gh release create v<version> <zip> -R Allenator/BeaverBuddies-CoOp-PvP-Edition --verify-tag --prerelease --title <version> --notes-file <notes>`. Without `-R`, `gh` may pick upstream's repository. The notes follow the previous release's:
   - what's new;
   - installing: close the game; unzip into `Documents/Timberborn/Mods`, replacing `BeaverBuddies` and `GameModes`; keep Harmony and Mod Settings from the Steam Workshop; don't subscribe to Game Modes on the Workshop; everyone in a game needs the same build;
   - where the zip's Game Modes comes from: built unchanged from timberborn-game-modes at its tag (GPL-3.0);
   - the zip's SHA-256.
4. Once the players have played it, make it the latest release: `gh release edit v<version> -R Allenator/BeaverBuddies-CoOp-PvP-Edition --prerelease=false --latest`.

## Merging upstream

Merge upstream's release tag into `master`, keep its new changelog entries above this fork's (the next release's entry then goes on top, saying what it's built on), take its manifest version and Game Modes requirement, and run the full suite before the next release.
