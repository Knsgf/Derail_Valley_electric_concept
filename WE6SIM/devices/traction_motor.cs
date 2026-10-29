// Distributed under terms and conditions of CC0 licence. See LICENCE_CC0.txt for details.

using System;
using System.Collections.Generic;

using UnityEngine;

using electric_sim.utilities;

using static electric_sim.circuit_sim.circuit;

namespace electric_sim.devices;

public class traction_motor
{
    const float max_flux = 300.0f, min_flux = 1.0f, kickstarter_winding_flux = 10.0f;
    const float kickstarter_on_maximum_current = 50.0f, kickstarter_off_minimum_current = 100.0f;
    const float torque_factor = 0.0347f, EMF_factor = 0.003634f, fixed_delta_time = 1.0f / 60.0f, inductance = 0.1f;

    private readonly branch_user _armature_branch, _field1_branch, _field2_branch;
    private readonly float       _armature_resistance, _field1_resistance, _field2_resistance, _torque_factor, _EMF_factor;
    private readonly bool        _has_kickstarter;

    private bool  _dynamic_brake_kickstarter_winding_on = false;
    private float _armature_current = 0.0f, _field1_current = 0.0f, _field2_current = 0.0f, _magnetic_flux, _back_EMF;

    public const float gear_ratio = 5.36f, internal_resistance = 0.21f, armature_part = 0.65f, field_partitioning = 0.75f;
    public const float base_armature_resistance = internal_resistance * (       armature_part);   // Includes both rotor and compensating winding
    public const float base_field1_resistance   = internal_resistance * (1.0f - armature_part) * (1.0f - field_partitioning);
    public const float base_field2_resistance   = internal_resistance * (1.0f - armature_part) * (       field_partitioning);

    public float RPM           { get; private set; }
    public float wheel_torque  { get; private set; }
    public float EMF           { get; private set; }
    public float heat_emission { get; private set; }
    public float load_current  => _armature_current;
    public float field_current => _field2_current;
    public float voltage_drop  => _armature_branch.voltage_drop + _field1_branch.voltage_drop + _field2_branch.voltage_drop;

#if DEBUG
    private readonly int _motor_number;
    public static Action<string>? print;
#endif

    public traction_motor(int motor_number, float torque_multiplier, float EMF_multiplier, 
        Dictionary<string, branch_user> named_branches)
    {
        assert.test(motor_number >= 1 && motor_number <= 6);
        _armature_branch = named_branches[$"MA{motor_number}" ];
        _field1_branch   = named_branches[$"MF{motor_number}a"];
        _field2_branch   = named_branches[$"MF{motor_number}b"];

        assert.test(_armature_branch.closed_conductance > 0.0f 
                 &&   _field1_branch.closed_conductance > 0.0f
                 &&   _field2_branch.closed_conductance > 0.0f);
        _armature_resistance = _armature_branch.closed_resistance;
        _field1_resistance   =   _field1_branch.closed_resistance;
        _field2_resistance   =   _field2_branch.closed_resistance;

        _torque_factor = torque_multiplier * torque_factor * gear_ratio;
        _EMF_factor    =    EMF_multiplier * (-EMF_factor);

#if DEBUG
        _motor_number = motor_number;
#endif
        _has_kickstarter = (motor_number & 1) != 0;
    }

    private static float self_induction_EMF(float voltage_drop, float resistance, float current)
    {
        float new_current = (fixed_delta_time * voltage_drop + inductance * current) 
                          / (fixed_delta_time *   resistance + inductance          );
        return inductance /  fixed_delta_time * (current - new_current);
    }
    
    private static float self_induction_EMF(float voltage_drop, float resistance, float external_voltage_drop, 
        float current, float instant_current)
    {
        if (Mathf.Abs(instant_current) < 1.0f)
            return self_induction_EMF(voltage_drop, resistance, current);
        float new_current = (fixed_delta_time * (voltage_drop + external_voltage_drop                  ) + inductance * current) 
                          / (fixed_delta_time * (  resistance + external_voltage_drop / instant_current) + inductance          );
        return inductance /  fixed_delta_time * (current - new_current);
    }
    
    public void estalblish_EMFs(bool rheostatic_braking, float wheel_RPM)
    {
        float       motor_RPM       = RPM = wheel_RPM * gear_ratio;
        branch_user armature_branch = _armature_branch, field1_branch = _field1_branch, field2_branch = _field2_branch;
        //field1_branch.EMF = self_induction_EMF(field1_branch.voltage_drop, _field1_resistance, _field1_current);
        //field2_branch.EMF = self_induction_EMF(field2_branch.voltage_drop, _field2_resistance, _field2_current);

        float field1_current = field1_branch.current, field2_current = field2_branch.current;
        float magnetic_flux1 = (min_flux + Mathf.Clamp(Mathf.Abs(field1_current), 0.0f, max_flux - min_flux)) * (1.0f - field_partitioning);
        float magnetic_flux2 = (min_flux + Mathf.Clamp(Mathf.Abs(field2_current), 0.0f, max_flux - min_flux)) *         field_partitioning;
        //if (field1_current < 0.0f)
        //    magnetic_flux1 = -magnetic_flux1;
        //if (field2_current < 0.0f)
        //    magnetic_flux2 = -magnetic_flux2;
        float magnetic_flux = magnetic_flux1 + magnetic_flux2;

        float absolute_current = Mathf.Abs(_armature_current);
        if (!rheostatic_braking || absolute_current >= kickstarter_off_minimum_current)
            _dynamic_brake_kickstarter_winding_on = false;
        else if (absolute_current < kickstarter_on_maximum_current)
            _dynamic_brake_kickstarter_winding_on = _has_kickstarter;
        if (_dynamic_brake_kickstarter_winding_on)
            magnetic_flux += kickstarter_winding_flux;

        float back_EMF      = _EMF_factor * magnetic_flux * motor_RPM;
        //float core_EMF      = self_induction_EMF(armature_branch.voltage_drop + back_EMF, _armature_resistance, external_voltage_drop, _armature_current, armature_branch.current);
        armature_branch.EMF = EMF = /*core_EMF +*/ back_EMF;
        _magnetic_flux      = magnetic_flux;
    }

    public void establish_currents()
    {
        float armature_current = _armature_current = _armature_branch.current;
        float field1_current   = _field1_current   = _field1_branch.current;
        float field2_current   = _field2_current   = _field2_branch.current;
        wheel_torque           =   _torque_factor  * armature_current *       _magnetic_flux;
        heat_emission          = armature_current  * armature_current * _armature_resistance 
                               +   field1_current  *   field1_current *   _field1_resistance
                               +   field2_current  *   field2_current *   _field2_resistance;
        //_armature_branch.EMF = _EMF_factor * _magnetic_flux * RPM;
    }
}
