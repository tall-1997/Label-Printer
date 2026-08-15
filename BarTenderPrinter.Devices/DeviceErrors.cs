namespace BarTenderPrinter.Devices;

public static class DeviceErrorCodes
{
    public const string InvalidConfiguration = "DEVICE_INVALID_CONFIGURATION";
    public const string ConnectionFailed = "DEVICE_CONNECTION_FAILED";
    public const string Timeout = "DEVICE_TIMEOUT";
    public const string ProtocolError = "DEVICE_PROTOCOL_ERROR";
    public const string ExecutionFailed = "DEVICE_EXECUTION_FAILED";
    public const string ReadBackMismatch = "DEVICE_READBACK_MISMATCH";
    public const string Uncertain = "DEVICE_UNCERTAIN";
}

public sealed record DeviceError(
    string Code,
    string Message,
    bool Retryable,
    string? DiagnosticCode = null);

public sealed class DeviceAdapterException : Exception
{
    public DeviceAdapterException(DeviceError error)
        : base(error?.Message)
    {
        Error = error ?? throw new ArgumentNullException(nameof(error));
    }

    public DeviceError Error { get; }
}
