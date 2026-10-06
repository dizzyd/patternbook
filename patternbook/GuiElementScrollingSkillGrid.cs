using System;
using System.Collections.Generic;
using Cairo;
using Vintagestory.API.Client;

namespace patternbook;

/// <summary>
/// A skill item grid that shows a fixed number of rows and scrolls the rest, and whose
/// items can be swapped out without recomposing the dialog. The vanilla grid sizes itself
/// to fit every item, which is how the anvil's picker ends up taller than the screen.
/// </summary>
public class GuiElementScrollingSkillGrid : GuiElement
{
    public static double UnscaledCellSize => GuiElementPassiveItemSlot.unscaledSlotSize + GuiElementItemSlotGrid.unscaledSlotPadding;

    readonly int cols;
    List<SkillItem> items = new();
    double scrollY;

    // Where the mouse was when it last chose a slot; a mouse resting over the grid must
    // not keep overriding a selection made by typing
    int lastMouseX = int.MinValue, lastMouseY = int.MinValue;

    LoadedTexture slotTexture;
    LoadedTexture hoverTexture;
    LoadedTexture selectedTexture;

    /// <summary>Index into the current items drawn with a selection outline, or -1. The owner sets it.</summary>
    public int SelectedIndex = -1;

    public Action<int> OnSlotClick;
    public Action<int> OnSlotOver;
    public GuiElementScrollbar Scrollbar;

    public GuiElementScrollingSkillGrid(ICoreClientAPI capi, int cols, int visibleRows, ElementBounds bounds) : base(capi, bounds)
    {
        this.cols = cols;
        slotTexture = new LoadedTexture(capi);
        hoverTexture = new LoadedTexture(capi);
        selectedTexture = new LoadedTexture(capi);

        Bounds.fixedWidth = cols * UnscaledCellSize;
        Bounds.fixedHeight = visibleRows * UnscaledCellSize;
    }

    public int Rows => (items.Count + cols - 1) / cols;

    public void SetItems(List<SkillItem> items)
    {
        this.items = items;
        scrollY = 0;
        SelectedIndex = -1;
        lastMouseX = api.Input.MouseX;
        lastMouseY = api.Input.MouseY;
    }

    /// <param name="y">Offset in unscaled units, as the scrollbar reports it.</param>
    public void SetScrollY(float y)
    {
        scrollY = scaled(y);
    }

    public override void ComposeElements(Context ctx, ImageSurface surface)
    {
        Bounds.CalcWorldBounds();

        int w = (int)scaled(GuiElementPassiveItemSlot.unscaledSlotSize);
        int h = w;

        ImageSurface slotSurface = new ImageSurface(Format.Argb32, w, h);
        Context slotCtx = genContext(slotSurface);
        slotCtx.SetSourceRGBA(1, 1, 1, 0.2);
        RoundRectangle(slotCtx, 0, 0, w, h, GuiStyle.ElementBGRadius);
        slotCtx.Fill();
        EmbossRoundRectangleElement(slotCtx, 0, 0, w, h, true);
        generateTexture(slotSurface, ref slotTexture);
        slotCtx.Dispose();
        slotSurface.Dispose();

        ImageSurface hoverSurface = new ImageSurface(Format.Argb32, w - 2, h - 2);
        Context hoverCtx = genContext(hoverSurface);
        hoverCtx.SetSourceRGBA(1, 1, 1, 0.7);
        RoundRectangle(hoverCtx, 1, 1, w, h, GuiStyle.ElementBGRadius);
        hoverCtx.Fill();
        generateTexture(hoverSurface, ref hoverTexture);
        hoverCtx.Dispose();
        hoverSurface.Dispose();

        // An outline in the hotbar's active slot colour, so it reads as "selected" and stays
        // distinct from the hover fill when both land on the same slot
        ImageSurface selectedSurface = new ImageSurface(Format.Argb32, w, h);
        Context selectedCtx = genContext(selectedSurface);
        double line = scaled(2);
        selectedCtx.SetSourceRGBA(GuiStyle.ActiveSlotColor);
        selectedCtx.LineWidth = line;
        RoundRectangle(selectedCtx, line / 2, line / 2, w - line, h - line, GuiStyle.ElementBGRadius);
        selectedCtx.Stroke();
        generateTexture(selectedSurface, ref selectedTexture);
        selectedCtx.Dispose();
        selectedSurface.Dispose();
    }

