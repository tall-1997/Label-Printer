namespace BarTenderPrinter.MesApi;

public static class MesFeatureEndpoints
{
    public static RouteGroupBuilder MapMesFeatureEndpoints(this RouteGroupBuilder api)
    {
        api.MapCoreEndpoints();
        api.MapExtendedEndpoints();
        return api;
    }
}
