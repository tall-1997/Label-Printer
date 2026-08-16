namespace BarTenderPrinter.Application.Identity;

public static class PlatformCapabilities
{
    private static readonly IReadOnlyDictionary<string, string[]> RoleCapabilities =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["Planner"] = ["dashboard.view", "orders.view", "traceability.view"],
            ["ProcessEngineer"] = ["dashboard.view", "engineering.view", "numbering.view", "stations.view", "traceability.view"],
            ["ProductionOperator"] = ["dashboard.view", "production.view", "workspace.use", "traceability.view"],
            ["PackagingOperator"] = ["dashboard.view", "production.view", "workspace.use", "traceability.view"],
            ["ProductionSupervisor"] = ["dashboard.view", "orders.view", "engineering.view", "production.view", "stations.view", "workspace.use", "traceability.view"],
            ["QualityEngineer"] = ["dashboard.view", "quality.view", "workspace.use", "traceability.view"],
            ["QualityManager"] = ["dashboard.view", "quality.view", "traceability.view"],
            ["WarehouseOperator"] = ["dashboard.view", "warehouse.view", "workspace.use", "traceability.view"],
            ["WarehouseSupervisor"] = ["dashboard.view", "warehouse.view", "traceability.view"],
            ["ArchiveAdministrator"] = ["dashboard.view", "traceability.view"],
            ["PrintSupervisor"] = ["dashboard.view", "production.view", "workspace.use", "traceability.view"]
        };

    public static IReadOnlyList<string> Resolve(IEnumerable<string> roles) => roles
        .SelectMany(role => RoleCapabilities.GetValueOrDefault(role) ?? [])
        .Distinct(StringComparer.Ordinal)
        .Order(StringComparer.Ordinal)
        .ToArray();
}

public sealed record PlatformSessionView(
    string UserId,
    string DisplayName,
    string StationId,
    string ShiftId,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Capabilities);