    public override void RenderInteractiveElements(float deltaTime)
    {
        double cell = scaled(UnscaledCellSize);
        double slotSize = scaled(GuiElementPassiveItemSlot.unscaledSlotSize);

        bool mouseInside = Bounds.PointInside(api.Input.MouseX, api.Input.MouseY);
        int hovered = mouseInside ? IndexAt(api.Input.MouseX, api.Input.MouseY) : -1;

        int firstRow = (int)(scrollY / cell);
        int lastRow = (int)((scrollY + Bounds.InnerHeight) / cell);

        api.Render.PushScissor(Bounds, true);

        for (int row = firstRow; row <= lastRow; row++)
        {
            for (int col = 0; col < cols; col++)
            {
                int i = row * cols + col;
                if (i >= items.Count) break;

                double x = Bounds.renderX + col * cell;
                double y = Bounds.renderY + row * cell - scrollY;

                api.Render.Render2DTexturePremultipliedAlpha(slotTexture.TextureId, x, y, slotSize, slotSize);
                if (i == hovered)
                {
                    api.Render.Render2DTexture(hoverTexture.TextureId, (float)x, (float)y, (float)slotSize, (float)slotSize);
                }
                if (i == SelectedIndex)
                {
                    api.Render.Render2DTexturePremultipliedAlpha(selectedTexture.TextureId, x, y, slotSize, slotSize);
                }

                SkillItem item = items[i];
                item.RenderHandler?.Invoke(item.Code, deltaTime, x + 1, y + 1);
            }
        }

        api.Render.PopScissor();

        // Only a mouse that moves chooses a slot. One left resting where the grid happened to
        // put a recipe under it, after a search or a scroll, would otherwise take the
        // selection from the first match on the very next frame.
        int mouseX = api.Input.MouseX, mouseY = api.Input.MouseY;
        if (hovered >= 0 && (mouseX != lastMouseX || mouseY != lastMouseY))
        {
            lastMouseX = mouseX;
            lastMouseY = mouseY;
            OnSlotOver?.Invoke(hovered);
        }
    }

    /// <returns>The index into the current items under an absolute mouse position, or -1.</returns>
    int IndexAt(int mouseX, int mouseY)
    {
        double cell = scaled(UnscaledCellSize);
        double dx = mouseX - Bounds.absX;
        double dy = mouseY - Bounds.absY + scrollY;
        if (dx < 0 || dy < 0) return -1;

        int col = (int)(dx / cell);
        int row = (int)(dy / cell);
        if (col >= cols) return -1;

        int index = row * cols + col;
        return index < items.Count ? index : -1;
    }

    public override void OnMouseDownOnElement(ICoreClientAPI api, MouseEvent args)
    {
        base.OnMouseDownOnElement(api, args);

        int index = IndexAt(args.X, args.Y);
        if (index >= 0)
        {
            args.Handled = true;
            OnSlotClick?.Invoke(index);
        }
    }

    public override void OnMouseWheel(ICoreClientAPI api, MouseWheelEventArgs args)
    {
        if (!Bounds.PointInside(api.Input.MouseX, api.Input.MouseY)) return;
        Scrollbar?.OnMouseWheel(api, args);
    }

    public override void Dispose()
    {
        base.Dispose();
        slotTexture.Dispose();
        hoverTexture.Dispose();
        selectedTexture.Dispose();
    }
}
