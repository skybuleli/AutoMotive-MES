using MudBlazor;

namespace MesAdmin.Web.Themes;

/// <summary>
/// MudBlazor 双主题配置（T0.9）。
/// - 暗色主题（默认）：深邃工业风，薰衣草紫 #CBA6F7
/// - 亮色主题（车间终端）：洁净工业风，深紫 #8F6AAF
/// 全局圆角 12px，字体 Inter Tight
/// </summary>
public static class MesTheme
{
    /// <summary>暗色主题（默认，车间大屏用）</summary>
    public static readonly MudTheme DarkTheme = new()
    {
        PaletteLight = LightPalette,
        PaletteDark = DarkPalette,
        LayoutProperties = new LayoutProperties
        {
            DefaultBorderRadius = "12px",
        },
        Typography = new Typography
        {
            Default = new DefaultTypography
            {
                FontFamily = ["Inter Tight", "Helvetica", "Arial", "sans-serif"],
                FontSize = "0.875rem",
                FontWeight = "400",
                LineHeight = "1.5",
            },
            H1 = new H1Typography { FontFamily = ["Inter Tight"], FontWeight = "700" },
            H3 = new H3Typography { FontFamily = ["Inter Tight"], FontWeight = "700" },
            H5 = new H5Typography { FontFamily = ["Inter Tight"], FontWeight = "500" },
        },
    };

    private static readonly PaletteLight LightPalette = new()
    {
        Black = "#000000",
        White = "#FFFFFF",
        Primary = "#8F6AAF",
        PrimaryContrastText = "#FFFFFF",
        Secondary = "#9CA3AF",
        SecondaryContrastText = "#000000",
        Tertiary = "#6B7280",
        Info = "#3B82F6",
        Success = "#22C55E",
        Warning = "#F59E0B",
        Error = "#EF4444",
        Dark = "#1F2937",
        DarkContrastText = "#FFFFFF",
        TextPrimary = "#1F2937",
        TextSecondary = "#6B7280",
        TextDisabled = "#9CA3AF",
        Background = "#FFFFFF",
        Surface = "#F5F5FA",
        DrawerBackground = "#F5F5FA",
        DrawerText = "#1F2937",
        AppbarBackground = "#8F6AAF",
        AppbarText = "#FFFFFF",
        LinesDefault = "#E5E7EB",
        LinesInputs = "#D1D5DB",
        TableLines = "#E5E7EB",
        TableStriped = "#F9FAFB",
        TableHover = "#F3F4F6",
        Divider = "#E5E7EB",
        DividerLight = "#F3F4F6",
        PrimaryDarken = "#7C5A9E",
        PrimaryLighten = "#A78BCA",
        SecondaryDarken = "#7C8A98",
        SecondaryLighten = "#B0B8C4",
        TertiaryDarken = "#555E6A",
        TertiaryLighten = "#8B95A0",
        InfoDarken = "#2563EB",
        InfoLighten = "#60A5FA",
        SuccessDarken = "#16A34A",
        SuccessLighten = "#4ADE80",
        WarningDarken = "#D97706",
        WarningLighten = "#FBBF24",
        ErrorDarken = "#DC2626",
        ErrorLighten = "#F87171",
        DarkDarken = "#111827",
        DarkLighten = "#374151",
        HoverOpacity = 0.06,
        RippleOpacity = 0.1,
        Skeleton = "#E5E7EB",
    };

    private static readonly PaletteDark DarkPalette = new()
    {
        Black = "#000000",
        White = "#FFFFFF",
        Primary = "#CBA6F7",
        PrimaryContrastText = "#1E1E2E",
        Secondary = "#9CA3AF",
        SecondaryContrastText = "#000000",
        Tertiary = "#6B7280",
        Info = "#3B82F6",
        Success = "#22C55E",
        Warning = "#F59E0B",
        Error = "#EF4444",
        Dark = "#1F2937",
        DarkContrastText = "#CDD6F4",
        TextPrimary = "#CDD6F4",
        TextSecondary = "#9CA3AF",
        TextDisabled = "#6B7280",
        Background = "#11111B",
        Surface = "#1E1E2E",
        DrawerBackground = "#181825",
        DrawerText = "#CDD6F4",
        AppbarBackground = "#1A1A2E",
        AppbarText = "#CDD6F4",
        LinesDefault = "#313244",
        LinesInputs = "#45475A",
        TableLines = "#313244",
        TableStriped = "#181825",
        TableHover = "#1E1E2E",
        Divider = "#313244",
        DividerLight = "#181825",
        PrimaryDarken = "#B48EF0",
        PrimaryLighten = "#DDB9FF",
        SecondaryDarken = "#7C8A98",
        SecondaryLighten = "#B0B8C4",
        TertiaryDarken = "#555E6A",
        TertiaryLighten = "#8B95A0",
        InfoDarken = "#2563EB",
        InfoLighten = "#60A5FA",
        SuccessDarken = "#16A34A",
        SuccessLighten = "#4ADE80",
        WarningDarken = "#D97706",
        WarningLighten = "#FBBF24",
        ErrorDarken = "#DC2626",
        ErrorLighten = "#F87171",
        DarkDarken = "#111827",
        DarkLighten = "#374151",
        HoverOpacity = 0.06,
        RippleOpacity = 0.1,
        Skeleton = "#313244",
    };
}
