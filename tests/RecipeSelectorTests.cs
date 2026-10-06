using System;
using System.Linq;
using System.Threading.Tasks;
using patternbook;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.GameContent;
using VsTestkit.Testing;
using static VsTestkit.Testing.Vs;

namespace PatternBook.Tests
{
    [RequiresClient]
    public class RecipeSelectorTests
    {
        static readonly System.Reflection.MethodInfo OpenDialogMethod =
            AccessTools.Method(typeof(BlockEntityAnvil), "OpenDialog");

        /// <summary>Puts an anvil in the plot and opens its picker for an iron ingot, as a click with a hot ingot would.</summary>
        PatternBookConfig savedConfig;

        // These tests are about patternbook's picker, so make sure it is the one that opens even
        // when Smithing Plus or AnvilGuard is loaded alongside. The step-aside tests below set
        // the overrides they need.
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

        static async Task<(BlockEntityAnvil anvil, GuiDialogSearchableRecipeSelector dlg)> OpenPicker()
        {
            var anvil = await OpenAnvilDialog();
            var dlg = await Gui.WaitFor<GuiDialogSearchableRecipeSelector>(120);
            return (anvil, dlg);
        }

        /// <summary>Opens the anvil's picker without assuming whose picker it will be.</summary>
        static async Task<BlockEntityAnvil> OpenAnvilDialog()
        {
            World.SetBlock("game:anvil-iron", P(4, 1, 4));
            await Ticks(4);
            await Player.StandNear(P(4, 1, 4));

            await OnClient();
            var anvil = (BlockEntityAnvil)Capi.World.BlockAccessor.GetBlockEntity(P(4, 1, 4));
            Assert.NotNull(anvil, "client-side anvil block entity");

            var ingot = new ItemStack(Capi.World.GetItem(new AssetLocation("game:ingot-iron")));
            OpenDialogMethod.Invoke(anvil, [ingot]);
            return anvil;
        }

        static readonly System.Reflection.MethodInfo ClayOpenDialogMethod =
            AccessTools.Method(typeof(BlockEntityClayForm), nameof(BlockEntityClayForm.OpenDialog));

        [VsTest]
        public async Task PatchesAreRegisteredOnce()
        {
            foreach (var method in new[] { OpenDialogMethod, ClayOpenDialogMethod })
            {
                var info = Harmony.GetPatchInfo(method);
                Assert.NotNull(info, method.DeclaringType.Name + ".OpenDialog is patched");
                Assert.Equal(1, info.Transpilers.Count(p => p.owner == PatternBookModSystem.HarmonyId), method.DeclaringType.Name + " transpilers from patternbook");
            }
            await Task.CompletedTask;
        }

        static async Task<(BlockEntityClayForm form, GuiDialogSearchableRecipeSelector dlg)> OpenClayPicker()
        {
            var form = await OpenClayDialog();
            var dlg = await Gui.WaitFor<GuiDialogSearchableRecipeSelector>(120);
            return (form, dlg);
        }

        static async Task<BlockEntityClayForm> OpenClayDialog()
        {
            World.SetBlock("game:clayform", P(6, 1, 6));
            await Ticks(4);
            await Player.StandNear(P(6, 1, 6));

            await OnClient();
            var form = (BlockEntityClayForm)Capi.World.BlockAccessor.GetBlockEntity(P(6, 1, 6));
            Assert.NotNull(form, "client-side clay form block entity");

            var clay = new ItemStack(Capi.World.GetItem(new AssetLocation("game:clay-blue")));
            form.OpenDialog(Capi.World, P(6, 1, 6), clay);
            return form;
        }

        static readonly System.Reflection.ConstructorInfo VanillaCtor =
            AccessTools.Constructor(typeof(GuiDialogBlockEntityRecipeSelector), RecipeSelectorSwap.CtorArgs);
        static readonly System.Reflection.MethodInfo VanillaOnSlotClick =
            AccessTools.Method(typeof(GuiDialogBlockEntityRecipeSelector), "OnSlotClick");

        public static void FakeSmithingPlusPostfix() { }
        public static bool FakeAnvilGuardPrefix() => true;

