namespace patternbook;

public class PatternBookConfig
{
    public const string FileName = "patternbook.json";

    /// <summary>
    /// Use this picker even when Smithing Plus has replaced the vanilla one with its own.
    /// Off by default, so Smithing Plus's picker (and its column setting) keeps working.
    /// </summary>
    public bool OverrideSmithingPlus { get; set; } = false;

    /// <summary>
    /// Use this picker even when AnvilGuard is installed. AnvilGuard's confirmation prompt
    /// hooks the vanilla picker, so it does not appear in this one.
    /// </summary>
    public bool OverrideAnvilGuard { get; set; } = false;
}
