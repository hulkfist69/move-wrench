# Move Wrench

A Vintage Story mod that lets you nudge doors, trapdoors, and fence gates at sub-voxel precision with a wrench.

- Right-click a face → block shifts toward that face by the active step
- Sneak + right-click → snap back to aligned
- `F` → open the step-size selector (1, 2, 4, or 8 voxels per click)
- Double doors and two-block-tall doors move as a single group
- Non-destructive: collision, animation, and interaction all preserved

Target game version: **1.20.x**

## Build

You need the .NET 7 SDK and a local Vintage Story install.

```bash
# Point at your VS install
export VINTAGE_STORY="/path/to/Vintagestory"           # macOS/Linux
setx VINTAGE_STORY "C:\Users\<you>\AppData\Roaming\Vintagestory"   # Windows (reopen terminal after)

# Build + package
./build.sh    # macOS/Linux
.\build.ps1   # Windows
```

Output lands in `dist/MoveWrench-<version>.zip`.

## Install

Drop the zip into your Vintage Story mods folder:

- Windows: `%APPDATA%\VintagestoryData\Mods\`
- macOS:   `~/Library/Application Support/VintagestoryData/Mods/`
- Linux:   `~/.config/VintagestoryData/Mods/`

Launch the game, enable in the mod manager. The wrench is in the creative inventory under tools (no survival recipe yet).

## Source layout

```
src/
  MoveDoorsModSystem.cs    ModSystem lifecycle, Harmony, F-key hotkey
  ItemDoorWrench.cs        The wrench item
  BlockOffsetManager.cs    Server-authoritative offset store + networking + persistence
  DoorGroup.cs             Paired/double door detection
  HarmonyPatches.cs        Collision, selection, and door mesh translation
  Network/Packets.cs       ProtoBuf network packets
  Gui/GuiDialogMoveStep.cs Cairo-rendered step-size GUI

assets/movedoors/
  itemtypes/doorwrench.json
  shapes/item/doorwrench.json
  lang/en.json
```

Modid is `movedoors` (legacy from earlier door-only versions) — kept for asset-path compatibility.
