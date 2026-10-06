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

    // ConfigKit takes registrations up to AssetsLoaded, which comes before StartClientSide
    public override void StartPre(ICoreAPI api)
    {
        Config = LoadConfig(api);
        RegisterWithConfigKit(api);
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        capi = api;
        Instance = this;

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

    const string ConfigKitSystem = "ConfigKit.ConfigKitModSystem";
    const string ConfigKitRegister = "RegisterManagedConfig";
    const string ConfigKitGetConfig = "GetConfig";

    /// <summary>Whether ConfigKit is installed and holds the config. Asserted in a test.</summary>
    public static bool ConfigKitBound { get; private set; }

    /// <summary>
    /// Hands <see cref="Config"/> to ConfigKit if it is installed, for its settings screen.
    /// Bound by reflection, as fornax and crucibulum do, so ConfigKit is optional to build
    /// against as well as to run: the surface is one method taking BCL types. ConfigKit fills
    /// in this same object, so the picker sees its changes without a callback, and it reloads
    /// the file when the picker's switch writes it.
    /// </summary>
    void RegisterWithConfigKit(ICoreAPI api)
    {
        ConfigKitBound = false;
        var system = api.ModLoader.GetModSystem(ConfigKitSystem);
        if (system == null) return;   // not installed, which is the ordinary case

        var register = system.GetType().GetMethod(ConfigKitRegister);
        if (register == null)
        {
            api.Logger.Warning("[patternbook] ConfigKit is installed but has no {0}; Pattern Book's settings will not appear in it.", ConfigKitRegister);
            return;
        }

        try
        {
            register.Invoke(system, ["patternbook", Config, PatternBookConfig.FileName, null, null, null]);

            // A refusal (ConfigKit standing down for configlib, say) logs and returns rather
            // than throwing, so ask whether it took the config rather than assume it did
            ConfigKitBound = system.GetType().GetMethod(ConfigKitGetConfig)?.Invoke(system, ["patternbook"]) != null;
        }
        catch (Exception e)
        {
            // Reflection wraps the real failure in a TargetInvocationException that says nothing
            api.Logger.Warning("[patternbook] Could not hand the config to ConfigKit: {0}",
                (e as System.Reflection.TargetInvocationException)?.InnerException ?? e);
        }
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
