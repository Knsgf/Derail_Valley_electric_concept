// Distributed under terms and conditions of CC0 licence. See LICENCE_CC0.txt for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using LocoSim.Implementations;

using WE6SIM.devices;
using WE6SIM.utilities;

using static WE6SIM.utilities.signal_cable;

namespace WE6SIM.unit_B;

internal class battery_panel: electric_device
{
    private readonly Fuse _appliances;
    private readonly Port _control_BA1;
    
    public battery_panel(Dictionary<string, Fuse> fuses, Dictionary<string, Port> ports): base("Battery panel")
    {
        _appliances = sensor_grabber.grab_fuse(fuses, "fusebox.ELECTRONICS_MAIN");
        set_up_fuses(_appliances);
        power_supply_toggled += battery_toggle;
        
        _control_BA1 = sensor_grabber.grab_port(ports, "[internal_MU].CONTROL_BA1");
    }
    
    private void battery_toggle(bool turned_on)
    {
        toggle_port_signal(_control_BA1, (int) BA1_signals.battery, turned_on);
    }

	public override void Dispose()
	{
		base.Dispose();
        power_supply_toggled -= battery_toggle;
	}
}
