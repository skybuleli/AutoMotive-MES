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
        Primary = "#8F6AAF",        // 深紫（车间终端洁净风）
        Secondary = "#9CA3AF",
        Background = "#FFFFFF",
        Surface = "#F5F5FA",
        AppbarBackground = "#8F6AAF",
    };

    private static readonly PaletteDark DarkPalette = new()
    {
        Primary = "#CBA6F7",        // 薰衣草紫（深邃工业风）
        Secondary = "#9CA3AF",
        Background = "#11111B",      // 深邃背景
        Surface = "#1E1E2E",         // 卡片表面
        AppbarBackground = "#1A1A2E",
        DrawerBackground = "#181825",
        DrawerText = "#CDD6F4",
    };
}
