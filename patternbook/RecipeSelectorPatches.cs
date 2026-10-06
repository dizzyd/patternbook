using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace patternbook;

/// <summary>
/// Makes a block entity's OpenDialog get its recipe picker from <see cref="Create"/> where it
/// constructed the vanilla GuiDialogBlockEntityRecipeSelector. Create takes the same arguments,
/// so only the constructor and the SetIngredientCounts calls change; everything else the method
/// does - the recipe list, the callbacks, the block entity's own state - stays vanilla's.
///
/// A mod that replaces OpenDialog with a prefix of its own (smithscanvas does) skips the
/// rewritten body along with the rest of the original, so the two never both open a picker.
/// </summary>
public static class RecipeSelectorSwap
{
    static readonly Type VanillaType = typeof(GuiDialogBlockEntityRecipeSelector);

    public static readonly Type[] CtorArgs = [typeof(string), typeof(ItemStack[]), typeof(Action<int>), typeof(Action), typeof(BlockPos), typeof(ICoreClientAPI)];
    static readonly ConstructorInfo VanillaCtor = AccessTools.Constructor(VanillaType, CtorArgs);
    static readonly MethodInfo VanillaSetCounts = AccessTools.Method(VanillaType, nameof(GuiDialogBlockEntityRecipeSelector.SetIngredientCounts));
    static readonly MethodInfo CreateMethod = AccessTools.Method(typeof(RecipeSelectorSwap), nameof(Create));
    static readonly MethodInfo SetCountsMethod = AccessTools.Method(typeof(RecipeSelectorSwap), nameof(SetIngredientCounts));

    /// <summary>Set before patching; a transpiler has no API of its own to log through.</summary>
    public static ILogger Logger;

    /// <summary>
    /// Stands in for the vanilla constructor. Builds the vanilla picker when a mod that relies
    /// on it (Smithing Plus, AnvilGuard) is active and not overridden in the config.
    /// </summary>
    public static GuiDialog Create(string title, ItemStack[] recipeOutputs, Action<int> onSelectedRecipe, Action onCancelSelect, BlockPos pos, ICoreClientAPI capi)
    {
        if (PatternBookModSystem.Instance?.StepAsideFor() != null)
        {
            return new GuiDialogBlockEntityRecipeSelector(title, recipeOutputs, onSelectedRecipe, onCancelSelect, pos, capi);
        }
        return new GuiDialogSearchableRecipeSelector(title, recipeOutputs, onSelectedRecipe, onCancelSelect, pos, capi);
    }

    /// <summary>Stands in for <c>(dlg as GuiDialogBlockEntityRecipeSelector).SetIngredientCounts(...)</c>.</summary>
    public static void SetIngredientCounts(GuiDialog dlg, int num, ItemStack[] ingredStacks)
    {
        switch (dlg)
        {
            case GuiDialogSearchableRecipeSelector ours: ours.SetIngredientCounts(num, ingredStacks); break;
            case GuiDialogBlockEntityRecipeSelector vanilla: vanilla.SetIngredientCounts(num, ingredStacks); break;
        }
    }

    public static IEnumerable<CodeInstruction> Swap(IEnumerable<CodeInstruction> instructions, MethodBase original)
    {
        var codes = instructions.ToList();
        var swapped = codes.Select(c => new CodeInstruction(c)).ToList();

        int ctors = 0;
        foreach (var code in swapped)
        {
            if (code.opcode == OpCodes.Newobj && Equals(code.operand, VanillaCtor))
            {
                code.opcode = OpCodes.Call;
                code.operand = CreateMethod;
                ctors++;
            }
            else if (code.opcode == OpCodes.Isinst && Equals(code.operand, VanillaType))
            {
                // The dialog stays typed as GuiDialog; SetIngredientCounts sorts out which it is
                code.opcode = OpCodes.Nop;
                code.operand = null;
            }
            else if (code.Calls(VanillaSetCounts))
            {
                code.opcode = OpCodes.Call;
                code.operand = SetCountsMethod;
            }
        }

        // Anything left referring to the vanilla dialog would be handed ours and fail
        bool leftovers = swapped.Any(c => Equals(c.operand, VanillaType) || (c.operand is MethodBase m && m.DeclaringType == VanillaType));

        if (ctors != 1 || leftovers)
        {
            Logger?.Warning("[patternbook] {0}.{1} does not build its recipe picker the way 1.22 does ({2} constructor calls{3}); leaving the vanilla picker in place.",
                original.DeclaringType?.Name, original.Name, ctors, leftovers ? ", other uses of the vanilla dialog" : "");
            return codes;
        }

        return swapped;
    }
}

[HarmonyPatch(typeof(BlockEntityAnvil), "OpenDialog")]
static class AnvilOpenDialogPatch
{
    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original) =>
        RecipeSelectorSwap.Swap(instructions, original);
}

[HarmonyPatch(typeof(BlockEntityClayForm), nameof(BlockEntityClayForm.OpenDialog))]
static class ClayFormOpenDialogPatch
{
    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original) =>
        RecipeSelectorSwap.Swap(instructions, original);
}
