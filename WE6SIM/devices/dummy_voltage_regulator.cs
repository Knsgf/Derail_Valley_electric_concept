// Distributed under terms and conditions of CC0 licence. See LICENCE_CC0.txt for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using DV.ServicePenalty;

using LocoSim.Implementations;

using UnityEngine;

using WE6SIM.utilities;

namespace WE6SIM.devices;

// Receives traction motor heat emission from the main simulation and feeds input voltage
// to vanilla TractionMotorSet in such a way so that its heat generation matches
internal class dummy_voltage_regulator
{
    private readonly Port _effective_voltage_drop, _effective_resistance, _functional_motors;
    
    public dummy_voltage_regulator(Dictionary<string, Port> ports)
    { 
        _effective_voltage_drop = sensor_grabber.grab_port(ports, "[DummyVoltageRegulator].EFFECTIVE_VOLTAGE_DROP");
        _effective_resistance   = sensor_grabber.grab_port(ports, "tm.SINGLE_MOTOR_EFFECTIVE_RESISTANCE");
        _functional_motors      = sensor_grabber.grab_port(ports, "tm.WORKING_TRACTION_MOTORS"          );
    }

    public void simulate(float traction_motor_heat_emission)
    {
        float effective_resistance       = _effective_resistance.Value, dummy_voltage = 0.0f;
        float single_motor_heat_emission = 0.0f;
        if (_functional_motors.Value > 0.0f)
            single_motor_heat_emission = traction_motor_heat_emission / _functional_motors.Value;
        if (!float.IsNaN(effective_resistance) && !float.IsInfinity(effective_resistance))
            dummy_voltage = effective_resistance * Mathf.Sqrt(single_motor_heat_emission / traction_motor.internal_resistance);
        _effective_voltage_drop.Value = dummy_voltage;
        Main.diagnostics?.Value = single_motor_heat_emission;
    }
}
