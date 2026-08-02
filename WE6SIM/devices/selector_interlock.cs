// Distributed under terms and conditions of CC0 licence. See LICENCE_CC0.txt for details.

using System;

using UnityEngine;

using LocoSim.Implementations;

using static electric_sim.devices.control_stand;

namespace electric_sim.devices;

internal class selector_interlock(Action<float> unit_selector_handler, float handle_initial_position)
{
    private readonly Action<float> unit_selector_handler = unit_selector_handler;

    private float _current_selector = raw_selector_to_notch(handle_initial_position);

    private static float raw_selector_to_notch(float raw_selector)
    {
        return Mathf.Clamp(Mathf.RoundToInt(raw_selector * selector_last_notch), 0.0f, selector_last_notch) / selector_last_notch;
    }
    
    public void interlocked_handler(float raw_selector, float raw_throttle, int primary_notch, int secondary_notch,
        Port? transition_lamp)
    {
        if (   raw_throttle                                < 0.5f / throttle_notches 
            || Mathf.Abs(raw_selector - _current_selector) < 0.5f / selector_notches)
        {
            _current_selector = raw_selector_to_notch(raw_selector);
            if (transition_lamp != null && transition_lamp.Value >= 0.7f)
                transition_lamp.Value = 0.0f;
            unit_selector_handler(raw_selector);
        }
        else if (transition_lamp != null && transition_lamp.Value < 0.7f)
        {
            int     selector = Mathf.RoundToInt(_current_selector * selector_last_notch);
            int new_selector = Mathf.RoundToInt(     raw_selector * selector_last_notch);
            if (       selector is (int) selector_modes.series_power or (int) selector_modes.parallel_power 
                && new_selector is (int) selector_modes.series_power or (int) selector_modes.parallel_power 
                && (primary_notch <= 6 || secondary_notch <= 6))
            {
                _current_selector = raw_selector_to_notch(raw_selector);
                unit_selector_handler(raw_selector);
            }
            else
                transition_lamp.Value = 1.0f;
        }
    }
}
