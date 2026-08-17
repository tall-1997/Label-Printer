using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace BarTenderPrinter
{
    public sealed class LocalEndpointAddress
    {
        public string Value { get; init; } = "";
        public string Family { get; init; } = "";
        public string InterfaceType { get; init; } = "";
        public int Priority { get; init; }
    }

    public sealed class LocalNetworkInterface
    {
        public string InterfaceType { get; init; } = "";
        public bool IsUp { get; init; }
        public bool HasDefaultRoute { get; init; }
        public IReadOnlyList<IPAddress> Addresses { get; init; } = Array.Empty<IPAddress>();
    }

    public interface ILocalNetworkInterfaceSource
    {
        IReadOnlyList<LocalNetworkInterface> GetInterfaces();
    }

    internal sealed class SystemLocalNetworkInterfaceSource : ILocalNetworkInterfaceSource
    {
        public IReadOnlyList<LocalNetworkInterface> GetInterfaces()
        {
            return NetworkInterface.GetAllNetworkInterfaces().Select(networkInterface =>
            {
                var properties = networkInterface.GetIPProperties();
                return new LocalNetworkInterface
                {
                    InterfaceType = networkInterface.NetworkInterfaceType.ToString(),
                    IsUp = networkInterface.OperationalStatus == OperationalStatus.Up,
                    HasDefaultRoute = properties.GatewayAddresses.Any(gateway =>
                        gateway?.Address != null && !gateway.Address.Equals(IPAddress.Any) && !gateway.Address.Equals(IPAddress.IPv6Any)),
                    Addresses = properties.UnicastAddresses.Select(address => address.Address).Where(address => address != null).ToArray()
                };
            }).ToArray();
        }
    }

    public sealed class LocalEndpointCollector
    {
        private readonly ILocalNetworkInterfaceSource _source;

        public LocalEndpointCollector(ILocalNetworkInterfaceSource source = null)
        {
            _source = source ?? new SystemLocalNetworkInterfaceSource();
        }

        public IReadOnlyList<LocalEndpointAddress> Collect(string lastSuccessfulAddress = null)
        {
            var candidates = new Dictionary<string, LocalEndpointAddress>(StringComparer.OrdinalIgnoreCase);
            foreach (var networkInterface in _source.GetInterfaces().Where(item => item.IsUp && item.HasDefaultRoute))
            {
                foreach (var address in networkInterface.Addresses.Where(IsPublishable))
                {
                    var value = address.ToString();
                    var candidate = new LocalEndpointAddress
                    {
                        Value = value,
                        Family = address.AddressFamily == AddressFamily.InterNetwork ? "IPv4" : "IPv6",
                        InterfaceType = networkInterface.InterfaceType,
                        Priority = GetPriority(address, networkInterface.InterfaceType, value, lastSuccessfulAddress)
                    };
                    if (!candidates.TryGetValue(value, out var existing) || candidate.Priority > existing.Priority)
                        candidates[value] = candidate;
                }
            }

            return candidates.Values
                .OrderByDescending(item => item.Priority)
                .ThenBy(item => item.Value, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        internal static bool IsPublishable(IPAddress address)
        {
            if (address == null || IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any) ||
                address.Equals(IPAddress.None) || address.Equals(IPAddress.IPv6None)) return false;
            if (address.AddressFamily == AddressFamily.InterNetwork)
            {
                var bytes = address.GetAddressBytes();
                return !(bytes[0] == 169 && bytes[1] == 254) && bytes[0] < 224;
            }
            if (address.AddressFamily == AddressFamily.InterNetworkV6)
                return !address.IsIPv6LinkLocal && !address.IsIPv6Multicast && !address.IsIPv6SiteLocal;
            return false;
        }

        private static int GetPriority(IPAddress address, string interfaceType, string value, string lastSuccessfulAddress)
        {
            if (string.Equals(value, lastSuccessfulAddress, StringComparison.OrdinalIgnoreCase)) return 1000;
            var interfacePriority = string.Equals(interfaceType, NetworkInterfaceType.Ethernet.ToString(), StringComparison.OrdinalIgnoreCase) ? 30 :
                string.Equals(interfaceType, NetworkInterfaceType.Wireless80211.ToString(), StringComparison.OrdinalIgnoreCase) ? 20 : 10;
            if (address.AddressFamily == AddressFamily.InterNetwork)
                return (IsPrivateIpv4(address) ? 200 : 100) + interfacePriority;
            return 50 + interfacePriority;
        }

        private static bool IsPrivateIpv4(IPAddress address)
        {
            var bytes = address.GetAddressBytes();
            return bytes[0] == 10 ||
                (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
                (bytes[0] == 192 && bytes[1] == 168);
        }
    }
}
