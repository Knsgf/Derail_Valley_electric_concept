// Distributed under terms and conditions of CC0 licence. See LICENCE_CC0.txt for details.

using System.Collections.Generic;
using System.Threading.Tasks;

using UnityEngine;

using DV.Simulation.Brake;
using LocoSim.Implementations;

using electric_sim.devices;
using electric_sim.utilities;

using static electric_sim.utilities.signal_cable;

namespace electric_sim.unit_B;

internal class battery_panel: electric_device
{
    const float auxiliary_air_pressure_buildup_per_second = 1.0f / 5.0f;
    
    public const float battery_EMF = 120.0f, battery_internal_resistance = 0.4f;
    
    private readonly Fuse _appliances, _control_air;
    private readonly Port _control_BA1, _control_air_valve, _auxiliary_compressor_switch, _jogging_switch;
    private readonly Port _battery_voltmeter, _jogging_voltage, _control_air_pressure;

    private readonly BrakeSystem _main_reservoir_connection;

    private Task? _auxiliary_compressor_running = null;
    
    public battery_panel(Dictionary<string, Fuse> fuses, Dictionary<string, Port> ports, BrakeSystem air_brakes)
        : base("Battery panel")
    {
        _control_air = sensor_grabber.grab_fuse(fuses, "fusebox.CONTROL_AIR"     );
        _appliances  = sensor_grabber.grab_fuse(fuses, "fusebox.ELECTRONICS_MAIN");
        set_up_fuses(_appliances);
        power_supply_toggled += battery_toggle;
        
        _control_BA1                 = sensor_grabber.grab_port(ports, "[internal_MU].CONTROL_BA1"          );
        _auxiliary_compressor_switch = sensor_grabber.grab_port(ports, "[BatteryPanel].AUXILIARY_COMPRESSOR");
        _control_air_valve           = sensor_grabber.grab_port(ports, "[PantographAirValve].EXT_IN"        );
        _jogging_switch              = sensor_grabber.grab_port(ports, "[Jogging].EXT_IN"                   );
        _battery_voltmeter           = sensor_grabber.grab_port(ports, "[BatteryPanel].BATTERY_VOLTAGE"     );
        _jogging_voltage             = sensor_grabber.grab_port(ports, "[BatteryPanel].JOG_VOLTS"           );
        _control_air_pressure        = sensor_grabber.grab_port(ports, "[PantographAir].EXT_IN"             );
        _control_air_valve.ValueUpdatedInternally    += control_air_toggle;
        _control_air_pressure.ValueUpdatedInternally += control_air_toggle;
        _jogging_switch.ValueUpdatedInternally       += jog_toggle;
        _jogging_voltage.ValueUpdatedInternally      += jog_voltage;

        _main_reservoir_connection         = air_brakes;
        air_brakes.MainResPressureChanged += main_reservoir_to_auxiliary_check_valve;
    }
    
    private float battery_voltage(bool auxiliay_compressor_running)
    {
        if (!is_powered)
            return (_jogging_switch.Value < 0.5f) ? 0.0f : _jogging_voltage.Value;
        return battery_EMF - (auxiliay_compressor_running ? 20.0f : 10.0f) * battery_internal_resistance;
    }

    private async Task run_auxiliary_compressor()
    {
        while (is_powered && _control_air_pressure.Value < 4.0f)
        {
            _auxiliary_compressor_switch.Value = 1.0f;
            await Task.Delay(100);
            _control_air_pressure.Value += auxiliary_air_pressure_buildup_per_second / 10.0f;
        }
        _auxiliary_compressor_switch.Value = 0.0f;
        _battery_voltmeter.Value           = battery_voltage(false);
    }
    
    private void battery_toggle(bool turned_on)
    {
        jog_toggle(turned_on ? 0.0f : _jogging_switch.Value);
        _auxiliary_compressor_running = run_auxiliary_compressor();
        _battery_voltmeter.Value      = battery_voltage(!_auxiliary_compressor_running.IsCompleted);
        toggle_port_signal(_control_BA1, (int) BA1_signals.battery, turned_on);
    }

    private void control_air_toggle(float _)
    {
        bool sufficient_pressure = _control_air_valve.Value >= 0.5f && _control_air_pressure.Value >= 3.0f;
        _control_air.ChangeState(sufficient_pressure);
        toggle_port_signal(_control_BA1, (int) BA1_signals.control_air_usable, sufficient_pressure);
    }

    private void main_reservoir_to_auxiliary_check_valve(float _, float pressure)
    {
        float connection_pressure = Mathf.Clamp(_main_reservoir_connection.mainReservoirPressure - 1.01325f, 0.0f, 5.0f);
        if (_control_air_pressure.Value < connection_pressure)
            _control_air_pressure.Value = connection_pressure;
    }

    private void jog_toggle(float jog_switch)
    {
        toggle_port_signal(_control_BA1, (int) BA1_signals.jog, jog_switch >= 0.5f && !is_powered);
    }

    private void jog_voltage(float _)
    {
        _battery_voltmeter.Value = battery_voltage(false);
    }

	public override void Dispose()
	{
		base.Dispose();
        power_supply_toggled                              -= battery_toggle;
        _control_air_valve.ValueUpdatedInternally         -= control_air_toggle;
        _control_air_pressure.ValueUpdatedInternally      -= control_air_toggle;
        _jogging_switch.ValueUpdatedInternally            -= jog_toggle;
        _jogging_voltage.ValueUpdatedInternally           -= jog_voltage;
        _main_reservoir_connection.MainResPressureChanged -= main_reservoir_to_auxiliary_check_valve;
	}
}
