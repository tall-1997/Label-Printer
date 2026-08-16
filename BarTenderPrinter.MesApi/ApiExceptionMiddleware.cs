using Npgsql;

namespace BarTenderPrinter.MesApi;

public sealed class ApiExceptionMiddleware(RequestDelegate next, ILogger<ApiExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.Headers["X-Correlation-ID"] = context.TraceIdentifier;
        try
        {
            await next(context);
        }
        catch (Exception exception)
        {
            var (status, error) = Map(exception, context.TraceIdentifier);
            if (status >= StatusCodes.Status500InternalServerError)
                logger.LogError(exception, "MES API request failed. CorrelationId={CorrelationId}", context.TraceIdentifier);
            context.Response.StatusCode = status;
            await context.Response.WriteAsJsonAsync(error, context.RequestAborted);
        }
    }

    private static (int Status, ApiError Error) Map(Exception exception, string correlationId) => exception switch
    {
        BadHttpRequestException => (400, new ApiError("VALIDATION_FAILED", "请求正文格式无效。", correlationId)),
        ArgumentException => (400, new ApiError("VALIDATION_FAILED", exception.Message, correlationId)),
        KeyNotFoundException => (404, new ApiError("NOT_FOUND", exception.Message, correlationId)),
        InvalidOperationException { Message: "IDEMPOTENCY_CONFLICT" } =>
            (409, new ApiError("IDEMPOTENCY_CONFLICT", "幂等键已用于其他请求。", correlationId)),
        InvalidOperationException { Message: "NUMBER_RANGE_EXHAUSTED" } =>
            (409, new ApiError("NUMBER_RANGE_EXHAUSTED", "号段已耗尽。", correlationId)),
        InvalidOperationException { Message: "AUTHENTICATION_CONTEXT_INVALID" } =>
            (401, new ApiError("UNAUTHORIZED", "工位会话上下文无效。", correlationId)),
        BarTenderPrinter.Persistence.PersistenceConcurrencyException =>
            (409, new ApiError("CONCURRENCY_CONFLICT", "数据版本已变化，请刷新后重试。", correlationId, true)),
        BarTenderPrinter.Persistence.PersistenceBusinessException businessException =>
            (BusinessStatus(businessException.Code), new ApiError(
                businessException.Code, businessException.Message, correlationId, false, businessException.Details)),
        PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } =>
            (409, new ApiError("CONFLICT", "唯一业务标识已存在。", correlationId)),
        _ => (500, new ApiError("INTERNAL_ERROR", "服务处理请求时发生错误。", correlationId, true))
    };

    private static int BusinessStatus(string code) => code switch
    {
        "NOT_FOUND" => StatusCodes.Status404NotFound,
        "ORDER_UNAVAILABLE" or "UNIT_UNAVAILABLE" or "STATION_NOT_QUALIFIED" or "ROUTE_MISMATCH" or
        "PREVIOUS_OPERATION_INCOMPLETE" or "OPERATION_ALREADY_COMPLETED" or "REWORK_CONTEXT_REQUIRED" or
        "IDEMPOTENCY_CONFLICT" or "PACKAGING_BINDING_CONFLICT" or "PACKAGING_UNIT_CLOSED" or
        "PACKAGING_TYPE_MISMATCH" or "PACKAGING_PRODUCT_MISMATCH" or "PACKAGING_CHILD_NOT_READY" or
        "PACKAGING_CAPACITY_EXCEEDED" or "PRINT_JOB_STATE_CONFLICT" or
        "PRINT_JOB_STATION_MISMATCH" or "INSPECTION_LOT_CLOSED" or "INSPECTION_SAMPLE_MISMATCH" or
        "INSPECTION_RESULTS_REQUIRED" or "INSPECTION_DISPOSITION_NOT_REQUIRED" or "REWORK_STATE_CONFLICT" or
        "REWORK_ROUTE_INCOMPLETE" or "SHIPMENT_ORDER_MISMATCH" or "SHIPMENT_STATE_CONFLICT" or
        "SHIPMENT_CARTON_INVALID" or "QUALITY_HOLD" or "CARTON_ALREADY_SHIPPED" or
         "SHIPMENT_QUANTITY_MISMATCH" or "ORDER_NOT_ARCHIVABLE" or "ORDER_STATE_CONFLICT" or
         "IDENTIFIER_ORDER_MISMATCH" or "NUMBER_STATUS_CONFLICT" or "WEIGHT_UNIT_MISMATCH" or
         "WRITE_TASK_STATE_CONFLICT" or "WRITE_TASK_STATION_MISMATCH" => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status400BadRequest
    };
}
