using System;
using System.Collections.Generic;
using System.Net;
using BarTenderPrinter;
using Xunit;

namespace BarTenderPrinter.Tests
{
    public sealed class EndpointCollectorTests
    {
        [Fact]
        public void CollectFiltersUnsafeAndInactiveAddressesAndPrioritizesLastSuccess()
        {
            var source = new FakeInterfaceSource(new[]
            {
                new LocalNetworkInterface
                {
                    InterfaceType = "Ethernet",
                    IsUp = true,
                    HasDefaultRoute = true,
                    Addresses = new[]
                    {
                        IPAddress.Parse("192.168.1.8"),
                        IPAddress.Parse("10.0.0.4"),
                        IPAddress.Loopback,
                        IPAddress.Parse("169.254.3.4"),
                        IPAddress.Parse("224.0.0.1"),
                        IPAddress.Parse("fe80::1")
                    }
                },
                new LocalNetworkInterface
                {
                    InterfaceType = "Wireless80211",
                    IsUp = false,
                    HasDefaultRoute = true,
                    Addresses = new[] { IPAddress.Parse("192.168.2.8") }
                },
                new LocalNetworkInterface
                {
                    InterfaceType = "Ethernet",
                    IsUp = true,
                    HasDefaultRoute = false,
                    Addresses = new[] { IPAddress.Parse("10.2.3.4") }
                }
            });

            var result = new LocalEndpointCollector(source).Collect("10.0.0.4");

            Assert.Equal(2, result.Count);
            Assert.Equal("10.0.0.4", result[0].Value);
            Assert.Equal(1000, result[0].Priority);
            Assert.Equal("192.168.1.8", result[1].Value);
        }

        private sealed class FakeInterfaceSource : ILocalNetworkInterfaceSource
        {
            private readonly IReadOnlyList<LocalNetworkInterface> _interfaces;

            public FakeInterfaceSource(IReadOnlyList<LocalNetworkInterface> interfaces) => _interfaces = interfaces;

            public IReadOnlyList<LocalNetworkInterface> GetInterfaces() => _interfaces;
        }
    }
}
