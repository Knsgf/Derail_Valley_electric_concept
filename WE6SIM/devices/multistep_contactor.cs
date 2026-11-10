using System;
using System.Collections.Generic;

using LocoSim.Implementations;

using electric_sim.circuit_sim;

namespace electric_sim.devices;

internal class multistep_contactor: contactor
{
    private static (camshaft_motor drive, camshaft_contactor_set contacts) assemble(
        string contactor_diagram, int notches, Dictionary<string, circuit.branch_user> contactor_locations,
        Action<bool>? contactor_toggle_sound, Fuse electric_supply, Fuse? air_supply)
    {
        camshaft_motor         drive    = new(notches, electric_supply, drop_to_1_on_power_loss: true, air_supply);
        camshaft_contactor_set contacts = new(contactor_diagram, contactor_locations, drive, contactor_toggle_sound);
        return (drive, contacts);
    }

    public multistep_contactor(string contactor_diagram, int notches, Dictionary<string, circuit.branch_user> contactor_locations,
        Action<bool>? contactor_toggle_sound, Fuse electric_supply, Fuse? air_supply = null)
        : base(
            assemble(contactor_diagram, notches, contactor_locations, contactor_toggle_sound, electric_supply, air_supply),
            "multi-step contactor", electric_supply, air_supply)
    {}
}
