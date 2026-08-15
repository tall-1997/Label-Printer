namespace BarTenderPrinter.Devices;

public interface IDeviceAdapter
{
    string AdapterId { get; }
    bool IsSimulation { get; }
}
