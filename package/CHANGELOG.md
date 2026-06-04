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
