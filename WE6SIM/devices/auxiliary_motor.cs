// Distributed under terms and conditions of CC0 licence. See LICENCE_CC0.txt for details.

using UnityEngine;

using LocoSim.Implementations;

namespace electric_sim.devices;

internal class auxiliary_motor
{
    private readonly float _power, _efficiency, _nominal_voltage, _minimum_voltage, _speedup, _slowdown, _idle_slowdown;
    private readonly Port  _audio;

    public float relative_speed { get; private set; } = 0.0f;
    public float current_draw   { get; private set; } = 0.0f;
    
    public auxiliary_motor(Port audio, float power, float efficiency, float nominal_voltage, float minimum_voltage, 
        float speed_up, float slowdown, float idle_slowdown = 0.0f)
    {
        _audio           = audio;
        _power           = power;
        _efficiency      = efficiency;
        _nominal_voltage = nominal_voltage;
        _minimum_voltage = minimum_voltage;
        _speedup         = speed_up;
        _slowdown        = slowdown;
        _idle_slowdown   = (idle_slowdown > 0.0f) ? idle_slowdown : slowdown;
    }

    public void run(float current_voltage)
    {
        bool  is_running;
        float final_relative_speed, relative_speed = this.relative_speed;
        if (current_voltage >= _minimum_voltage)
        {
            is_running           = true;
            final_relative_speed = current_voltage / _nominal_voltage;
        }
        else
        {
            if (relative_speed < 0.001f)
                return;
            is_running      = false;
            current_voltage = final_relative_speed = 0.0f;
        }
        float relative_acceleration;
        if (!is_running)
            relative_acceleration = _idle_slowdown;
        else
            relative_acceleration = (relative_speed <= final_relative_speed) ? _speedup : _slowdown;
        float acceleration_ratio = Mathf.Pow(relative_acceleration, Time.deltaTime);
        relative_speed           = Mathf.LerpUnclamped(final_relative_speed, relative_speed, acceleration_ratio);
        _audio.Value             = this.relative_speed = relative_speed;

        float current = !is_running ? 0.0f : (_power / (current_voltage * _efficiency));
        if (relative_speed > 0.0f)
            current *= Mathf.Min(7.0f, final_relative_speed / relative_speed);
        current_draw = Mathf.LerpUnclamped(current_draw, current, 0.1f);
    }
}
