// Distributed under terms and conditions of CC0 licence. See LICENCE_CC0.txt for details.

using System;

using LocoSim.Implementations;

namespace WE6SIM.utilities;

internal static class signal_cable
{
    public enum AB1_shift 
    { 
        unit_B_pantograph = 0,
        unit_B_sidepan    = 1, 
        main_breaker      = 2,
        compressor_supply = 3, 
        sander            = 4,
        contactor_off     = 5,
        contactor_on      = 6,
        
        independent_brake     = 14,
        unit_A_camshaft_notch = 17,
        unit_B_camshaft_notch = 20, 
    };
    public enum AB1_signals
    {
        unit_B_pantograph = 0x1 << AB1_shift.unit_B_pantograph,
        unit_B_sidepan    = 0x1 << AB1_shift.unit_B_sidepan,
        main_breaker      = 0x1 << AB1_shift.main_breaker,
        compressor_power  = 0x1 << AB1_shift.compressor_supply,
        sander            = 0x1 << AB1_shift.sander,
        contactor_off     = 0x1 << AB1_shift.contactor_off,
        contactor_on      = 0x1 << AB1_shift.contactor_on,
        
        independent_brake     = 0x7 << AB1_shift.independent_brake,
        unit_A_camshaft_notch = 0x7 << AB1_shift.unit_A_camshaft_notch,
        unit_B_camshaft_notch = 0xF << AB1_shift.unit_B_camshaft_notch
    };
    public enum BA1_shift 
    { 
        cab_change     = 0,
        battery        = 1,
        control_air    = 2,
        jog            = 3,
        sander         = 4,
        breaker_engage = 5,
        breaker_trip   = 6,

        reverser              = 7,
        throttle              = 8,
        field                 = 11,
        selector              = 15,
        independent_brake     = 18,
        unit_B_camshaft_notch = 21 
    };
    public enum BA1_signals
    {
        cab_change            = 0x1 << BA1_shift.cab_change,
        battery               = 0x1 << BA1_shift.battery,
        control_air_usable    = 0x1 << BA1_shift.control_air,
        jog                   = 0x1 << BA1_shift.jog,
        sander                = 0x1 << BA1_shift.sander,
        breaker_engage         = 0x1 << BA1_shift.breaker_engage,
        breaker_trip          = 0x1 << BA1_shift.breaker_trip,
        reverser              = 0x1 << BA1_shift.reverser,
        throttle              = 0x7 << BA1_shift.throttle,
        field                 = 0xF << BA1_shift.field,
        selector              = 0x7 << BA1_shift.selector,
        independent_brake     = 0x7 << BA1_shift.independent_brake,
        unit_B_camshaft_notch = 0x7 << BA1_shift.unit_B_camshaft_notch
    }
    public enum BA2_shift
    {
        unit_B_active_motors = 0
    }
    public enum BA2_signals
    {
        unit_B_active_motors = 0x7 << BA2_shift.unit_B_active_motors
    }

    private static void check_signal_mask(int signal_mask)
    {
        if (signal_mask is < 0 or >= (1 << 24))
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
