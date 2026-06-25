// Distributed under terms and conditions of CC0 licence. See LICENCE_CC0.txt for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using LocoSim.Implementations;

using UnityEngine;

using WE6SIM.devices;

namespace WE6SIM.unit_A;

internal partial class unit_A_sim
{
    private class exciter
	{
        const float max_exciter_voltage = 75.0f, min_exciter_voltage = 25.0f, max_exciter_current = 2000.0f;
        const float max_exciter_power = max_exciter_voltage * max_exciter_current;
        const float min_exciter_power = min_exciter_voltage * max_exciter_current / 2.0f;

        private unit_A_sim _unit;
        
        public float relative_speed { get; private set; } = 0.0f;

        public exciter(unit_A_sim unit, Dictionary<string, Port> ports)
        {
            _unit = unit;
        }

		public void simulate(bool regenerative_on, int field_handle_postion, float line_voltage, float motors_volts)
        {
            unit_A_sim unit = _unit;
            float exciter_EMF;
            if (!regenerative_on || line_voltage < 1200.0f || !unit._main_breaker_closed.State || !unit._contactors._voltmeters.engaged)
            {
                exciter_EMF     = 0.0f;
                relative_speed *= Mathf.Pow(0.95f, Time.deltaTime);
            }
            else
            {
                float final_relative_speed = (line_voltage * line_voltage) / (1500.0f * 1500.0f);
                float acceleration_ratio   = Mathf.Pow(0.1f, Time.deltaTime);
                relative_speed             = Mathf.LerpUnclamped(final_relative_speed, relative_speed, acceleration_ratio);
                
                float raw_field_position = field_handle_postion / control_stand.field_handle_last_notch;
                float voltage_adjust = (1.0f - motors_volts / line_voltage) * max_exciter_voltage;
                exciter_EMF = Mathf.Clamp(min_exciter_voltage * (1.0f - raw_field_position) 
                    + max_exciter_voltage * raw_field_position + voltage_adjust, min_exciter_voltage, max_exciter_voltage);
                float exciter_power = unit._currents["EXT"] * exciter_EMF;
                float handle_power  = Mathf.LerpUnclamped(min_exciter_power, max_exciter_power, field_handle_postion / control_stand.field_handle_last_notch);
                if (relative_speed < 1.0f)
                    handle_power *= relative_speed;
                if (exciter_power > handle_power)
                    exciter_EMF *= handle_power / exciter_power;
            }
            unit._named_branches["EXT"].EMF = exciter_EMF;
        }
    }
}
