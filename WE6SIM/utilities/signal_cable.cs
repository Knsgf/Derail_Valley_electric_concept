// Distributed under terms and conditions of CC0 licence. See LICENCE_CC0.txt for details.

using System;

using LocoSim.Implementations;

namespace WE6SIM.utilities;

internal static class signal_cable
{
	public enum AB1_shift { unit_B_camshaft_LSB = 1, unit_B_overhead_supply = 5, unit_B_sidepan = 6, battery = 23 };
	public enum AB1_signals
	{
		unit_B_pantograph     = 0x1,
		unit_b_camshaft_notch = 0xF << AB1_shift.unit_B_camshaft_LSB,
		overhead_power        = 0x1 << AB1_shift.unit_B_overhead_supply,
		unit_B_sidepan        = 0x1 << AB1_shift.unit_B_sidepan,
		battery = 0x1 << AB1_shift.battery
	};
	public enum BA1_shift { unit_b_camshaft_lsb = 0 };
	public enum BA1_signals
	{
		unit_b_camshaft_notch = 0x7 << BA1_shift.unit_b_camshaft_lsb
	}

	private static void check_signal_mask(int signal_mask)
	{
		if (signal_mask < 0 || signal_mask >= (1 << 24))
			throw new ArgumentOutOfRangeException("Signal bits cannot go beyond bit #23");
	}

	public static void toggle_port_signal(Port port, int signal_mask, bool toggle_on)
	{
		check_signal_mask(signal_mask);
		int current_setting = (int) port.Value;
		if (toggle_on)
			current_setting |= signal_mask;
		else
			current_setting &= ~signal_mask;
		port.Value = current_setting;
	}

	public static void set_port_signal(Port port, int signal_mask, int signal_shift, int signal_value)
	{
		check_signal_mask(signal_mask);
		signal_value <<= signal_shift;
		if (signal_value < 0 || signal_value > signal_mask)
			throw new ArgumentOutOfRangeException("Signal value doesn't fit into allocated bits");
		int signal_field = ((int) port.Value) & ~signal_mask;
		port.Value = signal_field | signal_value;
	}

	/*
	public static bool port_signal_active(Port port, int signal_mask)
	{
		return port_value_signal_active(port.Value, signal_mask);
	}
	*/

	public static bool port_value_signal_active(float port_value, int signal_mask)
	{
		check_signal_mask(signal_mask);
		return (((int) port_value) & signal_mask) != 0;
	}

	public static int extract_signal_from_port_value(float port_value, int signal_mask, int signal_shift)
	{
		check_signal_mask(signal_mask);
		int shifted_value = ((int) port_value) & signal_mask;
		return shifted_value >> signal_shift;
	}
}
