# valheimCreative

`valheimCreative` is a server-side Valheim mod for isolated creative build zones on dedicated servers.

The server owns session state, zone allocation, location spawning, teleporting, and targeted creative keys.

## Current behavior

- `!creative`: starts a creative session if the player is in bed, sends that player a targeted fake world-key list with creative build keys, then teleports them to their off-map build zone.
- `!return`: sends the player the normal world-key list, teleports them back to their saved entry position, and ends the session.
- Per-player zones: each owner gets a persistent creative zone allocation. Zone centers are spaced 192m apart by default.
- Invites: `!creative invite` shows the owner's invite code. `!creative join CODE` teleports another player to that owner's active zone.
- Death recovery: if a creative-session player dies, the server waits for vanilla respawn, reapplies creative keys, and teleports them back to the creative slot.
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
!creative biome
!creative biome Plains
```

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
`Plains`, `Mistlands`, `AshLands`, `DeepNorth`, and `Ocean`.

## Expand World files

Packaged config templates live in `package/BepInEx/config/expand_world/`.

- `expand_locations_valheim_creative.yaml`: disabled `StartTemple:valheim_creative` clone that removes the visible temple objects and levels/paints the terrain.
- `expand_prefabs_valheim_creative.yaml`: placeholder runtime rules file.
- `valheim_creative.cs`: placeholder Expand World Code file.
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
