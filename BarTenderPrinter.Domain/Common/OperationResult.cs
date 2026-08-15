namespace BarTenderPrinter.Domain.Common;

public static class ErrorCodes
{
    public const string ValidationFailed = "VALIDATION_FAILED";
    public const string Conflict = "CONFLICT";
    public const string NotFound = "NOT_FOUND";
    public const string Unauthorized = "UNAUTHORIZED";
    public const string Forbidden = "FORBIDDEN";
    public const string Uncertain = "UNCERTAIN";
}

public sealed record OperationError(string Code, string Message, bool Retryable = false);

public sealed record OperationResult<T>
{
    public bool IsSuccess { get; init; }
    public T? Value { get; init; }
    public OperationError? Error { get; init; }

    public static OperationResult<T> Success(T value) => new()
    {
        IsSuccess = true,
        Value = value
    };

    public static OperationResult<T> Failure(string code, string message, bool retryable = false)
    {
        if (string.IsNullOrWhiteSpace(code)) throw new ArgumentException("错误码不能为空。", nameof(code));
        if (string.IsNullOrWhiteSpace(message)) throw new ArgumentException("错误消息不能为空。", nameof(message));

        return new OperationResult<T>
        {
            Error = new OperationError(code.Trim(), message.Trim(), retryable)
        };
    }
}
