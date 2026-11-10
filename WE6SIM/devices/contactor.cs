using System;

using LocoSim.Implementations;

namespace electric_sim.devices;

internal abstract class contactor: electric_device
{
    private readonly camshaft_motor         _drive;
    private readonly camshaft_contactor_set _contacts;

    private bool _engaged = false;

    public bool engaged
    {
        get
        {
            check_if_disposed();
            return _engaged;
        }
    }
    public event Action<bool>? on_toggle;

    protected contactor((camshaft_motor drive, camshaft_contactor_set contacts) assembled_contactor, string designation,
        Fuse electric_supply, Fuse? air_supply): base(designation, electric_supply, air_supply)
    {
        _drive    = assembled_contactor.drive;
        _contacts = assembled_contactor.contacts;
        _drive.notch_changed += notch_changed;
        notch_changed(_drive.current_notch);
    }

    private void notch_changed(int notch)
    {
        bool old_state = _engaged; 
        _engaged       = _drive.current_notch == _drive.total_notches;
        if (old_state != _engaged)
            on_toggle?.Invoke(_engaged);
    }

    public void toggle(bool turn_on)
    {
        check_if_disposed();
        _drive.target_notch = turn_on ? _drive.total_notches : 1;
    }

    public override void Dispose()
    {
        if (!disposed)
        {
            _drive.notch_changed -= notch_changed;
            base.Dispose();
            _drive.Dispose();
            _contacts.Dispose();
        }
    }
}
