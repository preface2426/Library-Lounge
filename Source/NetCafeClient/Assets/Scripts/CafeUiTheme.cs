using UnityEngine;

// Shared visual tokens for the lobby UI. Keep presentation values here, not in game logic.
public static class CafeUiTheme
{
    // v0.1.2: warm paper, brown ink and fine brass.
    public static readonly Color Paper = new Color32(0xF3, 0xEF, 0xE8, 0xFF);
    public static readonly Color PaperDeep = new Color32(0xEA, 0xE2, 0xD6, 0xFF);
    public static readonly Color Ink = new Color32(0x30, 0x2A, 0x24, 0xFF);
    public static readonly Color InkSoft = new Color32(0x77, 0x6C, 0x5D, 0xFF);
    public static readonly Color Rail = PaperDeep;
    public static readonly Color RailDeep = new Color32(0xE4, 0xD9, 0xC7, 0xFF);
    public static readonly Color Accent = new Color32(0xBA, 0xA0, 0x75, 0xFF);
    public static readonly Color Amber = new Color32(0x9E, 0x7B, 0x43, 0xFF);
    public static readonly Color Line = new Color32(0xD5, 0xC7, 0xB1, 0xFF);
    public static readonly Color Danger = new Color32(0xA4, 0x38, 0x25, 0xFF);
    public static readonly Color Focus = Amber;
    public static readonly Color Scrim = new Color32(0x30, 0x2A, 0x24, 0x33);
    public static readonly Color RailText = Ink;
    public static readonly Color RailMuted = InkSoft;
    public static readonly Color Hover = new Color32(0xE4, 0xD9, 0xC7, 0xFF);
    public static readonly Color Pressed = new Color32(0xD5, 0xC1, 0xA0, 0xFF);
    public static readonly Color OnPrimary = Ink;
    public const float ReferenceWidth = 1600f;
    public const float ReferenceHeight = 900f;
    public const float MenuWideBreakpoint = 1.38f;
    public const float MenuRailWidth = 0f;
    public const float MenuRailStackedHeight = 0f;
    public const float MenuMaxWidth = 600f;
    public const float MenuMaxHeight = 780f;
    public const float ButtonHeight = 52f;
    public const float PrimaryButtonHeight = 56f;
}
