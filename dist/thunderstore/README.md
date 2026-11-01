# Room Hub for PEAK

**Every room you have been in, every player you have met, and which of your Steam friends are climbing right now, in one panel that looks like PEAK built it.**

Rebuilt from scratch for **PEAK 3.0.1**. Crash-safe, zero-config, EN / 简体中文 / 繁體中文, gamepad-ready.

> **Source, issues and changelog:** https://github.com/why-ban/peak-room-hub

---

## What is new in 3.0.0

- **A real panel.** The old wall of text rows is gone. Room Hub now uses PEAK's own UI palette, button anatomy and typography, laid out as a full-screen menu: header, tabs, list, footer hints, and a permanently visible Steam friends rail on the right.
- **Friends by biome.** PEAK publishes 13 rich-presence states; the friends rail reads them, so a friend shows **"In game · Alpine"** instead of a vague "In game".
- **Gamepad support.** Open the panel and something is already selected. D-pad moves between rows and footer actions, A activates, B backs out.
- **Built for PEAK 3.x.** Rebuilt against the 3.0.1 assembly, including its refactored networking layer.
- **Your history is now hard to lose.** A locked or unreadable history file can no longer be overwritten with an empty one, and every shrinking write keeps a `.bak` copy first.

---

## Features

### Room history
- Every lobby you join is saved locally, so a crash or a disconnect never costs you the room.
- Rooms you host are saved too, so you can invite a stranger and add them as a friend afterwards.
- A room you hosted that never had a second player in it is dropped when you leave, so a solo session does not clutter the list.
- Up to **20 recent rooms** (configurable), pruned by age (default 7 days).
- The room host is frozen to the **creator**; Steam ownership transfers never move the badge.
- Join, inspect who was there, or delete a room from the panel.

### Player search
- Search everyone you have ever met by display name.
- Each hit shows when you met them and which room it was.
- Invite them into your lobby or send a Steam friend request from the row.

### Steam invites
- PEAK has no invite button of its own, and Steam's lobby invite works for people who are **not your friends** and do not have this mod.
- The button flips to a grey **Invited** state and stays that way for the session.

### Friends rail
| State | Meaning | Action |
|---|---|---|
| In game · *biome* | joinable lobby exposed | **Join** |
| In main menu | in PEAK, not in a room | **Invite** into your lobby |
| At the airport | waiting between runs | none |
| Invite only | private room, Steam hides the lobby | none |
| Single player | offline-mode session | none |
| Status unknown | their game published nothing we can read | none |

### Join feedback
PEAK fails joins silently. Room Hub tells you when a room is **full**, **gone**, or **not joinable**, and stops offering the Join button for a room that just failed.

### Hotkeys and entry points
| Action | Key / place |
|---|---|
| Quick join most recent room | **J** (title screen) |
| Open / close the panel | **K** (title screen and in-game) |
| Open the panel | pink button in the pause menu |

---

## Install

1. Install [BepInExPack_PEAK](https://thunderstore.io/c/peak/p/BepInEx/BepInExPack_PEAK/), [PEAKLib Core](https://thunderstore.io/c/peak/p/PEAKModding/PEAKLib_Core/) and [PEAKLib UI](https://thunderstore.io/c/peak/p/PEAKModding/PEAKLib_UI/).
2. Drop `RoomHub.dll` into `BepInEx/plugins/`.
3. Launch the game. Config and history folders are created automatically.

---

## Configuration

File: `BepInEx/config/com.roomhub.RoomHub.cfg`, also editable in game with [PEAKLib ModConfig](https://thunderstore.io/c/peak/p/PEAKModding/PEAKLib_ModConfig/).

| Setting | Default | Description |
|---|---|---|
| Show Main Menu Button | true | Title-screen ribbon button |
| Use Custom Button Position / Pos X/Y | none | Saved automatically when you right-click-drag the ribbon |
| Enable Hotkey | true | Master switch for both hotkeys |
| Quick Join Hotkey | J | Title screen: rejoin the most recent room |
| Mod Menu Hotkey | K | Title and in-game: toggle the panel |
| Max Recent Rooms | 20 | Oldest rooms are removed first |
| History Expire Hours | 168 | 7 days |

---

## Upgrading from 3.0.x

- `Enable Mod` is gone. The mod is always on; an old `Enable Mod = false` line left in your `.cfg` is ignored and can be deleted by hand.
- `Max Recent Rooms` now defaults to **20** for fresh installs. If you already have a `.cfg`, your existing value stays until you change it.

## FAQ

**A friend shows "Invite only"; why can I not join?**
Their room is private. Steam does not expose private lobbies to non-members, so a direct join is impossible; they have to invite you. Room Hub shows the same state Steam's own Join Game button would.

**A friend shows "Status unknown".**
Steam only pushes a friend's rich presence when it changes. If they set it before you launched the game and never re-published, there is nothing to read yet. The row updates the moment they change state.

**Where is my history stored?**
`BepInEx/config/RoomHub/history.tsv`. Plain text; back it up or edit it while the game is closed.

**Language switching does not apply instantly?**
It does. Room Hub listens to PEAK's own language-change event and refreshes the panel without a restart.

---

*Made for the PEAK community. Report issues at https://github.com/why-ban/peak-room-hub/issues with your `BepInEx/LogOutput.log` attached. MIT licensed.*
