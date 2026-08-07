# AlphaChannel Screen (emote + VFX)

> **Back burner** — not part of the active product path right now. Live video uses AlphaChannel’s
> ScreenPainter + relay. This Penumbra pack stays in the repo for a later Thunderdome-style
> Lightless brand panel; don’t block on finishing `/vfxedit` for day-to-day use.

Original pack using the **same technique as Thunderdome** (not their files):

1. Penumbra provides `vfx/alphachannel/screen.avfx` + `brand.atex`
2. An emote `.pap` (edited in VFXEditor) plays that VFX on your character
3. Lightless syncs the Penumbra files so pairs see the brand panel
4. **Live video** still needs AlphaChannel (ScreenPainter + relay)

## What’s in the box

| File | Purpose |
| --- | --- |
| `vfx/alphachannel/brand.atex` | Original AlphaChannel brand card (16:9) |
| `vfx/alphachannel/screen.avfx` | Starter AVFX shell (from VFXEditor empty template) — **finish the billboard once** |
| `files/placeholder.png` | Same art as PNG for previews / redirects |
| `group_001_vj screen.json` | Penumbra option: Off / Brand panel |

## One-time setup (VFXEditor) — when we pick this up again

You already have VFXEditor (MareSempiterne / `/vfxedit`).

### A. Finish the screen billboard

1. `/vfxedit` → open **AVFX**
2. Load file:  
   `…/FF14 Mods/AlphaChannelScreen/vfx/alphachannel/screen.avfx`
3. Add a **Particle → Quad** (billboard / screen-facing)
4. Scale it large (stage-sized; tune in-game)
5. **Textures**: add path `vfx/alphachannel/brand.atex`
6. Assign that texture to the particle color slot
7. **Update** / save over `screen.avfx`

### B. Bind it to an emote (so Lightless can see it)

1. In VFXEditor open **PAP**
2. Load vanilla:  
   `chara/human/c0101/animation/a0001/bt_common/emote/dance16_loop.pap`  
   (repeat for your race codes, or start with Midlander and copy)
3. Add a VFX binder / timeline entry pointing at  
   `vfx/alphachannel/screen.avfx`
4. Export/save the PAP into this mod folder, e.g.  
   `chara/human/c0101/animation/a0001/bt_common/emote/dance16_loop.pap`
5. Penumbra → Advanced Editing → Files → add those PAP redirections  
   (or put them under a new option group “Stage emote”)

### C. Enable

1. Penumbra → Rediscover mods → enable **AlphaChannel Screen**
2. Option **VJ Screen → Brand panel**
3. Do `/dance` (dance16 loop) — brand panel should appear for you and Lightless pairs
4. Host in AlphaChannel (public); AC users get **live** pixels via join

## Host tip

`/achannel stage` runs `/dance` for stage presence. The Penumbra VFX bind above is optional / later.

## Do not

- Copy Thunderdome `.pap` / `.avfx` / `.atex` into this mod
- Expect Lightless-only users to see YouTube/OBS — they only see this brand VFX panel
