# ── AutoMES Web 生产 Dockerfile ──
# 多阶段构建：还原（NuGet 缓存挂载）→ 发布 → chiseled 运行时镜像
# syntax=docker/dockerfile:1

# ── Stage 1: 还原 ──
# 先只复制项目文件：csproj 未变时此层命中缓存，源码改动不触发重新还原；
# NuGet 包放在 BuildKit 缓存挂载中，即使还原层失效也不会重新下载。
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS restore
WORKDIR /src

COPY Directory.Build.props .
COPY src/MesAdmin.Domain/MesAdmin.Domain.csproj src/MesAdmin.Domain/
COPY src/MesAdmin.Generators/MesAdmin.Generators.csproj src/MesAdmin.Generators/
COPY src/MesAdmin.Application/MesAdmin.Application.csproj src/MesAdmin.Application/
COPY src/MesAdmin.Infrastructure/MesAdmin.Infrastructure.csproj src/MesAdmin.Infrastructure/
COPY src/MesAdmin.Web/MesAdmin.Web.csproj src/MesAdmin.Web/
RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet restore src/MesAdmin.Web/MesAdmin.Web.csproj
# 供 runtime 阶段 COPY 创建 DataProtection 密钥目录用（chiseled 无 shell）
RUN touch /dpkeys.stamp

# ── Stage 2: 发布 ──
FROM restore AS publish
COPY src/ src/
WORKDIR /src/src/MesAdmin.Web
# 完整源码覆盖后再还原一次：csproj 内容（而非文件列表）变化时刷新包资产，
# 避免复用还原层导致资产过期；包来自缓存挂载，秒级完成
RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet restore MesAdmin.Web.csproj
# .NET 10 framework asset manifest 在 publish 期间生成，但 SDK 镜像不会把
# 框架文件复制进独立输出目录。保留 blazor.web.js 供 Interactive Server 引导。
# 必须与 publish 同一个 RUN：NuGet 缓存挂载仅在该步骤内可见
RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet publish MesAdmin.Web.csproj -c Release --no-restore -o /publish \
    && framework_assets="$(find /root/.nuget/packages/microsoft.aspnetcore.app.internal.assets -type f -path '*/_framework/blazor.web.js' | sort -V | tail -n 1)" \
    && test -n "$framework_assets" \
    && mkdir -p /publish/wwwroot/_framework \
    && cp "$framework_assets" /publish/wwwroot/_framework/blazor.web.js

# ── Stage 3: 运行时 ──
# chiseled-extra：无 shell、非 root、含 ICU/tzdata，体积比标准 aspnet 小约 40%
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra AS runtime
WORKDIR /app
EXPOSE 5138

# chiseled 无 shell/apt，无法 RUN 安装 curl：复制静态链接的 busybox，
# 用其内置 wget 探活首页（注意：curlimages/curl 的二进制动态链接 musl，
# 在 glibc 的 chiseled 上无法运行，不能用它）
COPY --from=busybox:1.37.0 /bin/busybox /bin/busybox

# chiseled 无 shell 无法 mkdir/chown：COPY 一个占位文件让 Docker 以 app 用户
# 属主创建 DataProtection 密钥目录，命名卷首次挂载时继承该属主，
# 否则 ASP.NET 无法持久化会话/防伪密钥（每次部署全员掉登录）
COPY --from=restore --chown=$APP_UID:$APP_UID \
    /dpkeys.stamp /home/app/.aspnet/DataProtection-Keys/.keep

# 安全：非 root 用户（chiseled 默认即 app 用户，显式声明以防基础镜像变更）
USER $APP_UID

COPY --from=publish /publish .

# 健康检查：Blazor Server 首页（无独立 /health 端点）
HEALTHCHECK --interval=15s --timeout=5s --start-period=45s --retries=5 \
  CMD ["/bin/busybox", "wget", "-q", "-O", "/dev/null", "http://localhost:5138/"]

ENTRYPOINT ["dotnet", "MesAdmin.Web.dll"]
