using MesAdmin.Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ZLogger;

namespace MesAdmin.Infrastructure.Storage;

/// <summary>
/// 本地文件存储实现（S03）。
/// 按配置 DocumentStorage:BasePath 落盘，未配置则落 Temp/automes-documents；
/// 返回的 storagePath 为相对路径（供 DocumentVersion.FileStoragePath 持久化），形如 "202608/ULID_原文件名.pdf"。
/// </summary>
public sealed class LocalFileStorage : IFileStorage
{
    private readonly string _basePath;
    private readonly ILogger<LocalFileStorage> _logger;

    public LocalFileStorage(IConfiguration configuration, ILogger<LocalFileStorage> logger)
    {
        _basePath = configuration["DocumentStorage:BasePath"]
            ?? Path.Combine(Path.GetTempPath(), "automes-documents");
        _logger = logger;
    }

    // 暴露给测试：可直接用 Temp 路径构造，不走 DI
    internal string BasePath => _basePath;

    public async Task<string> SaveAsync(Stream content, string fileName, string contentType, CancellationToken ct = default)
    {
        var safeName = SanitizeFileName(fileName);
        var dir = Path.Combine(_basePath, DateTimeOffset.UtcNow.ToString("yyyyMM"));
        Directory.CreateDirectory(dir);

        var storageName = $"{Ulid.NewUlid()}_{safeName}";
        var fullPath = Path.Combine(dir, storageName);

        await using var fs = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await content.CopyToAsync(fs, ct);

        // 返回相对路径，DB 存相对路径便于迁移
        var relative = Path.Combine(DateTimeOffset.UtcNow.ToString("yyyyMM"), storageName);
        _logger.ZLogInformation($"受控文档已落盘：{relative} ({fs.Length} 字节)");
        return relative;
    }

    public async Task<(Stream Content, string ContentType, string FileName, long FileSize)> LoadAsync(string storagePath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(storagePath))
            throw new ArgumentException("存储路径不能为空", nameof(storagePath));

        var fullPath = Path.IsPathRooted(storagePath) ? storagePath : Path.Combine(_basePath, storagePath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"文档文件不存在：{storagePath}", fullPath);

        var fi = new FileInfo(fullPath);
        // 推断 ContentType：若调用方已存则应从 DB 取；此处按扩展名回退
        var contentType = InferContentType(fi.Extension);

        // 调用方负责 Dispose
        var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);

        // 满足 IAsync 要求
        await Task.CompletedTask;
        return (stream, contentType, fi.Name.Contains('_') ? fi.Name[(fi.Name.IndexOf('_') + 1)..] : fi.Name, fi.Length);
    }

    private static string SanitizeFileName(string fileName)
    {
        var name = Path.GetFileName(fileName.Trim());
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        if (name.Length > 180) name = name[..180];
        return string.IsNullOrWhiteSpace(name) ? "document" : name;
    }

    private static string InferContentType(string ext) => ext.ToLowerInvariant() switch
    {
        ".pdf" => "application/pdf",
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ".doc" => "application/msword",
        ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        ".xls" => "application/vnd.ms-excel",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        _ => "application/octet-stream",
    };
}
