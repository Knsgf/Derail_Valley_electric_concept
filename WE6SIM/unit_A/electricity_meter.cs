// Distributed under terms and conditions of CC0 licence. See LICENCE_CC0.txt for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using LocoSim.Implementations;

using UnityEngine;

using WE6SIM.catenary_editor;
using WE6SIM.utilities;

namespace WE6SIM.unit_A;

internal class electricity_meter
{
    const float minimum_current = 10.0f, energy_unit_price = 7.5f * 2.0f;
    
    private readonly Port  _consumption, _regeneration, _energy, _leftover_bank, _powertrain;
    private readonly float _zero_energy, _negative_zero_energy, _usage_factor;

    private float _last_integrity;

    public electricity_meter(Dictionary<string, Port> ports)
    {
        _consumption   = sensor_grabber.grab_port(ports, "[ElectricityMeter].CONSUME_EXT_IN" );
        _regeneration  = sensor_grabber.grab_port(ports, "[ElectricityMeter].REFILL_EXT_IN"  );
        _energy        = sensor_grabber.grab_port(ports, "[ElectricityMeter].AMOUNT"         );
        _zero_energy   = sensor_grabber.grab_port(ports, "[ElectricityMeter].CAPACITY"       ).Value;
        _leftover_bank = sensor_grabber.grab_port(ports, "[LeftoverMeter].EXT_IN"            );
        _powertrain    = sensor_grabber.grab_port(ports, "[CustomSimulation].MOTOR_INTEGRITY");

        _negative_zero_energy = -_zero_energy;
        _last_integrity       = _powertrain.Value;
        _usage_factor         = (editor_settings.kWh_price / energy_unit_price) / (1000.0f * 3600.0f);
    }

    private float add_with_remainder(float a, float b, ref float remainder)
    {
        float sum           = a + b + remainder;
        float new_remainder = sum - a;
        new_remainder      -= b;
        remainder          -= new_remainder;
        return sum;
    }
    
    public void count_energy(float voltage, float current)
    {
        if (_powertrain.Value - _last_integrity > 0.01f)
        {
            if (_leftover_bank.Value < 0.0f)
                _consumption.Value = -_leftover_bank.Value;
            _leftover_bank.Value = 0.0f;
        }
        _last_integrity = _powertrain.Value;
        if (Mathf.Abs(current) < minimum_current)
            return;
        float energy_added  = (voltage * (-current)) * _usage_factor * Time.deltaTime, energy_remainder = 0.0f;
        float current_level = _energy.Value;
        float total_energy  = add_with_remainder(current_level, _leftover_bank.Value, ref energy_remainder);
        total_energy        = add_with_remainder( total_energy,         energy_added, ref energy_remainder);
        if (total_energy > _zero_energy || total_energy == _zero_energy && energy_remainder > 0.0f)
        {
            _regeneration.Value  = _zero_energy - current_level;
            float banked_energy  = add_with_remainder(total_energy, _negative_zero_energy, ref energy_remainder);
            _leftover_bank.Value = banked_energy + energy_remainder;
        }
        else
        {
            float difference = total_energy - current_level;
            if (difference <= 0.0f)
                _consumption.Value  = -difference;
            else
                _regeneration.Value =  difference;
            _leftover_bank.Value = energy_remainder;
        }
        Main.diagnostics?.Value = _zero_energy - _energy.Value;
        Main.diagnostics2?.Value = _leftover_bank.Value;
    }
}
