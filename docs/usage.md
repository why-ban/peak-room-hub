# Room History — usage

## Install

`RoomHub.dll` → `BepInEx/plugins/`  
Remove any old `ReTrace.dll` / `QuickRejoin.dll`.

Needs PEAKLib Core + UI (ModConfig optional).

## Title screen

- Left-click **Room History** → history UI
- **Join** / double-click room → rejoin lobby
- **View** → met players; **Invite** / **Add Friend** on the right
- **Search** → filter by **player display name only**
- **Back** / **Esc** → nested back, then close
- Hotkey **J** (default) → latest room
- **Right-drag** ribbon → save position

## Data

`BepInEx/config/RoomHub/history.tsv`  
Config: `com.roomhub.RoomHub.cfg` (expire default 7 days)

Old ReTrace / Quick Rejoin history is migrated on first load.
