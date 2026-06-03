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
```

Blueprint loads can be height-adjusted with `blueprint-load-offsets.json` in
the configured blueprint directory. Use deployed filenames as keys and
`loadYOffset` values in meters. Negative values lower the loaded build.
