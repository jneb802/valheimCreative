# valheimCreative

Private Praetoris creative build-zone package.

Install `BepInEx/plugins/valheimCreative/valheimCreative.dll` on the dedicated server.

The server allocates each owner a persistent creative zone and spawns the
creative StartTemple clone when the zone is first used.

Players enter by lying in bed and using `!creative`.
They exit by using `!return`.

Invite commands:

```text
!creative invite
!creative join CODE
!creative biome
!creative biome Plains
```

Blueprint loads use `blueprint-metadata.json` in the configured blueprint
directory. Use deployed filenames as keys with `loadYOffset` values in meters
and optional `biome` values. Negative `loadYOffset` values lower the loaded
build.
