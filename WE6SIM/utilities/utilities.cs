// Distributed under terms and conditions of CC0 licence. See LICENCE_CC0.txt for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using LocoSim.Implementations;

namespace WE6SIM;

internal static class utilities
{
	public static Fuse get_fuse(Dictionary<string, Fuse> fuses, string name)
	{
		if (!fuses.TryGetValue(name, out Fuse fuse))
			throw new ArgumentException("No fuse " + name);
		return fuse;
	}

	public static Port get_port(Dictionary<string, Port> ports, string name)
	{
		if (!ports.TryGetValue(name, out Port port))
			throw new ArgumentException("No port " + name);
		return port;
	}

}
