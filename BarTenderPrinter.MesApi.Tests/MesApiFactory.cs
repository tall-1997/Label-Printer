using BarTenderPrinter.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace BarTenderPrinter.MesApi.Tests;

public sealed class MesApiFactory(string operatorStationId = "station-1", string operatorShiftId = "shift-a") : WebApplicationFactory<Program>
{
    public const string PlannerToken = "test-planner-token";
    public const string OperatorToken = "test-operator-token";
    public const string ViewerToken = "test-viewer-token";
    public const string HighRiskToken = "test-high-risk-token";
    public const string QualityEngineerToken = "test-quality-engineer-token";
    public const string ReworkExecutorToken = "test-rework-executor-token";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:MesDatabase"] = Environment.GetEnvironmentVariable("BARTENDER_TEST_POSTGRES"),
            ["MesSecurity:Sessions:0:Token"] = PlannerToken,
            ["MesSecurity:Sessions:0:UserId"] = "planner-1",
            ["MesSecurity:Sessions:0:StationId"] = "planning-station",
            ["MesSecurity:Sessions:0:ShiftId"] = "shift-a",
            ["MesSecurity:Sessions:0:Roles:0"] = "Planner",
            ["MesSecurity:Sessions:0:Roles:1"] = "ProcessEngineer",
            ["MesSecurity:Sessions:1:Token"] = OperatorToken,
            ["MesSecurity:Sessions:1:UserId"] = "operator-1",
            ["MesSecurity:Sessions:1:StationId"] = operatorStationId,
            ["MesSecurity:Sessions:1:ShiftId"] = operatorShiftId,
            ["MesSecurity:Sessions:1:Roles:0"] = "ProductionOperator",
            ["MesSecurity:Sessions:1:Roles:1"] = "PackagingOperator",
            ["MesSecurity:Sessions:2:Token"] = ViewerToken,
            ["MesSecurity:Sessions:2:UserId"] = "viewer-1",
            ["MesSecurity:Sessions:2:StationId"] = "viewer-station",
            ["MesSecurity:Sessions:2:ShiftId"] = "shift-a",
            ["MesSecurity:Sessions:2:Roles:0"] = "Viewer",
            ["MesSecurity:Sessions:3:Token"] = HighRiskToken,
            ["MesSecurity:Sessions:3:UserId"] = "manager-1",
            ["MesSecurity:Sessions:3:StationId"] = "manager-station",
            ["MesSecurity:Sessions:3:ShiftId"] = "shift-a",
            ["MesSecurity:Sessions:3:Roles:0"] = "QualityManager",
            ["MesSecurity:Sessions:3:Roles:1"] = "WarehouseSupervisor",
            ["MesSecurity:Sessions:3:Roles:2"] = "ArchiveAdministrator",
            ["MesSecurity:Sessions:4:Token"] = QualityEngineerToken,
            ["MesSecurity:Sessions:4:UserId"] = "quality-engineer-1",
            ["MesSecurity:Sessions:4:StationId"] = "quality-station",
            ["MesSecurity:Sessions:4:ShiftId"] = "shift-a",
            ["MesSecurity:Sessions:4:Roles:0"] = "QualityEngineer",
            ["MesSecurity:Sessions:5:Token"] = ReworkExecutorToken,
            ["MesSecurity:Sessions:5:UserId"] = "production-supervisor-1",
            ["MesSecurity:Sessions:5:StationId"] = "production-station",
            ["MesSecurity:Sessions:5:ShiftId"] = "shift-a",
            ["MesSecurity:Sessions:5:Roles:0"] = "ProductionSupervisor"
        }));
    }

    public static async Task MigrateAsync()
    {
        await using var dataSource = NpgsqlDataSource.Create(Environment.GetEnvironmentVariable("BARTENDER_TEST_POSTGRES")!);
        await new PostgresMigrator(dataSource).MigrateAsync();
    }
}