        [VsTest(TimeoutMs = 60000)]
        public async Task StepsAsideForSmithingPlusUnlessOverridden()
        {
            var config = PatternBookModSystem.Instance.Config;
            var fake = new HarmonyMethod(typeof(RecipeSelectorTests).GetMethod(nameof(FakeSmithingPlusPostfix)));
            var harmony = new Harmony("smithingplus");
            harmony.Patch(VanillaCtor, postfix: fake);
            try
            {
                config.OverrideSmithingPlus = false;
                await OpenAnvilDialog();
                await Gui.WaitFor<GuiDialogBlockEntityRecipeSelector>(120);
                Assert.False(await Gui.IsOpen<GuiDialogSearchableRecipeSelector>(), "steps aside for Smithing Plus");
                await Gui.CloseDialogs();

                config.OverrideSmithingPlus = true;
                await OpenAnvilDialog();
                await Gui.WaitFor<GuiDialogSearchableRecipeSelector>(120);
                Assert.False(await Gui.IsOpen<GuiDialogBlockEntityRecipeSelector>(), "the override takes over from Smithing Plus");
            }
            finally
            {
                // Only the fake: a real Smithing Plus may be loaded under the same id
                harmony.Unpatch(VanillaCtor, fake.method);
            }
        }

        [VsTest(TimeoutMs = 60000)]
        public async Task StepsAsideForAnvilGuardUnlessOverridden()
        {
            var config = PatternBookModSystem.Instance.Config;
            var fake = new HarmonyMethod(typeof(RecipeSelectorTests).GetMethod(nameof(FakeAnvilGuardPrefix)));
            var harmony = new Harmony("anvilguard.recipeconfirm");
            harmony.Patch(VanillaOnSlotClick, prefix: fake);
            try
            {
                // AnvilGuard's prompt hooks the shared vanilla picker, so clay forming too
                config.OverrideAnvilGuard = false;
                await OpenClayDialog();
                await Gui.WaitFor<GuiDialogBlockEntityRecipeSelector>(120);
                Assert.False(await Gui.IsOpen<GuiDialogSearchableRecipeSelector>(), "steps aside for AnvilGuard");
                await Gui.CloseDialogs();

                config.OverrideAnvilGuard = true;
                await OpenClayDialog();
                await Gui.WaitFor<GuiDialogSearchableRecipeSelector>(120);
                Assert.False(await Gui.IsOpen<GuiDialogBlockEntityRecipeSelector>(), "the override takes over from AnvilGuard");
            }
            finally
            {
                harmony.Unpatch(VanillaOnSlotClick, fake.method);
            }
        }

        // The two below need the real mods: --mods with smithingplus and anvilguard in it.

        [VsTest(TimeoutMs = 60000)]
        public async Task RealSmithingPlusKeepsItsPicker()
        {
            if (!Capi.ModLoader.IsModEnabled("smithingplus")) Skip("Smithing Plus is not loaded");
            PatternBookModSystem.Instance.Config.OverrideSmithingPlus = false;

            await OpenAnvilDialog();
            await Gui.WaitFor<GuiDialogBlockEntityRecipeSelector>(120);
            Assert.False(await Gui.IsOpen<GuiDialogSearchableRecipeSelector>(), "Smithing Plus's picker, not ours");
            Log(await Shot.Take("patternbook-smithingplus.png"));
        }

        [VsTest(TimeoutMs = 60000)]
        public async Task RealAnvilGuardStillPrompts()
        {
            if (!Capi.ModLoader.IsModEnabled("anvilguard")) Skip("AnvilGuard is not loaded");
            PatternBookModSystem.Instance.Config.OverrideAnvilGuard = false;

            await OpenAnvilDialog();
            var vanilla = await Gui.WaitFor<GuiDialogBlockEntityRecipeSelector>(120);

            await OnClient();
            VanillaOnSlotClick.Invoke(vanilla, [0]);
            await Gui.WaitFor<GuiDialogConfirm>(120);
            Log(await Shot.Take("patternbook-anvilguard.png"));
        }

        [VsTest(TimeoutMs = 60000)]
        public async Task ClayFormingOpensTheSearchablePickerSortedByName()
        {
            var (_, dlg) = await OpenClayPicker();

            Assert.False(await Gui.IsOpen<GuiDialogBlockEntityRecipeSelector>(), "vanilla picker should not open");

            var names = dlg.VisibleNames.ToList();
            Log($"{names.Count} clay forming recipes: " + string.Join(", ", names));
            Assert.Greater(names.Count, 1, "blue clay has recipes");

            var expected = names.OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList();
            Assert.True(names.SequenceEqual(expected), "names are in alphabetical order");

            Log(await Shot.Take("patternbook-clay.png"));
        }

