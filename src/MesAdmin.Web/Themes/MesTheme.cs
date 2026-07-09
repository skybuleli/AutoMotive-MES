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
        PaletteLight = LightPalette!,
        PaletteDark = DarkPalette!,
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
            H1 = new H1Typography { FontFamily = ["Inter Tight"], FontWeight = "700", LetterSpacing = "-0.03em" },
            H2 = new H2Typography { FontFamily = ["Inter Tight"], FontWeight = "700", LetterSpacing = "-0.025em" },
            H3 = new H3Typography { FontFamily = ["Inter Tight"], FontWeight = "700", LetterSpacing = "-0.02em" },
            H4 = new H4Typography { FontFamily = ["Inter Tight"], FontWeight = "600", LetterSpacing = "-0.018em" },
            H5 = new H5Typography { FontFamily = ["Inter Tight"], FontWeight = "600", LetterSpacing = "-0.015em" },
            H6 = new H6Typography { FontFamily = ["Inter Tight"], FontWeight = "600", LetterSpacing = "-0.01em" },
            Subtitle1 = new Subtitle1Typography { FontFamily = ["Inter Tight"], FontWeight = "600" },
            Button = new ButtonTypography { FontFamily = ["Inter Tight"], FontWeight = "500", TextTransform = "none" },
            Caption = new CaptionTypography { FontFamily = ["Inter Tight"], LetterSpacing = "0.02em" },
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
        Primary = "#D0B4F9",
        PrimaryContrastText = "#1B1030",
        Secondary = "#93BBFB",
        SecondaryContrastText = "#0E1526",
        Tertiary = "#ADE6A8",
        Info = "#93BBFB",
        Success = "#ADE6A8",
        Warning = "#FBE7B6",
        Error = "#F595B0",
        Dark = "#252533",
        DarkContrastText = "#F4F4FA",
        TextPrimary = "#F4F4FA",
        TextSecondary = "#BFC3DA",
        TextDisabled = "#9096B0",
        Background = "#191925",
        Surface = "#252533",
        DrawerBackground = "#1C1C29",
        DrawerText = "#F4F4FA",
        AppbarBackground = "#1F1F2D",
        AppbarText = "#F4F4FA",
        LinesDefault = "#39394B",
        LinesInputs = "#45455A",
        TableLines = "#33334B",
        TableStriped = "#20202C",
        TableHover = "#2A2A3A",
        Divider = "#39394B",
        DividerLight = "#2E2E40",
        PrimaryDarken = "#B48EF0",
        PrimaryLighten = "#DDB9FF",
        SecondaryDarken = "#6E9AF0",
        SecondaryLighten = "#A6C8FF",
        TertiaryDarken = "#8ACF86",
        TertiaryLighten = "#C0EEBC",
        InfoDarken = "#6E9AF0",
        InfoLighten = "#A6C8FF",
        SuccessDarken = "#8ACF86",
        SuccessLighten = "#C0EEBC",
        WarningDarken = "#EAD08C",
        WarningLighten = "#FBEEC0",
        ErrorDarken = "#E06C8C",
        ErrorLighten = "#F7AEC1",
        DarkDarken = "#0B0B12",
        DarkLighten = "#22222F",
        HoverOpacity = 0.06,
        RippleOpacity = 0.1,
        Skeleton = "#22222C",
    };
}
