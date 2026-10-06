using System;
using System.Linq;
using System.Threading.Tasks;
using HarmonyLib;
using patternbook;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
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
            await Hover(dlg, "Iron pickaxe head");
            await Snap("anvil");
        }

        [VsTest(TimeoutMs = 60000)]
        public async Task AnvilSearch()
        {
            var dlg = await OpenAnvil();
            await Input.Type("head");
            await Hover(dlg, "Iron axe head");
            await Snap("anvil-search");
        }

        [VsTest(TimeoutMs = 60000)]
        public async Task ClayFormingSearch()
        {
            var dlg = await OpenClayForm();
            await Input.Type("mold");
            await Hover(dlg, "Raw blue clay pickaxe mold");
            await Snap("clayforming-search");
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

        /// <summary>Puts the mouse over the named recipe, so the picker shows its name and description.</summary>
        static async Task Hover(GuiDialogSearchableRecipeSelector dlg, string name)
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
        }

        static async Task Snap(string name)
        {
            await Frames.Wait(30);
            Log(await Shot.Take($"patternbook-gallery-{name}.png"));
        }
    }
}
