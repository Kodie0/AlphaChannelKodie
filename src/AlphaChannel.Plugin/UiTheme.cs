namespace AlphaChannel.Plugin;

internal enum UiTheme
{
    Purple = 0,
    Gold = 1,
    Green = 2,
    Red = 3,
}

internal readonly record struct ThemeColors(
    Vector4 Accent,
    Vector4 AccentHover,
    Vector4 AccentActive,
    Vector4 BlueGlow,
    Vector4 MagentaGlow,
    Vector4 Gold,
    Vector4 GoldHover,
    Vector4 FrameBg,
    Vector4 FrameBgHover,
    Vector4 Danger,
    Vector4 Good,
    Vector4 WindowBg,
    Vector4 SidebarBg,
    Vector4 CardBg,
    Vector4 CardBgHover,
    Vector4 MutedText);

internal static class ThemeCatalog
{
    private static Vector4 Hex(int rgb) => new(
        ((rgb >> 16) & 0xFF) / 255f,
        ((rgb >> 8) & 0xFF) / 255f,
        (rgb & 0xFF) / 255f,
        1f);

    internal static string Label(UiTheme theme) => theme switch
    {
        UiTheme.Purple => "Purple",
        UiTheme.Gold => "Gold",
        UiTheme.Green => "Green",
        UiTheme.Red => "Red",
        _ => theme.ToString(),
    };

    internal static ThemeColors Get(UiTheme theme) => theme switch
    {
        UiTheme.Gold => Gold,
        UiTheme.Green => Green,
        UiTheme.Red => Red,
        _ => Purple,
    };

    private static readonly ThemeColors Purple = new(
        Accent: Hex(0x8B5CF6),
        AccentHover: Hex(0xA78BFA),
        AccentActive: Hex(0x6D28D9),
        BlueGlow: Hex(0x22D3EE),
        MagentaGlow: Hex(0xE879F9),
        Gold: Hex(0xD4AF37),
        GoldHover: Hex(0xE8C547),
        FrameBg: Hex(0x151B2C),
        FrameBgHover: Hex(0x1C2438),
        Danger: Hex(0xEF4444),
        Good: Hex(0x22C55E),
        WindowBg: Hex(0x070A12),
        SidebarBg: Hex(0x0A0E1A),
        CardBg: Hex(0x121826),
        CardBgHover: Hex(0x1A2234),
        MutedText: Hex(0x8B93A7));

    private static readonly ThemeColors Gold = new(
        Accent: Hex(0xD4AF37),
        AccentHover: Hex(0xE4C363),
        AccentActive: Hex(0xB8942A),
        BlueGlow: Hex(0xE8D48A),
        MagentaGlow: Hex(0xD4AF37),
        Gold: Hex(0xF0D060),
        GoldHover: Hex(0xFFE08A),
        FrameBg: Hex(0x16140F),
        FrameBgHover: Hex(0x1F1C16),
        Danger: Hex(0xEF4444),
        Good: Hex(0x22C55E),
        WindowBg: Hex(0x0A0907),
        SidebarBg: Hex(0x100E0A),
        CardBg: Hex(0x16130E),
        CardBgHover: Hex(0x1F1B14),
        MutedText: Hex(0xA89F8A));

    private static readonly ThemeColors Green = new(
        Accent: Hex(0x34D399),
        AccentHover: Hex(0x6EE7B7),
        AccentActive: Hex(0x10B981),
        BlueGlow: Hex(0x5EEAD4),
        MagentaGlow: Hex(0x34D399),
        Gold: Hex(0xF5D78A),
        GoldHover: Hex(0xFFE9A8),
        FrameBg: Hex(0x121816),
        FrameBgHover: Hex(0x1A221E),
        Danger: Hex(0xEF4444),
        Good: Hex(0x4ADE80),
        WindowBg: Hex(0x080B0A),
        SidebarBg: Hex(0x0C1210),
        CardBg: Hex(0x121A17),
        CardBgHover: Hex(0x1A2420),
        MutedText: Hex(0x8FA399));

    private static readonly ThemeColors Red = new(
        Accent: Hex(0xE11D48),
        AccentHover: Hex(0xFB7185),
        AccentActive: Hex(0xBE123C),
        BlueGlow: Hex(0xF87171),
        MagentaGlow: Hex(0xE11D48),
        Gold: Hex(0xFBBF24),
        GoldHover: Hex(0xFCD34D),
        FrameBg: Hex(0x181414),
        FrameBgHover: Hex(0x221A1A),
        Danger: Hex(0xF87171),
        Good: Hex(0x22C55E),
        WindowBg: Hex(0x0A0A0A),
        SidebarBg: Hex(0x100C0C),
        CardBg: Hex(0x161212),
        CardBgHover: Hex(0x1F1818),
        MutedText: Hex(0xA3A3A3));
}