        [VsTest(TimeoutMs = 60000)]
        public async Task ClayFormingEnterPicksTheFirstMatch()
        {
            var (form, dlg) = await OpenClayPicker();

            await Input.Type("bowl");
            await Frames.Wait(5);
            var names = dlg.VisibleNames.ToList();
            Log("matches: " + string.Join(", ", names));
            Assert.Greater(names.Count, 0, "something matches 'bowl'");
            Assert.True(names.All(n => n.Contains("bowl", StringComparison.OrdinalIgnoreCase)), "every match contains the search text");

            await Input.Press(GlKeys.Enter);
            await Gui.WaitGone<GuiDialogSearchableRecipeSelector>(120);

            await OnClient();
            var recipe = Traverse.Create(form).Field<ClayFormingRecipe>("selectedRecipe").Value;
            Assert.NotNull(recipe, "the clay form took the selection");
            Assert.Equal(names[0], recipe.Output.ResolvedItemstack.GetName(), "selected recipe");
        }

        /// <summary>Stands in for another mod's prefix that opens its own picker and skips the original.</summary>
        public static bool OtherModPrefix() => false;

        [VsTest(TimeoutMs = 60000)]
        public async Task StandsAsideWhenAnotherPrefixTookOver()
        {
            var other = new Harmony("patternbook.tests.othermod");
            other.Patch(OpenDialogMethod, prefix: new HarmonyMethod(typeof(RecipeSelectorTests).GetMethod(nameof(OtherModPrefix))) { priority = Priority.Normal });
            try
            {
                World.SetBlock("game:anvil-iron", P(4, 1, 4));
                await Ticks(4);
                await Player.StandNear(P(4, 1, 4));

                await OnClient();
                var anvil = (BlockEntityAnvil)Capi.World.BlockAccessor.GetBlockEntity(P(4, 1, 4));
                var ingot = new ItemStack(Capi.World.GetItem(new AssetLocation("game:ingot-iron")));
                OpenDialogMethod.Invoke(anvil, [ingot]);
                await Frames.Wait(10);

                Assert.False(await Gui.IsOpen<GuiDialogSearchableRecipeSelector>(), "patternbook's picker should not open");
                Assert.False(await Gui.IsOpen<GuiDialogBlockEntityRecipeSelector>(), "nor vanilla's");
            }
            finally
            {
                other.UnpatchAll("patternbook.tests.othermod");
            }
        }

        [VsTest(TimeoutMs = 60000)]
        public async Task AnvilOpensTheSearchablePickerSortedByName()
        {
            var (_, dlg) = await OpenPicker();

            Assert.False(await Gui.IsOpen<GuiDialogBlockEntityRecipeSelector>(), "vanilla picker should not open");

            var names = dlg.VisibleNames.ToList();
            Log($"{names.Count} recipes for an iron ingot");
            Assert.Greater(names.Count, 1, "an iron ingot has recipes");

            var expected = names.OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList();
            Assert.True(names.SequenceEqual(expected), "names are in alphabetical order");

            Log(await Shot.Take("patternbook-unfiltered.png"));
        }

        [VsTest(TimeoutMs = 60000)]
        public async Task TypingFiltersTheGrid()
        {
            var (_, dlg) = await OpenPicker();
            int total = dlg.RecipeCount;

            await Input.Type("hammer");
            await Frames.Wait(5);

            var names = dlg.VisibleNames.ToList();
            Log("matches: " + string.Join(", ", names));
            Assert.Greater(names.Count, 0, "something matches 'hammer'");
            Assert.Less(names.Count, total, "filter narrows the list");
            Assert.True(names.All(n => n.Contains("hammer", StringComparison.OrdinalIgnoreCase)), "every match contains the search text");

            Log(await Shot.Take("patternbook-filtered.png"));
        }

        [VsTest(TimeoutMs = 60000)]
        public async Task MultipleWordsMustAllMatch()
        {
            var (_, dlg) = await OpenPicker();

            await Input.Type("iron plate");
            await Frames.Wait(5);

            var names = dlg.VisibleNames.ToList();
            Log("matches: " + string.Join(", ", names));
            Assert.Greater(names.Count, 0, "something matches 'iron plate'");
            Assert.True(names.All(n => n.Contains("iron", StringComparison.OrdinalIgnoreCase)
                                    && n.Contains("plate", StringComparison.OrdinalIgnoreCase)), "every match has both words");
        }

