# 0.2.34

- Added a packaged `TrollCave02:valheim_creative_siege_gateway` test location with a `portal_wood` gateway configured for the `troll_gate_test` siege.
- Extended siege portal entry RPCs to accept a relative entry position while preserving legacy client compatibility.

# 0.2.33

- Fixed BlackForest pine placement in creative zones by ignoring shallow ocean-depth rejection during copied terrain vegetation placement.
- Restores missing creative vegetation once per active zone after server restart or zone reload when terrain and vegetation are enabled.

# 0.2.32

- Changed creative vegetation placement to treat creative zones as biome interior: median vegetation is allowed without relying on outside-map biome-area checks, while edge-only vegetation is skipped.

# 0.2.31

- Made creative vegetation placement ignore vanilla edge/median biome-area classification so default vegetation spawns reliably in out-of-map creative zones.

# 0.2.30

- Fixed explicit creative vegetation presets spawning zero objects by allowing creative placement entries to pass Valheim's sector-corner biome precheck.

# 0.2.29

- Made `!creative poi` admin-only while POI terrain support is still experimental.

# 0.2.28

- Replaced runtime vegetation preset cloning with explicit YAML default presets generated from the Jotunn vegetation list.
- Added full default presets for Meadows, BlackForest, Swamp, Mountain, Plains, Mistlands, AshLands, DeepNorth, and Ocean.
- Changed custom preset inheritance to use named presets only, so duplicate prefab rows such as BlackForest `FirTree` entries remain configurable as separate vegetation rules.

# 0.2.27

- Added `!creative poi`, `!creative poi <name>`, and `!creative poi none`.
- POI terrain uses an existing generated vanilla or modded location instance as the terrain source and spawns the selected location at the creative zone center.
- Saved creative zone state now includes the selected `poiName`.

# 0.2.26

- Fixed `!creative biome none` so it disables the biome override and switches the zone back to flat terrain instead of selecting a random world terrain source.
- Prevented `!creative terrain world` while the zone biome is `None`.

# 0.2.25

- Added `!creative terrain`, `!creative terrain flat`, and `!creative terrain world`.
- Added `!creative biome none`.
- New creative zones now default to Meadows with world terrain through `DefaultCreativeTerrainMode`, while old saved zones without `terrainMode` load as flat and keep their saved radius.
- Updated `!creative tools` to spawn a blackmetal pickaxe and blackmetal axe.

# 0.2.8

- Added automatic safe object migration when `CreativeZoneSpacing` changes, with JSON backups before applying moved zone/session positions.
- Added `creative_zone_migrate_spacing <targetSpacing> [apply]` to preview or manually apply creative zone spacing migration.
- Increased the default creative zone spacing to 1920m.
- Updated creative zone size changes to also refresh the packaged terrain modifier radius.

# 0.2.7

- Restricted `!creative size` and `!creative offset` to server admins.

# 0.2.6

- Saved all player-built pieces in a shared creative zone, including invited players' pieces.
- Blocked natural and event creature spawn points inside allocated creative zones.
- Added `!creative size` and `!creative size <radius>` for in-game creative zone size changes.
- Added `!creative offset <blueprintName> [loadYOffset]` and `creative_blueprint_offset` for blueprint vertical offset management.
- Added the serving tray to `!creative tools`.

# 0.2.5

- Fixed slow creative chat commands executing once per chat recipient instead of once per sender.

# 0.2.4

- Added `!creative biome` and `!creative biome <biome>` for creative zone biome paint selection.
- Replaced `blueprint-load-offsets.json` with `blueprint-metadata.json`, storing `loadYOffset` and optional biome per blueprint.
- Set the default creative zone biome to Meadows when no biome is stored yet.

# 0.2.3

- Removed the bed requirement from creative entry. Players now only need an empty main inventory and empty Shudnal ExtraSlots inventory for creative entry, return, and join commands.

# 0.2.2

- Fixed creative entry from bed leaving the player counted as sleeping, which could skip night for the server.
- Fixed creative chat command handling so a command can only affect the sender's own character.

# 0.2.1

- Added `loadYOffset` support for per-blueprint vertical placement adjustment.
- Allowed `creative_load_player` to target either a Valheim player ID or the player's platform ID, so Discord-triggered loads can resolve linked Steam users.

# 0.2.0

- Added `!creative load <blueprintName>` and `!creative reset` support for creative-zone blueprint validation.
- Added `creative_load_player <playerId> <blueprintName>` for bot/RCON-driven blueprint loading.
- Anchored loaded blueprints by bottom-center bounds at the creative zone origin.
- Added routing protection for creative chat commands when Expand World Prefabs is installed.
- Kept creative entry/return inventory checks, including ExtraSlots inventory.
