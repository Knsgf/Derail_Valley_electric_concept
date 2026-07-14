// Distributed under terms and conditions of CC0 licence. See LICENCE_CC0.txt for details.

using System;
using System.Collections.Generic;

using UnityEngine;

using LocoSim.Implementations;

using electric_sim.utilities;
using electric_sim.unit_A;

namespace electric_sim.devices;

internal class control_stand: electric_device
{
    public const int   independent_brake_notches = 6;
    public const int   reverser_notches          = 3, throttle_notches = 6, field_handle_notches = 13, selector_notches = 6;
    public const float independent_brake_last_notch = independent_brake_notches - 1;
    public const float reverser_last_notch          =          reverser_notches - 1, throttle_last_notch = throttle_notches - 1;
    public const float field_handle_last_notch      =      field_handle_notches - 1, selector_last_notch = selector_notches - 1;

    public enum selector_modes 
    { 
        series_regenerative = 0, parallel_regenerative = 1, rheostatic_brake = 2,
        yard_power          = 3, series_power          = 4, parallel_power   = 5 
    };
    
    private static readonly Dictionary<string, string> _port_id_map = new()
    {
        ["reverser_handle"] = "[Reverser].CONTROL_EXT_IN",
        ["throttle_handle"] = "[Throttle].EXT_IN",
        ["field_handle"   ] = "[FieldControl].EXT_IN",
        ["selector_handle"] = "[Selector].EXT_IN",

        ["front_pantograph_switch"] = "[FrontPantographSwitch].EXT_IN",
        ["back_pantograph_switch" ] = "[BackPantographSwitch].EXT_IN",
        ["left_sidepan_switch"    ] = "[LeftSidePanSwitch].EXT_IN",
        ["right_sidepan_switch"   ] = "[RighttSidePanSwitch].EXT_IN",
        ["fast_notching_switch"   ] = "[FastNotchingSwitch].EXT_IN",
        ["blower_speed_switch"    ] = "[BlowerSpeedSwitch].EXT_IN",

        ["primary_notch_hand"  ] = "[CustomSimulation].PRIMARY_NOTCH",
        ["secondary_notch_hand"] = "[CustomSimulation].SECONDARY_NOTCH",
        ["throttle_HUD_readout"] = "[CustomGauges].HUD_THROTTLE",
        ["supply_volts"        ] = "[CustomGauges].SUPPLY",
        ["motors_volts"        ] = "[CustomGauges].ALL_MOTOR_TERMINAL",
        //["total_load"          ] = "[CustomGauges].CURRENT_DRAW",

        ["reverse_current_lamp" ] = "[CustomGauges].REVERSE_CURRENT",
        ["transition_lamp"      ] = "[CustomGauges].TRANSITION",
        ["resistance_notch_lamp"] = "[CustomGauges].RESISTANCE_NOTCH",

        ["main_breaker_on_button" ] = "[MainBreakerOnButton].EXT_IN",
        ["main_breaker_off_button"] = "[MainBreakerOffButton].EXT_IN",
        ["sander"                 ] = "[Sander].CONTROL_EXT_IN",
        ["independent_brake"      ] = "[IndependentBrake].EXT_IN",
        ["brake_cutout"           ] = "[BrakeValveCutout].EXT_IN"
    };
    
    static control_stand()
    {
        for (int group = 1; group <= 3; ++group)
        {
            _port_id_map[ $"load_meter_{group}"] =  $"[CustomGauges].LOAD_GROUP{group}";
            _port_id_map[$"field_meter_{group}"] = $"[CustomGauges].FIELD_GROUP{group}";
        }
    }

    private readonly Dictionary<string,          Port?> _port_map         = [];
    private readonly Dictionary<string, Action<float>?> _port_handlers    = [];
    private readonly Dictionary<string,          float> _default_settings = [];

    private selector_interlock? _selector_interlock;
    private Port? _transition_lamp;
    private float _raw_throttle, _raw_selector, _primary_notch, _secondary_notch;
    private bool  _throttle_moved = false, _stand_active = false, _reset_all_controls = false;

    public control_stand(Fuse electric_supply, Dictionary<string, Port> ports): base("Control stand", electric_supply)
    {
        foreach (KeyValuePair<string, string> port_pair in _port_id_map)
        {
            try
            {
                _port_map[port_pair.Key] = sensor_grabber.grab_port(ports, port_pair.Value);
            }
            catch (ArgumentException _)
            { 
                _port_map[port_pair.Key] = null;
            }
        }
        power_supply_toggled += control_stand_toggled;
    }

    private void control_stand_toggled(bool turned_on)
    {
        if (turned_on && _stand_active)
        {
            foreach (KeyValuePair<string, Action<float>?> control_pair in _port_handlers)
            { 
                Port? control_port = _port_map[control_pair.Key];
                if (control_port != null)
                    control_pair.Value?.Invoke(control_port.Value);
            }
        }
    }

