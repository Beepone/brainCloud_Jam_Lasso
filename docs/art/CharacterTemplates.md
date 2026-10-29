# Loot Lasso: character, treasure and lasso templates

All templates are in `docs/art/templates/`. Each has a 1× file (draw on top of it) and a big `_preview` copy (for looking at only). Draw on layers above the template, then **hide the template layer before exporting**.

## Pixel scale (the same as the map)
- **1 world unit = 23 px** (the Pixel Perfect Camera uses 23 pixels per unit). The screen is 180 × 320 px.
- On a 1080 × 1920 phone every art pixel becomes 6 × 6 screen pixels, so keep it crisp: no anti-aliasing or smooth scaling.
- Export **PNG with transparency**.

## What the guide marks mean
- **Coloured cells** = one animation frame each. Each **row** is one animation, left to right (rows have different tints; frames alternate light/dark so you can see the edges).
- **Cyan circle** = the footprint: about how big the thing is in the game (what the lasso has to land on). Stay roughly inside it; a little overhang is fine.
- **Red dot** = the pivot, the sprite's centre point. The game positions sprites by this point.
- **Yellow mark** (player only) = forward. Draw the character **facing up** (toward the field). The rival uses the same art, turned around by the game.

## The sheets

| File | Cell | Rows (4 frames each) | Notes |
|---|---|---|---|
| `Player_24px_4x4` | 24 × 24 | 1 Idle · 2 Spin (lasso overhead) · 3 Throw · 4 Pull (hauling in) | Top-down treasure hunter, ~23 px across. The lasso loop and rope are separate sprites, so in Spin, draw the arm/hand, not the loop |
| `Coin_10px_4x4` | 10 × 10 | 1 Idle · 2 Walk · 3 Caught (struggling on the rope) · 4 In hoard | Lazy, waddles, naps |
| `Chest_16px_4x4` | 16 × 16 | same | Grumpy, stomps, heavy |
| `Idol_14px_4x4` | 14 × 14 | same | Vain and twitchy, sprints |
| ~~`LassoLoop_24px`~~ | — | — | **Replaced Oct 29** by `lasso_loop_guides.png` (35 × 35, tags `spin` / `fly` / `catch`); the catch circle grew since. See `lasso_spec.png` |
| ~~`RopeTile_8x2`~~ | — | — | **Replaced Oct 29** by `lasso_rope_guides.png` (8 × 3). See `lasso_spec.png` |

- **4 frames per row** is a starting point: fewer is fine (e.g. 2-frame idle), just keep frames in the same cell grid, left-aligned.
- **Size changes:** if you want a treasure bigger or smaller, tell Joey. The template sizes match the current game balance (the lasso's catch area is sized around them).

## Naming when committing (to `project_brainCloudJam/Assets/Art/`)
`Player.png`, `Coin.png`, `Chest.png`, `Idol.png`, `LassoLoop.png`, `RopeTile.png`, `Map.png`, plus your source files (`.aseprite`, `.psd`, ...) next to them. Keep the sources: they're also for the asset pack.
