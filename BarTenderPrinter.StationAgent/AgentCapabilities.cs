namespace BarTenderPrinter.StationAgent;

public sealed record AgentCapability(string Id, string State, bool Simulated, string Detail);

public sealed record AgentHealth(
    string Status,
    string AgentVersion,
    string StationId,
    DateTimeOffset UtcNow,
    IReadOnlyList<AgentCapability> Capabilities);

public static class AgentCapabilities
{
    public static AgentHealth Create(IConfiguration configuration, DateTimeOffset utcNow)
    {
        var stationId = configuration["StationAgent:StationId"]?.Trim();
        if (string.IsNullOrWhiteSpace(stationId)) stationId = "UNCONFIGURED";
        return new AgentHealth("healthy", typeof(AgentCapabilities).Assembly.GetName().Version?.ToString() ?? "1.0.0",
            stationId, utcNow,
            [
                new("bartender", "configuration-required", false, "等待 Windows BarTender 运行时和打印适配器配置。"),
                new("scale", "ready", true, "当前使用可配置模拟电子秤适配器。"),
                new("identifier-writer", "ready", true, "当前使用可配置模拟写号适配器。"),
                new("offline-outbox", "ready", false, "SQLite 操作账本、同步 outbox 和人工恢复已启用。")
            ]);
    }
}
