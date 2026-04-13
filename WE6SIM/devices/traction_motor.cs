// Distributed under terms and conditions of CC0 licence. See LICENCE_CC0.txt for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

using LocoSim.Implementations;
using WE6SIM.circuit_sim;
using WE6SIM.utilities;

namespace WE6SIM.devices;

internal class traction_motor
{
    //const int nm = 3;
    
    const float max_flux = 300.0f, min_flux = 1.0f;
    const float gear_ratio = 5.36f, torque_factor = 0.0347f, EMF_factor = 0.003634f;

    private readonly Port   _wheel_RPM;
    private readonly string _armature_name, _field_name1, _field_name2;

    public const float field_partitioning = 0.63f;

    public float RPM           { get; private set; }
    public float wheel_torque  { get; private set; }
    public float load_current  { get; private set; }
    public float field_current { get; private set; }
    public float EMF           { get; private set; }

    public traction_motor(int motor_number, Port wheel_RPM)
    {
        _wheel_RPM = wheel_RPM;
        assert.test(motor_number >= 1 && motor_number <= 6);
        _armature_name = $"MA{motor_number}";
        _field_name1   = $"MF{motor_number}a";
        _field_name2   = $"MF{motor_number}b";
    }
    
    public void simulate(bool rheostatic_braking, Dictionary<string, float> currents, Dictionary<string, circuit.branch_user> named_branches)
    {
        float motor_RPM      = _wheel_RPM.Value * gear_ratio;
        field_current        = currents[_field_name2];
        float magnetic_flux1 = (min_flux + Mathf.Clamp(Mathf.Abs(currents[_field_name1]), 0.0f, max_flux - min_flux)) * (1.0f - field_partitioning);
        float magnetic_flux2 = (min_flux + Mathf.Clamp(Mathf.Abs(         field_current), 0.0f, max_flux - min_flux)) *         field_partitioning;
        float magnetic_flux  = magnetic_flux1 + magnetic_flux2;
        if (field_current < 0.0f)
        {
            if (!rheostatic_braking || motor_RPM < -10.0f || motor_RPM > 10.0f)   // Prevent round-off errors from causing current to spontaneously reverse at low speeds
                magnetic_flux = -magnetic_flux;
        }
        float motor_EMF = (-EMF_factor) * magnetic_flux * motor_RPM;
        string armature_name = _armature_name;
        EMF          = named_branches[armature_name].EMF = named_branches[armature_name].EMF * 0.7f + motor_EMF * 0.3f;
        RPM          = motor_RPM;
        load_current = currents[armature_name];
        wheel_torque = (torque_factor * gear_ratio) * load_current * magnetic_flux;
    }
}
