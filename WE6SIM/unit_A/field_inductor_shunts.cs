using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using electric_sim.circuit_sim;

using static electric_sim.circuit_sim.circuit;

namespace electric_sim.unit_A;

internal class field_inductor_shunts
{
    private static readonly string[][] _shunt_A_resistor_names, _shunt_B_resistor_names;
    private static readonly string[]   _shunt_A_inductor_names, _shunt_B_inductor_names;

    private readonly branch_user[][] _shunt_A_resistors, _shunt_B_resistors;
    private readonly branch_user[]   _shunt_A_inductors, _shunt_B_inductors;

    static field_inductor_shunts()
    {
        _shunt_A_resistor_names = new string[unit_A_sim.motors][];
        _shunt_B_resistor_names = new string[unit_A_sim.motors][];
        _shunt_A_inductor_names = new string[unit_A_sim.motors];
        _shunt_B_inductor_names = new string[unit_A_sim.motors];
        for (int motor = 1; motor <= unit_A_sim.motors; ++motor)
        {
            string shunt_prefix = $"FR{motor}.", inductor_prefix = $"LS{motor}";
            int    motor_index  = motor - 1;
            _shunt_A_resistor_names[motor_index] = [ shunt_prefix + "1", shunt_prefix + "2" ];
            _shunt_B_resistor_names[motor_index] = [ shunt_prefix + "4", shunt_prefix + "5", shunt_prefix + "6" ];
            _shunt_A_inductor_names[motor_index] = inductor_prefix + "a";
            _shunt_B_inductor_names[motor_index] = inductor_prefix + "b";
        }
    }

    public field_inductor_shunts(Dictionary<string, branch_user> named_branches)
    {
        branch_user[][] shunt_A_resistors = _shunt_A_resistors = new branch_user[unit_A_sim.motors][];
        branch_user[][] shunt_B_resistors = _shunt_B_resistors = new branch_user[unit_A_sim.motors][];
        branch_user[]   shunt_A_inductors = _shunt_A_inductors = new branch_user[unit_A_sim.motors];
        branch_user[]   shunt_B_inductors = _shunt_B_inductors = new branch_user[unit_A_sim.motors];
        string[][] shunt_A_resistor_names = _shunt_A_resistor_names;
        string[][] shunt_B_resistor_names = _shunt_B_resistor_names;
        string[]   shunt_A_inductor_names = _shunt_A_inductor_names;
        string[]   shunt_B_inductor_names = _shunt_B_inductor_names;
        for (int motor_index = unit_A_sim.motors - 1; motor_index >= 0; --motor_index)
        {
            string[] A_group = shunt_A_resistor_names[motor_index];
            string[] B_group = shunt_B_resistor_names[motor_index];
            shunt_A_resistors[motor_index] = [ named_branches[A_group[0]], named_branches[A_group[1]] ];
            shunt_B_resistors[motor_index] = [ named_branches[B_group[0]], named_branches[B_group[1]], named_branches[B_group[2]] ];
            shunt_A_inductors[motor_index] = named_branches[shunt_A_inductor_names[motor_index]];
            shunt_B_inductors[motor_index] = named_branches[shunt_B_inductor_names[motor_index]];
        }
    }

    private float group_voltage_drop(branch_user[] series_branches)
    {
        float voltage_drop = 0.0f;
        for (int branch_index = series_branches.Length - 1; branch_index >= 0; --branch_index)
            voltage_drop += series_branches[branch_index].voltage_drop;
        return voltage_drop;
    }
    
    public void set_time_constants()
    {
        branch_user[][] shunt_A_resistors = _shunt_A_resistors;
        branch_user[][] shunt_B_resistors = _shunt_B_resistors;
        branch_user[]   shunt_A_inductors = _shunt_A_inductors;
        branch_user[]   shunt_B_inductors = _shunt_B_inductors;
        for (int motor_index = unit_A_sim.motors - 1; motor_index >= 0; --motor_index)
        {
            shunt_A_inductors[motor_index].set_time_constant(group_voltage_drop(shunt_A_resistors[motor_index]));
            shunt_B_inductors[motor_index].set_time_constant(group_voltage_drop(shunt_B_resistors[motor_index]));
        }
    }
}
