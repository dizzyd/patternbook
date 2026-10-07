using System;
using System.Linq;
using System.Threading.Tasks;
using HarmonyLib;
using patternbook;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using VsTestkit.Testing;
using static VsTestkit.Testing.Vs;

namespace PatternBook.Tests
{
    /// <summary>
    /// Pictures, not assertions: the picker as a player sees it, for the ModDB page.
    /// Run with --filter Gallery, by scripts/screenshots.sh, which copies the PNGs back into
    /// screenshots/. Shots land in the game install directory as patternbook-gallery-*.png.
    /// </summary>
    [RequiresClient]
    public class Gallery
    {
        // Off to the side of the player's view at about 35 degrees and 9 blocks out, which clears
        // the picker in the middle of the frame at the default field of view.
        static BlockPos Stand => P(8, 1, 2);
        static BlockPos Prop => P(13, 1, 9);
        static BlockPos PropBeside => P(14, 1, 10);
        // An empty clay form renders as nothing, so it goes out of view and the clay shot gets
        // pottery to look at instead.
        static BlockPos ClayForm => P(3, 1, 0);

        PatternBookConfig savedConfig;

        [BeforeEach]
        public void UseOurPicker()
        {
            var sys = PatternBookModSystem.Instance;
            savedConfig = sys.Config;
            sys.Config = new PatternBookConfig { OverrideSmithingPlus = true, OverrideAnvilGuard = true };
        }

        [AfterEach]
        public void RestoreConfig()
        {
            PatternBookModSystem.Instance.Config = savedConfig;
        }

        [VsTest(TimeoutMs = 60000)]
        public async Task AnvilPicker()
        {
            var dlg = await OpenAnvil();
            await Select(dlg, "Iron pickaxe head");
            await Snap("anvil");
        }

        [VsTest(TimeoutMs = 60000)]
        public async Task AnvilSearch()
        {
            var dlg = await OpenAnvil();
            await Input.Type("head");
            await Select(dlg, "Iron axe head");
            await Snap("anvil-search");
        }

        [VsTest(TimeoutMs = 60000)]
        public async Task ClayFormingSearch()
        {
            var dlg = await OpenClayForm();
            await Input.Type("mold");
            await Select(dlg, "Raw blue clay pickaxe mold");
            await Snap("clayforming-search");
        }

        /// <summary>
        /// The ModDB mod icon, cropped to 480x480 by scripts/screenshots.sh using the bounds this
        /// logs. A real clay forming picker, but given eight of its mold recipes rather than all of
        /// them: eight columns make it narrow enough to fit the square, where the full list's ten
        /// do not at this window size.
        /// </summary>
        [VsTest(TimeoutMs = 60000)]
        public async Task Icon()
        {
            await SetScene(
                ("game:toolmold-blue-raw-pickaxe", Prop),
                ("game:storagevessel-blue-raw", PropBeside));

            await OnClient();
            string[] molds = ["anvil", "axe", "blade-falx", "hammer", "hoe", "pickaxe", "prospectingpick", "shovel"];
            var stacks = molds
                .Select(m => Capi.World.GetBlock(new AssetLocation($"game:toolmold-blue-raw-{m}")))
                .Where(b => b != null && b.Id != 0)
                .Select(b => new ItemStack(b))
                .ToArray();
            Assert.Equal(molds.Length, stacks.Length, "every mold resolved");

            var dlg = new GuiDialogSearchableRecipeSelector(Lang.Get("Select recipe"), stacks, _ => { }, () => { }, Prop, Capi);

            // The clay each takes, by vanilla's own formula in BlockEntityClayForm.OpenDialog
            var clay = Capi.World.GetItem(new AssetLocation("game:clay-blue"));
            var recipes = Capi.GetClayformingRecipes();
            for (int i = 0; i < stacks.Length; i++)
            {
                var recipe = recipes.First(r => r.Output.ResolvedItemstack?.Collectible.Code == stacks[i].Collectible.Code);
                int voxels = recipe.Voxels.Cast<bool>().Count(v => v);
                dlg.SetIngredientCounts(i, [new ItemStack(clay, (int)Math.Ceiling(Math.Max(1, (voxels - 64) / 25f)))]);
            }

            dlg.TryOpen();
            await Frames.Wait(5);
            await Input.Type("mold");
            await Select(dlg, "Raw blue clay pickaxe mold");
            await Frames.Wait(30);

            await OnClient();
            var b = dlg.SingleComposer.Bounds;
            Log($"icon-bounds {(int)b.absX} {(int)b.absY} {(int)b.OuterWidth} {(int)b.OuterHeight} frame {Capi.Render.FrameWidth} {Capi.Render.FrameHeight}");
            Log(await Shot.Take("patternbook-gallery-icon-full.png"));

            await OnClient();
            dlg.TryClose();
        }

