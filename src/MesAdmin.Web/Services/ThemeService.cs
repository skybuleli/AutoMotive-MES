using Microsoft.JSInterop;

namespace MesAdmin.Web.Services;

/// <summary>
/// 明暗主题状态服务（暗色默认，车间终端可切亮色）。
/// 偏好经 localStorage 持久化（key: mes.theme），首帧由 App.razor 内联脚本恢复避免闪烁；
/// 切换时在 <html> 上同步 data-mes-theme 属性，供 app.css 令牌分组选择器使用。
/// </summary>
public sealed class ThemeService(IJSRuntime js)
{
    /// <summary>主题变化时通知布局重新渲染 MudThemeProvider。</summary>
    public event Action? Changed;

    public bool IsDark { get; private set; } = true;

    private bool _initialized;

    /// <summary>电路就绪后调用一次，恢复用户偏好。</summary>
    public async Task InitializeAsync()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        try
        {
            var stored = await js.InvokeAsync<string?>("mesTheme.get");
            if (stored is not null)
            {
                IsDark = stored != "light";
            }
        }
        catch (JSDisconnectedException)
        {
            // 电路断开时静默忽略，保持默认暗色
        }
        catch (InvalidOperationException)
        {
            // 预渲染期无 JS 运行时，静默忽略
        }
    }

    public async Task ToggleAsync()
    {
        IsDark = !IsDark;
        try
        {
            await js.InvokeVoidAsync("mesTheme.set", IsDark ? "dark" : "light");
        }
        catch (JSDisconnectedException)
        {
        }
        catch (InvalidOperationException)
        {
        }

        Changed?.Invoke();
    }
}
