// Distributed under terms and conditions of CC0 licence. See LICENCE_CC0.txt for details.

using UnityEngine;

using LocoSim.Implementations;

using WE6SIM.devices;

namespace WE6SIM.unit_A;

internal partial class unit_A_sim
{
    private class exciter
	{
        const float max_exciter_voltage = 75.0f, min_exciter_voltage = 25.0f, max_exciter_current = 2000.0f, exciter_efficiency = 0.8f;
        const float max_exciter_power = max_exciter_voltage * max_exciter_current;
        const float min_exciter_power = min_exciter_voltage * max_exciter_current / 2.0f;
        const float nominal_supply = 1500.0f, minimum_supply = 1200.0f, speedup_rate = 0.1f, slowdown_rate = 0.9f;

        private unit_A_sim      _unit;
        private auxiliary_motor _drive;
        
        public float relative_speed { get; private set; } = 0.0f;
        public float current_draw   { get; private set; } = 0.0f;

        public exciter(unit_A_sim unit, Port audio)
        {
            _unit  = unit;
            _drive = new(audio, max_exciter_power, exciter_efficiency, nominal_supply, minimum_supply, 
                speedup_rate, speedup_rate, slowdown_rate);
        }

		public void simulate(bool regenerative_on, int field_handle_postion, float line_voltage, float motors_volts)
        {
            unit_A_sim unit = _unit;
            float exciter_EMF;
            if (!regenerative_on || !unit._main_breaker_closed.State || !unit._contactors._voltmeter.engaged)
            {
                _drive.run(0.0f);
                exciter_EMF    = 0.0f;
                relative_speed = _drive.relative_speed;
            }
            else
            {
                _drive.run(line_voltage);
                float raw_field_position = field_handle_postion / control_stand.field_handle_last_notch;
                float voltage_adjust = (1.0f - motors_volts / line_voltage) * max_exciter_voltage;
                exciter_EMF = Mathf.Clamp(min_exciter_voltage * (1.0f - raw_field_position) 
                    + max_exciter_voltage * raw_field_position + voltage_adjust, min_exciter_voltage, max_exciter_voltage);
                float exciter_power  = unit._currents["EXT"] * exciter_EMF;
                float handle_power   = Mathf.LerpUnclamped(min_exciter_power, max_exciter_power, field_handle_postion / control_stand.field_handle_last_notch);
                float relative_speed = _drive.relative_speed;
                if (relative_speed < 1.0f)
                    handle_power *= relative_speed;
                if (exciter_power > handle_power)
                    exciter_EMF *= handle_power / exciter_power;
                this.relative_speed = relative_speed;
            }
            unit._named_branches["EXT"].EMF = exciter_EMF;
            current_draw = _drive.current_draw;
        }
    }
}
