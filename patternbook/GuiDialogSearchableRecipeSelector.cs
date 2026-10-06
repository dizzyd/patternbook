using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace patternbook;

/// <summary>
/// Drop-in replacement for the vanilla GuiDialogBlockEntityRecipeSelector: the same callbacks,
/// keyed by the recipe's index in the list the caller passed in, but sorted by name, filtered
/// by a search box and scrolled rather than sized to fit.
/// </summary>
public class GuiDialogSearchableRecipeSelector : GuiDialogGeneric
{
    const int MaxCols = 8;
    const int MaxVisibleRows = 6;

    record Entry(int Index, SkillItem Item, string SearchText);

    readonly BlockPos blockEntityPos;
    readonly Action<int> onSelectedRecipe;
    readonly Action onCancelSelect;

    // One SkillItem per recipe, in the caller's order, so SetIngredientCounts and the
    // selection callback can keep using the caller's indices.
    readonly List<SkillItem> skillItems = new();
    readonly List<Entry> sorted;
    List<Entry> filtered;

    int prevSlotOver = -1;
    bool didSelect;

    public GuiDialogSearchableRecipeSelector(string dialogTitle, ItemStack[] recipeOutputs, Action<int> onSelectedRecipe, Action onCancelSelect, BlockPos blockEntityPos, ICoreClientAPI capi) : base(dialogTitle, capi)
    {
        this.blockEntityPos = blockEntityPos;
        this.onSelectedRecipe = onSelectedRecipe;
        this.onCancelSelect = onCancelSelect;

        double size = GuiElementScrollingSkillGrid.UnscaledCellSize;

        foreach (ItemStack stack in recipeOutputs)
        {
            ItemSlot dummySlot = new DummySlot(stack);

            string key = GetCraftDescKey(stack);
            string desc = Lang.GetMatching(key);
            if (desc == key) desc = "";

            skillItems.Add(new SkillItem
            {
                Code = stack.Collectible.Code.Clone(),
                Name = stack.GetName(),
                Description = desc,
                RenderHandler = (code, dt, posX, posY) =>
                {
                    // Same placement as vanilla's selector
                    double scsize = GuiElement.scaled(size - 5);
                    capi.Render.RenderItemstackToGui(dummySlot, posX + scsize / 2, posY + scsize / 2, 100, (float)GuiElement.scaled(GuiElementPassiveItemSlot.unscaledItemSize), ColorUtil.WhiteArgb);
                }
            });
        }

        sorted = skillItems
            .Select((item, i) => new Entry(i, item, item.Name.ToLowerInvariant()))
            .OrderBy(e => e.Item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(e => e.Index)
            .ToList();
        filtered = sorted;

        SetupDialog();
    }

    static string GetCraftDescKey(ItemStack stack)
    {
        return stack.Collectible.Code?.Domain + AssetLocation.LocationSeparator + stack.Class.Name() + "craftdesc-" + stack.Collectible.Code?.Path;
    }

    /// <summary>Names of the recipes the grid currently shows, in display order.</summary>
    public IEnumerable<string> VisibleNames => filtered.Select(e => e.Item.Name);

    public int RecipeCount => sorted.Count;

    public void SetIngredientCounts(int num, ItemStack[] ingredStacks)
    {
        skillItems[num].Data = ingredStacks;
    }

    void SetupDialog()
    {
        double cell = GuiElementScrollingSkillGrid.UnscaledCellSize;
        int cols = Math.Clamp(skillItems.Count, 1, MaxCols);
        int rows = Math.Max(1, (skillItems.Count + cols - 1) / cols);
        int visibleRows = Math.Min(rows, MaxVisibleRows);

        double gridWidth = cols * cell;
        double scrollbarWidth = 20;
        double innerWidth = Math.Max(300, gridWidth + 5 + scrollbarWidth);
        double countWidth = 80;

        ElementBounds searchBounds = ElementBounds.Fixed(0, 30, innerWidth - countWidth - 10, 30);
        ElementBounds countBounds = ElementBounds.Fixed(innerWidth - countWidth, 36, countWidth, 25);
        ElementBounds gridBounds = ElementBounds.Fixed(0, 70, gridWidth, visibleRows * cell);
        ElementBounds scrollbarBounds = ElementBounds.Fixed(gridWidth + 5, 70, scrollbarWidth, visibleRows * cell);
        ElementBounds nameBounds = ElementBounds.Fixed(0, 70 + visibleRows * cell + 15, innerWidth, 33);
        ElementBounds descBounds = nameBounds.BelowCopy(0, 10, 0, 0);
        ElementBounds ingredientBounds = descBounds.BelowCopy(0, 20, 0, 0);

        ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
        bgBounds.BothSizing = ElementSizing.FitToChildren;

        var grid = new GuiElementScrollingSkillGrid(capi, cols, visibleRows, gridBounds)
        {
            OnSlotClick = OnSlotClick,
            OnSlotOver = OnSlotOver
        };

        SingleComposer = capi.Gui
            .CreateCompo("patternbook-recipeselect" + blockEntityPos, ElementStdBounds.AutosizedMainDialog)
            .AddShadedDialogBG(bgBounds, true)
            .AddDialogTitleBar(DialogTitle, () => TryClose())
            .BeginChildElements(bgBounds)
                .AddTextInput(searchBounds, OnSearchChanged, CairoFont.WhiteSmallishText(), "search")
                .AddDynamicText("", CairoFont.WhiteDetailText().WithOrientation(EnumTextOrientation.Right), countBounds, "count")
                .AddInteractiveElement(grid, "grid")
                .AddVerticalScrollbar(OnScroll, scrollbarBounds, "scrollbar")
                .AddDynamicText("", CairoFont.WhiteSmallishText(), nameBounds, "name")
                .AddDynamicText("", CairoFont.WhiteDetailText(), descBounds, "desc")
                .AddDynamicText("", CairoFont.WhiteDetailText(), ingredientBounds, "ingredient")
            .EndChildElements()
            .Compose();

        grid.Scrollbar = SingleComposer.GetScrollbar("scrollbar");
        SingleComposer.GetTextInput("search").SetPlaceHolderText(Lang.Get("patternbook:search-placeholder"));

        ApplyFilter();
    }

    GuiElementScrollingSkillGrid Grid => (GuiElementScrollingSkillGrid)SingleComposer.GetElement("grid");

    void OnSearchChanged(string text)
    {
        string[] terms = (text ?? "").ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        filtered = terms.Length == 0
            ? sorted
            : sorted.Where(e => terms.All(t => e.SearchText.Contains(t))).ToList();

        // Called once during Compose, before the grid exists to receive it
        if (SingleComposer?.Composed == true) ApplyFilter();
    }

    void ApplyFilter()
    {
        var grid = Grid;
        grid.SetItems(filtered.Select(e => e.Item).ToList());

        double cell = GuiElementScrollingSkillGrid.UnscaledCellSize;
        var scrollbar = SingleComposer.GetScrollbar("scrollbar");
        scrollbar.SetHeights((float)grid.Bounds.fixedHeight, (float)Math.Max(grid.Rows * cell, grid.Bounds.fixedHeight));
        scrollbar.SetScrollbarPosition(0);

        SingleComposer.GetDynamicText("count").SetNewText(Lang.Get("patternbook:match-count", filtered.Count, sorted.Count));

        prevSlotOver = -1;
        if (filtered.Count == 0)
        {
            ShowDetails(Lang.Get("patternbook:no-matches"), "", "");
        }
        else
        {
            ShowDetails("", "", "");
        }
    }

    void OnScroll(float value)
    {
        Grid.SetScrollY(value);
    }

    void ShowDetails(string name, string desc, string ingredient)
    {
        SingleComposer.GetDynamicText("name").SetNewText(name);
        SingleComposer.GetDynamicText("desc").SetNewText(desc);
        SingleComposer.GetDynamicText("ingredient").SetNewText(ingredient);
    }

    void OnSlotOver(int num)
    {
        if (num >= filtered.Count || num == prevSlotOver) return;
        prevSlotOver = num;

        SkillItem item = filtered[num].Item;
        string requires = "";
        if (item.Data is ItemStack[] ingredients)
        {
            requires = Lang.Get("recipeselector-requiredcount", ingredients[0].StackSize, ingredients[0].GetName().ToLower());
        }
        ShowDetails(item.Name, item.Description, requires);
    }

    void OnSlotClick(int num)
    {
        if (num >= filtered.Count) return;
        Select(filtered[num].Index);
    }

    void Select(int recipeIndex)
    {
        onSelectedRecipe(recipeIndex);
        didSelect = true;
        TryClose();
    }

    public override void OnGuiOpened()
    {
        base.OnGuiOpened();
        SingleComposer.FocusElement(SingleComposer.GetTextInput("search").TabIndex);
    }

    public override void OnKeyDown(KeyEvent args)
    {
        // Enter takes the first match, so "type a few letters, hit enter" picks a recipe.
        // Not on an empty search, where it would pick whatever sorts first.
        if ((args.KeyCode == (int)GlKeys.Enter || args.KeyCode == (int)GlKeys.KeypadEnter)
            && filtered.Count > 0 && filtered != sorted)
        {
            args.Handled = true;
            Select(filtered[0].Index);
            return;
        }

        base.OnKeyDown(args);
    }

    public override void OnGuiClosed()
    {
        base.OnGuiClosed();

        if (!didSelect)
        {
            onCancelSelect();
        }
    }

    // Floaty dialog for Immersive Mouse Mode, as in the vanilla selector.

    const double FloatyDialogPosition = 0.5;
    const double FloatyDialogAlign = 0.75;

    public override bool PrefersUngrabbedMouse => false;

    public override void OnRenderGUI(float deltaTime)
    {
        if (capi.Settings.Bool["immersiveMouseMode"])
        {
            Vec3d aboveHeadPos = new Vec3d(blockEntityPos.X + 0.5, blockEntityPos.Y + FloatyDialogPosition, blockEntityPos.Z + 0.5);
            Vec3d pos = MatrixToolsd.Project(aboveHeadPos, capi.Render.PerspectiveProjectionMat, capi.Render.PerspectiveViewMat, capi.Render.FrameWidth, capi.Render.FrameHeight);
            if (pos.Z < 0) return;

            SingleComposer.Bounds.Alignment = EnumDialogArea.None;
            SingleComposer.Bounds.fixedOffsetX = 0;
            SingleComposer.Bounds.fixedOffsetY = 0;
            SingleComposer.Bounds.absFixedX = pos.X - SingleComposer.Bounds.OuterWidth / 2;
            SingleComposer.Bounds.absFixedY = capi.Render.FrameHeight - pos.Y - SingleComposer.Bounds.OuterHeight * FloatyDialogAlign;
            SingleComposer.Bounds.absMarginX = 0;
            SingleComposer.Bounds.absMarginY = 0;
        }

        base.OnRenderGUI(deltaTime);
    }
}