    public void register_handler(string device, Action<float> handler, bool needs_power = true, float default_setting = 0.0f)
    {
        if (_port_handlers.ContainsKey(device))
            throw new InvalidOperationException($"Cannot attach {device} handler more than once");
        if (!_port_map.TryGetValue(device, out Port? hooked_port))
            throw new ArgumentException($"Unknown device {device}");
        if (hooked_port == null)
            throw new ArgumentException($"No {device} installed");
        Action<float> new_handler;
        if (string.Equals(device, "throttle_handle", StringComparison.Ordinal))
        {
            new_handler = delegate (float raw_throttle)
            {
                if (!disposed && is_powered)
                {
                    if (!_stand_active)
                        raw_throttle = 0.0f;
                    _raw_throttle   = raw_throttle;
                    _throttle_moved = true;
                    _selector_interlock?.interlocked_handler(_raw_selector, raw_throttle, 
                        Mathf.CeilToInt(_primary_notch), Mathf.CeilToInt(_secondary_notch), _transition_lamp);
                    handler(raw_throttle);
                }
            };
        }
        else if (string.Equals(device, "selector_handle", StringComparison.Ordinal))
        {
            _raw_selector       = hooked_port.Value;
            _selector_interlock = new(handler, _raw_selector);
            new_handler = delegate (float raw_selector)
            {
                if (!disposed && is_powered)
                {
                    if (!_stand_active)
                        raw_selector = (float) selector_modes.yard_power / selector_last_notch;
                    _raw_selector = raw_selector;
                    _selector_interlock.interlocked_handler(raw_selector, _raw_throttle, 
                        Mathf.CeilToInt(_primary_notch), Mathf.CeilToInt(_secondary_notch), _transition_lamp);
                }
            };
        }
        else if (string.Equals(device, "brake_cutout", StringComparison.Ordinal))
        {
            new_handler = delegate (float port_value)
            {
                if (!disposed && !_reset_all_controls)
                {
                    _stand_active = port_value >= 0.5f;
                    handler(port_value);
                    if (!_stand_active)
                    {
                        _reset_all_controls = true;
                        foreach (KeyValuePair<string, float> control_default in _default_settings)
                            _port_map[control_default.Key]?.Value = control_default.Value;
                        _reset_all_controls = false;
                    }
                }
            };
        }
        else
        {
            new_handler = delegate (float port_value)
            {
                if (!disposed)
                {
                    if (!needs_power)
                        handler(port_value);
                    else if (is_powered)
                        handler(_stand_active ? port_value : default_setting);
                }
            };
        }
        _port_handlers   [device]           = new_handler;
        _default_settings[device]           = default_setting;
        hooked_port.ValueUpdatedInternally += new_handler;
        handler(hooked_port.Value);
    }

    public Action<float> create_setter(string device)
    {
        if (!_port_map.TryGetValue(device, out Port? hooked_port))
            throw new ArgumentException($"Unknown device {device}");
        if (hooked_port == null)
            throw new ArgumentException($"No {device} installed");
        Action<float> new_setter;
        if (string.Equals(device, "primary_notch_hand", StringComparison.Ordinal))
        {
            _port_map.TryGetValue("resistance_notch_lamp", out Port? resistance_light);
            new_setter = delegate (float primary_notch)
            {
                if (_primary_notch != primary_notch || _throttle_moved)
                {
                    _throttle_moved   = false;
                    hooked_port.Value = _primary_notch = primary_notch;
                    resistance_light?.Value = (_raw_throttle >= 0.5f / throttle_last_notch
                            && (_primary_notch < unit_A_sim.camshaft_notches || _secondary_notch < unit_A_sim.camshaft_notches)) ? 1.0f : 0.0f;
                }
            };
        }
        else if (string.Equals(device, "secondary_notch_hand", StringComparison.Ordinal))
        {
            _port_map.TryGetValue("resistance_notch_lamp", out Port? resistance_light);
            new_setter = delegate (float secondary_notch)
            {
                if (_secondary_notch != secondary_notch || _throttle_moved)
                {
                    _throttle_moved   = false;
                    hooked_port.Value = _secondary_notch = secondary_notch;
                    resistance_light?.Value = (_raw_throttle >= 0.5f / throttle_last_notch
                            && (_primary_notch < unit_A_sim.camshaft_notches || _secondary_notch < unit_A_sim.camshaft_notches)) ? 1.0f : 0.0f;
                }
            };
        }
        else if (string.Equals(device, "transition_lamp", StringComparison.Ordinal))
        {
            _transition_lamp = hooked_port;
            new_setter = delegate (float lamp_state)
            {
                if (hooked_port.Value < 0.7f)
                    hooked_port.Value = lamp_state;
            };
        }
        else
            new_setter = (float new_value) => hooked_port.Value = new_value;
        return new_setter;
    }

    public override void Dispose()
    {
        if (!disposed)
        {
            base.Dispose();
            foreach (KeyValuePair<string, Action<float>?> control_pair in _port_handlers)
            {
                Port? control_port                    = _port_map[control_pair.Key];
                control_port?.ValueUpdatedInternally -= control_pair.Value;
            }
        }
    }
}
