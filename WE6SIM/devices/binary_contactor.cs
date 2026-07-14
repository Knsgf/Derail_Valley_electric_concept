// Distributed under terms and conditions of CC0 licence. See LICENCE_CC0.txt for details.

using System;
using System.Collections.Generic;

using LocoSim.Implementations;

using electric_sim.circuit_sim;

namespace electric_sim.devices;

interface contactor: IDisposable
{
    bool engaged { get; }
    void toggle(bool turn_on);
}

internal class binary_contactor: electric_device, contactor
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

    public binary_contactor(string[]? normally_open, string[]? normally_closed, Dictionary<string, circuit.branch_user> contactor_locations,
        Action<bool>? contactor_toggle_sound, Fuse electric_supply, Fuse? air_supply = null)
        : base("contactor", electric_supply, air_supply)
    {
        _drive    = new(2, electric_supply, drop_to_1_on_power_loss: true, air_supply);
        _contacts = camshaft_contactor_set.on_off(normally_open, normally_closed, contactor_locations, _drive, contactor_toggle_sound);
        _engaged  = _drive.current_notch == 2;
        _drive.notch_changed += (int notch) => _engaged = _drive.current_notch == 2;
    }

    public void toggle(bool turn_on)
    {
        check_if_disposed();
        _drive.target_notch = turn_on ? 2 : 1;
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
