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
    public const int MaxCols = 10;
    public const int MaxVisibleRows = 8;

    /// <summary>Unscaled height of everything but the grid: title bar, padding, search row, details.</summary>
    const double NonGridHeight = 250;
    const double NonGridWidth = 80;
    const int MinVisibleRows = 3;

    const double GridTop = 70;
    const double ScrollbarWidth = 20;
    const double ScrollbarGap = 5;

    record Entry(int Index, SkillItem Item, string SearchText);

    readonly BlockPos blockEntityPos;
    readonly Action<int> onSelectedRecipe;
    readonly Action onCancelSelect;

    // One SkillItem per recipe, in the caller's order, so SetIngredientCounts and the
    // selection callback can keep using the caller's indices.
    readonly List<SkillItem> skillItems = new();
    readonly List<Entry> sorted;
    List<Entry> filtered;

    bool didSelect;
    GuiDialogConfirmRecipe confirmDialog;

    // Fixed when the picker opens, from the full list: the width never changes, so the search
    // box stays put, and the height only shrinks as a search narrows the list.
    int cols;
    int maxVisibleRows;
    double innerWidth;
    double? dialogTop;

    // What the current composition was built for, so a search recomposes only when it must
    int composedRows = -1;
    bool composedScrollbar;
    bool recomposing;

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

        // As big as MaxCols x MaxVisibleRows, but no bigger than 90% of the screen allows
        double screenW = capi.Render.FrameWidth / GuiElement.scaled(1);
        double screenH = capi.Render.FrameHeight / GuiElement.scaled(1);
        int fitCols = Math.Max(1, (int)((screenW * 0.9 - NonGridWidth) / cell));
        int fitRows = Math.Max(MinVisibleRows, (int)((screenH * 0.9 - NonGridHeight) / cell));

        cols = Math.Clamp(skillItems.Count, 1, Math.Min(MaxCols, fitCols));
        maxVisibleRows = Math.Min(MaxVisibleRows, fitRows);

        // Room for the scrollbar only if the full list will ever need one
        bool everScrolls = RowsFor(skillItems.Count) > maxVisibleRows;
        innerWidth = Math.Max(400, cols * cell + (everScrolls ? ScrollbarGap + ScrollbarWidth : 0));

        ApplyFilter();
    }

    /// <summary>
    /// The unscaled top for a line of text whose capitals are centred on <paramref name="centreY"/>.
    /// Text is drawn with its baseline one ascent below the top of its bounds, so centring the
    /// bounds would leave the letters sitting high; this lines them up with a control's middle.
    /// </summary>
    static double TextTopCentredOn(CairoFont font, double centreY)
    {
        double ascent = font.GetFontExtents().Ascent / GuiElement.scaled(1);
        double capHeight = font.UnscaledFontsize * 0.7;
        return centreY + capHeight / 2 - ascent;
    }

    int RowsFor(int count) => Math.Max(1, (count + cols - 1) / cols);

    /// <summary>
    /// Builds the dialog for a grid of the given height, keeping whatever has been typed.
    /// Only the height and whether there is a scrollbar ever differ between compositions.
    /// </summary>
    void Compose(int visibleRows, bool withScrollbar)
    {
        double cell = GuiElementScrollingSkillGrid.UnscaledCellSize;
        double gridWidth = cols * cell;
        double gridHeight = visibleRows * cell;

        // Centre the grid, with its scrollbar if it has one, in the dialog's width
        double blockWidth = gridWidth + (withScrollbar ? ScrollbarGap + ScrollbarWidth : 0);
        double gridLeft = Math.Floor((innerWidth - blockWidth) / 2);

        // Search row: [search box][12 of 60][switch Confirm]
        double confirmWidth = 100;
        double countWidth = 90;
        double searchWidth = innerWidth - countWidth - confirmWidth - 20;
        ElementBounds searchBounds = ElementBounds.Fixed(0, 30, searchWidth, 30);
        double rowTop = 30, rowHeight = 30;
        CairoFont rowFont = CairoFont.WhiteDetailText();
        double rowTextTop = TextTopCentredOn(rowFont, rowTop + rowHeight / 2);
        ElementBounds countBounds = ElementBounds.Fixed(searchWidth + 10, rowTextTop, countWidth, rowHeight);
        ElementBounds switchBounds = ElementBounds.Fixed(innerWidth - confirmWidth, rowTop, rowHeight, rowHeight);
        ElementBounds switchLabelBounds = ElementBounds.Fixed(innerWidth - confirmWidth + 36, rowTextTop, confirmWidth - 36, rowHeight);
        ElementBounds switchHoverBounds = ElementBounds.Fixed(innerWidth - confirmWidth, rowTop, confirmWidth, rowHeight);

        ElementBounds gridBounds = ElementBounds.Fixed(gridLeft, GridTop, gridWidth, gridHeight);
        ElementBounds scrollbarBounds = ElementBounds.Fixed(gridLeft + gridWidth + ScrollbarGap, GridTop, ScrollbarWidth, gridHeight);

        // Details: the name with what it takes on the right, and the description under them
        // The name's width is set per recipe in ShowDetails, to whatever the material leaves it
        ElementBounds nameBounds = ElementBounds.Fixed(0, GridTop + gridHeight + 12, innerWidth, 30);
        ElementBounds requiresBounds = ElementBounds.Fixed(0, nameBounds.fixedY + 5, innerWidth, 25);
        ElementBounds descBounds = ElementBounds.Fixed(0, nameBounds.fixedY + 32, innerWidth, 40);

        ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
        bgBounds.BothSizing = ElementSizing.FitToChildren;

        // Centred on the first composition, then held at that top edge so a shrinking
        // dialog does not move the search box out from under the player
        ElementBounds dialogBounds = dialogTop is double top
            ? ElementStdBounds.AutosizedMainDialogAtPos(top)
            : ElementStdBounds.AutosizedMainDialog;

        var grid = new GuiElementScrollingSkillGrid(capi, cols, visibleRows, gridBounds)
        {
            OnSlotClick = OnSlotClick,
            OnSlotOver = OnSlotOver
        };

        string searchText = SingleComposer?.GetTextInput("search")?.GetText() ?? "";
        bool hadFocus = SingleComposer != null && IsOpened();

        // The search box reports its text while being built, and again when it is given back
        // what was typed; neither is a new search
        recomposing = true;
        var composer = capi.Gui
            .CreateCompo("patternbook-recipeselect" + blockEntityPos, dialogBounds)
            .AddShadedDialogBG(bgBounds, true)
            .AddDialogTitleBar(DialogTitle, () => TryClose())
            .BeginChildElements(bgBounds)
                .AddTextInput(searchBounds, OnSearchChanged, CairoFont.WhiteSmallishText(), "search")
                .AddDynamicText("", rowFont.Clone().WithOrientation(EnumTextOrientation.Right), countBounds, "count")
                .AddSwitch(OnConfirmToggled, switchBounds, "confirm")
                .AddStaticText(Lang.Get("patternbook:confirm-toggle"), rowFont, switchLabelBounds)
                .AddHoverText(Lang.Get("patternbook:confirm-hover"), CairoFont.WhiteDetailText(), 250, switchHoverBounds)
                .AddInteractiveElement(grid, "grid");
        if (withScrollbar)
        {
            composer.AddVerticalScrollbar(OnScroll, scrollbarBounds, "scrollbar");
        }
        SingleComposer = composer
                .AddDynamicText("", CairoFont.WhiteSmallishText(), nameBounds, "name")
                .AddDynamicText("", CairoFont.WhiteDetailText().WithOrientation(EnumTextOrientation.Right), requiresBounds, "requires")
                .AddDynamicText("", CairoFont.WhiteDetailText(), descBounds, "desc")
            .EndChildElements()
            .Compose();

        dialogTop ??= SingleComposer.Bounds.absY / GuiElement.scaled(1);
        composedRows = visibleRows;
        composedScrollbar = withScrollbar;

        SingleComposer.GetSwitch("confirm").SetValue(PatternBookModSystem.Instance?.Config.ConfirmChoice ?? false);
        grid.Scrollbar = SingleComposer.GetScrollbar("scrollbar");

        var search = SingleComposer.GetTextInput("search");
        search.SetPlaceHolderText(Lang.Get("patternbook:search-placeholder"));
        if (searchText.Length > 0) search.SetValue(searchText);
        recomposing = false;
        if (hadFocus) SingleComposer.FocusElement(search.TabIndex);
    }

    GuiElementScrollingSkillGrid Grid => (GuiElementScrollingSkillGrid)SingleComposer.GetElement("grid");

    void OnSearchChanged(string text)
    {
        if (recomposing) return;

        string[] terms = (text ?? "").ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        filtered = terms.Length == 0
            ? sorted
            : sorted.Where(e => terms.All(t => e.SearchText.Contains(t))).ToList();

        if (SingleComposer?.Composed == true) ApplyFilter();
    }

    void ApplyFilter()
    {
        // As tall as the matches need, up to the cap; a scrollbar only if they overflow it
        int rows = RowsFor(filtered.Count);
        int visibleRows = Math.Min(rows, maxVisibleRows);
        bool withScrollbar = rows > visibleRows;
        if (visibleRows != composedRows || withScrollbar != composedScrollbar)
        {
            Compose(visibleRows, withScrollbar);
        }

        var grid = Grid;
        grid.SetItems(filtered.Select(e => e.Item).ToList());

        if (SingleComposer.GetScrollbar("scrollbar") is { } scrollbar)
        {
            double cell = GuiElementScrollingSkillGrid.UnscaledCellSize;
            scrollbar.SetHeights((float)grid.Bounds.fixedHeight, (float)(grid.Rows * cell));
            scrollbar.SetScrollbarPosition(0);
        }

        SingleComposer.GetDynamicText("count").SetNewText(Lang.Get("patternbook:match-count", filtered.Count, sorted.Count));

        // Start on the first match once something is typed. Not on an empty search, where
        // Enter would pick whatever sorts first.
        grid.SelectedIndex = filtered.Count > 0 && filtered != sorted ? 0 : -1;
        if (filtered.Count == 0)
        {
            ShowDetails(Lang.Get("patternbook:no-matches"), "", "");
        }
        else if (grid.SelectedIndex >= 0)
        {
            ShowItem(grid.SelectedIndex);
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

    void ShowDetails(string name, string desc, string requires)
    {
        // The material is right-aligned across the whole row; the name gets the rest, so a
        // long name in a narrow picker is not cut off to make room for a fixed-width column
        double requiresWidth = requires.Length == 0 ? 0
            : CairoFont.WhiteDetailText().GetTextExtents(requires).Width / GuiElement.scaled(1) + 15;
        var nameText = SingleComposer.GetDynamicText("name");
        nameText.Bounds.fixedWidth = innerWidth - requiresWidth;
        nameText.SetNewText(name, forceRedraw: true);
        SingleComposer.GetDynamicText("desc").SetNewText(desc);
        SingleComposer.GetDynamicText("requires").SetNewText(requires);
    }

    void OnSlotOver(int num)
    {
        // The hovered recipe becomes the selected one, and stays so after the mouse moves off,
        // so the outline always marks the recipe the details describe and Enter picks
        var grid = Grid;
        if (num >= filtered.Count || num == grid.SelectedIndex) return;
        grid.SelectedIndex = num;
        ShowItem(num);
    }

    void ShowItem(int num)
    {
        SkillItem item = filtered[num].Item;
        string requires = "";
        if (item.Data is ItemStack[] ingredients)
        {
            requires = Lang.Get("patternbook:ingredient-count", ingredients[0].StackSize, ingredients[0].GetName());
        }
        ShowDetails(item.Name, item.Description, requires);
    }

    void OnSlotClick(int num)
    {
        if (num >= filtered.Count) return;
        Choose(num);
    }

    void OnConfirmToggled(bool on)
    {
        // Clicking the switch takes focus; give it back so typing still searches
        SingleComposer.FocusElement(SingleComposer.GetTextInput("search").TabIndex);

        var mod = PatternBookModSystem.Instance;
        if (mod == null) return;
        mod.Config.ConfirmChoice = on;
        mod.SaveConfig();
    }

    /// <summary>Takes the recipe at a grid position, asking first if confirming is switched on.</summary>
    void Choose(int num)
    {
        if (confirmDialog?.IsOpened() == true) return;

        Entry entry = filtered[num];
        if (!SingleComposer.GetSwitch("confirm").On)
        {
            Select(entry.Index);
            return;
        }

        confirmDialog = new GuiDialogConfirmRecipe(capi, Lang.Get("patternbook:confirm-recipe", entry.Item.Name), confirmed =>
        {
            confirmDialog = null;
            if (confirmed)
            {
                Select(entry.Index);
            }
            else if (IsOpened())
            {
                // Back to typing where the player left off
                Focus();
                SingleComposer.FocusElement(SingleComposer.GetTextInput("search").TabIndex);
            }
        });
        confirmDialog.TryOpen();
    }

    // Nothing behind the confirmation is clickable while it is up
    public override bool ShouldReceiveMouseEvents() => confirmDialog?.IsOpened() != true && base.ShouldReceiveMouseEvents();

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
        // Enter takes the selected recipe - the first match after typing, so "type a few
        // letters, hit enter" picks a recipe - or whichever was hovered since
        int selected = Grid.SelectedIndex;
        if ((args.KeyCode == (int)GlKeys.Enter || args.KeyCode == (int)GlKeys.KeypadEnter)
            && selected >= 0 && selected < filtered.Count)
        {
            args.Handled = true;
            Choose(selected);
            return;
        }

        base.OnKeyDown(args);
    }

    public override void OnGuiClosed()
    {
        base.OnGuiClosed();
        confirmDialog?.TryClose();

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
