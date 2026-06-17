// Distributed under terms and conditions of CC0 licence. See LICENCE_CC0.txt for details.

using System.Threading.Tasks;
using UnityEngine;

using LocoSim.Implementations;
using WE6SIM.devices;

namespace WE6SIM.unit_A;

internal class blower_controller: electric_device
{
    const float full_motor_cooling_power_at_1C = 750.0f, ambient_temperature_C = 25.0f;
    const float resistor_group_continous_power = 1.0E+6f, resistor_max_temeprature_C = 950.0f;
    const float full_resistor_cooling_power_at_1C = resistor_group_continous_power / (resistor_max_temeprature_C - ambient_temperature_C);
    
    const float speedup_rate = 0.1f, slowdown_rate = 0.95f;     // Bigger is slower
    const float series_3_parallel_2 = 1.0f / 3.0f, series_2_parallel_3 = 1.0f / 2.0f, parallel_6 = 1.0f;
    const float fan_motor_power = 40.0E+3f;

    const float dynamic_braking_parallel_maximum_voltage  = 950.0f;
    const float dynamic_braking_series_minimum_voltage    = 900.0f;
    const float traction_low_speed_maximum_motor_current  = 300.0f;
    const float traction_high_speed_minimum_motor_current = 250.0f;

    private readonly Port _blower_audio, _contactor_on_sound, _contactor_off_sound;
    private readonly Port _traction_motor_temperature, _motor_cooling_rate, _resistor_temperature, _resistor_cooling_rate;
    
    private float _line_voltage = 0.0f, _motor_current = 0.0f;
    private float _line_voltage_multiplier = series_3_parallel_2;
    private bool  _reconfiguration = false, _previously_active = false;

    public bool active                { get; set; }
    public bool full_speed_mode       { get; set; }
    public bool rheostatic_braking_on { get; set; }
    public float line_voltage
    {
        get => _line_voltage;
        set => _line_voltage = Mathf.Abs(value);
    }
    public float motor_current
    {
        get => _motor_current;
        set => _motor_current = Mathf.Abs(value);
    }
    public float relative_speed { get; private set; } = 0.0f;
    public float current_draw   { get; private set; }

    public blower_controller(Fuse electric_supply, Port audio, Port traction_motor_temperature, Port motor_cooling_rate, 
        Port resistor_temperature, Port resistor_cooling_rate, 
        Port contactor_on_sound, Port contactor_off_sound): base("blower", electric_supply)
    {
        _blower_audio        = audio;
        _contactor_on_sound  = contactor_on_sound;
        _contactor_off_sound = contactor_off_sound;

        _traction_motor_temperature = traction_motor_temperature;
        _motor_cooling_rate         = motor_cooling_rate;
        _resistor_temperature       = resistor_temperature;
        _resistor_cooling_rate      = resistor_cooling_rate;
    }

    private float voltage_divider()
    {
        if (!is_powered)
            return series_3_parallel_2;
        
        if (rheostatic_braking_on)
        {
            return (line_voltage >= (dynamic_braking_parallel_maximum_voltage + dynamic_braking_series_minimum_voltage) / 2.0f) 
                ? series_2_parallel_3 : parallel_6;
        }
        
        float low_speed_setting, high_speed_setting;
        if (_line_voltage <= dynamic_braking_series_minimum_voltage)
        {
            low_speed_setting  = series_2_parallel_3;
            high_speed_setting = parallel_6;
        }
        else
        {
            low_speed_setting  = series_3_parallel_2;
            high_speed_setting = series_2_parallel_3;
        }
        return (full_speed_mode || motor_current >= (traction_high_speed_minimum_motor_current + traction_low_speed_maximum_motor_current) / 2.0f) 
            ? high_speed_setting : low_speed_setting;
    }

    private async void switch_configuration()
    {
        if (_reconfiguration || voltage_divider() == _line_voltage_multiplier)
            return;
        _reconfiguration           = true;
        _contactor_off_sound.Value = 1.0f;
        await Task.Delay(1000);
        _line_voltage_multiplier  = voltage_divider();
        _contactor_on_sound.Value = 1.0f;
        _reconfiguration          = false;
    }
    
    public void simulate()
    {
        check_if_disposed();
        float fan_motor_voltage;
        if (!is_powered || (!active && !full_speed_mode) || _reconfiguration)
        {
            fan_motor_voltage = 0.0f;
            if (_line_voltage >= 500.0f)
                switch_configuration();
            if (_previously_active && !_reconfiguration)
                _contactor_off_sound.Value = 1.0f;
            _previously_active = false;
        }
        else
        {
            if (!_previously_active)
                _contactor_on_sound.Value = 1.0f;
            _previously_active = true;
            fan_motor_voltage  = _line_voltage * _line_voltage_multiplier;
            if (    rheostatic_braking_on && _line_voltage is > dynamic_braking_parallel_maximum_voltage 
                                                           or < dynamic_braking_series_minimum_voltage
                || !rheostatic_braking_on && (   _motor_current > traction_low_speed_maximum_motor_current  || full_speed_mode) 
                                              || _motor_current < traction_high_speed_minimum_motor_current && !full_speed_mode)
            {
                switch_configuration();
            }
        }

        float relative_speed       = this.relative_speed;
        float final_relative_speed = fan_motor_voltage / 1000.0f;
        float acceleration_ratio   = Mathf.Pow((relative_speed <= final_relative_speed) ? speedup_rate : slowdown_rate, Time.deltaTime);
        relative_speed             = Mathf.LerpUnclamped(final_relative_speed, relative_speed, acceleration_ratio);
        _blower_audio.Value        = this.relative_speed = relative_speed;

        if (rheostatic_braking_on)
            current_draw = 0.0f;
        else
        {
            float current = (fan_motor_voltage < 1.0f) ? 0.0f : (fan_motor_power / fan_motor_voltage) * (6 * _line_voltage_multiplier);
            if (relative_speed > 0.0f)
                current *= Mathf.Min(7.0f, final_relative_speed / relative_speed);
            current_draw = Mathf.LerpUnclamped(current_draw, current, 0.1f);
        }

        _motor_cooling_rate.Value    = relative_speed * (ambient_temperature_C - _traction_motor_temperature.Value) * full_motor_cooling_power_at_1C;
        _resistor_cooling_rate.Value = relative_speed * (ambient_temperature_C -       _resistor_temperature.Value) * full_resistor_cooling_power_at_1C;
    }
}
