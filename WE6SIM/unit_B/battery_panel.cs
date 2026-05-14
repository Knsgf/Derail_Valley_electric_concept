// Distributed under terms and conditions of CC0 licence. See LICENCE_CC0.txt for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using DV.Simulation.Brake;
using LocoSim.Implementations;

using UnityEngine;

using WE6SIM.devices;
using WE6SIM.utilities;

using static UnityEngine.UI.CanvasScaler;
using static WE6SIM.utilities.signal_cable;

namespace WE6SIM.unit_B;

internal class battery_panel: electric_device
{
    const float auxiliary_air_pressure_buildup_per_second = 1.0f / 5.0f;
    
    public const float battery_internal_resistance = 0.4f;
    
    private readonly Fuse _appliances;
    private readonly Port _control_BA1, _battery_voltmeter, _control_air_pressure, _auxiliary_compressor_switch;
    
    private readonly BrakeSystem _main_reservoir_connection;

    private Task? _auxiliary_compressor_running = null;
    
    public battery_panel(Dictionary<string, Fuse> fuses, Dictionary<string, Port> ports, BrakeSystem air_brakes)
        : base("Battery panel")
    {
        _appliances = sensor_grabber.grab_fuse(fuses, "fusebox.ELECTRONICS_MAIN");
        set_up_fuses(_appliances);
        power_supply_toggled += battery_toggle;
        
        _control_BA1                 = sensor_grabber.grab_port(ports, "[internal_MU].CONTROL_BA1");
        _battery_voltmeter           = sensor_grabber.grab_port(ports, "[BatteryPanel].BATTERY_VOLTAGE");
        _control_air_pressure        = sensor_grabber.grab_port(ports, "[PantographAir].EXT_IN");
        _auxiliary_compressor_switch = sensor_grabber.grab_port(ports, "[BatteryPanel].AUXILIARY_COMPRESSOR");

        _main_reservoir_connection         = air_brakes;
        air_brakes.MainResPressureChanged += main_reservoir_to_auxiliary_check_valve;
    }
    
    private float battery_voltage(bool auxiliay_compressor_running)
    {
        if (!is_powered)
            return 0.0f;
        return 120.0f - (auxiliay_compressor_running ? 20.0f : 10.0f) * battery_internal_resistance;
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
        _auxiliary_compressor_running = run_auxiliary_compressor();
        _battery_voltmeter.Value      = battery_voltage(!_auxiliary_compressor_running.IsCompleted);
        toggle_port_signal(_control_BA1, (int) BA1_signals.battery, turned_on);
    }

    private void main_reservoir_to_auxiliary_check_valve(float _, float pressure)
    {
        float connection_pressure = Mathf.Clamp(_main_reservoir_connection.mainReservoirPressure - 1.01325f, 0.0f, 5.0f);
        if (_control_air_pressure.Value < connection_pressure)
            _control_air_pressure.Value = connection_pressure;
    }

	public override void Dispose()
	{
		base.Dispose();
        power_supply_toggled                              -= battery_toggle;
        _main_reservoir_connection.MainResPressureChanged -= main_reservoir_to_auxiliary_check_valve;
	}
}
