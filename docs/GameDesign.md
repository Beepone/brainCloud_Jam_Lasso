# Loot Lasso: design doc and jam plan

**Working title:** Loot Lasso
**Jam:** [brainCloud Jam](https://itch.io/jam/braincloud-jam). Theme: **Treasure**
**Dates:** Thu Oct 16, 9:00 AM PT → **Thu Oct 30, 9:00 AM PT** (submission deadline)
**Team:** Joey (code, audio, design) · two artists (split decided at kickoff)
**Engine:** Unity 6.6 (6000.6.6f1), 2D URP, Web (WebGL) build on itch.io
**Screen:** **portrait** (9:16). The jam build is web; an **iOS** version is the post-jam goal (see section 15)
**Repo:** https://github.com/Beepone/brainCloud_Jam_Lasso

---

## 1. The pitch

Two cowpokes, one field full of living treasure. Spin your lasso, flick it out, and drag loot back to your hoard before your rival does. Or reach over and rope theirs.

A short, real-time 1v1 browser game about **timing and greed**. Fast treasure is worth more but hard to hit, heavy treasure snaps weak ropes, and anything in your hoard can be stolen.

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
      └── treasure lands in your hoard ←── pull it back (rope can snap if overloaded)
```

A match ends when:
- a player owns **all** the treasure, or
- the timer runs out → the player with the most treasure value wins.

Match length defaults to **2 minutes** and is a field in the Inspector, so it can be tuned in playtests.

---

## 3. The lasso (controls)

All values live on `LassoThrower` and can be tuned in the Inspector. The numbers below are the current prototype values.

| Step | What the player does | What the game does |
|---|---|---|
| **Wind up** | Hold left mouse and circle the mouse around your character | The lasso spins with your mouse. **Power = average spin speed over the last 1 second**, so throws need a wind-up. Pausing for 0.25 s restarts it. |
| **Timing** | Watch the ring around your character | The ring shrinks, then resets (1.4 s cycle). Its color shows quality: **red = Sloppy, yellow = Good, green = Perfect**. |
| **Flick** | Flick the mouse outward in the direction to throw | Locks in direction, power (from 0.15 s before the flick) and timing quality. |
| **Release** | Let go within 0.2 s of the flick | The lasso flies. Releasing without a flick cancels; right click also cancels. |

- **Power → distance:** 1.5 to 15 units. Full power is about 4 circles per second.
- **Aim:** the lasso lands **exactly** where you flicked. No random scatter.
- **Mouse and touch:** input goes through the Input System's `Pointer`, so a finger works exactly like the mouse.
- **Screen-relative tuning:** flick speed and circle size are measured relative to the screen size, not in world units, so the throw feels the same on a phone and a big monitor.
- **To test early:** on a phone your thumb covers the character while circling it. Compare "circle around your character" with "circle anywhere on screen" on a real phone (Oct 18).
- **Timing quality affects the rope, not the aim:**

| Quality | Rope strength | Flight speed |
|---|---|---|
| Perfect | ×1.5 (9) | ×1.3 |
| Good | ×1.0 (6) | ×1.0 |
| Sloppy | ×0.6 (3.6) | ×0.8 |

---

## 4. Treasure

Treasure is **anthropomorphised**: little characters that wander the field. More valuable = faster, but each keeps its weight.

| Type | Value | Weight | Speed (units/s) | Personality ideas |
|---|---|---|---|---|
| **Coin** | 1 | 1 | 1.0 | Lazy, waddles, naps |
| **Chest** | 5 | 3 | 2.0 | Grumpy, stomps, heavy |
| **Idol** | 15 | 6 | 3.5 | Vain and twitchy, sprints away |

- **Catching:** everything inside the loop when it lands is caught. Catch radius is 1 unit.
- **Rope snapping:** if the total weight caught is more than the rope's strength, the rope **snaps** 40% of the way back. The treasure drops right there and starts wandering again. Snapped loot can end up closer to *someone*, which is part of the strategy.
- **Wandering** is calculated from the match seed and clock, so both players see identical movement without any network traffic.

**Should have:** treasure reacts to the game. It flees from a lasso landing nearby, struggles while being pulled, and celebrates or sulks in a hoard.
**Could have:** a **Mimic** chest that bites your rope (snaps it instantly).

---

## 5. Hoards and stealing

- Each player has a **hoard zone** on their side of the field. Delivered treasure sits there and counts toward their score.
- A lasso landing on the **rival's hoard** catches treasure out of it: a **steal**.
- Where caught treasure is placed in a hoard comes from the shared seed, so steal throws line up on both screens.

**Could have:** a short "guard" window after delivering, or hoard defenses as power-ups.

---

## 6. Power-ups (stretch goal)

Power-ups wander the field like treasure and are caught the same way. Only attempted if the core game is done and solid by **Oct 24**.

| Power-up | Effect |
|---|---|
| Golden Rope | The next throw can't snap |
| Big Loop | Next throw's catch radius ×2 |
| Quick Draw | Wind-up time halved for 10 s |
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
- **What gets sent:** throws only (landing point, speed, rope strength, match time). Treasure movement is never sent.
- **The host decides contested results:** when a lasso lands, the host decides what was caught and sends `caught(treasure ids, thrower)`. The guest shows its own throws immediately and corrects if the host disagrees. This fixes simultaneous grabs and snap mismatches.
- **Messages:** `hello`, `start(seed)`, `throw`, `caught`, `snap`, `matchEnd(scores)`, plus `ping` for latency.
- **No opponent?** After about **15 s** of waiting, the match starts against a **bot**. The bot uses the same throws as a player (picks a target, adds aim error, throws on a timer), so it needs no special rules.
- **Disconnects:** if the opponent leaves mid-match, a bot takes over or the match ends with a "win by forfeit".

---

## 8. brainCloud features (judging requirement: Authentication + 1 more)

| Feature | Used for | Status |
|---|---|---|
| **Authentication** | Anonymous login plus a display name | ✅ working |
| **RTT + Lobbies** | Matchmaking and in-match messages (lobby signals) | ✅ prototyped |
| **Leaderboards** | **Total treasure ever** caught (bot and online matches both count), shown on the title screen | ✅ prototyped (`Highscores`) |
| Player statistics | Lifetime catches / steals / perfect throws on the results screen | Could have |

---

## 9. Screens

1. **Title:** logo, Play, Leaderboard, How to Play, Settings (volume).
2. **Name entry:** on first launch only, saved to brainCloud.
3. **Matchmaking:** "Looking for a rival..." with a countdown → bot match if none found.
4. **Match HUD:** both scores, timer, your name and the rival's name.
5. **Results:** win/lose, score breakdown (caught, stolen, snapped), Play Again / Title.
6. **Leaderboard:** top 10 plus your rank.
7. **How to Play:** a short animated or illustrated card. The throw needs explaining.

---

## 10. Art list

**Style:** TBD at the kickoff meeting. Decide **pixel art vs. hand-drawn**, **pixels per unit**, and a **palette** before drawing final assets.
**Screen:** **portrait 9:16**, top-down. Rival's hoard at the top, yours at the bottom, field in the middle.
**Resolution:** design and lay out for a **1080×1920** reference screen, and draw art large enough to stay sharp on a phone. The itch.io embed is **450×800** with a fullscreen button.
**UI:** every button at least **~120 px tall at 1080 wide** (finger-sized), and nothing important in the top ~100 px or bottom ~80 px, where phone notches and home bars sit.
**Delivery:** PNG (or `.aseprite` / `.psd` sources), committed under `project_brainCloudJam/Assets/Art/<Category>/`. Name files like `Treasure_Coin_Run.png`. Big files go through Git LFS automatically.

Who takes what is decided at the kickoff meeting. The two groups below are one natural split.

### Characters and treasure
| Asset | Frames / notes | Priority |
|---|---|---|
| Player character (top-down cowpoke) | Idle, Spin (lasso overhead), Throw, Pull. Two color variants (you vs. rival) | Must |
| Lasso | Rope segment (tiles along a line), loop (open and closed) | Must |
| Coin | Idle, Walk, Caught/struggle, In-hoard idle | Must |
| Chest | Idle, Walk, Caught/struggle, In-hoard idle | Must |
| Idol | Idle, Run, Caught/struggle, In-hoard idle | Must |
| Treasure reactions | Scared/flee, celebrate | Should |
| Mimic chest | Idle disguised, reveal/bite | Could |
| Power-up items | One icon/sprite each | Could |

### World, UI and the itch.io page
| Asset | Notes | Priority |
|---|---|---|
| Arena ground | Tileable ground plus edge/border | Must |
| Two hoard zones | Clearly "mine" vs. "theirs" | Must |
| Timing ring | Ring sprite (tinted red/yellow/green in code) | Must |
| UI kit | Buttons, panels, score display, timer, font choice | Must |
| Title logo | "Loot Lasso" | Must |
| VFX | Rope snap, catch puff, steal sparkle, Perfect flash, coin burst | Should |
| Decorations | Cacti, rocks, props around the arena edge | Should |
| itch.io page | Cover image **630×500**, banner, background, 3–5 screenshots, 1 GIF | Must (Oct 27–28) |

---

## 11. Audio (Joey)

| Sound | Status |
|---|---|
| Lasso twirl (loop, pitch follows spin) | ✅ done |
| Lasso throw (fades on landing) | ✅ done |
| Lasso grab | ✅ done |
| Rope snap, steal, deliver/score | To do |
| Treasure voices (coin squeak, chest grumble, idol gasp) | To do |
| UI clicks, timer warning, win/lose stingers | To do |
| Music: title, match, final 30 s | To do. Doubles as the first tracks of the asset pack; stems allow adaptive layers (Could have) |

---

## 12. Timeline

Joey works evenings on weekdays. Artists work around their jobs. **Must** items first; **Should/Could** only when the day's Musts are done.

| Day | Date | Joey (code/audio) | Artists |
|---|---|---|---|
| 1 | Thu Oct 16 | ✅ Setup, brainCloud, throw/treasure/network prototypes | Kickoff: idea agreed |
| 2 | Fri Oct 17 | Rebuild the throw together: spin, wind-up, flick, timing ring. Portrait camera, `Pointer` input, screen-relative flick | **Kickoff/style meeting:** pixel vs. drawn, PPU, palette, mood board, who does what. Concept sketches |
| 3 | Sat Oct 18 | Treasure wandering, catching, pulling, snapping, hoards. **Phone test** of the throw (itch.io in a phone browser) | Player + treasure concepts → first coin in engine. Ground + zone concepts |
| 4 | Sun Oct 19 | Solo match: timer, scoring, win conditions, **bot v1** | Coin/chest/idol idle + walk. Arena ground, hoard zones |
| 5 | Mon Oct 20 | Online: matchmaking, host-decided catches, message protocol | Player idle/spin/throw. Timing ring, HUD elements |
| 6 | Tue Oct 21 | Online: steals and snaps synced, bot fallback, disconnects. **First online build on itch** | Player pull, lasso rope/loop. UI kit, title logo |
| 7 | Wed Oct 22 | Menus: title, name entry, matchmaking, results, leaderboard. **Art integration #1** | **Midpoint playtest together**, feedback |
| 8 | Thu Oct 23 | Treasure reactions (flee, struggle), juice (shake, particles) | Treasure caught/struggle anims. VFX |
| 9 | Fri Oct 24 | **Decision: power-ups or polish.** Audio: snap/steal/UI sounds | Reactions or power-ups. Decorations |
| 10 | Sat Oct 25 | Music and audio pass. **Art integration #2**. Online playtest with friends | Fix-ups from playtest |
| 11 | Sun Oct 26 | Balance and tuning, bug fixing. **🔒 Feature freeze at end of day** | Final art fixes |
| 12 | Mon Oct 27 | Bugs, load size/performance, fresh-browser test on desktop **and phone** | **itch page:** cover, banner, screenshots, GIF |
| 13 | Tue Oct 28 | Final bug pass, itch description (list brainCloud features) | Page layout and text review |
| 14 | Wed Oct 29 | **Final build uploaded and submitted by the evening** (buffer night) | Final check of the page |
| — | Thu Oct 30 | **Deadline 9:00 AM PT.** No work planned | — |

**After submission:** uploads lock during voting, but brainCloud portal settings can still change. Keep the lobby and leaderboard working, and watch for bugs players report.

---

## 13. Risks and what we cut first

| Risk | Plan |
|---|---|
| Online sync bugs eat the schedule | The bot match is a complete game on its own. If networking isn't solid by **Oct 23**, ship bot-first with online as a bonus |
| Nobody online during voting | Bot fallback after 15 s (Must) |
| Art arrives late | Code uses placeholder shapes until art lands. Integrate art in batches (Oct 22, Oct 25) |
| The throw is too hard for new players | How-to-Play card, generous defaults, tune on Oct 26 |
| Scope creep | Cut in this order: power-ups → Mimic → player stats → treasure reactions → adaptive music |

---

## 14. Open questions

- Art style: pixel or hand-drawn, pixels per unit, and the palette? (kickoff meeting)
- Who does what between the two artists? (kickoff meeting)
- Is "Loot Lasso" the final title?

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
