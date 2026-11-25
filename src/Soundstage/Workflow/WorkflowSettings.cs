// No Dalamud types in this file: the offline self-check compiles it as a shared file.
namespace Soundstage.Workflow;

/// <summary>Settings for the Workflow tab (capture, booth, contact sheet, onion skin), kept in <see cref="Configuration.Sections"/> under <see cref="Key"/>.</summary>
public sealed class WorkflowSettings
{
    public const string Key = "workflow";

    /// <summary>Where captures go. Empty means Documents\Soundstage\Captures.</summary>
    public string OutputFolder { get; set; } = "";

    /// <summary>How long each route gets before the next one is tried.</summary>
    public int TimeoutSeconds { get; set; } = 8;

    /// <summary>Route ids in the order they are tried: "addon", "key", "window".</summary>
    public List<string> RouteOrder { get; set; } = new() { "addon", "key", "window" };

    /// <summary>The virtual-key code sent for the "key" route. 0x2C is Print Screen.</summary>
    public int ScreenshotKey { get; set; } = 0x2C;

    /// <summary>The game's screenshot folder for the "key" route. Empty: ReShade's SavePath, then the game's default folder.</summary>
    public string ScreenshotFolder { get; set; } = "";

    /// <summary>Send Scroll Lock to hide the game's UI around a capture, and again to bring it back.</summary>
    public bool HideGameUi { get; set; } = true;

    public int BoothSeconds { get; set; } = 5;

    /// <summary>Milliseconds to wait after hiding things so the hidden frame is the one captured.</summary>
    public int HideSettleMs { get; set; } = 250;

    /// <summary>Scene ids picked for the contact sheet, in order, at most <see cref="MaxSheetScenes"/>.</summary>
    public List<string> SheetSceneIds { get; set; } = new();
    public const int MaxSheetScenes = 12;
    public int SheetColumns { get; set; } = 3;
    public int SheetCellWidth { get; set; } = 480;

    /// <summary>Milliseconds to let the stage settle after a Scene is applied, before its capture. Starts at 400; tune it by looking at a sheet.</summary>
    public int SettleMs { get; set; } = 400;

    public OnionSettings Onion { get; set; } = new();
}

/// <summary>The onion-skin overlay. Only the path is stored; the image itself is read from where it is and never copied.</summary>
public sealed class OnionSettings
{
    public bool On { get; set; }
    public string ImagePath { get; set; } = "";
    public float Opacity { get; set; } = 0.4f;
    public float Scale { get; set; } = 1f;
    public float OffsetX { get; set; }
    public float OffsetY { get; set; }
    public bool FlipHorizontal { get; set; }
    public bool EdgeOnly { get; set; }
}
