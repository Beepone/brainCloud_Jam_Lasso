"""Typesets the itch page's How-to-play cards (the writer's text, verbatim) on the game's Panel art
with the game's pixel fonts, in a grid (GRID_COLS across). Usage: python -P howto_cards.py <project Assets/Sprites dir> <panel.png> <out.png>"""
import sys
import cv2
import numpy as np

SPRITES, PANEL, OUT = sys.argv[1], sys.argv[2], sys.argv[3]
CHARS = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789!@#$.,?()':;+-"
COLS = 16

CARDS = [
    ("Wind up", "Use your finger to swing the lasso around your head. The faster you spin the farther the lasso will go!"),
    ("Pick your target", "Choose a piece of loot and aim. Remember that the lasso will bounce off walls, so make sure not to overextend your shot!"),
    ("Release", "Release when the lasso is pointed in the direction you want. Be careful, the lasso will follow its own trajectory, not your finger's."),
    ("Steal", "Low on points? Toss your lasso into your opponents loot pile, and you just might be able to even the score."),
]

def hexc(h):  # -> BGRA
    return (int(h[4:6], 16), int(h[2:4], 16), int(h[0:2], 16), 255)

GOLD, BONE, EDGE, SHADOW = hexc("d3bd46"), hexc("ecebe7"), hexc("a76343"), hexc("1a0a01")


class Font:
    def __init__(self, path, cw, ch, space):
        self.img = cv2.imread(path, cv2.IMREAD_UNCHANGED)
        self.cw, self.ch, self.space = cw, ch, space
        self.glyphs = {}
        a = self.img[:, :, 3] > 0
        for i, c in enumerate(CHARS):
            x0, y0 = i % COLS * cw, i // COLS * ch
            if y0 + ch > a.shape[0]:
                break
            cols = np.where(a[y0:y0 + ch, x0:x0 + cw].any(axis=0))[0]
            if len(cols):
                self.glyphs[c] = (x0 + cols[0], y0, cols[-1] - cols[0] + 1)

    def glyph(self, c):
        return self.glyphs.get(c.upper()) or self.glyphs["?"]

    def width(self, text):
        w = 0
        for i, c in enumerate(text):
            if i:
                w += 1
            w += self.space if c == " " else self.glyph(c)[2]
        return w

    def wrap(self, text, maxw):
        lines, cur = [], ""
        for word in text.split():
            trial = word if not cur else cur + " " + word
            if self.width(trial) <= maxw:
                cur = trial
            else:
                lines.append(cur)
                cur = word
        lines.append(cur)
        return lines

    def draw(self, canvas, text, x, y, color):
        pen = x
        for c in text:
            if c == " ":
                pen += self.space + 1
                continue
            gx, gy, gw = self.glyph(c)
            mask = self.img[gy:gy + self.ch, gx:gx + gw, 3] > 0
            region = canvas[y:y + self.ch, pen:pen + gw]
            region[mask] = color
            pen += gw + 1


large = Font(f"{SPRITES}/Font_Large_10x14.png", 10, 14, 4)
small = Font(f"{SPRITES}/Font_Small_6x9.png", 6, 9, 3)
panel = cv2.imread(PANEL, cv2.IMREAD_UNCHANGED)


def sliced(w, h, b=8):
    """9-slice the panel to w x h: corners as drawn, edges and middle stretched (nearest neighbour)."""
    ph, pw = panel.shape[:2]
    xs, ys = [0, b, pw - b, pw], [0, b, ph - b, ph]
    tx, ty = [0, b, w - b, w], [0, b, h - b, h]
    out = np.zeros((h, w, 4), np.uint8)
    for r in range(3):
        for c in range(3):
            src = panel[ys[r]:ys[r + 1], xs[c]:xs[c + 1]]
            dw, dh = tx[c + 1] - tx[c], ty[r + 1] - ty[r]
            out[ty[r]:ty[r + 1], tx[c]:tx[c + 1]] = cv2.resize(src, (dw, dh), interpolation=cv2.INTER_NEAREST)
    return out


GRID_COLS = 4                          # 4 = one row of four, 2 = 2x2
CARD_W, PAD, GAP, LINE = 112, 7, 6, 11  # art pixels (2x2 used 148, 9, 8)
SCALE = 2                              # screen pixels per art pixel (2x2 used 3)
inner = CARD_W - PAD * 2
laid = []
for title, body in CARDS:
    tl = large.wrap(title, inner)
    bl = small.wrap(body, inner)
    laid.append((tl, bl, PAD + len(tl) * 16 + 4 + len(bl) * LINE + PAD - 2))

# Each row is as tall as its tallest card.
nrows = (len(laid) + GRID_COLS - 1) // GRID_COLS
rows = [max(c[2] for c in laid[r * GRID_COLS:(r + 1) * GRID_COLS]) for r in range(nrows)]
M = 2  # room for the outline and shadow
W = M * 2 + CARD_W * GRID_COLS + GAP * (GRID_COLS - 1)
H = M * 2 + sum(rows) + GAP * (nrows - 1)
canvas = np.zeros((H, W, 4), np.uint8)

for i, (tl, bl, _) in enumerate(laid):
    col, row = i % GRID_COLS, i // GRID_COLS
    x = M + col * (CARD_W + GAP)
    y = M + sum(rows[:row]) + GAP * row
    h = rows[row]
    card = sliced(CARD_W, h)
    alpha = (card[:, :, 3] > 0).astype(np.uint8)
    # Outline one pixel around the card's rough silhouette, and a pixel of shadow below it.
    pad = np.pad(alpha, 1)
    ring = cv2.dilate(pad, np.ones((3, 3), np.uint8)) - pad
    sub = canvas[y - 1:y + h + 1, x - 1:x + CARD_W + 1]
    shadow = np.zeros_like(pad)
    shadow[1:, :] = pad[:-1, :]
    sub[(shadow > 0) & (pad == 0)] = SHADOW
    sub[ring > 0] = EDGE
    body = canvas[y:y + h, x:x + CARD_W]
    body[alpha > 0] = card[alpha > 0]
    ty = y + PAD
    for line in tl:
        large.draw(canvas, line, x + PAD, ty, GOLD)
        ty += 16
    ty += 4
    for line in bl:
        small.draw(canvas, line, x + PAD, ty, BONE)
        ty += LINE

big = cv2.resize(canvas, (W * SCALE, H * SCALE), interpolation=cv2.INTER_NEAREST)
cv2.imwrite(OUT, big)
print(OUT, big.shape[1], "x", big.shape[0])
