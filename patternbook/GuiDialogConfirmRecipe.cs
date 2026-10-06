using System;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace patternbook;

/// <summary>
/// "Make X?" with Cancel and Confirm, opened over the picker when confirming is switched on.
/// Unlike the vanilla GuiDialogConfirm it answers Enter and Escape itself, ahead of the
/// picker: the game's own Escape handling closes every open dialog, the picker included.
/// Closing it any way other than Confirm counts as cancelling.
/// </summary>
public class GuiDialogConfirmRecipe : GuiDialog
{
    readonly string text;
    readonly Action<bool> onAnswer;
    bool answered;

    public override double DrawOrder => 0.21;
    public override string ToggleKeyCombinationCode => null;

    public GuiDialogConfirmRecipe(ICoreClientAPI capi, string text, Action<bool> onAnswer) : base(capi)
    {
        this.text = text;
        this.onAnswer = onAnswer;
        Compose();
    }

    void Compose()
    {
        CairoFont font = CairoFont.WhiteSmallText();
        double textWidth = 350;
        double textHeight = new TextDrawUtil().GetMultilineTextHeight(font, text, GuiElement.scaled(textWidth)) / RuntimeEnv.GUIScale;

        ElementBounds textBounds = ElementBounds.Fixed(0, 30, textWidth, textHeight);
        ElementBounds cancelBounds = ElementBounds.Fixed(0, 30 + textHeight + 20, 0, 0).WithFixedPadding(8, 4);
        ElementBounds confirmBounds = ElementBounds.Fixed(0, 30 + textHeight + 20, 0, 0).WithFixedPadding(8, 4).WithAlignment(EnumDialogArea.RightFixed);

        ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
        bgBounds.BothSizing = ElementSizing.FitToChildren;

        SingleComposer = capi.Gui
            .CreateCompo("patternbook-confirm", ElementStdBounds.AutosizedMainDialog)
            .AddShadedDialogBG(bgBounds, true)
            .AddDialogTitleBar(Lang.Get("Please Confirm"), () => TryClose())
            .BeginChildElements(bgBounds)
                .AddStaticText(text, font, textBounds)
                .AddSmallButton(Lang.Get("Cancel"), () => Answer(false), cancelBounds)
                .AddSmallButton(Lang.Get("Confirm"), () => Answer(true), confirmBounds)
            .EndChildElements()
            .Compose();
    }

    bool Answer(bool confirmed)
    {
        if (answered) return true;
        answered = true;
        TryClose();
        onAnswer(confirmed);
        return true;
    }

    // Ahead of the GuiManager's Escape handling and the picker's own Enter
    public override bool CaptureAllInputs() => IsOpened();

    public override void OnKeyDown(KeyEvent args)
    {
        if (args.KeyCode == (int)GlKeys.Enter || args.KeyCode == (int)GlKeys.KeypadEnter)
        {
            args.Handled = true;
            Answer(true);
            return;
        }
        if (args.KeyCode == (int)GlKeys.Escape)
        {
            args.Handled = true;
            Answer(false);
            return;
        }
        base.OnKeyDown(args);
    }

    public override void OnGuiClosed()
    {
        base.OnGuiClosed();
        if (!answered)
        {
            answered = true;
            onAnswer(false);
        }
    }
}
