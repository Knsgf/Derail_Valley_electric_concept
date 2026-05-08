// Distributed under terms and conditions of CC0 licence. See LICENCE_CC0.txt for details.

using System.Threading.Tasks;
using UnityEngine;

using LocoSim.Implementations;
using WE6SIM.devices;

namespace WE6SIM.unit_A;

internal class blower_controller: electric_device
{
    const float acceleration_ratio = 0.95f, slowdown_ratio = 0.999f;
    const float series_3_parallel_2 = 1.0f / 3.0f, series_2_parallel_3 = 1.0f / 2.0f, parallel_6 = 1.0f;

    private readonly Port _blower_audio, _contactor_on_sound, _contactor_off_sound;
    
    private float _relative_speed = 0.0f, _line_voltage = 0.0f, _motor_current = 0.0f;
    private float _line_voltage_multiplier = 0.5f;
    private bool  _reconfiguration = false, _previously_active = false;

    public bool active { get; set; }
    public bool full_speed_mode { get; set; }
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

    public blower_controller(Fuse electric_supply, Port audio, Port contactor_on_sound, Port contactor_off_sound)
        : base("blower", electric_supply)
    {
        _blower_audio        = audio;
        _contactor_on_sound  = contactor_on_sound;
        _contactor_off_sound = contactor_off_sound;
    }

    private float voltage_divider()
    {
        if (!is_powered)
            return series_3_parallel_2;
        if (rheostatic_braking_on)
            return (line_voltage >= 975.0f) ? series_2_parallel_3 : parallel_6;
        return (full_speed_mode || motor_current >= 275.0f) ? series_2_parallel_3 : series_3_parallel_2;
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
            if (_previously_active && !_reconfiguration)
            {
                if (_line_voltage_multiplier != series_3_parallel_2)
                    switch_configuration();
                else
                    _contactor_off_sound.Value = 1.0f;
            }
            _previously_active = false;
        }
        else
        {
            if (!_previously_active)
                _contactor_on_sound.Value = 1.0f;
            _previously_active = true;
            fan_motor_voltage  = line_voltage * _line_voltage_multiplier;
            if (    rheostatic_braking_on && _line_voltage is >= 950 or <= 900
                || !rheostatic_braking_on && (   _motor_current >= 300.0f || full_speed_mode) 
                                              || _motor_current <= 250.0f && !full_speed_mode)
            {
                switch_configuration();
            }
        }

        float final_relative_speed = fan_motor_voltage / 1000.0f;
        if (_relative_speed <= final_relative_speed)
            _relative_speed = acceleration_ratio * _relative_speed + (1.0f - acceleration_ratio) * final_relative_speed;
        else
            _relative_speed =     slowdown_ratio * _relative_speed + (1.0f -     slowdown_ratio) * final_relative_speed;
        _blower_audio.Value = _relative_speed;
    }
}
