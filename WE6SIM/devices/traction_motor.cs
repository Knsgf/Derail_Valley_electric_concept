// Distributed under terms and conditions of CC0 licence. See LICENCE_CC0.txt for details.

using System.Collections.Generic;

using UnityEngine;

using LocoSim.Implementations;

using electric_sim.circuit_sim;
using electric_sim.utilities;

namespace electric_sim.devices;

internal class traction_motor
{
    const float max_flux = 300.0f, min_flux = 1.0f, kickstarter_winding_flux = 10.0f;
    const float kickstarter_on_maximum_current = 50.0f, kickstarter_off_minimum_current = 100.0f;
    const float torque_factor = 0.0347f, EMF_factor = 0.003634f;

    private readonly Port   _wheel_RPM;
    private readonly string _armature_name, _field1_name, _field2_name;
    private readonly float  _armature_resistance, _field1_resistance, _field2_resistance, _torque_factor, _EMF_factor;

    private bool _dynamic_brake_kickstarter_winding_on = false;

    public const float gear_ratio = 5.36f, internal_resistance = 0.21f, armature_part = 0.65f, field_partitioning = 0.75f;

    public float RPM           { get; private set; }
    public float wheel_torque  { get; private set; }
    public float load_current  { get; private set; }
    public float field_current { get; private set; }
    public float EMF           { get; private set; }
    public float heat_emission { get; private set; }

    //private readonly int _motor_number;
    private readonly bool _has_kickstarter;

    public traction_motor(int motor_number, float torque_multiplier, float EMF_multiplier, 
        Port wheel_RPM, Dictionary<string, circuit.branch_user> named_branches)
    {
        _wheel_RPM = wheel_RPM;
        assert.test(motor_number >= 1 && motor_number <= 6);
        _armature_name = $"MA{motor_number}";
        _field1_name   = $"MF{motor_number}a";
        _field2_name   = $"MF{motor_number}b";

        assert.test(named_branches[_armature_name].closed_conductance > 0.0f 
                 && named_branches[  _field1_name].closed_conductance > 0.0f
                 && named_branches[  _field1_name].closed_conductance > 0.0f);
        _armature_resistance = named_branches[_armature_name].closed_resistance;
        _field1_resistance   = named_branches[  _field1_name].closed_resistance;
        _field2_resistance   = named_branches[  _field2_name].closed_resistance;

        _torque_factor = torque_multiplier * torque_factor * gear_ratio;
        _EMF_factor    =    EMF_multiplier * (-EMF_factor);

        //_motor_number = motor_number;
        _has_kickstarter = (motor_number & 1) != 0;
    }
    
    public void simulate(bool rheostatic_braking, Dictionary<string, float> currents, Dictionary<string, circuit.branch_user> named_branches)
    {
        float motor_RPM = _wheel_RPM.Value * gear_ratio, armature_current, field1_current, field2_current;

        //const float test_speed = 10.0f;
        //motor_RPM = (test_speed / 3.6f) / 0.56f * (30.0f / Mathf.PI) * gear_ratio;
        
        string armature_name = _armature_name;
        load_current         = armature_current = currents[armature_name];
        field1_current                          = currents[ _field1_name];
        field_current        = field2_current   = currents[ _field2_name];
        float magnetic_flux1 = (min_flux + Mathf.Clamp(Mathf.Abs(field1_current), 0.0f, max_flux - min_flux)) * (1.0f - field_partitioning);
        float magnetic_flux2 = (min_flux + Mathf.Clamp(Mathf.Abs(field2_current), 0.0f, max_flux - min_flux)) *         field_partitioning;
        if (field1_current < 0.0f)
            magnetic_flux1 = -magnetic_flux1;
        if (field2_current < 0.0f)
            magnetic_flux2 = -magnetic_flux2;
        float magnetic_flux    = magnetic_flux1 + magnetic_flux2;
        float absolute_current = Mathf.Abs(armature_current);
        if (!rheostatic_braking || absolute_current >= kickstarter_off_minimum_current)
            _dynamic_brake_kickstarter_winding_on = false;
        else if (absolute_current < kickstarter_on_maximum_current)
            _dynamic_brake_kickstarter_winding_on = _has_kickstarter;
        if (_dynamic_brake_kickstarter_winding_on)
            magnetic_flux += kickstarter_winding_flux;

        float  motor_EMF     = _EMF_factor * magnetic_flux * motor_RPM;
        EMF                  = named_branches[armature_name].EMF = named_branches[armature_name].EMF * 0.7f + motor_EMF * 0.3f;
        RPM                  = motor_RPM;
        wheel_torque         = _torque_factor * armature_current * magnetic_flux;
        heat_emission        = armature_current * armature_current * _armature_resistance 
                             +   field1_current *   field1_current *   _field1_resistance
                             +   field2_current *   field2_current *   _field2_resistance;

        /*
        if (_motor_number == 1)
        {
            Main.diagnostics?.Value = armature_current;
        }
        else if (_motor_number == 2)
            Main.diagnostics2?.Value = armature_current;
        */
    }
}
