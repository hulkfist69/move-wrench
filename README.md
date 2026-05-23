# Move Wrench

A Vintage Story wrench for nudging doors, trapdoors, and fence gates at sub-voxel precision. Press **F** to pick a step size (1, 2, 4, or 8 voxels per click). Non-destructive — hitbox, animation, and interaction all stay intact.

> Target game version: **1.20.x** · Side: Universal · Multiplayer-safe

---

## Features

- **Sub-voxel offset** — push doors as little as 1/16 of a block per click
- **Chisel-style step selector** — F opens a 4-tile picker for 1/2/4/8 voxel steps
- **Connected doors move together** — double-door pairs and two-block-tall doors stay aligned as a single group
- **Sneak-reset** — Sneak + right-click snaps the block back to its aligned position
- **Persistent + synced** — offsets save with the world and sync to all clients on join

## Controls

| Input | Action |
|---|---|
| Right-click block face | Shift block toward that face by the current step |
| Sneak + right-click | Reset block to its aligned position |
| `F` (while holding the wrench) | Open the step-size selector |

## Install

Drop the packaged zip into your Vintage Story mods folder:

- **Windows:** `%APPDATA%\VintagestoryData\Mods\`
- **macOS:** `~/Library/Application Support/VintagestoryData/Mods/`
- **Linux:** `~/.config/VintagestoryData/Mods/`

Launch the game, enable in the mod manager. The wrench appears in the creative inventory under **Tools** (no survival recipe yet).

## Build from source

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) and a local Vintage Story install.

```bash
# Point at your VS install (one-time)
export VINTAGE_STORY="/path/to/Vintagestory"                              # macOS/Linux
setx VINTAGE_STORY "C:\Users\<you>\AppData\Roaming\Vintagestory"          # Windows

# Build + package
./build.sh     # macOS/Linux
.\build.ps1    # Windows
```

The output lands in `dist/MoveWrench-<version>.zip` — drop that into the Mods folder above.

## How it works

Offsets are stored server-side as `(BlockPos → Vec3i in voxels)` and synced to clients full-state on join, then incrementally on each change. Harmony postfixes on `BlockDoor` / `BlockTrapDoor` / `BlockFenceGate` for `GetCollisionBoxes` and `GetSelectionBoxes` translate the cuboids by `offset / 16`. A postfix on `BlockEntityDoor.OnTesselation` translates the cached mesh, so door animations still pivot correctly around their (shifted) hinge.

## Known limits

- **Doors with BEs only get visual offset.** Trapdoors and fence gates in 1.20 have their collision/selection offset properly, but the rendered mesh may stay in the chunk-batched position. Functional, but visually clipped — a future version will hook the chunk tesselator.
- **No survival recipe yet.** Currently creative-only. A scrap-metal + stick recipe is a one-line JSON addition if you want it.
- **Generic (non-door) blocks are out of scope.** Doing this non-destructively conflicts with the "preserved exactly" guarantee — see commit history for the design rationale.

## Source layout

```
src/
  MoveDoorsModSystem.cs    ModSystem lifecycle, Harmony, F-key hotkey
  ItemDoorWrench.cs        The wrench item
  BlockOffsetManager.cs    Offset store, networking, persistence
  DoorGroup.cs             Paired/two-tall door detection
  HarmonyPatches.cs        Collision, selection, mesh translation
  Network/Packets.cs       ProtoBuf network packets
  Gui/GuiDialogMoveStep.cs Cairo-rendered step-size GUI
```

Modid is `movedoors` (legacy from earlier door-only versions) — kept for asset-path compatibility.

## License

MIT — see [LICENSE](LICENSE).
