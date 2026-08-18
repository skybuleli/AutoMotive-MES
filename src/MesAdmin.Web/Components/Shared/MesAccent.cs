using MudBlazor;

namespace MesAdmin.Web.Components.Shared;

/// <summary>全站强调色语义：映射 app.css 的 accent 工具类与 MudBlazor 颜色。</summary>
public enum MesAccent
{
    Primary,
    Success,
    Warning,
    Error,
    Info,
}

public static class MesAccentExtensions
{
    public static string ToTopClass(this MesAccent accent) => accent switch
    {
        MesAccent.Success => "accent-top-success",
        MesAccent.Warning => "accent-top-warning",
        MesAccent.Error => "accent-top-error",
        MesAccent.Info => "accent-top-info",
        _ => "accent-top-primary",
    };

    public static string ToLeftClass(this MesAccent accent) => accent switch
    {
        MesAccent.Success => "accent-left-success",
        MesAccent.Warning => "accent-left-warning",
        MesAccent.Error => "accent-left-error",
        MesAccent.Info => "accent-left-info",
        _ => "accent-left-primary",
    };

    public static Color ToMudColor(this MesAccent accent) => accent switch
    {
        MesAccent.Success => Color.Success,
        MesAccent.Warning => Color.Warning,
        MesAccent.Error => Color.Error,
        MesAccent.Info => Color.Info,
        _ => Color.Primary,
    };
}

/// <summary>设备状态语义：映射 app.css 的 status-dot 发光圆点类。</summary>
public enum MesDotStatus
{
    Running,
    Idle,
    Alarm,
    Offline,
}

public static class MesDotStatusExtensions
{
    public static string ToStatusDotClass(this MesDotStatus status) => status switch
    {
        MesDotStatus.Running => "running",
        MesDotStatus.Idle => "idle",
        MesDotStatus.Alarm => "alarm",
        _ => "offline",
    };
}