        /// <summary>Midday, with what is being worked on in view beside where the picker opens.</summary>
        static async Task SetScene(params (string code, BlockPos pos)[] props)
        {
            await World.SetCalendarTo(500 * 24 + 12);
            foreach (var (code, pos) in props) World.SetBlock(code, pos);
            await Player.Teleport(Stand);
            await Ticks(5);
            // Straight down the middle between the two, tilted a little down to lift them clear of the hotbar
            await Interact.LookAt(new Vec3d(Stand.X + 0.5, Stand.Y - 2, Stand.Z + 30));

            // The minimap HUD sits in the top corner and says nothing about this mod
            await OnClient();
            foreach (var hud in Capi.OpenedGuis.OfType<GuiDialog>().Where(d => d.GetType().Name.Contains("WorldMap")).ToArray())
            {
                hud.TryClose();
            }
            await OnServer();
        }

        static async Task<GuiDialogSearchableRecipeSelector> OpenAnvil()
        {
            await SetScene(("game:anvil-iron", Prop));
            await OnClient();
            var anvil = (BlockEntityAnvil)Capi.World.BlockAccessor.GetBlockEntity(Prop);
            var ingot = new ItemStack(Capi.World.GetItem(new AssetLocation("game:ingot-iron")));
            AccessTools.Method(typeof(BlockEntityAnvil), "OpenDialog").Invoke(anvil, [ingot]);
            return await Gui.WaitFor<GuiDialogSearchableRecipeSelector>(120);
        }

        static async Task<GuiDialogSearchableRecipeSelector> OpenClayForm()
        {
            await SetScene(
                ("game:clayform", ClayForm),
                ("game:toolmold-blue-raw-pickaxe", Prop),
                ("game:storagevessel-blue-raw", PropBeside));
            await OnClient();
            var form = (BlockEntityClayForm)Capi.World.BlockAccessor.GetBlockEntity(ClayForm);
            var clay = new ItemStack(Capi.World.GetItem(new AssetLocation("game:clay-blue")));
            form.OpenDialog(Capi.World, ClayForm, clay);
            return await Gui.WaitFor<GuiDialogSearchableRecipeSelector>(120);
        }

        /// <summary>
        /// Selects the named recipe the way a player does, by pointing at it, then moves the mouse
        /// off the picker: the selection outline and the details stay, without the hover fill
        /// drawn over them.
        /// </summary>
        static async Task Select(GuiDialogSearchableRecipeSelector dlg, string name)
        {
            await Frames.Wait(5);
            await OnClient();

            int index = dlg.VisibleNames.ToList().IndexOf(name);
            Assert.True(index >= 0, $"'{name}' is in the picker: {string.Join(", ", dlg.VisibleNames)}");

            var grid = dlg.SingleComposer.GetElement("grid");
            int cols = (int)Math.Round(grid.Bounds.fixedWidth / GuiElementScrollingSkillGrid.UnscaledCellSize);
            double cell = GuiElement.scaled(GuiElementScrollingSkillGrid.UnscaledCellSize);
            double x = grid.Bounds.absX + (index % cols + 0.5) * cell;
            double y = grid.Bounds.absY + (index / cols + 0.5) * cell;
            await Input.MouseMove((int)x, (int)y);
            await Frames.Wait(5);

            await OnClient();
            var selected = ((GuiElementScrollingSkillGrid)dlg.SingleComposer.GetElement("grid")).SelectedIndex;
            Assert.Equal(index, selected, $"'{name}' is selected");
            await Input.MouseMove(5, 5);
        }

        static async Task Snap(string name)
        {
            await Frames.Wait(30);
            Log(await Shot.Take($"patternbook-gallery-{name}.png"));
        }
    }
}
