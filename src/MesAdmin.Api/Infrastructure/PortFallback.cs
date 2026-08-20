using System.Net;
using System.Net.Sockets;

namespace MesAdmin.Api.Infrastructure;

/// <summary>
/// 端口冲突自动避让：当配置的监听地址端口已被占用时，自动向上查找空闲端口，
/// 避免宿主启动时因 "address already in use" 直接崩溃。
/// 仅用于开发环境；生产环境端口必须保持稳定（Docker EXPOSE / 健康检查 / 反向代理依赖）。
/// </summary>
public static class PortFallback
{
    /// <summary>被占用后最多向上探测的空闲端口数量。</summary>
    private const int MaxProbeAttempts = 100;

    /// <summary>
    /// 解析配置的监听地址列表（分号分隔），探测被占用的端口并重映射到下一个空闲端口。
    /// 返回与输入一一对应的地址列表，以及是否有任何端口发生了重映射。
    /// 无法解析为绝对 URI 的条目（如 Kestrel 通配符语法 *:8080）保持原样。
    /// </summary>
    public static (string Urls, bool Changed) Resolve(string urls)
    {
        if (string.IsNullOrWhiteSpace(urls))
        {
            return (urls, false);
        }

        var parts = urls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var resolved = new List<string>(parts.Length);
        var changed = false;

        foreach (var part in parts)
        {
            if (!Uri.TryCreate(part, UriKind.Absolute, out var uri) || uri.Port <= 0)
            {
                resolved.Add(part);
                continue;
            }

            if (IsPortFree(uri.Port))
            {
                resolved.Add(part);
                continue;
            }

            var newPort = FindFreePort(uri.Port + 1);
            var builder = new UriBuilder(uri) { Port = newPort };
            resolved.Add(builder.Uri.ToString());
            changed = true;

            Console.WriteLine(
                $"[PortFallback] 端口 {uri.Port} 已被占用，自动切换监听端口至 {newPort}（当前 API 地址：{builder.Uri}）。" +
                "如 Web 前端需访问 API，请同步更新 appsettings.json 的 Api:BaseUrl 与 SignalR 地址。");
        }

        return (string.Join(';', resolved), changed);
    }

    /// <summary>端口在 IPv4 / IPv6 回环地址上均未被占用时视为可用。</summary>
    private static bool IsPortFree(int port)
    {
        foreach (var address in new[] { IPAddress.Loopback, IPAddress.IPv6Loopback })
        {
            try
            {
                using var listener = new TcpListener(address, port);
                listener.Start();
            }
            catch (SocketException)
            {
                return false;
            }
        }

        return true;
    }

    private static int FindFreePort(int start)
    {
        for (var port = start; port < start + MaxProbeAttempts; port++)
        {
            if (IsPortFree(port))
            {
                return port;
            }
        }

        throw new InvalidOperationException(
            $"端口 {start - 1} 被占用后，向上探测 {MaxProbeAttempts} 个端口均不可用，无法自动避让。");
    }
}
