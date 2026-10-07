# Pattern Book

Replaces the recipe picker on the smithing anvil and in clay forming with one that is
easier to use when there are a lot of recipes:

- **Sorted** alphabetically by name.
- **Searchable.** The search box has focus as soon as the picker opens. Every word you
  type has to appear in the name, so `iron plate` finds "Iron plate".
- **Enter** picks the selected recipe, outlined in blue. Typing selects the first match,
  and pointing at a recipe selects it. It stays selected after the mouse moves away, so
  the outline always marks the recipe the details below describe.
- **Confirm before choosing**, the switch beside the search box. When it is on, picking a recipe
  asks "Make … ?" first: Enter or Confirm takes it, Escape or Cancel goes back to the
  picker. It is off by default, and the picker remembers it across restarts.
- **Scrolls.** The grid shows at most 10×8 recipes, fewer if the screen is too small for
  that, and scrolls the rest. Vanilla keeps growing until the picker runs off the screen.
  A search shrinks the picker to its matches, keeping the top edge and width where they
  were so the search box does not move.

The picker is client-side only. The mod sends the same packets as vanilla, so it works
on servers that do not have it installed.

## How it works

A Harmony transpiler on `BlockEntityAnvil.OpenDialog` and `BlockEntityClayForm.OpenDialog`
swaps the `new GuiDialogBlockEntityRecipeSelector(...)` in each for
`GuiDialogSearchableRecipeSelector`, which takes the same arguments. The rest of each
method — which recipes are offered, what selecting one does — stays vanilla's. If a game
update changes how a method builds its picker, the transpiler logs a warning and leaves
that one vanilla.

Only these two are changed. Knapping and other mods' uses of the vanilla picker are
left alone. A mod that replaces `OpenDialog` outright with a prefix (smithscanvas) wins,
and this mod's picker does not open on top of it.

## Other mods

By default this mod **steps aside** for these two, which build on the vanilla picker:

| mod | what it keeps | override |
|---|---|---|
| Smithing Plus | its own searchable picker, when its `AnvilShowRecipeVoxels` option is on | `OverrideSmithingPlus` |
| AnvilGuard | the "Smith a … ?" confirmation, on the anvil and in clay forming | `OverrideAnvilGuard` |

Set the override to `true` in `ModConfig/patternbook.json` to use this picker anyway.
Overriding AnvilGuard means its prompt no longer appears. The check runs each time a
picker opens, and it looks for the other mod's patch rather than whether the mod is
installed. So Smithing Plus with its picker switched off does not make this mod step aside.

## Configuration

`ModConfig/patternbook.json`, written with defaults on first launch:

```json
{
  "OverrideSmithingPlus": false,
  "OverrideAnvilGuard": false,
  "ConfirmChoice": false
}
```

`ConfirmChoice` is the picker's switch; flipping it there writes it here.

With [ConfigKit](https://github.com/dizzyd/configkit) installed, the same three settings
are on its settings screen (**P**, or **Mod settings** in the pause menu). ConfigKit is
optional: Pattern Book registers with it by reflection when it is there and does nothing
when it is not. Every setting is marked `clientside`, so on a server running ConfigKit
they stay yours rather than being shown read-only or replaced by the server's.

## Building

```bash
export VINTAGE_STORY="$(ls -d ~/.cairn/games/1.22* | sort -V | tail -1)"
./build.sh
```

The zip lands in `Releases/`.

## Testing

`tests/` is a vstestkit suite and needs the client tier:

```bash
bash scripts/sync-linux.sh dizzyd@vsclient.home --mod ../patternbook/patternbook
ssh dizzyd@vsclient.home 'cd vstestkit-patternbook && bash scripts/run.sh mods/patternbook/tests --mod mods/patternbook/patternbook --client'
```

Some tests need the real Smithing Plus, AnvilGuard and ConfigKit, and skip or no-op
without them. Run `tests/fixtures/fetch.sh` on the box (it checks each download's
sha256), then add `--mods $PWD/mods/patternbook/tests/fixtures/Mods`. The path must be
absolute. Run the fetch after each `sync-linux.sh`, which clears the fixtures.

## Screenshots

```bash
bash scripts/screenshots.sh            # or: bash scripts/screenshots.sh user@host
```

This runs the `Gallery` tests on the test box and copies the PNGs into `screenshots/`
(gitignored):
- `anvil`: the full anvil list, with a recipe selected
- `anvil-search`: the anvil list searched for "head"
- `clayforming-search`: the clay forming list searched for "mold"
- `icon-full`, and `modicon.png` cut from it at 480×480 for the ModDB mod icon. The picker
  is given eight of the clay forming molds rather than all of them, so it is narrow enough
  to fit the square. The crop needs macOS's `sips`.

Each shot selects its recipe by pointing at it, then moves the mouse away, so the outline
and details show without the hover fill over them.

The scenes are set at midday on a fixed day, so repeat runs come out the same.

It refuses to run while another client is up on the box. Every slot shares one game
screenshots folder, so two clients taking pictures at once can swap them. `--force`
overrides the check. The shots are 960×600, the client window size in vstestkit's
`templates/clientsettings.json`.

## License

MIT License
