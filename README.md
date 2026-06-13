# valheimCreative

`valheimCreative` is a server-side Valheim mod for isolated creative build zones on dedicated servers.

The server owns session state, zone allocation, location spawning, teleporting, and targeted creative keys.

## Current behavior

- `!creative`: starts a creative session, sends that player a targeted fake world-key list with creative build keys, then teleports them to their off-map build zone.
- `!return`: sends the player the normal world-key list, teleports them back to their saved entry position, and ends the session.
- Per-player zones: each owner gets a persistent creative zone allocation. Zone centers are spaced 1920m apart by default.
- Invites: `!creative invite` shows the owner's invite code. `!creative join CODE` teleports another player to that owner's active zone.
- Blueprint save includes all player-built pieces in the owner's creative radius, including pieces built by invited players.
- `!creative size <radius>` lets a server admin change the active creative radius in game.
- Death recovery: if a creative-session player dies, the server waits for vanilla respawn, reapplies creative keys, and teleports them back to the creative slot.
- Natural and event creature spawns are blocked inside allocated creative zones.
- Creative location spawn: the server spawns and registers the configured creative location the first time a player enters creative mode.
- Global key changes: when the real server global-key list changes, the mod resends creative keys only to active creative players.

Creative sessions send these targeted keys by default:

- `NoBuildCost`: lets known build pieces place without consuming build resources.
- `NoWorkbench`: bypasses build-station range checks for known station-gated pieces.
- `NoCraftCost`: supports build pieces whose vanilla free-build key is craft cost instead of build cost.

The mod does not call `ZoneSystem.SetGlobalKey` for these keys, so the real world keys are not changed for other players.

## Install

Install `valheimCreative.dll` on the dedicated server.

## Commands

```text
!creative
!return
!creative status
!creative invite
!creative join CODE
!creative tools
!creative reset
!creative load blueprintName
!creative save blueprintName
!creative biome
!creative biome Plains
!creative biome none
!creative terrain
!creative terrain flat
!creative terrain world
!creative poi                  # admin only
!creative poi StartTemple      # admin only
!creative poi none             # admin only
!creative size                 # admin only
!creative size 192             # admin only
!creative offset blueprintName # admin only
!creative offset blueprintName -1.25 # admin only
```

New creative zones default to Meadows with world terrain. Saved zones keep their
stored `terrainMode`; old production zone records that do not have `terrainMode`
load as flat terrain. Saved zone radii are preserved on load even when they are
above the current `MaxCreativeZoneRadius`; that max applies to future size
changes.
`!creative biome none` disables the biome override and switches the zone to flat
terrain because world terrain needs a real source biome.
`!creative poi <name>` finds an existing generated vanilla or modded location
with that name, uses its world terrain as the creative terrain source, and
spawns the same location at the creative zone center. Use `!creative poi none`
to clear the selected POI.

`valheimCreative.environment.yaml` ships explicit default vegetation presets
for each supported biome. Custom presets should inherit from names like
`defaultMeadows`, `defaultBlackForest`, or `defaultMountain`, then override
specific entry names.

Server console migration command:

```text
creative_zone_migrate_spacing 1920       # dry run
creative_zone_migrate_spacing 1920 apply # backs up JSON state, moves zone objects, saves new spacing
```

Changing `CreativeZoneSpacing` automatically migrates existing saved zones after
the server object system is ready. Existing saved zones keep their stored
positions during early startup, then the migration backs up JSON state, moves
zone objects, updates saved zone/session positions, and saves the new spacing.
Use the dry-run command before changing the config if you want to preview the
movement.

## Blueprint Metadata

Loaded blueprints are anchored by their bottom-center bounds. To adjust one
blueprint after that anchor is calculated, or to set the biome applied when it
loads, create `blueprint-metadata.json` in the configured blueprint directory:

```json
{
  "blueprints": {
    "the_midnight_tavern.blueprint": {
      "loadYOffset": -1.0,
      "biome": "Plains"
    }
  }
}
```

Negative `loadYOffset` values lower the loaded build. Positive values raise it.
Supported biome values include `Meadows`, `BlackForest`, `Swamp`, `Mountain`,
`Plains`, `Mistlands`, `AshLands`, `DeepNorth`, `Ocean`, and `None`.

The same offset value can be read or changed by admins in game with
`!creative offset <blueprintName> [loadYOffset]`, or from the server console
with `creative_blueprint_offset <blueprintName> [loadYOffset]`.

## Expand World files

Packaged config templates live in `package/BepInEx/config/expand_world/`.

- `expand_locations_valheim_creative.yaml`: disabled `StartTemple:valheim_creative` clone that removes the visible temple objects and levels/paints the terrain.
- `expand_prefabs_valheim_creative.yaml`: placeholder runtime rules file.
- `valheim_creative.cs`: Expand World Code helpers used by the creative prefab data.
- `valheim_creative_setup.txt`: command notes for spawning/registering the pad.

## Build

```bash
dotnet build
```

The DLL is written to `bin/Debug/valheimCreative.dll`.

After building, copy the DLL into the local package:

```bash
cp bin/Debug/valheimCreative.dll package/BepInEx/plugins/valheimCreative/
```
