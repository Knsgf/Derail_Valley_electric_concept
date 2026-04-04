// Distributed under terms and conditions of CC0 licence. See LICENCE_CC0.txt for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using LocoSim.Implementations;

namespace WE6SIM.utilities;

internal static class signal_cable
{
	public enum AB1_signals { back_pantograph = 1 };

	public static void toggle_port_signal(Port port, int signal_mask, bool toggle_on)
	{
		if (signal_mask < 0 || signal_mask >= (1 << 24))
			throw new ArgumentOutOfRangeException("Signal bits cannot go beyond bit #23");
		int current_setting = (int) port.Value;
		if (toggle_on)
			current_setting |= signal_mask;
		else
			current_setting &= ~signal_mask;
		port.Value = current_setting;
	}

	public static bool port_signal_active(Port port, int signal_mask)
	{
		return port_value_signal_active(port.Value, signal_mask);
	}

	public static bool port_value_signal_active(float port_value, int signal_mask)
	{
		if (signal_mask < 0 || signal_mask >= (1 << 24))
			throw new ArgumentOutOfRangeException("Signal bits cannot go beyond bit #23");
		return (((int) port_value) & signal_mask) != 0;
	}
}
