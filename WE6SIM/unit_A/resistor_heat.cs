// Distributed under terms and conditions of CC0 licence. See LICENCE_CC0.txt for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using LocoSim.Implementations;

using WE6SIM.utilities;

namespace WE6SIM.unit_A;

internal class resistor_heat
{
    const int groups = 3, resistors_in_group = 7;
    
    private static readonly string[][] _resistor_groups = new string[groups][];

    private readonly float[][] _resistances = new float[groups][];
    private readonly Port _heat_emission;

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
        _heat_emission = sensor_grabber.grab_port(ports, "[CustomSimulation].RESISTOR_HEAT");
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
}
