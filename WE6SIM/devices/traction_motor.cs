// Distributed under terms and conditions of CC0 licence. See LICENCE_CC0.txt for details.

using System.Collections.Generic;

using UnityEngine;

using LocoSim.Implementations;

using WE6SIM.circuit_sim;
using WE6SIM.utilities;

namespace WE6SIM.devices;

internal class traction_motor
{
    //const int nm = 3;
    
    const float max_flux = 300.0f, min_flux = 1.0f, kickstarter_winding_flux = 10.0f;
    const float kickstarter_on_maximum_current = 50.0f, kickstarter_off_minimum_current = 100.0f;
    const float gear_ratio = 5.36f, torque_factor = 0.0347f, EMF_factor = 0.003634f;

    private readonly Port   _wheel_RPM;
    private readonly string _armature_name, _field_name1, _field_name2;

    private bool _dynamic_brake_kickstarter_winding_on = false;

    public const float field_partitioning = 0.65f;

    public float RPM           { get; private set; }
    public float wheel_torque  { get; private set; }
    public float load_current  { get; private set; }
    public float field_current { get; private set; }
    public float EMF           { get; private set; }

    //private readonly int _motor_number;

    public traction_motor(int motor_number, Port wheel_RPM)
    {
        _wheel_RPM = wheel_RPM;
        assert.test(motor_number >= 1 && motor_number <= 6);
        _armature_name = $"MA{motor_number}";
        _field_name1   = $"MF{motor_number}a";
        _field_name2   = $"MF{motor_number}b";

        //_motor_number = motor_number;
    }
    
    public void simulate(bool rheostatic_braking, Dictionary<string, float> currents, Dictionary<string, circuit.branch_user> named_branches)
    {
        float motor_RPM = _wheel_RPM.Value * gear_ratio;
        
        field_current        = currents[_field_name2];
        float magnetic_flux1 = (min_flux + Mathf.Clamp(Mathf.Abs(currents[_field_name1]), 0.0f, max_flux - min_flux)) * (1.0f - field_partitioning);
        float magnetic_flux2 = (min_flux + Mathf.Clamp(Mathf.Abs(         field_current), 0.0f, max_flux - min_flux)) *         field_partitioning;
        float magnetic_flux  = magnetic_flux1 + magnetic_flux2;
        if (!rheostatic_braking || load_current >= kickstarter_off_minimum_current)
            _dynamic_brake_kickstarter_winding_on = false;
        else if (load_current < kickstarter_on_maximum_current)
            _dynamic_brake_kickstarter_winding_on = true;
        if (_dynamic_brake_kickstarter_winding_on)
            magnetic_flux += kickstarter_winding_flux;
        if (field_current < 0.0f)
            magnetic_flux = -magnetic_flux;

        float  motor_EMF     = (-EMF_factor) * magnetic_flux * motor_RPM;
        string armature_name = _armature_name;
        EMF                  = named_branches[armature_name].EMF = named_branches[armature_name].EMF * 0.7f + motor_EMF * 0.3f;
        RPM                  = motor_RPM;
        load_current         = currents[armature_name];
        wheel_torque         = (torque_factor * gear_ratio) * load_current * magnetic_flux;

        /*
        if (_motor_number == 1)
        {
            Main.diagnostics?.Value = (field_current == 0.0f) ? 0.0f : Mathf.Abs(magnetic_flux / field_current);
            Main.diagnostics2?.Value = Mathf.Abs(currents[_field_name1]);
        }
        */
    }
}
