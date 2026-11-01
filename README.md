# Room Hub

PEAK BepInEx mod: room history, quick rejoin, player search, Steam invite / add friend, and a live Steam friends rail.

**Version:** 3.1.0 · **Assembly:** `RoomHub.dll` · **GUID:** `com.roomhub.RoomHub` · **Target:** PEAK 3.0.1
**Repository:** https://github.com/why-ban/peak-room-hub · **License:** MIT

## What it does

- Remembers every lobby you join, so a crash or a disconnect never costs you the room
- Rooms you host are saved too, but a room that never saw a second player is dropped on exit
- Quick-rejoins the most recent room from the title screen
- Searches everyone you have met by display name
- Sends Steam invites and friend requests without leaving the game
- Shows which Steam friends are playing PEAK right now, which biome they are in, and lets you join or invite them

The panel is rebuilt in PEAK's own visual language: colours and geometry are measured from the shipped game UI,
buttons are the game's own button widget, and rounded surfaces are generated at runtime so no game art is redistributed.

## Layout

- `src/RoomHub/` — product source (the only tree that ships)
  - `src/RoomHub/Ui/` — panel toolkit and views (design tokens, sprites, buttons, rows, page shell)
- `docs/` — design and audit documentation
- `dist/thunderstore/` — Thunderstore package files
- `dist/RoomHub-3.1.0.zip` — upload-ready package
- `tools/uimock/` — offline HTML mock of the panel plus its Playwright render script

## Build

```powershell
dotnet build "src\RoomHub\RoomHub.csproj"
```

The project references the game assemblies and PEAKLib from your local PEAK install; adjust `GameDir` in
`src/RoomHub/RoomHub.csproj` if the game lives somewhere else. Copy `build\out\RoomHub.dll` to
`PEAK\BepInEx\plugins\` with the game closed.

Requires [BepInEx 5](https://github.com/BepInEx/BepInEx), [PEAKLib Core and UI](https://github.com/PEAKModding/PEAKLib).

## Panel design

The panel is built entirely in code and redistributes no game art:

- colours and geometry come from PEAK 3.0.1's own UI, extracted from the shipped assets (`docs/audit/ui-palette.md`)
- rounded surfaces are generated at runtime as nine-sliced sprites (`src/RoomHub/Ui/UiSprites.cs`)
- buttons are cloned from the game's own button template and recoloured per role
- rows are pooled by key, so a friends update never rebuilds or scroll-jumps the list

To preview a layout change before writing code, edit `tools/uimock/panel.html` and render it:

```powershell
cd tools\uimock
npm i
node render.mjs "panel.html?variant=B&state=rooms" out.png
```

## Docs

- `docs/ui-spec.md` — panel implementation spec (tokens, geometry, components, navigation)
- `docs/ui-impl-notes.md` — how the implementation maps onto that spec, and what still needs in-game verification
- `docs/integration-plan.md` — what was fixed and what remains
- `docs/regression-3.0.0.md` — the text-rendering regression and its root cause
- `docs/audit/` — evidence-backed audits of the game API, data layer, Steam layer, UI layer, dependencies and the extracted UI palette

## Thunderstore

See `dist/thunderstore/` and the wiki: [Creating a Package](https://wiki.thunderstore.io/mods/creating-a-package).

The zip root must contain `icon.png` (256×256), `README.md`, `manifest.json` and `RoomHub.dll`.
Package name is `Room_Hub`.

## License

MIT, see [LICENSE](LICENSE). PEAK, PEAKLib and BepInEx belong to their respective authors; no game code or assets are
redistributed by this repository.
