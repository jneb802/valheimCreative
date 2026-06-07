# valheimCreative

Private Praetoris creative build-zone package.

Install `BepInEx/plugins/valheimCreative/valheimCreative.dll` on the dedicated server.

The server allocates each owner a persistent creative zone and spawns the
creative StartTemple clone when the zone is first used.

Players enter by using `!creative`.
They exit by using `!return`.

Invite commands:

```text
!creative invite
!creative join CODE
!creative tools
!creative reset
!creative load blueprintName
!creative save blueprintName
!creative biome
!creative biome Plains
!creative size                 # admin only
!creative size 192             # admin only
!creative offset blueprintName # admin only
!creative offset blueprintName -1.25 # admin only
```

Blueprint loads use `blueprint-metadata.json` in the configured blueprint
directory. Use deployed filenames as keys with `loadYOffset` values in meters
and optional `biome` values. Negative `loadYOffset` values lower the loaded
build. Admins can read or change offsets in game with `!creative offset`.
Blueprint save includes invited players' pieces when they are built inside the
owner's creative zone.
