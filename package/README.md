# valheimCreative

Private Praetoris creative build-zone package.

Install `BepInEx/plugins/valheimCreative/valheimCreative.dll` on the dedicated server. Copy `BepInEx/config/expand_world` into the server config folder if the creative pad location should be managed by Expand World Data.

Spawn the packaged pad once:

```text
spawn_location VC_CreativePad pos=0,-12000,45
```

Players enter by lying in bed and using `!creative`. They exit with `!return`.

