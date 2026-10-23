# Loot Lasso: map template (180 × 320)

Use `templates/MapTemplate_180x320.png` as a **guide layer**: draw the map on layers above it, then hide or delete the guide before exporting. `templates/MapTemplate_180x320_preview_x4.png` is the same thing at 4× size, for looking at only.

## Canvas
- **180 × 320 pixels**, portrait. This is the whole screen.
- On a 1080 × 1920 phone, every art pixel becomes exactly **6 × 6 screen pixels**.
- **All other art uses the same pixel scale**: a sprite that is 23 px wide covers about 23 px of this map. In world units, **1 unit = 23 px** exactly (the Pixel Perfect Camera uses 23 pixels per unit). The player character is ~1 unit, so **~23 px** wide.
- Export as **PNG**, no smoothing or anti-aliasing when scaling.

## Zones (pixel rows counted from the top, row 0 = top edge)

| Rows | Zone | What happens there |
|---|---|---|
| 0–36 | **Rival hoard** (red) | The rival's treasure pile. Must mirror yours exactly |
| 36–60 | Rival spin room (grey) | The rival stands at row 36 and spins their lasso |
| 60–224 | **Field** (green), columns 8–172 | Treasure wanders here. Yellow dashed line (row 142) = the middle; the map's top half should mirror the bottom half in layout (decoration can differ) |
| 224–248 | Your spin room (grey) | You stand at row 248 and spin your lasso |
| 248–284 | **Your hoard** (blue) | Your treasure pile |
| 284–320 | Spare strip (dark) | Under your hoard. Free for decoration, or the score/UI later |

- The **dark strips left and right of the field** (columns 0–8 and 172–180) are the arena's edges: the lasso bounces off the screen's left and right sides, so walls, fences or rock edges fit there.
- **Circles:** solid = the character (~23 px); dotted = the biggest the lasso swings around them (radius ~23 px). Keep the spin rooms clear enough to read the lasso.
- The two **hoards must match** in size and shape (online, both players see the same world from opposite ends). Each player sees themselves at the bottom.

## Not in the map image
Separate sprites, same pixel scale: the players, treasure, the lasso rope and loop, UI (see `CharacterTemplates.md`). The map is just the ground and scenery underneath.
