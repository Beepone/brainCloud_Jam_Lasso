# Loot Lasso: UI templates (180 × 320)

All UI templates are in `docs/art/templates/ui/`. Same rules as the other templates: draw on layers above the guide, hide the guide before exporting, and keep the big `_preview` copies for looking at only.

## Pixel scale
- The screen is **180 × 320 art pixels**; the game renders at exactly that and scales up in whole steps (6× on a 1080 × 1920 phone). **Draw UI at 1×**, the same pixel size as everything else.
- Every position and size below is measured from the game as it is now, so art drawn to these sizes drops straight in.
- **Red hatched strips** (top 17 px, bottom 13 px): phone notch and home-bar zones. Nothing important goes there.

## Screen layouts
Rows are pixel rows from the top (row 0 = top edge); all elements are centred left-to-right unless noted.

**`UI_Screen_Title`**
| Element | Size | Centre row |
|---|---|---|
| Logo "LOOT LASSO" | about 140 × 32 | 51 |
| "Hi, <name>" text | text line | 86 |
| "change name" small button | 64 × 14 | 102 |
| PLAY / LEADERBOARD / HOW TO PLAY buttons | 108 × 24 each | 141 / 176 / 211 |
| Online status text | small text | 288 |

**`UI_Screen_NameEntry`**: heading (row 96), subtext (115), **text field 126 × 22** (141), OK button 108 × 24 (179).

**`UI_Screen_Leaderboard`**: heading (row 32), **10 rows of 150 × 14**, one every ~17.6 px starting at row 64 (rank · name · score; your own row is highlighted), BACK button (262). How to Play uses the same heading and BACK positions with lines of text in between.

**`UI_Screen_MatchAndResults`** (over the live game, so not dimmed)
| Element | Where |
|---|---|
| RIVAL score | left-aligned from column 6, row 22 (inside the rival hoard) |
| Timer "0:00" | right-aligned to column 174, row 22. Turns red in the last 10 s |
| YOU score | centred, row ~301 (the strip under your hoard) |
| "+5" popups, "STOLEN!" | small floating text that rises and fades |
| **Result panel** | **144 × 116** at columns 18–162, rows 102–218: result (WIN / LOSE / DRAW), reason, both scores, score-saved line, and two **60 × 23 buttons** (PLAY AGAIN, MENU) at columns 27–87 and 93–153 |

## Components (`UI_Components`)
Draw these as separate sprites. **9-slice**: the pink dotted lines mark the borders. Unity keeps the four corners exactly as drawn and stretches only the edges and middle, so one small button drawing works at any width. Keep corner details inside the border.

| Sprite | Size | 9-slice border | States |
|---|---|---|---|
| Button | 108 × 24 | 4 px | Normal, Hover, Pressed, Off (disabled), one row each |
| Small button | 64 × 14 | 3 px | same four |
| Panel (result panel, dialogs) | 96 × 64 (stretches to 144 × 116) | 6 px | one |
| Text field | 126 × 22 | 4 px | one |
| Leaderboard row | 150 × 14 | 3 px | normal + a highlighted "you" version |

Buttons need to be **at least ~20 px tall** to be easy to tap; the 24 px buttons already are.

## Fonts (`Font_Small_6x9`, `Font_Large_10x14`)
A pixel font as a grid, one character per cell: `A–Z 0–9 : ! + - . ? ' / ( )` and space. The faint letter in each cell is only a hint of which character goes there.
- **Red line** = baseline (letters sit on it); **blue dotted line** = cap height (top of capitals).
- **Small (6 × 9 cells, ~7 px caps):** body text, buttons, HUD scores, leaderboard rows.
- **Large (10 × 14 cells, ~11 px caps):** headings, the result title, maybe the big scores.
- Capitals only is fine (the game shows names in whatever case players type, so lowercase is a nice extra).
- Leave at least 1 px of empty space on the right of each glyph so letters don't touch.
- **Or use an existing pixel font**: fine for the game, but check its licence allows commercial use *and* redistribution if it's going in the asset pack, and list it in `docs/Credits.md`.

## Colours the code currently uses (change freely, just tell Joey)
- Your side: light blue · Rival: light red · Highlights / titles / "+5": gold · STOLEN!: pink · Timer warning: red.

## Delivery
Commit to `project_brainCloudJam/Assets/Art/UI/`: `Logo.png`, `Button.png` (4 states stacked), `ButtonSmall.png`, `Panel.png`, `TextField.png`, `LeaderboardRow.png`, `Font_Small.png`, `Font_Large.png`, plus source files. Joey wires them in (Unity's UI with 9-slicing, and the fonts as bitmap fonts).
