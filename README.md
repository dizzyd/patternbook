# Pattern Book

Replaces the recipe picker on the smithing anvil and in clay forming with one that is
easier to use when there are a lot of recipes:

- **Sorted** alphabetically by name.
- **Searchable.** The search box has focus as soon as the picker opens. Every word you
  type has to appear in the name, so `iron plate` finds "Iron plate".
- **Enter** picks the first match once you have typed something.
- **Scrolls.** The grid shows at most 8×6 recipes and scrolls the rest, where vanilla
  keeps growing until the picker runs off the screen.

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
  "OverrideAnvilGuard": false
}
```

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

Two tests need the real Smithing Plus and AnvilGuard and skip without them. Run
`tests/fixtures/fetch.sh` (it checks each download's sha256), then add
`--mods $PWD/mods/patternbook/tests/fixtures/Mods`. The path must be absolute.

## Screenshots

```bash
bash scripts/screenshots.sh            # or: bash scripts/screenshots.sh user@host
```

This runs the `Gallery` tests on the test box and copies the PNGs into `screenshots/`
(gitignored):
- `anvil`: the full anvil list, with a recipe hovered
- `anvil-search`: the anvil list searched for "head"
- `clayforming-search`: the clay forming list searched for "mold"

The scenes are set at midday on a fixed day, so repeat runs come out the same.

It refuses to run while another client is up on the box. Every slot shares one game
screenshots folder, so two clients taking pictures at once can swap them. `--force`
overrides the check. The shots are 960×600, the client window size in vstestkit's
`templates/clientsettings.json`.

## License

MIT License
