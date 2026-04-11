using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using LocoSim.Implementations;

namespace WE6SIM;

internal class blower_controller: electric_device
{
    const float acceleration_ratio = 0.95f, slowdown_ratio = 0.999f;
    const float series_6 = 1.0f / 6.0f, series_3_parallel_2 = 1.0f / 3.0f, series_2_parallel_3 = 1.0f / 2.0f, parallel_6 = 1.0f;

    private readonly Port _blower_audio, _contactor_on_sound, _contactor_off_sound;
    
    private float _relative_speed = 0.0f, _line_voltage = 0.0f, _motor_current = 0.0f;
    private float _line_voltage_multiplier = series_3_parallel_2;
    private bool  _reconfiguration = false, _previously_active = false;

    public bool active { get; set; }
    public bool full_speed_mode { get; set; }

    public blower_controller(Fuse electric_supply, Port audio, Port contactor_on_sound, Port contactor_off_sound)
        : base("blower", electric_supply)
    {
        _blower_audio        = audio;
        _contactor_on_sound  = contactor_on_sound;
        _contactor_off_sound = contactor_off_sound;
    }

    private float voltage_divider()
    {
        if (_line_voltage <= 675.0f)
            return parallel_6;
        if (_line_voltage <= 1350.0f)
            return series_2_parallel_3;
		return (full_speed_mode || _motor_current >= 300.0f) ? series_3_parallel_2 : series_6;
	}

	private async void switch_configuration()
    {
        if (_reconfiguration || voltage_divider() == _line_voltage_multiplier)
            return;
        _reconfiguration = true;
        _contactor_off_sound.Value = 1.0f;
        await Task.Delay(1000);
        _line_voltage_multiplier = voltage_divider();
        _contactor_on_sound.Value = 1.0f;
        _reconfiguration = false;
    }
    
    public void simulate(float line_voltage, float motor_current)
    {
        check_if_disposed();
        _line_voltage = line_voltage;
        float motor_voltage;
        if (!is_powered || !active || _reconfiguration)
        {
            motor_voltage = 0.0f;
            if (_previously_active)
                _contactor_off_sound.Value = 1.0f;
            _previously_active = false;
        }
        else
        {
            if (!_previously_active)
                _contactor_on_sound.Value = 1.0f;
            _previously_active = true;
            motor_voltage = line_voltage * _line_voltage_multiplier;
            if (motor_voltage <= 650.0f || motor_voltage >= 700.0f)
                switch_configuration();
        }

        float final_relative_speed = motor_voltage / 500.0f;
        if (_relative_speed <= final_relative_speed)
            _relative_speed = acceleration_ratio * _relative_speed + (1.0f - acceleration_ratio) * final_relative_speed;
        else
            _relative_speed = slowdown_ratio * _relative_speed + (1.0f - slowdown_ratio) * final_relative_speed;
        _blower_audio.Value = _relative_speed;
    }
}
