using MudBlazor;

namespace MesAdmin.Web.Themes;

/// <summary>
/// MudBlazor 双主题配置（T0.9）。
/// - 暗色主题（默认）：深邃工业风，薰衣草紫 #CBA6F7
/// - 亮色主题（车间终端）：护眼豆沙绿纸感，深紫 #7C5A9E
/// 全局圆角 12px，字体 Inter Tight
/// </summary>
public static class MesTheme
{
    // 注意：静态字段按文本顺序初始化，调色板必须先于 CurrentTheme 声明，
    // 否则 CurrentTheme 初始化时取到的还是 null（曾导致 MudThemeProvider 空引用）。

    private static readonly PaletteLight LightPalette = new()
    {
        // 护眼豆沙绿纸感：画布淡绿、卡面暖白、文字加深（高对比）、品牌紫加深。
        // 与 app.css :root[data-mes-theme="light"] 令牌组保持一致。
        Black = "#000000",
        White = "#FFFFFF",
        Primary = "#7C5A9E",
        PrimaryContrastText = "#FFFFFF",
        Secondary = "#6B7462",
        SecondaryContrastText = "#FFFFFF",
        Tertiary = "#15803D",
        TertiaryContrastText = "#FFFFFF",
        Info = "#2563EB",
        Success = "#15803D",
        SuccessContrastText = "#FFFFFF",
        Warning = "#B45309",
        WarningContrastText = "#FFFFFF",
        Error = "#DC2626",
        Dark = "#232B1D",
        DarkContrastText = "#FBFCF4",
        TextPrimary = "#232B1D",
        TextSecondary = "#4F5A44",
        TextDisabled = "#8A9179",
        Background = "#F0F4E6",
        Surface = "#FBFCF4",
        DrawerBackground = "#F4F7EA",
        DrawerText = "#232B1D",
        AppbarBackground = "#7C5A9E",
        AppbarText = "#FFFFFF",
        LinesDefault = "#D9DEC9",
        LinesInputs = "#C8CDB6",
        TableLines = "#D9DEC9",
        TableStriped = "#F4F7EB",
        TableHover = "#EDF1DF",
        Divider = "#D9DEC9",
        DividerLight = "#EAEDDD",
        PrimaryDarken = "#68488A",
        PrimaryLighten = "#9B7CB8",
        SecondaryDarken = "#565E4E",
        SecondaryLighten = "#8A927C",
        TertiaryDarken = "#116633",
        TertiaryLighten = "#3F9D5A",
        InfoDarken = "#1D4ED8",
        InfoLighten = "#60A5FA",
        SuccessDarken = "#116633",
        SuccessLighten = "#4CAF6D",
        WarningDarken = "#92400E",
        WarningLighten = "#D97706",
        ErrorDarken = "#B91C1C",
        ErrorLighten = "#EF4444",
        DarkDarken = "#181E14",
        DarkLighten = "#39452F",
        HoverOpacity = 0.06,
        RippleOpacity = 0.1,
        Skeleton = "#E9EDDB",
    };

    private static readonly PaletteDark DarkPalette = new()
    {
        Black = "#000000",
        White = "#FFFFFF",
        Primary = "#CBA6F7",
        PrimaryContrastText = "#1B1030",
        Secondary = "#93BBFB",
        SecondaryContrastText = "#0E1526",
        Tertiary = "#ADE6A8",
        TertiaryContrastText = "#0E1526",
        Info = "#93BBFB",
        InfoContrastText = "#0E1526",
        Success = "#4ADE80",
        SuccessContrastText = "#0E1526",
        Warning = "#FBBF24",
        WarningContrastText = "#0E1526",
        Error = "#F87171",
        ErrorContrastText = "#0E1526",
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
        PrimaryLighten = "#DBBFFB",
        SecondaryDarken = "#6E9AF0",
        SecondaryLighten = "#A6C8FF",
        TertiaryDarken = "#8ACF86",
        TertiaryLighten = "#C0EEBC",
        InfoDarken = "#6E9AF0",
        InfoLighten = "#A6C8FF",
        SuccessDarken = "#16A34A",
        SuccessLighten = "#86EFAC",
        WarningDarken = "#D97706",
        WarningLighten = "#FCD34D",
        ErrorDarken = "#DC2626",
        ErrorLighten = "#FCA5A5",
        DarkDarken = "#0B0B12",
        DarkLighten = "#22222F",
        HoverOpacity = 0.06,
        RippleOpacity = 0.1,
        Skeleton = "#22222C",
    };

    /// <summary>当前主题：单一 MudTheme 携带双调色板，由 MudThemeProvider 的 IsDarkMode 切换（暗色默认）</summary>
    public static readonly MudTheme CurrentTheme = new()
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
            // 工业仪表型字阶：以 16px 为基准收窄（Material 默认过大），
            // 负字距随字号递减，行高随字号递增，制造数据面板信息密度优先。
            H1 = new H1Typography { FontFamily = ["Inter Tight"], FontWeight = "700", FontSize = "2.25rem", LineHeight = "1.2", LetterSpacing = "-0.03em" },
            H2 = new H2Typography { FontFamily = ["Inter Tight"], FontWeight = "700", FontSize = "1.75rem", LineHeight = "1.25", LetterSpacing = "-0.025em" },
            H3 = new H3Typography { FontFamily = ["Inter Tight"], FontWeight = "650", FontSize = "1.375rem", LineHeight = "1.3", LetterSpacing = "-0.02em" },
            H4 = new H4Typography { FontFamily = ["Inter Tight"], FontWeight = "650", FontSize = "1.25rem", LineHeight = "1.35", LetterSpacing = "-0.018em" },
            H5 = new H5Typography { FontFamily = ["Inter Tight"], FontWeight = "600", FontSize = "1.125rem", LineHeight = "1.4", LetterSpacing = "-0.015em" },
            H6 = new H6Typography { FontFamily = ["Inter Tight"], FontWeight = "600", FontSize = "1rem", LineHeight = "1.45", LetterSpacing = "-0.01em" },
            Subtitle1 = new Subtitle1Typography { FontFamily = ["Inter Tight"], FontWeight = "600", FontSize = "0.9375rem", LineHeight = "1.4", LetterSpacing = "-0.005em" },
            Subtitle2 = new Subtitle2Typography { FontFamily = ["Inter Tight"], FontWeight = "600", FontSize = "0.875rem", LineHeight = "1.4", LetterSpacing = "-0.005em" },
            Body1 = new Body1Typography { FontFamily = ["Inter Tight"], FontWeight = "400", FontSize = "0.9375rem", LineHeight = "1.6" },
            Body2 = new Body2Typography { FontFamily = ["Inter Tight"], FontWeight = "400", FontSize = "0.875rem", LineHeight = "1.55" },
            Button = new ButtonTypography { FontFamily = ["Inter Tight"], FontWeight = "500", FontSize = "0.875rem", LetterSpacing = "0.01em", TextTransform = "none" },
            Caption = new CaptionTypography { FontFamily = ["Inter Tight"], FontWeight = "500", FontSize = "0.75rem", LineHeight = "1.5", LetterSpacing = "0.02em" },
            Overline = new OverlineTypography { FontFamily = ["Inter Tight"], FontWeight = "600", FontSize = "0.7rem", LineHeight = "1.4", LetterSpacing = "0.12em", TextTransform = "uppercase" },
        },
    };
}
