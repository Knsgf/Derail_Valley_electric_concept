// Distributed under terms and conditions of CC0 licence. See LICENCE_CC0.txt for details.

using System.Collections.Generic;

using LocoSim.Implementations;

using WE6SIM.utilities;

namespace WE6SIM.devices;

internal class red_ditch_light_controller: electric_device
{
    private Port _light_switch_port, _light_control_port;
    
    public red_ditch_light_controller(Fuse appliances, Dictionary<string, Port> ports): base("Red light", appliances)
    {
        _light_control_port = sensor_grabber.grab_port(ports, "[CustomGauges].RED_DITCH_LIGHT");
        _light_switch_port  = sensor_grabber.grab_port(ports, "[DitchLightsSwitch].EXT_IN"    );
        _light_switch_port.ValueUpdatedInternally += switch_red_light;
    }

    private void switch_red_light(float switch_setting)
    {
        _light_control_port.Value = (switch_setting is > 0.3f and < 0.7f) ? 1.0f: 0.0f;
    }

    public override void Dispose()
    {
        base.Dispose();
        _light_switch_port.ValueUpdatedInternally -= switch_red_light;
    }
}
