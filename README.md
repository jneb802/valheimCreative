# valheimCreative

`valheimCreative` is a server-side Valheim mod for isolated creative build zones.

## Current behavior

- `!creative`: starts a creative session if the player is in bed, sends that player a targeted fake world-key list with creative build keys, then teleports them to the configured off-map build zone.
- `!return`: sends the player the normal world-key list, teleports them back to their saved entry position, and ends the session.
- Death recovery: if a creative-session player dies, the server waits for vanilla respawn, reapplies creative keys, and teleports them back to the creative slot.
- Global key changes: when the real server global-key list changes, the mod resends creative keys only to active creative players.

Creative sessions send these targeted keys by default:

- `NoBuildCost`: lets known build pieces place without consuming build resources.
- `NoWorkbench`: bypasses build-station range checks for known station-gated pieces.
- `NoCraftCost`: supports build pieces whose vanilla free-build key is craft cost instead of build cost.

The mod does not call `ZoneSystem.SetGlobalKey` for these keys, so the real world keys are not changed for other players.

## Commands

```text
!creative
!return
!creative status
```

## Expand World files

Packaged config templates live in `package/BepInEx/config/expand_world/`.

- `expand_locations_valheim_creative.yaml`: disabled location definition for the creative pad.
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
