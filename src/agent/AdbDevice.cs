using System;
using System.Collections.Generic;
using System.Globalization;

namespace PcKvm
{
    public sealed class AdbDevice
    {
        public readonly string Serial;
        public readonly string State;
        public readonly int TransportId;
        public readonly bool IsUsb;
        public readonly string Model;

        AdbDevice(string serial, string state, int transportId, bool isUsb, string model)
        {
            Serial = serial; State = state; TransportId = transportId; IsUsb = isUsb; Model = model;
        }

        public bool IsOnline { get { return State == "device"; } }
        public AdbTarget Target
        {
            get { return TransportId > 0 ? AdbTarget.ForTransport(TransportId, Serial) : AdbTarget.ForSerial(Serial); }
        }

        public static List<AdbDevice> Parse(string output)
        {
            List<AdbDevice> devices = new List<AdbDevice>();
            if (String.IsNullOrEmpty(output)) return devices;
            foreach (string raw in output.Replace("\r", "").Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("List of devices attached", StringComparison.OrdinalIgnoreCase)
                    || line[0] == '*') continue;
                string[] words = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                if (words.Length < 2 || words[0].IndexOfAny(new char[] { '\r', '\n' }) >= 0) continue;
                int id = 0;
                bool usb = false;
                string model = "";
                for (int i = 2; i < words.Length; i++)
                {
                    if (words[i].StartsWith("transport_id:", StringComparison.Ordinal))
                        int.TryParse(words[i].Substring(13), NumberStyles.None, CultureInfo.InvariantCulture, out id);
                    else if (words[i].StartsWith("usb:", StringComparison.Ordinal)) usb = true;
                    else if (words[i].StartsWith("model:", StringComparison.Ordinal)) model = words[i].Substring(6);
                }
                devices.Add(new AdbDevice(words[0], words[1], id, usb, model));
            }
            return devices;
        }
    }

    public sealed class AdbTarget
    {
        public readonly string Serial;
        public readonly int TransportId;
        AdbTarget(string serial, int transportId) { Serial = serial; TransportId = transportId; }
        public static AdbTarget ForSerial(string serial)
        {
            if (String.IsNullOrWhiteSpace(serial) || serial.IndexOfAny(new char[] { '\r', '\n', '\0', ' ', '\t' }) >= 0)
                throw new ArgumentException("Invalid adb serial", "serial");
            return new AdbTarget(serial, 0);
        }
        public static AdbTarget ForTransport(int id, string serial)
        {
            if (id <= 0) throw new ArgumentOutOfRangeException("id");
            return new AdbTarget(ForSerial(serial).Serial, id);
        }
    }
}
