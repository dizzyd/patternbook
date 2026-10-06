using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace patternbook;

public class PatternBookModSystem : ModSystem
{
    public const string HarmonyId = "com.dizzyd.patternbook";

    // Other mods' Harmony ids, and the vanilla picker methods whose patching means that mod's
    // picker behaviour is live.
    const string SmithingPlusHarmonyId = "smithingplus";
    const string AnvilGuardHarmonyId = "anvilguard.recipeconfirm";

    public static PatternBookModSystem Instance { get; private set; }

    public PatternBookConfig Config { get; set; }

    ICoreClientAPI capi;
    Harmony harmony;
    readonly HashSet<string> loggedStepAside = new();

    // The picker is client-only, and patching from StartClientSide rather than Start
    // keeps singleplayer from registering the patch once per side.
    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

    public override void StartClientSide(ICoreClientAPI api)
    {
        capi = api;
        Instance = this;
        Config = LoadConfig(api);

        RecipeSelectorSwap.Logger = api.Logger;
        harmony = new Harmony(HarmonyId);
        harmony.PatchAll();
    }

    static PatternBookConfig LoadConfig(ICoreAPI api)
    {
        PatternBookConfig config = null;
        try
        {
            config = api.LoadModConfig<PatternBookConfig>(PatternBookConfig.FileName);
        }
        catch (Exception e)
        {
            api.Logger.Error("[patternbook] Could not read {0}, using defaults: {1}", PatternBookConfig.FileName, e.Message);
            return new PatternBookConfig();
        }

        config ??= new PatternBookConfig();
        api.StoreModConfig(config, PatternBookConfig.FileName);
        return config;
    }

    public void SaveConfig()
    {
        try
        {
            capi.StoreModConfig(Config, PatternBookConfig.FileName);
        }
        catch (Exception e)
        {
            capi.Logger.Error("[patternbook] Could not write {0}: {1}", PatternBookConfig.FileName, e.Message);
        }
    }

    /// <summary>
    /// The mod whose own picker behaviour should win over this one, or null to use this one.
    /// Asked each time a picker opens, so load order between the mods does not matter.
    /// </summary>
    public string StepAsideFor()
    {
        string other = null;
        if (!Config.OverrideSmithingPlus && IsPatchedBy(AccessTools.Constructor(typeof(GuiDialogBlockEntityRecipeSelector), RecipeSelectorSwap.CtorArgs), SmithingPlusHarmonyId))
        {
            other = "Smithing Plus";
        }
        else if (!Config.OverrideAnvilGuard && IsPatchedBy(AccessTools.Method(typeof(GuiDialogBlockEntityRecipeSelector), "OnSlotClick"), AnvilGuardHarmonyId))
        {
            other = "AnvilGuard";
        }

        if (other != null && loggedStepAside.Add(other))
        {
            capi.Logger.Notification("[patternbook] {0} is handling the recipe picker; set its override in ModConfig/{1} to use Pattern Book's instead.", other, PatternBookConfig.FileName);
        }
        return other;
    }

    static bool IsPatchedBy(System.Reflection.MethodBase method, string harmonyId)
    {
        var info = method == null ? null : Harmony.GetPatchInfo(method);
        return info != null && info.Owners.Contains(harmonyId);
    }

    public override void Dispose()
    {
        harmony?.UnpatchAll(HarmonyId);
        if (Instance == this) Instance = null;
    }
}
