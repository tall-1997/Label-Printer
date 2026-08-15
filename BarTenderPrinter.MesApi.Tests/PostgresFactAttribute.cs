using Xunit;

namespace BarTenderPrinter.MesApi.Tests;

public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BARTENDER_TEST_POSTGRES")))
            Skip = "BARTENDER_TEST_POSTGRES 未配置。";
    }
}
