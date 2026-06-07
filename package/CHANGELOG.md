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
