// Distributed under terms and conditions of CC0 licence. See LICENCE_CC0.txt for details.

using System;
using System.Collections.Generic;

using LocoSim.Implementations;

namespace WE6SIM.utilities;

internal static class sensor_grabber
{
    public static Fuse grab_fuse(Dictionary<string, Fuse> fuses, string name)
    {
        if (!fuses.TryGetValue(name, out Fuse fuse))
            throw new ArgumentException("No fuse " + name);
        return fuse;
    }

    public static Port grab_port(Dictionary<string, Port> ports, string name)
    {
        if (!ports.TryGetValue(name, out Port port))
            throw new ArgumentException("No port " + name);
        return port;
    }
}
