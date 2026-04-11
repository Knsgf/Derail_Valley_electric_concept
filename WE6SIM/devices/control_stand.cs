// Distributed under terms and conditions of CC0 licence. See LICENCE_CC0.txt for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using LocoSim.Implementations;
using WE6SIM.utilities;

namespace WE6SIM;

internal class control_stand: electric_device
{
    private static readonly Dictionary<string, string> _port_id_map = new()
	{
        ["reverser_handle"] = "[Reverser].EXT_IN",
        ["throttle_handle"] = "[Throttle].EXT_IN",
        ["field_handle"   ] = "[FieldControl].EXT_IN",
        ["throttle_handle"] = "[Throttle].EXT_IN",

        ["front_pantograph_switch"] = "[FrontPantographSwitch].EXT_IN",
        ["fast_notching_switch"   ] = "[FastNotchingSwitch].EXT_IN",

        ["primary_notch_hand"  ] = "[CustomSimulation].PRIMARY_NOTCH",
        ["secondary_notch_hand"] = "[CustomSimulation].SECONDARY_NOTCH",
        ["supply_volts"        ] = "[CustomGauges].SUPPLY",
        ["motor_volts"         ] = "[CustomGauges].ALL_MOTOR_TERMINAL",
    };
    
    static control_stand()
    {
        for (int group = 1; group <= 3; ++group)
        {
            _port_id_map[$"load_meter_{group}" ] = $"[CustomGauges].LOAD_GROUP{group}";
            _port_id_map[$"field_meter_{group}"] = $"[CustomGauges].FIELD_GROUP{group}";
        }
    }

    private readonly Dictionary<string, Port> _port_map = [];
    private readonly Dictionary<string, Action<float>> _port_handlers = [];

    public control_stand(Fuse electric_supply, Dictionary<string, Port> ports): base("control_stand", electric_supply)
    {
        foreach (KeyValuePair<string, string> port_pair in _port_id_map)
            _port_map[port_pair.Key] = sensor_grabber.grab_port(ports, port_pair.Value);
    }

    public void register_handler(string device, Action<float> handler)
    {
        if (_port_handlers.ContainsKey(device))
            throw new InvalidOperationException($"Cannot attach {device} handler more than once");
        if (!_port_map.TryGetValue(device, out Port hooked_port))
            throw new ArgumentException($"No {device} installed");
        _port_handlers[device]              = handler;
        hooked_port.ValueUpdatedInternally += handler;
    }

    public Action<float> create_setter(string device)
    {
        if (!_port_map.TryGetValue(device, out Port hooked_port))
            throw new ArgumentException($"No {device} installed");
        Action<float> new_setter = (float new_value) => hooked_port.Value = new_value;
        return new_setter;
    }
}
