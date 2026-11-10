// Distributed under terms and conditions of CC0 licence. See LICENCE_CC0.txt for details.

using System;
using System.Collections.Generic;

using LocoSim.Implementations;

using electric_sim.circuit_sim;

namespace electric_sim.devices;

internal class binary_contactor: contactor
{
    private static (camshaft_motor drive, camshaft_contactor_set contacts) assemble(
        string[]? normally_open, string[]? normally_closed, Dictionary<string, circuit.branch_user> contactor_locations,
        Action<bool>? contactor_toggle_sound, Fuse electric_supply, Fuse? air_supply)
    {
        camshaft_motor drive    = new(2, electric_supply, drop_to_1_on_power_loss: true, air_supply);
        var            contacts = camshaft_contactor_set.on_off(normally_open, normally_closed, contactor_locations, drive, contactor_toggle_sound);
        return (drive, contacts);
    }
    
    public binary_contactor(string[]? normally_open, string[]? normally_closed, Dictionary<string, circuit.branch_user> contactor_locations,
        Action<bool>? contactor_toggle_sound, Fuse electric_supply, Fuse? air_supply = null)
        : base(
            assemble(normally_open, normally_closed, contactor_locations, contactor_toggle_sound, electric_supply, air_supply),
            "binary contactor", electric_supply, air_supply)
    {}
}
