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

Two treasure hunters, one field full of living treasure. Spin your lasso, flick it out, and drag loot back to your hoard before your rival does. Or reach over and rope theirs.

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

*Redesigned Oct 20.* The prototype's loop snapped straight to the pointer and power was a hidden 1-second average, so the wind-up didn't feel like anything. Now the loop has **momentum** like a real lasso, and each action has one clear job:

| Action | What it controls |
|---|---|
| **Spin** (the loop's momentum) | **Rope strength**: how much weight you can haul without the rope snapping |
| **Flick direction** | Where the lasso goes (exactly, no random scatter) |
| **Flick speed** | How far it goes |
| **Timing ring** (bonus) | A **PERFECT** flick opens the loop wider: a bigger catch area |

| Step | What the player does | What the game does |
|---|---|---|
| **Wind up** | Hold the button and circle the pointer around your character | The loop **chases your hand and always trails a little**. It can only speed up gradually (max acceleration), so getting it fast takes a few turns of steady spinning. Its speed sets the rope strength. |
| **Too fast** | Whip the pointer faster than the loop can follow | When your hand gets more than ~120° ahead of the loop, the rope **tangles**: the loop flashes red, stalls for 0.4 s, and the wind-up drops to zero. |
| **Steady** | Hold a consistent spin speed for ~1–2 s | The **timing ring** appears around your character. It shrinks, then resets (1.4 s cycle), and turns green in the PERFECT window. Speeding up or slowing down hides it again. |
| **Flick** | Flick the pointer outward in the direction to throw | Locks in direction, rope strength and PERFECT/normal at the start of the flick. The hardest moment of the flick sets the distance. |
| **Release** | Let go within 0.2 s of the flick | The lasso flies out, lands, and comes back. Releasing without a flick just stops the spin. |

- **Distance:** 1.5 to 12 units, from flick speed. Flick speed is measured in **screen heights per second**, so the same physical flick throws the same distance on a phone and a big monitor.
- **Rope strength:** 2 (half a circle per second) to 9 (three circles per second). Coin = 1, Chest = 3, Idol = 6.
- **PERFECT:** catch radius ×1.6.
- **Mouse and touch:** input goes through the Input System's `Pointer`, so a finger works exactly like the mouse.
- **To test early:** on a phone your thumb covers the character while circling it. Compare "circle around your character" with "circle anywhere on screen" on a real phone.
- All numbers are Inspector fields on `LassoSpin` (Scripts/Game) and will be tuned in playtests.

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

Power-ups wander the field like treasure and are caught the same way. Only attempted if milestones 1–5 are done before the feature freeze (**Oct 27**).

| Power-up | Effect |
|---|---|
| Golden Rope | The next throw can't snap |
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
**Pack-ready:** the art is also meant for a sellable asset pack (section 16). Keep layered source files, stick to one palette and pixels-per-unit, and only use your own work, or note anything else in `docs/Credits.md`.

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

All audio is also meant for a sound and music pack (section 16): export seamless loops as WAV (44.1 kHz, 16-bit) plus OGG, keep the DAW projects and stems, and name files like `LootLasso_LassoThrow_01.wav`.

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

*Re-planned Oct 20.* Work and other commitments made the first days short, and available hours vary a lot, so the plan is **milestones in priority order** instead of a fixed day-by-day schedule. Work down the list; wherever we are at the deadline, it's the most complete game possible. Claude writes most of the code (fully commented), and Joey designs, plays, tunes and reviews it.

**Done so far (Oct 16–20):** project setup, brainCloud login and leaderboards, prototypes of the throw, moving treasure and browser multiplayer (all proven on itch.io), design doc, first steps of the rebuilt throw (`LassoSpin.cs`).

| # | Milestone | Done when | Target | Artists, same window |
|---|---|---|---|---|
| 1 | **The throw** | Wind-up with a lasso that has momentum, flick, timing ring, the loop flying out and landing. Feels good | Tue Oct 21 | Coin/chest/idol idle + walk. Player idle/spin/throw |
| 2 | **Solo match vs. bot** | Moving treasure, catching, pulling, snapping, hoards, stealing, 2-minute timer, winner. **A complete, playable game** | Thu Oct 23 | Arena ground, hoard zones, timing ring, lasso rope/loop |
| 3 | **Art in** | Artists' sprites replace the placeholders; portrait build on itch.io; **tested on a phone** | Fri Oct 24 | Treasure caught/struggle anims, UI kit, title logo |
| 4 | **Online 1v1** | Lobby matchmaking, synced match, host decides catches, bot after 15 s if nobody's found | Sun Oct 26 | VFX, decorations |
| 5 | **Menus and brainCloud** | Title, name entry, results, leaderboard (total treasure) | Mon Oct 27 | Fix-ups from playtests |
| — | **🔒 Feature freeze** | Evening of **Mon Oct 27**: after this, only bugs, tuning and polish | | |
| 6 | **Polish and sound** | Joey's SFX and music, juice (shake, particles), How to Play | Tue Oct 28 | **itch page:** cover 630×500, banner, screenshots, GIF |
| 7 | **Ship** | itch.io page done, final build tested on desktop and phone, **submitted** | **Evening of Wed Oct 29** | Final check of the page |
| — | **Deadline** | Thu Oct 30, 9:00 AM PT. No work planned | | |

**Cut first if short on time:** power-ups (already a stretch goal), then treasure reactions, then music.
**Never cut:** the bot opponent. Voters need someone to play against.

**After submission:** uploads lock during voting, but brainCloud portal settings can still change. Keep the lobby and leaderboard working, and watch for bugs players report.

---

## 13. Risks and what we cut first

| Risk | Plan |
|---|---|
| Online sync bugs eat the schedule | The bot match is a complete game on its own. If networking isn't solid by **Oct 26**, ship bot-first with online as a bonus |
| Nobody online during voting | Bot fallback after 15 s (Must) |
| Art arrives late | Code uses placeholder shapes until art lands. Integrate art in batches (Oct 24, then whatever's ready by Oct 27) |
| The throw is too hard for new players | How-to-Play card, generous defaults, tune on Oct 26 |
| Scope creep | Cut in this order: power-ups → Mimic → player stats → treasure reactions → adaptive music |

---

## 14. Open questions

- Art style: pixel or hand-drawn, pixels per unit, and the palette? (kickoff meeting)
- Who does what between the two artists? (kickoff meeting)
- Is "Loot Lasso" the final title?
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
| Treasure & loot SFX | Lasso twirl/throw/grab/snap, coin/chest/idol voices, steal, UI | Joey |
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