        [VsTest(TimeoutMs = 60000)]
        public async Task NoMatchesShowsAnEmptyGrid()
        {
            var (_, dlg) = await OpenPicker();

            await Input.Type("zzzznotarecipe");
            await Frames.Wait(5);

            Assert.Equal(0, dlg.VisibleNames.Count(), "no matches");
            Log(await Shot.Take("patternbook-nomatch.png"));
        }

        [VsTest(TimeoutMs = 60000)]
        public async Task TheFirstMatchIsHighlighted()
        {
            // The box's mouse rests mid-screen, over the grid: a resting mouse must not select
            var (_, dlg) = await OpenPicker();
            await Frames.Wait(5);
            var grid = (GuiElementScrollingSkillGrid)dlg.SingleComposer.GetElement("grid");
            Assert.Equal(-1, grid.SelectedIndex, "nothing highlighted before typing");

            await Input.Type("hammer");
            await Frames.Wait(5);
            grid = (GuiElementScrollingSkillGrid)dlg.SingleComposer.GetElement("grid");
            Assert.Equal(0, grid.SelectedIndex, "first match highlighted, mouse or no mouse");
            Log(await Shot.Take("patternbook-highlighted.png"));

            await Input.Type("zzzz");
            await Frames.Wait(5);
            grid = (GuiElementScrollingSkillGrid)dlg.SingleComposer.GetElement("grid");
            Assert.Equal(-1, grid.SelectedIndex, "nothing highlighted without a match");
        }

        [VsTest(TimeoutMs = 60000)]
        public async Task EnterPicksTheFirstMatch()
        {
            var (anvil, dlg) = await OpenPicker();

            await Input.Type("hammer");
            await Frames.Wait(5);
            string first = dlg.VisibleNames.First();

            await Input.Press(GlKeys.Enter);
            await Gui.WaitGone<GuiDialogSearchableRecipeSelector>(120);

            await OnClient();
            // Read the client's own copy straight away: the server turns the selection
            // down, as there is no work item on this anvil, and resets it.
            var recipe = Capi.GetSmithingRecipes().First(r => r.RecipeId == anvil.SelectedRecipeId);
            Assert.Equal(first, recipe.Output.ResolvedItemstack.GetName(), "selected recipe");
        }

        [VsTest(TimeoutMs = 60000)]
        public async Task ConfirmAsksBeforeChoosingAndIsSaved()
        {
            var mod = PatternBookModSystem.Instance;
            try
            {
                var (anvil, dlg) = await OpenPicker();
                await OnClient();
                dlg.SingleComposer.GetSwitch("confirm").OnMouseDownOnElement(Capi, new MouseEvent(0, 0, EnumMouseButton.Left, 0));
                Assert.True(mod.Config.ConfirmChoice, "the switch turns confirming on");
                var saved = Capi.LoadModConfig<PatternBookConfig>(PatternBookConfig.FileName);
                Assert.True(saved.ConfirmChoice, "and it is written to the config file");

                await Input.Type("hammer");
                await Frames.Wait(5);
                string first = dlg.VisibleNames.First();

                // Escape cancels the confirmation only, leaving the picker up
                await Input.Press(GlKeys.Enter);
                await Gui.WaitFor<GuiDialogConfirmRecipe>(120);
                Log(await Shot.Take("patternbook-confirm.png"));
                await Input.Press(GlKeys.Escape);
                await Gui.WaitGone<GuiDialogConfirmRecipe>(120);
                Assert.True(await Gui.IsOpen<GuiDialogSearchableRecipeSelector>(), "picker still open after cancelling");

                // Enter twice: once to pick, once to confirm
                await Input.Press(GlKeys.Enter);
                await Gui.WaitFor<GuiDialogConfirmRecipe>(120);
                await Input.Press(GlKeys.Enter);
                await Gui.WaitGone<GuiDialogSearchableRecipeSelector>(120);

                await OnClient();
                var recipe = Capi.GetSmithingRecipes().First(r => r.RecipeId == anvil.SelectedRecipeId);
                Assert.Equal(first, recipe.Output.ResolvedItemstack.GetName(), "confirmed recipe");

                await Gui.CloseDialogs();
                await OnServer();
                var (_, again) = await OpenPicker();
                Assert.True(again.SingleComposer.GetSwitch("confirm").On, "the switch stays on the next time the picker opens");
            }
            finally
            {
                // Put the real config back on disk; mod.Config is this test's stand-in
                await OnClient();
                mod.Config.ConfirmChoice = false;
                Capi.StoreModConfig(savedConfig, PatternBookConfig.FileName);
                await Gui.CloseDialogs();
            }
        }

