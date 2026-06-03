# 0.2.1

- Added `loadYOffset` support for per-blueprint vertical placement adjustment.
- Allowed `creative_load_player` to target either a Valheim player ID or the player's platform ID, so Discord-triggered loads can resolve linked Steam users.

# 0.2.0

- Added `!creative load <blueprintName>` and `!creative reset` support for creative-zone blueprint validation.
- Added `creative_load_player <playerId> <blueprintName>` for bot/RCON-driven blueprint loading.
- Anchored loaded blueprints by bottom-center bounds at the creative zone origin.
- Added routing protection for creative chat commands when Expand World Prefabs is installed.
- Kept creative entry/return inventory checks, including ExtraSlots inventory.
