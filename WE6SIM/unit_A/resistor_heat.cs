// Distributed under terms and conditions of CC0 licence. See LICENCE_CC0.txt for details.

using System;
using System.Collections.Generic;

using UnityEngine;

using LocoSim.Implementations;

using WE6SIM.utilities;

namespace WE6SIM.unit_A;

internal class resistor_heat
{
    const int   groups = 3, resistors_in_group = 7;
    const float overheat_damage_per_degree_second = 0.5f, maximum_resistor_temperature = 900.0f;
    
    private static readonly string[][] _resistor_groups = new string[groups][];

    private readonly float[][] _resistances = new float[groups][];
    private readonly Port _heat_emission, _resistor_temperature, _resistor_damage;

    static resistor_heat()
    {
        for (int current_group = 1; current_group <= groups; ++current_group)
        {
            _resistor_groups[current_group - 1] = new string[resistors_in_group];
            for (int current_resitor = 1; current_resitor < resistors_in_group; ++current_resitor)
                _resistor_groups[current_group - 1][current_resitor - 1] = $"SR{current_group}.{current_resitor}";
            _resistor_groups[current_group - 1][resistors_in_group - 1] = $"SR{current_group}S";
        }
    }

    public resistor_heat(Dictionary<string, Port> ports, Dictionary<string, float> element_resistances)
    {
        _heat_emission        = sensor_grabber.grab_port(ports, "[CustomSimulation].RESISTOR_HEAT"  );
        _resistor_damage      = sensor_grabber.grab_port(ports, "[CustomSimulation].RESISTOR_DAMAGE");
        _resistor_temperature = sensor_grabber.grab_port(ports, "[ResistorHeat].TEMPERATURE"        );
        for (int current_group = 0; current_group < groups; ++current_group)
        {
            _resistances[current_group] = new float[resistors_in_group];
            for (int current_resitor = 0; current_resitor < resistors_in_group; ++current_resitor)
                _resistances[current_group][current_resitor] = element_resistances[_resistor_groups[current_group][current_resitor]];
        }
    }

    public void simulate(Dictionary<string, float> currents)
    {
        float maximum_heat_emission = 0.0f;
        for (int current_group_index = groups - 1; current_group_index >= 0; --current_group_index)
        {
            string[] current_group               = _resistor_groups[current_group_index];
            float [] current_group_resistances   =     _resistances[current_group_index];
            float    current_group_heat_emission = 0.0f;
            for (int resistor_index = resistors_in_group - 1; resistor_index >= 0; --resistor_index)
            {
                float current                = currents[current_group[resistor_index]];
                current_group_heat_emission += current * current * current_group_resistances[resistor_index];
            }
            if (maximum_heat_emission < current_group_heat_emission)
                maximum_heat_emission = current_group_heat_emission;
        }
        _heat_emission.Value = maximum_heat_emission;
    }

    public static void simulate_overheat_damage(resistor_heat? simulation = null, Port? damage_per_frame = null, float resistor_temperature = 0.0f)
    {
        if (simulation != null)
        {
            resistor_temperature = simulation._resistor_temperature.Value;
            damage_per_frame     = simulation._resistor_damage;
        }
        else if (damage_per_frame == null)
            throw new ArgumentNullException("Damage port not specified");
        damage_per_frame.Value = (resistor_temperature <= maximum_resistor_temperature)
			? 0.0f : ((resistor_temperature - maximum_resistor_temperature) * overheat_damage_per_degree_second * Time.deltaTime);
	}
}
