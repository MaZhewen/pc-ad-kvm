using System;
using System.Collections.Generic;

namespace PcKvm
{
    /** Immutable description of one verified deployment attempt. */
    public sealed class ConnectionProfile
    {
        public readonly long Generation;
        public readonly string Mode;
        public readonly AdbTarget Target;
        public readonly string Identity;
        public readonly string Endpoint;

        public ConnectionProfile(long generation, string mode, AdbTarget target, string identity, string endpoint)
        {
            Generation = generation; Mode = mode; Target = target; Identity = identity; Endpoint = endpoint;
        }

        public static AdbDevice ChooseUsb(IList<AdbDevice> devices, string savedSerial)
        {
            AdbDevice found = null;
            foreach (AdbDevice device in devices)
            {
                if (!device.IsOnline || !device.IsUsb) continue;
                if (!String.IsNullOrEmpty(savedSerial))
                {
                    if (device.Serial == savedSerial) return device;
                    continue;
                }
                if (found != null) return null;
                found = device;
            }
            return found;
        }

        public static AdbDevice ChooseWireless(IList<AdbDevice> devices, string identity,
                                               Func<AdbDevice, string> readIdentity)
        {
            bool ambiguous;
            return ChooseWireless(devices, identity, readIdentity, out ambiguous);
        }

        public static AdbDevice ChooseWireless(IList<AdbDevice> devices, string identity,
                                               Func<AdbDevice, string> readIdentity, out bool ambiguous)
        {
            ambiguous = false;
            if (String.IsNullOrEmpty(identity)) return null;
            AdbDevice found = null;
            foreach (AdbDevice device in devices)
            {
                if (!device.IsOnline || device.IsUsb) continue;
                if (readIdentity(device) != identity) continue;
                if (found != null) { ambiguous = true; return null; }
                found = device;
            }
            return found;
        }

        public static int BackoffMilliseconds(int baseSeconds, int failures, int jitterPercent)
        {
            long seconds = Math.Max(2, baseSeconds);
            for (int i = 0; i < failures && seconds < 60; i++) seconds = Math.Min(60, seconds * 2);
            long milliseconds = seconds * 1000;
            return (int)Math.Min(60000, milliseconds + milliseconds * Math.Max(0, Math.Min(20, jitterPercent)) / 100);
        }

        public static int UsbRecoveryAction(int failures, int level, bool allowKill)
        {
            if (failures >= 3 && level < 1) return 1;
            if (failures >= 6 && level < 2 && allowKill) return 2;
            return 0;
        }
    }
}
