# ── AutoMES Web 生产 Dockerfile ──
# 多阶段构建：SDK → 还原 → 构建 → 发布 → 运行时镜像

# ── Stage 1: 还原 ──
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS restore
WORKDIR /src

# 先复制项目文件以利用 Docker 层缓存
COPY Directory.Build.props .
COPY src/MesAdmin.Domain/MesAdmin.Domain.csproj src/MesAdmin.Domain/
COPY src/MesAdmin.Generators/MesAdmin.Generators.csproj src/MesAdmin.Generators/
COPY src/MesAdmin.Application/MesAdmin.Application.csproj src/MesAdmin.Application/
COPY src/MesAdmin.Infrastructure/MesAdmin.Infrastructure.csproj src/MesAdmin.Infrastructure/
COPY src/MesAdmin.Web/MesAdmin.Web.csproj src/MesAdmin.Web/
RUN dotnet restore src/MesAdmin.Web/MesAdmin.Web.csproj

# ── Stage 2: 构建 ──
FROM restore AS build
COPY src/ src/
WORKDIR /src/src/MesAdmin.Web
RUN dotnet build MesAdmin.Web.csproj -c Release --no-restore -o /build

# ── Stage 3: 发布 ──
FROM build AS publish
RUN dotnet publish MesAdmin.Web.csproj -c Release --no-restore -o /publish

# ── Stage 4: 运行时 ──
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
EXPOSE 5138

# 安全：非 root 用户
USER $APP_UID

COPY --from=publish /publish .

# 健康检查：Blazor Server 交互式
HEALTHCHECK --interval=30s --timeout=10s --start-period=30s --retries=3 \
  CMD curl -f http://localhost:5138/ || exit 1

ENTRYPOINT ["dotnet", "MesAdmin.Web.dll"]
