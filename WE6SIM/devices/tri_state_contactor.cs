using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using LocoSim.Implementations;

using WE6SIM.circuit_sim;

namespace WE6SIM.devices;

// A binary contactor with a defined intermediate state, used to either prevent or ensure a short circuit when switching
internal class tri_state_contactor: electric_device, contactor
{
    private readonly camshaft_motor         _drive;
    private readonly camshaft_contactor_set _contacts;

    private bool _engaged;

    public bool engaged
    {
        get
        {
            check_if_disposed();
            return _engaged;
        }
    }

    public tri_state_contactor(string[]? closed_contacts_off, string[]? closed_contacts_intermediate, 
        string[]? closed_contacts_on, Dictionary<string, circuit.branch_user> contactor_locations,
        Action<bool>? contactor_toggle_sound, Fuse electric_supply, Fuse? air_supply = null)
        : base("contactor", electric_supply, air_supply)
    {
        _drive    = new(3, electric_supply, drop_to_1_on_power_loss: true, air_supply);
        _contacts = camshaft_contactor_set.tri_state(closed_contacts_off, closed_contacts_intermediate, closed_contacts_on, 
            contactor_locations, _drive, contactor_toggle_sound);
        _engaged  = _drive.current_notch == 3;
        _drive.notch_changed += (int notch) => _engaged = _drive.current_notch == 3;
    }

    public void toggle(bool turn_on)
    {
        check_if_disposed();
        _drive.target_notch = turn_on ? 3 : 1;
    }

    public override void Dispose()
    {
        if (!disposed)
        {
            base.Dispose();
            _drive.Dispose();
            _contacts.Dispose();
        }
    }
}
