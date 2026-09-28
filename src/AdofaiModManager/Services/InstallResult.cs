namespace AdofaiModManager.Services;

/// <summary>一次操作（安装 / 卸载 / 启停）的结果。</summary>
public sealed record InstallResult(bool Success, string Message, string? ModId = null);
