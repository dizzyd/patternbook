using System.ComponentModel;

namespace patternbook;

/// <summary>
/// ModConfig/patternbook.json. With ConfigKit installed these also appear on its settings
/// screen: the [Category] and [Description] attributes are the whole schema it reads, and are
/// plain BCL attributes, inert without it. Every setting is "clientside" because this is a
/// client-only mod - on a server running ConfigKit, anything else would be shown read-only
/// and overwritten by values from a server that has never heard of this mod.
/// </summary>
public class PatternBookConfig
{
    public const string FileName = "patternbook.json";

    /// <summary>
    /// Ask "Make X?" before taking a recipe. Set from the switch on the picker, and saved
    /// whenever it is flipped, so it holds across restarts.
    /// </summary>
    [Category("Picker, clientside")]
    [Description("Ask before taking a recipe. The same setting as the Confirm switch in the picker.")]
    public bool ConfirmChoice { get; set; } = false;

    /// <summary>
    /// Use this picker even when Smithing Plus has replaced the vanilla one with its own.
    /// Off by default, so Smithing Plus's picker (and its column setting) keeps working.
    /// </summary>
    [Category("Other mods, clientside")]
    [Description("Use Pattern Book's picker even when Smithing Plus is showing its own.")]
    public bool OverrideSmithingPlus { get; set; } = false;

    /// <summary>
    /// Use this picker even when AnvilGuard is installed. AnvilGuard's confirmation prompt
    /// hooks the vanilla picker, so it does not appear in this one.
    /// </summary>
    [Category("Other mods, clientside")]
    [Description("Use Pattern Book's picker even with AnvilGuard installed. AnvilGuard's prompt will not appear; use Confirm instead.")]
    public bool OverrideAnvilGuard { get; set; } = false;
}
