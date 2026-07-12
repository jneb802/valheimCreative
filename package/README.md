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

New creative zones default to Meadows with world terrain. Existing saved zones
keep their saved terrain mode; old records without `terrainMode` load as flat.
Saved zone radii are preserved on load even when they are above the current
`MaxCreativeZoneRadius`; that max applies to future size changes.
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

Blueprint loads use `blueprint-metadata.json` in the configured blueprint
directory. Use deployed filenames as keys with `loadYOffset` values in meters
and optional `biome` values. Negative `loadYOffset` values lower the loaded
build. Admins can read or change offsets in game with `!creative offset`.
Blueprint save includes invited players' pieces when they are built inside the
owner's creative zone.

The package includes a disabled `TrollCave02:valheim_creative_siege_gateway`
test location and `troll_gate_test` siege definition. Spawn the location during
validation and use its `portal_wood` gateway to enter the configured siege.
