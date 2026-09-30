using System;
using System.Collections.Generic;
using System.Globalization;

namespace PcKvm
{
    public sealed class WirelessEndpoint
    {
        public readonly string Address;
        public readonly int Port;
        WirelessEndpoint(string address, int port) { Address = address; Port = port; }

        public static bool TryParse(string value, out WirelessEndpoint endpoint)
        {
            endpoint = null;
            if (String.IsNullOrEmpty(value) || value != value.Trim()) return false;
            string[] parts = value.Split(':');
            if (parts.Length != 2) return false;
            string[] octets = parts[0].Split('.');
            if (octets.Length != 4) return false;
            for (int i = 0; i < octets.Length; i++)
            {
                int n;
                if (octets[i].Length == 0 || octets[i].Length > 3) return false;
                foreach (char c in octets[i]) if (c < '0' || c > '9') return false;
                if (!int.TryParse(octets[i], NumberStyles.None, CultureInfo.InvariantCulture, out n) || n > 255) return false;
            }
            if (parts[1].Length == 0 || parts[1].Length > 5) return false;
            foreach (char c in parts[1]) if (c < '0' || c > '9') return false;
            int port;
            if (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out port) || port < 1 || port > 65535) return false;
            endpoint = new WirelessEndpoint(parts[0], port);
            return true;
        }
        public override string ToString() { return Address + ":" + Port.ToString(CultureInfo.InvariantCulture); }
    }

    public sealed class WirelessService
    {
        public readonly string Name;
        public readonly WirelessEndpoint Endpoint;
        public WirelessService(string name, WirelessEndpoint endpoint) { Name = name; Endpoint = endpoint; }
    }

    public static class WirelessDiscovery
    {
        public static List<WirelessService> Parse(string output)
        {
            const string serviceType = "_adb-tls-connect._tcp";
            List<WirelessService> services = new List<WirelessService>();
            if (String.IsNullOrEmpty(output)) return services;
            foreach (string raw in output.Replace("\r", "").Split('\n'))
            {
                string[] parts = raw.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2) continue;
                string name = parts[0].TrimEnd('.');
                bool combined = name.EndsWith("." + serviceType, StringComparison.Ordinal);
                bool separated = parts.Length >= 3 && parts[1].TrimEnd('.') == serviceType;
                if (!combined && !separated) continue;
                WirelessEndpoint endpoint;
                if (WirelessEndpoint.TryParse(parts[parts.Length - 1], out endpoint))
                    services.Add(new WirelessService(name, endpoint));
            }
            return services;
        }
    }
}
