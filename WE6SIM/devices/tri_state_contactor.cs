// Distributed under terms and conditions of CC0 licence. See LICENCE_CC0.txt for details.

using System;
using System.Collections.Generic;

using LocoSim.Implementations;

using electric_sim.circuit_sim;

namespace electric_sim.devices;

// A binary contactor with a defined intermediate state, used to either prevent or ensure a short circuit when switching
internal class tri_state_contactor: contactor
{
    private static (camshaft_motor drive, camshaft_contactor_set contacts) assemble(
        string[]? closed_contacts_off, string[]? closed_contacts_intermediate, 
        string[]? closed_contacts_on, Dictionary<string, circuit.branch_user> contactor_locations,
        Action<bool>? contactor_toggle_sound, Fuse electric_supply, Fuse? air_supply)
    { 
        camshaft_motor drive    = new(3, electric_supply, drop_to_1_on_power_loss: true, air_supply);
        var            contacts = camshaft_contactor_set.tri_state(closed_contacts_off, closed_contacts_intermediate, closed_contacts_on, 
                                                                   contactor_locations, drive, contactor_toggle_sound);
        return (drive, contacts);
    }

    public tri_state_contactor(string[]? closed_contacts_off, string[]? closed_contacts_intermediate, 
        string[]? closed_contacts_on, Dictionary<string, circuit.branch_user> contactor_locations,
        Action<bool>? contactor_toggle_sound, Fuse electric_supply, Fuse? air_supply = null)
        : base(
            assemble(closed_contacts_off, closed_contacts_intermediate, closed_contacts_on, contactor_locations, 
                      contactor_toggle_sound, electric_supply, air_supply),
            "tri-state contactor", electric_supply, air_supply)
    {}
}
