# Loot Lasso: design doc and jam plan

**Working title:** Loot Lasso
**Jam:** [brainCloud Jam](https://itch.io/jam/braincloud-jam). Theme: **Treasure**
**Dates:** Fri Oct 16, 9:00 AM PT → **Fri Oct 30, 9:00 AM PT** (submission deadline)
**Team:** Joey (code, audio, design) · two artists (split decided at kickoff)
**Engine:** Unity 6.6 (6000.6.6f1), 2D URP, Web (WebGL) build on itch.io
**Screen:** **portrait** (9:16). The jam build is web; an **iOS** version is the post-jam goal (see section 15)
**Repo:** https://github.com/Beepone/brainCloud_Jam_Lasso

---

## 1. The pitch

Two treasure hunters, one field full of living treasure. Spin your lasso, flick it out, and drag loot back to your hoard before your rival does. Or reach over and rope theirs.

A short, real-time 1v1 browser game about **timing and greed**. Fast treasure is worth more but hard to hit, heavy treasure ties you up while you haul it in, and anything in your hoard can be stolen.

### Design pillars
1. **The throw feels physical.** You really spin the mouse and really flick it. Skill comes from wind-up, timing and aim, not from menus.
2. **Treasure has personality.** Every piece of loot is a little character that runs, pauses and panics.
3. **Every catch is a decision.** Grab the easy coin, risk the fast idol, overload your rope on a pile, or steal from your rival.
4. **Playable by any voter at any time.** No opponent online? You play a bot. A match never needs more than a browser and a click.
5. **Built for a phone.** Portrait screen, finger-friendly controls and UI, so the same game becomes the iOS version after the jam.

---

## 2. Core loop

```
Spin up the lasso → flick to throw → loop lands → catch treasure inside it
      ↑                                                   ↓
      └── treasure lands in your hoard ←── pull it back (heavy hauls are slow)
```

A match ends when:
- a player owns **all** the treasure (only checked once every scheduled treasure has arrived, see section 4b), or
- the timer runs out → the player with the most treasure value wins.

Match length is **2 minutes**, exactly the length of the gameplay track (112 BPM), so match time equals song time and the match events in section 4b land on the music.

---

## 3. The lasso (controls)

*Redesigned Oct 20, simplified Oct 22 after playtests.* One motion, like a real lasso or a hammer throw: **spin it up, then let go when it's swinging toward your target.** Only two things to control:

| You control | By | It sets |
|---|---|---|
| **Direction** | When you let go | The lasso flies off along the loop's path at that moment |
| **Distance** | How fast you spin | Slow spins throw short; only a near-top-speed spin reaches far |

| Step | What the player does | What the game does |
|---|---|---|
| **Wind up** | Hold the button and circle the pointer around your character | The loop sits right under your pointer (0.6–1.0 units out) and has **momentum**: it trails your hand a little and only speeds up gradually, so getting it fast takes a few turns. |
| **Overspin** | Whip the pointer faster than the loop can follow | The rope's pull is capped (top speed ≈ 3.3 circles/s). Get your hand more than half a turn ahead and it **backlashes**: the loop is pulled the other way and slows down. You have to ease off and find the rhythm. |
| **Release** | Let go when the loop is swinging toward your target | The lasso flies out. Too slow a spin (under half a circle per second) just drops it. **No aim line:** reading the swing is the skill. |
| **Flight** | — | It **bounces off the left and right screen edges** (up to 3 bounces; the distance includes them) and stops if it reaches the top or bottom edge. The rope lies along its path. |
| **Catch** | — | Everything free inside the landed loop (radius 0.75) is caught. |
| **Pull back** | — | The rope **tightens** into a straight line (0.15–0.6 s), then it's pulled in. An **empty loop** comes back slower the farther out it is (10 units/s near, 2 at full distance). **With treasure on it** the pull is at most **3 units/s** (Max Haul Speed) and slower the heavier the catch (10% per unit of weight). The catch comes in **like a train** (Oct 29): pieces in a line nearest-first, 1 unit apart (Train Gap), the rope tied from piece to piece, and each piece is banked only when it reaches you. The rope only snaps on a steal (section 5). |

**Numbers (Inspector fields on `LassoSpin`, Scripts/Game):**
- **Distance:** 1.5 to 12 units, squared curve: about 2 units at 1 circle/s, 5 at 2, 12 at 3. **Reaching the rival's hoard is a skill shot**, and slow to recover from.
- **Weight → haul time:** Coin = 1, Idol = 6, Chest = 10. A chest comes back at half the haul speed, so grabbing one is a commitment: a full-distance haul takes several seconds while your rival keeps throwing.
- **Mouse and touch:** input goes through the Input System's `Pointer`.

**Tried and dropped** (all in git history): flick to throw, wind-up then swipe, a timing ring with a PERFECT bonus, rope tangling (replaced by backlash), loop size from circle width with rope strength and snapping, and the aim line / landing ring. All cut to keep the controls to one idea a new player gets in seconds.

**Still to test:** on a phone, your thumb covers the character while circling it.

---

## 4. Treasure

Treasure is **anthropomorphised**: little characters that wander the field. Each type trades value against speed and weight.

*Values tuned Oct 28; they're set on the Arena in the scene.*

| Type | Value | Weight | Speed (units/s) | At the start | Personality ideas |
|---|---|---|---|---|---|
| **Coin** | 2 | 1 | 2.0 | 10 | Lazy, waddles, naps |
| **Chest** | 5 | 10 | 1.0 | 4 | Grumpy, stomps, heavy |
| **Idol** | 8 | 6 | 3.5 | none (2 arrive mid-match) | Vain and twitchy, sprints away |

- **Catching:** everything inside the loop when it lands is caught. Catch radius is 0.75 units.
- **Weight:** heavier treasure is hauled back more slowly. A big haul ties you up while your rival keeps throwing.
- **Wandering** is calculated from the match seed and clock, so both players see identical movement without any network traffic.
- ✅ **Spooked:** free treasure within 1.8 units of a loop that lands without catching it sprints 2.6 units away from the landing point. Online, the host sends the spooks with its catch decision, so both screens agree.
- **After night falls** (section 4b), all treasure wanders 25% faster.

**Art in (Oct 28–29):**
- **Coin:** flies on little wings. Tags `idlefloat`, `flyleftface` / `flyrightface`, `struggleleftface` / `strugglerightface`.
- **Chest:** a spider-legged chest that **walks in 8 directions** (`treasurechestenemysprite.aseprite`). `TreasureArt` picks the walk from its direction on screen (`walkforward` = down the screen, `backchestwalk` = up, plus left, right and the four diagonals), plays `Idle` when still or in a hoard, and keeps walking when dragged on a rope.
- **Idol:** a **mockup** (`idol_sprite_mockup.aseprite`) with one untagged animation that loops all the time. The final art can be saved over the same file.
- **Shadows** are set per treasure kind on the Arena (Shadow Drop, Shadow Width): flyers have a gap under them (coin 7 px, idol 4 px and wider), and the chest's shadow sits right under its feet.

**Should have:** treasure celebrates or sulks in a hoard.
**Could have:** a **Mimic** chest that bites your rope: whatever's on it drops back into the field.

---

## 4b. Match events

*Added Oct 28 so matches don't go stale.* Every match follows the same script, timed to the gameplay track (match time = song time):

| Time | Event |
|---|---|
| 0:00 | Noon. 10 coins and 4 chests in the field |
| 0:34.29 | **Stampede:** 3 coins and 1 chest run in from the field's side edges |
| 0:51.43 | **Golden hour** (beat 96): the light peaks gold, the **first idol** appears in the centre (blinking and uncatchable for a second), plus a stampede |
| 1:08.57 | Stampede |
| 1:25.71 | **Nightfall**, at the song's switch-up (about 34 s left: the 30-second warning). Moonlit blue light, a NIGHTFALL! popup and shake, the **second idol** plus a stampede, and treasure is 25% faster from here on |
| last 16 beats | Shake on every kick, every half beat for the last 4, and the timer counts 0:08 to 0:01 on the beat |

- The light runs from noon to midnight over the match (`DayNight`), reading its golden hour and nightfall times from the Arena so light and gameplay agree.
- Everything runs off the shared match clock and seed, so online players see the same events. ✅ **Tested between two devices on Oct 28** (editor host vs. iPhone on itch, build 0.18): a full match with every event, and the final scores matched on both screens.
- Settings: Arena > "Match events" (times, stampede size, night speed, spook radius and distance).

---

## 5. Hoards and stealing

- Each player has a **hoard zone** behind them. Delivered treasure sits there and counts toward their score.
- **Score = the value of what's in your hoard right now** (not a running total), so being stolen from lowers it. Shown as YOU (under your hoard) and RIVAL (in their hoard), with a +value popup as each piece lands.
- Delivered treasure fills **evenly spaced spots** in the hoard (1.3 units apart), so steal throws line up on both screens.
- *Reworked Oct 28.* A lasso landing on the **rival's hoard** snatches treasure out of it: a **steal** (STOLEN! popup, screen shake, the robbed hoard flashes). You can never catch from your own hoard.
- **Steals don't score.** When the pull brings stolen treasure across the centre line, the rope **snaps** (KNOCKED LOOSE!) and the treasure hops loose there, back into play for either player. Anything caught in the field on the same throw keeps coming.
- Snatched treasure **still counts for its owner until it's dragged out of their hoard**, so a steal only costs you once it actually leaves.
- **Look:** each hoard has a light tint and a dashed 1-pixel line along its top and bottom in its hunter's colour (Oct 29; the old, stronger tint washed out the ground art).
- ✅ **Guard window:** delivered or knocked-loose treasure blinks and can't be caught for 1 second.
- The rival's hoard is at the far end, so a steal needs a near-top-speed spin: a real risk while your own hoard is open.

### Arena layout (world is symmetric)
The world mirrors top-to-bottom around the middle of the field, so both players have identical hoards (online, steals land the same on both screens). Each player sees themselves at the bottom.

| Zone | Map pixel rows (180×320) | World y |
|---|---|---|
| Rival hoard | 0–36 | 5.391 to 6.957 |
| Rival spin room (rival stands at its top edge) | 36–60 | 4.348 to 5.391 |
| Field (columns 8–172) | 60–224 | −2.783 to 4.348 |
| Your spin room | 224–248 | −3.826 to −2.783 |
| Your hoard (you stand at its top edge) | 248–284 | −5.391 to −3.826 |
| Spare strip (score display) | 284–320 | −6.957 to −5.391 |

**Could have:** hoard defenses as power-ups.

---

## 6. Power-ups (stretch goal)

Power-ups wander the field like treasure and are caught the same way. Only attempted if milestones 1–5 are done before the feature freeze (**Oct 27**).

| Power-up | Effect |
|---|---|
| Quick Haul | Your next pull-back ignores weight |
| Big Loop | Next throw's catch radius ×2 |
| Quick Draw | The loop speeds up twice as fast for 10 s |
| Shake-down | Scatters a few pieces of treasure out of the rival's hoard into the field |
| Padlock | Your hoard can't be stolen from for 8 s |

---

## 7. Multiplayer

### What we learned prototyping (commit `5160fe8`)
- **brainCloud Relay doesn't work on itch.io:** it only offers plain `ws://` on a bare IP address, and browsers block that on https pages.
- **Lobby signals over RTT do work** from the browser, with a round trip of about **200 ms**. That's fine for a game that only sends occasional throws.
- **Shared seed plus a synced clock** keeps treasure identical on both screens. The guest corrects its clock by half the measured round trip.
- **brainCloud's WebGL WebSocket file needed a 6-line patch for Unity 6.** It's documented in the file, and the SDK now lives in `Packages/`.

### How the real game works
- **Matchmaking:** login → RTT → find or create a `LassoLobby` lobby. The **lobby owner is the host**.
- **What gets sent:** throws only (direction, distance, match time). Treasure movement is never sent.
- **The host decides contested results:** when a lasso lands, the host decides what was caught and sends `caught(treasure ids, thrower)`. The guest shows its own throws immediately and corrects if the host disagrees. This fixes simultaneous grabs.
- **Messages:** `hello`, `start(seed)`, `throw`, `caught`, `matchEnd(scores)`, plus `ping` for latency.
- **No opponent?** After about **15 s** of waiting, the match starts against the **bot**. `LassoBot` drives the same `LassoSpin` as a player with a fake hand (circles, spins up to the speed for the distance, releases at the tangent angle with some aim error), so it follows exactly the same rules. Since Oct 28 it's slower and sloppier, and aims where moving treasure *was*, so it can miss. Its difficulty (think time, aim and distance error, steal preference) is tunable in the Inspector.
- **Disconnects:** if the opponent leaves mid-match, the match ends and **the player who stays wins** (forfeit), whatever the score.
- **Sides:** the host plays the bottom hoard, the guest the top, with its camera turned 180° so everyone sees themselves at the bottom. Hoards are coloured from each player's own view (yours blue, theirs red). The code is `Scripts/Game/NetMatch.cs`.

### Dealing with network delay (Oct 28)
A phone's messages arrive about 0.3–0.6 s late, longer than most throws fly. The first two-device tests showed lassos jumping to their target, throws that visibly landed on treasure but missed, and frozen treasure. Fixes:
- **The other player's throw flies the whole way** from their hand, sped up so it lands when it did on their screen (but never faster than 0.12 s). No more jumping.
- **The host judges a throw where the treasure was when it landed**, not where it is when the message arrives. Every treasure remembers its last 1.5 s of positions.
- **The guest's rope stays taut** until the host says what was caught (at most 1.5 s more). If the answer comes after the rope is already back, the catch goes straight into the hoard instead of being left frozen in the field.
- **The match music follows the match clock**, so it stays on the beat even when the clock is corrected.
- Tested on Oct 28 (0.18): far more of the phone's throws landed, and both screens ended on the same score.

---

## 8. brainCloud features (judging requirement: Authentication + 1 more)

| Feature | Used for | Status |
|---|---|---|
| **Authentication** | Anonymous login plus a display name | ✅ working |
| **RTT + Lobbies** | Matchmaking and in-match messages (lobby signals) | ✅ prototyped |
| **Leaderboards** | **Total treasure ever** (cumulative leaderboard `TotalTreasure`; every match's final score is added), top 10 on the RANKINGS screen | ✅ working |
| Player statistics | Lifetime catches / steals / perfect throws on the results screen | Could have |

---

## 9. Screens

1. **Title:** logo, Play, Rankings (the leaderboard), How to Play, Settings (volume).
2. **Name entry:** on first launch only, saved to brainCloud.
3. **Matchmaking:** "Looking for a rival..." with a countdown → bot match if none found. Once someone joins it says "Rival found! / Getting ready..." (the bot countdown is hidden).
4. **Match HUD:** both scores, timer, your name and the rival's name.
5. **Results:** win/lose, score breakdown (caught, stolen), Play Again / Title.
6. **Leaderboard:** top 10 plus your rank.
7. **How to Play:** a short animated or illustrated card. The throw needs explaining.

---

## 10. Art list

**Style:** **pixel art**. The palette is still to be chosen by the artists.
**Screen:** **portrait 9:16**, top-down, **180 × 320 art pixels**. Rival's hoard at the top, yours at the bottom, field in the middle.
**Pixel scale:** **23 pixels per world unit**. The camera is a **Pixel Perfect Camera** (reference 180 × 320, 23 PPU, Upscale Render Texture), so the whole game renders at 180 × 320 and is scaled up in whole steps: on a 1080 × 1920 phone each art pixel is 6 × 6 screen pixels. Sprites import with Pixels Per Unit 23, Filter Mode Point, Compression None.
**Templates:** `docs/art/MapTemplate.md` and `docs/art/CharacterTemplates.md`, with guide PNGs in `docs/art/templates/`.
**itch.io embed:** **540 × 960** (3×) with a fullscreen button. Whole-number multiples of 180 × 320 keep the pixels even.
**UI:** every button at least **~20 art px tall** (finger-sized), and nothing important in the top ~17 or bottom ~13 art px, where phone notches and home bars sit.
**Delivery:** PNG (or `.aseprite` / `.psd` sources), committed under `project_brainCloudJam/Assets/Art/<Category>/`. Name files like `Treasure_Coin_Run.png`. Big files go through Git LFS automatically.
**Sprites made in code** (placeholders, the rope, shadows, the ground) use the lit sprite material `Resources/SpriteLit.mat` on purpose: web builds otherwise give them an unlit one, and they stayed bright at night.
**Renaming art:** rename files inside Unity's Project window, or move the `.meta` file with them. Renaming outside Unity gives the file a new ID and the scene loses it.
**Pixel fonts as TTF:** `docs/art/fonts/` has both fonts as installable `.ttf` files (Loot Lasso Large and Small), for the itch page art. Use multiples of 14 px (Large) and 9 px (Small), with anti-aliasing off.
**Pack-ready:** the art is also meant for a sellable asset pack (section 16). Keep layered source files, stick to one palette and pixels-per-unit, and only use your own work, or note anything else in `docs/Credits.md`.

Who takes what is decided at the kickoff meeting. The two groups below are one natural split.

### Characters and treasure
| Asset | Frames / notes | Priority |
|---|---|---|
| Player character (top-down cowpoke) | Idle, Spin (lasso overhead), Throw, Pull. Two color variants (you vs. rival) | Must |
| Lasso | ✅ Loops in (Oct 29): `lasso_small` (11 px, while spinning) and `lasso_big` (35 px, thrown, exactly the catch circle), set on the Arena. Single frames; the art turns with the spin. The rope ends at the loop's edge, and once something's caught the loop hides and the rope runs piece to piece. **Rope piece still to come** (template: `docs/art/templates/lasso_*.png`, 8×3, repeats along the rope); the rope is a code-drawn line until then | Must |
| Coin | ✅ in | Must |
| Chest | ✅ in (8-direction walk) | Must |
| Idol | Mockup in (one loop); final art to come | Must |
| Treasure reactions | Scared/flee, celebrate | Should |
| Mimic chest | Idle disguised, reveal/bite | Could |
| Power-up items | One icon/sprite each | Could |

### World, UI and the itch.io page
| Asset | Notes | Priority |
|---|---|---|
| Arena ground | ✅ in (`background_1.png`): one 180×320 screen, a dirt arena framed by grass and rocks. Set as Ground Art on the Arena; empty the slot to get the code-drawn sand back | Must |
| Two hoard zones | Placeholder in code: light tint plus dashed edges in the hunter's colour | Must |
| UI kit | Buttons, panels, score display, timer, font choice | Must |
| Title logo | "Loot Lasso" | Must |
| VFX | Catch puff, steal sparkle, deliver/coin burst, wall-bounce spark | Should |
| Decorations | Cacti, rocks, props around the arena edge | Should |
| itch.io page | Cover image **630×500** (draw 315×250, export 2×), banner **960×300** (draw 320×100, export 3×), 3–5 screenshots from the final build, 1 GIF. Page text by the team's writer: a layout mockup with a brief for each section (no wording) is at https://claude.ai/artifact/VWayWsYAYeL71e7oSxwivi | Must (Oct 29) |

---

## 11. Audio (Joey)

All audio is also meant for a sound and music pack (section 16): export seamless loops as WAV (44.1 kHz, 16-bit) plus OGG, keep the DAW projects and stems, and name files like `LootLasso_LassoThrow_01.wav`.

| Sound | Status |
|---|---|
| Lasso twirl (loop, pitch follows spin) | ✅ done |
| Lasso throw (fades on landing) | ✅ done |
| Lasso grab | ✅ done |
| Wall bounce, steal (yours and the rival's), deliver (pitch varies, lower for heavy treasure) | ✅ done |
| UI click and hover, win/lose stingers | ✅ done |
| Timer warning | ✅ covered by nightfall at the song's switch-up and the beat-synced final countdown |
| Music: menu (intro into a loop, crossfades with the match) and match (2:00, follows the match clock) | ✅ done. Doubles as the first tracks of the asset pack; stems allow adaptive layers (Could have) |
| Treasure voices (coin squeak, chest grumble, idol gasp) | To do |

Source projects are in `SFX_Project/` (not committed yet, see section 14).

---

## 12. Timeline

*Re-planned Oct 20.* Work and other commitments made the first days short, and available hours vary a lot, so the plan is **milestones in priority order** instead of a fixed day-by-day schedule. Work down the list; wherever we are at the deadline, it's the most complete game possible. Claude writes most of the code (fully commented), and Joey designs, plays, tunes and reviews it.

**Done so far:**
- *Oct 16–20:* project setup, brainCloud login and leaderboards, prototypes of the throw, moving treasure and browser multiplayer (all proven on itch.io), design doc, the finished throw (milestone 1).
- *Oct 22:* throw simplified; symmetric arena; wandering treasure; catching and hauling; hoards, score display and stealing; pixel art templates for the artists; Pixel Perfect Camera at 23 PPU. Then the match timer, winner panel and Play Again, and the **bot** (LassoBot: the same lasso driven by a fake hand). **Milestone 2 done, a day early.**
- *Oct 23:* **milestone 5 done** (done before 3 and 4): title, first-launch name entry, How to Play, and a leaderboard of total treasure ever (brainCloud cumulative leaderboard `TotalTreasure`).
- *Oct 24:* **milestone 4 done, two days early:** online 1v1 over lobby signals. PLAY ONLINE falls back to the bot after 15 s, and PRACTICE plays the bot directly. It was tested editor vs itch build: synced treasure, scores and timer, the match end, and forfeit when a player leaves. The web build is now a 540×960 canvas, and the Pixel Perfect Camera uses Stretch Fill so any browser zoom fills the frame. **The first phone test on itch looks good.**
- *Oct 27:* the artists' pixel fonts for all text, plus the deliver, steal, bounce and win/lose sounds.
- *Oct 28:* UI sounds, menu music, juice (shake, coin bursts, steal flash, beat-synced finale), the stealing rework, noon-to-midnight lighting, pixel shadows, a code-drawn stand-in desert floor, and the match events (section 4b).
- *Oct 28, later:* the first **itch.io release** (0.17, html5 channel via butler) and an itch web template. Two-device online tests (editor vs. iPhone) found the network-delay problems in section 7; fixed in 0.18 and retested: scores matched on both screens. Then the night-lighting fix for web builds and the matchmaking screen fix (0.19), the walking chest and the arena ground art (0.20).
- *Oct 29:* the idol mockup (0.21), lighter hoards with dashed edges (0.22), the pixel fonts as TTF, the lasso art template, the itch page mockup for the writer, then the lasso loop art, its spin, and the train-style haul with a slower, capped pull (0.23).

| # | Milestone | Done when | Target | Artists, same window |
|---|---|---|---|---|
| 1 | ✅ **The throw** | Swing and release with momentum and backlash, wall bounces, rope trail and tightening (done Oct 20, simplified Oct 22) | Wed Oct 21 | Coin/chest/idol idle + walk. Player idle/spin/throw |
| 2 | ✅ **Solo match vs. bot** | Moving treasure, catching, hauling, hoards and scores, stealing, 2-minute timer, winner, bot (done Oct 22). **A complete, playable game** | Fri Oct 23 | Arena ground, hoard zones, lasso rope/loop |
| 3 | **Art in** | Artists' sprites replace the placeholders; portrait build on itch.io ✅; **tested on a phone** ✅ (placeholders, Oct 24). In so far: fonts, hunters (blue/red, front/back), winner animations, coin, chest, ground, logo, UI components, idol (mockup). Still placeholders: the lasso (template sent Oct 29) | Sat Oct 24 | Treasure caught/struggle anims, UI kit, title logo |
| 4 | ✅ **Online 1v1** | Lobby matchmaking, synced match, host decides catches, bot after 15 s if nobody's found (done Oct 24) | Mon Oct 26 | VFX, decorations |
| 5 | ✅ **Menus and brainCloud** | Title, name entry, How to Play, results, leaderboard (total treasure) | Tue Oct 27 | Fix-ups from playtests |
| — | **🔒 Feature freeze** | Evening of **Tue Oct 27**: after this, only bugs, tuning and polish | | |
| 6 | **Polish and sound** | Joey's SFX and music ✅ (treasure voices left), juice ✅ (shake, coin bursts, steal flash, finale) | Wed Oct 28 | **itch page:** cover 630×500, banner, screenshots, GIF |
| 7 | **Ship** | itch.io page done, final build tested on desktop and phone (online with match events ✅ Oct 28), **submitted**. Left as of Oct 29: lasso art in code, a final build and online test, new screenshots, the itch page text and art, leaderboard test-entry cleanup, then submit | **Evening of Thu Oct 29** | Final check of the page |
| — | **Deadline** | Fri Oct 30, 9:00 AM PT. No work planned | | |

**Cut first if short on time:** power-ups (already a stretch goal), then the remaining treasure reactions, then treasure voices.
**Never cut:** the bot opponent. Voters need someone to play against.

**After submission:** uploads lock during voting, but brainCloud portal settings can still change. Keep the lobby and leaderboard working, and watch for bugs players report.

---

## 13. Risks and what we cut first

| Risk | Plan |
|---|---|
| Online sync bugs eat the schedule | The bot match is a complete game on its own. If networking isn't solid by **Oct 26**, ship bot-first with online as a bonus |
| Match events (Oct 28) desync online | ✅ Tested between two devices on Oct 28: in sync |
| Nobody online during voting | Bot fallback after 15 s (Must) |
| Art arrives late | Code uses placeholder shapes until art lands. Integrate art in batches (Oct 24, then whatever's ready by Oct 27) |
| The throw is too hard for new players | How-to-Play card, generous defaults, tune on Oct 26 |
| Scope creep | Cut in this order: power-ups → Mimic → player stats → treasure reactions → adaptive music |

---

## 14. Open questions

- ~~Art style~~: pixel art at 23 PPU (decided, section 10). The palette is up to the artists.
- Is "Loot Lasso" the final title? (The logo says so.)
- Team agreement for asset pack sales: ownership and revenue split (section 16)
- Should audio source projects (`SFX_Project/`) live in this repo or somewhere separate?

---

## 15. Beyond the jam: iOS

The team wants Loot Lasso on iPhone after the jam. **None of this is jam work.** During the jam we only make choices that keep it easy: portrait layout, `Pointer` input, screen-relative tuning, finger-sized UI and sharp art.

### Why it fits
- **The controls translate directly:** spin a finger, flick, release.
- **brainCloud supports iOS natively.** In a native app the brainCloud **Relay** works (UDP/TCP, no https restriction), which is faster than lobby signals. The network code keeps both, so the iOS version can switch.
- **Login can upgrade:** brainCloud supports Apple Game Center and Sign in with Apple. Anonymous jam accounts can be linked to them later.
- **The jam rules allow it:** we keep ownership and can keep updating. Real-money purchases are only off-limits during the jam and voting.

### What's needed
| Need | Plan (no one on the team has a Mac) |
|---|---|
| Apple Developer account | $99/year, needed to put builds on phones (TestFlight) and the App Store |
| Building for iOS | iOS builds normally need a Mac with Xcode. Without one: **Unity Build Automation** (Unity's cloud build service, which builds and signs iOS in the cloud) or a **rented cloud Mac** (e.g. MacinCloud). Check current pricing when we get there |
| Signing certificates | Created on the Apple Developer website and uploaded to the cloud build service |
| Testing | TestFlight on the team's iPhones |
| App Store listing | Screenshots, description, privacy details (what data brainCloud stores), age rating |

### Probable order after voting ends
1. Fix the issues voters reported.
2. Add the Unity iOS build module and set up Unity Build Automation (or a cloud Mac).
3. Switch online play to the brainCloud Relay on iOS, keeping lobby signals for the web.
4. Add Game Center / Sign in with Apple.
5. TestFlight with friends, then decide on a business model (paid, free, cosmetics) before the App Store submission.

---

## 16. Beyond the jam: asset packs

The team also wants to sell what we make as **asset packs**: mainly the **art, sound effects and music**, and possibly some **scripts**. The game becomes the live demo: "as seen in Loot Lasso, play it free on itch.io".

### Possible packs
| Pack | Contents | Made by |
|---|---|---|
| Treasure & cowpoke sprites | Animated living treasure, player characters, lasso, arena tiles, props | Artists |
| Loot Lasso UI kit | Buttons, panels, timing ring, icons, logo-free versions | Artists |
| Treasure & loot SFX | Lasso twirl/throw/grab, coin/chest/idol voices, steal, UI | Joey |
| Music | Title, match and final-30-seconds loops, with stems for adaptive layers | Joey |
| *(Maybe)* Spin & flick throw | The lasso input system as a standalone Unity script package with a demo scene | Joey |

Where to sell: **itch.io** (asset listings, quick to publish) first, then the **Unity Asset Store** (submission review takes a while).

### Rules during the jam, so everything stays sellable
1. **Only original work in anything we plan to sell.** Free or CC0 assets are fine as placeholders or game-only extras, but we can't resell them. Keep a list in `docs/Credits.md` of anything not made by us (fonts too: check the font's license allows commercial use and redistribution).
2. **Keep the source files:** `.psd` / `.aseprite` / `.kra` layers for art, DAW projects and stems for audio. Packs sell better with sources and layered files.
3. **Consistent specs:** one palette, one pixels-per-unit, consistent sprite sizes and a clear naming scheme. That's needed for the game anyway, and buyers expect it.
4. **Export audio "pack-ready" the first time:** seamless loops, WAV (44.1 kHz, 16-bit) plus OGG, and names like `LootLasso_LassoThrow_01.wav`.
5. **Sellable scripts stay separate from brainCloud.** A throw package must not depend on the brainCloud SDK or our patch: the SDK has its own license, and buyers may not use brainCloud. That means the input system needs its own namespace and assembly, with brainCloud only in the game layer. Worth keeping in mind during the rewrite, but no extra packaging work during the jam.
6. **Check AI-assistance rules before selling code.** Parts of the code are written with AI help, and stores (including the Unity Asset Store) have their own policies on AI-generated content. Check the current rules before listing script packs. Art and audio made by the team aren't affected.

### Agree on ownership now
With three people making things to sell, write down a short **team agreement** before the first sale (ideally this week, while it's easy):
- who owns what (each person their own work, or shared by all three),
- how revenue from each pack is split,
- who can publish and under whose account,
- what happens if someone leaves the project.

### Probable order after voting ends
1. Clean up and export the art and audio at pack quality.
2. Make preview material: an animated GIF or video of the sprites, an audio preview playlist.
3. Publish on itch.io with a link to the game, and link the game's page to the packs.
4. Submit to the Unity Asset Store.
5. Consider the script package once the game code has settled.