        [VsTest(TimeoutMs = 60000)]
        public async Task LongListsScrollInsteadOfGrowing()
        {
            await Player.StandNear(P(4, 1, 4));
            await OnClient();

            // Vanilla has only a few dozen recipes per metal; a modded game is where the list
            // gets long, so build one rather than depend on a content mod.
            var stacks = Capi.World.Items
                .Where(i => i.Code != null && i.Code.Path.StartsWith("ingot-"))
                .Concat(Capi.World.Items.Where(i => i.Code != null && i.Code.Path.StartsWith("metalplate-")))
                .Concat(Capi.World.Items.Where(i => i.Code != null && i.Code.Path.StartsWith("nugget-")))
                .Concat(Capi.World.Items.Where(i => i.Code != null && i.Code.Path.StartsWith("metalbit-")))
                .Concat(Capi.World.Items.Where(i => i.Code != null && i.Code.Path.StartsWith("metalchain-")))
                .Select(i => new ItemStack(i))
                .Take(150)
                .ToArray();
            Log($"{stacks.Length} entries");
            Assert.Greater(stacks.Length, GuiDialogSearchableRecipeSelector.MaxCols * GuiDialogSearchableRecipeSelector.MaxVisibleRows, "more entries than the grid shows");

            int selected = -1;
            var dlg = new GuiDialogSearchableRecipeSelector("Scroll test", stacks, i => selected = i, () => { }, P(4, 1, 4), Capi);
            dlg.TryOpen();
            await Frames.Wait(5);

            var grid = dlg.SingleComposer.GetElement("grid");
            // The cap shrinks to fit a small screen, so assert the bound rather than the exact count
            Assert.LessOrEqual(grid.Bounds.fixedHeight, GuiDialogSearchableRecipeSelector.MaxVisibleRows * GuiElementScrollingSkillGrid.UnscaledCellSize + 0.01, "grid is capped");
            Assert.LessOrEqual(dlg.SingleComposer.Bounds.OuterHeight, (double)Capi.Render.FrameHeight, "dialog fits on the screen");

            var scrollbar = dlg.SingleComposer.GetScrollbar("scrollbar");
            Assert.Close(0f, scrollbar.CurrentYPosition, 0.01f, "starts at the top");
            Log(await Shot.Take("patternbook-long.png"));

            await OnClient();
            scrollbar.OnMouseWheel(Capi, new MouseWheelEventArgs { delta = -1, deltaPrecise = -1 });
            await Frames.Wait(5);
            Assert.Greater(scrollbar.CurrentYPosition, 0f, "wheel scrolls down");
            Log(await Shot.Take("patternbook-scrolled.png"));

            // A narrow search shrinks the dialog to the matches, keeping its top edge
            await OnClient();
            double cell = GuiElementScrollingSkillGrid.UnscaledCellSize;
            double widthBefore = dlg.SingleComposer.Bounds.OuterWidth;
            double topBefore = dlg.SingleComposer.Bounds.absY;
            await Input.Type("copper");
            await Frames.Wait(5);

            await OnClient();
            int matches = dlg.VisibleNames.Count();
            var shrunk = dlg.SingleComposer.GetElement("grid");
            int cols = (int)Math.Round(shrunk.Bounds.fixedWidth / cell);
            Log($"{matches} matches in {cols} columns");
            Assert.Greater(matches, 0, "something matches 'copper'");
            Assert.Close((matches + cols - 1) / cols * cell, shrunk.Bounds.fixedHeight, 0.01, "grid is as tall as its matches");
            Assert.True(dlg.SingleComposer.GetScrollbar("scrollbar") == null, "no scrollbar when nothing overflows");
            Assert.Close(widthBefore, dlg.SingleComposer.Bounds.OuterWidth, 0.5, "width does not change");
            Assert.Close(topBefore, dlg.SingleComposer.Bounds.absY, 1.0, "top edge stays put");
            Assert.Equal("copper", dlg.SingleComposer.GetTextInput("search").GetText(), "search text survives the recompose");
            Log(await Shot.Take("patternbook-shrunk.png"));

            await OnClient();
            dlg.TryClose();
            dlg.Dispose();
        }

        [VsTest(TimeoutMs = 60000)]
        public async Task EscapeCancels()
        {
            await OpenPicker();

            await Input.Press(GlKeys.Escape);
            await Gui.WaitGone<GuiDialogSearchableRecipeSelector>(120);
        }
    }
}
