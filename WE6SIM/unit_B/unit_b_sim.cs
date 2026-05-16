// Distributed under terms and conditions of CC0 licence. See LICENCE_CC0.txt for details.

using System;
using System.Collections.Generic;

using DV.Simulation.Brake;
using DV.Simulation.Cars;

using LocoSim.Implementations;

using WE6SIM.devices;
using WE6SIM.unit_A;
using WE6SIM.utilities;

using static WE6SIM.utilities.signal_cable;
using static WE6SIM.utilities.sensor_grabber;

namespace WE6SIM.unit_B;

internal class unit_b_sim: electric_device
{
    private readonly pantograph    _pantograph;
    private readonly roof_busbar   _roof_bus;
    private readonly battery_panel _battery_cabinet;

    private readonly TrainCar       _unit;
    private readonly SimController  _simulation;
    private readonly camshaft_motor _secondary_controller;

    private readonly Fuse _appliances, _overhead_power, _control_air;
    private readonly Port _control_AB1, _control_BA1;
    private readonly Port _independent_brake, _sander;
    private readonly Port _total_load;

    private int _secondary_camshaft_target_notch = 1;

    public unit_b_sim(Dictionary<string, Fuse> fuses, Dictionary<string, Port> ports, TrainCar unit)
        : base("unit_B_sim")
    {
        SimController? simulation = unit.SimController ?? throw new ArgumentNullException("No simulation component");

        _overhead_power = grab_fuse(fuses, "fusebox.OVERHEAD_POWER"  );
        _control_air    = grab_fuse(fuses, "fusebox.CONTROL_AIR"     );
        _appliances     = grab_fuse(fuses, "fusebox.ELECTRONICS_MAIN");
        set_up_fuses(_appliances);

        //_throttle_handle = get_port(ports, "throttle.EXT_IN");
        //_reverser_handle = get_port(ports, "reverser.REVERSER");

        //_front_pantograph_switch = get_port(ports, "[FrontPantographSwitch].EXT_IN");
        //_front_pantograph_switch.ValueUpdatedInternally += toggle_pole;

        //_torque_b = get_port(ports, "[internal_MU].TM4-6");
        _total_load = grab_port(ports, "[internal_MU].PANTOGRAPHS_LOAD");

        _control_AB1 = grab_port(ports, "[internal_MU].CONTROL_AB1");
        _control_BA1 = grab_port(ports, "[internal_MU].CONTROL_BA1");
        _control_AB1.ValueUpdatedInternally += MU_AB1_control;
        
        _independent_brake = grab_port(ports, "[IndependentBrake].EXT_IN");
        _sander            = grab_port(ports, "[Sander].CONTROL_EXT_IN"  );

        _secondary_controller = new camshaft_motor(unit_a_sim.camshaft_notches, _appliances, drop_to_1_on_power_loss: false);

        _unit = unit;
        _simulation = simulation;
        simulation.SimulationFlow.TickEvent += simulate;
        
        _battery_cabinet = new(fuses, ports, unit.brakeSystem);
        _roof_bus        = new(ports, is_unit_A: false);
        _pantograph      = new(unit.gameObject, _roof_bus, _appliances, _control_air);

        //_throttle = new throttle_controllers();
        //_throttle.traction_toggle += traction_toggle;
    }

    private void MU_AB1_control(float AB1)
    {
        if (disposed)
            return;
        
        _overhead_power.ChangeState(port_value_signal_active(AB1, (int) AB1_signals.unit_B_overhead_power));

        _pantograph.toggle        (!port_value_signal_active(AB1, (int) AB1_signals.unit_B_pantograph));
        _pantograph.sidepan_toggle(!port_value_signal_active(AB1, (int) AB1_signals.unit_B_sidepan   ));
        
        _independent_brake.Value = extract_signal_from_port_value(AB1, (int) AB1_signals.unit_B_independent_brake, 
            (int) AB1_shift.unit_B_independent_brake) / 5.0f;
        _sander.Value = port_value_signal_active(AB1, (int) AB1_signals.unit_B_sander) ? 1.0f : 0.0f;

        _secondary_camshaft_target_notch = extract_signal_from_port_value(AB1, (int) AB1_signals.unit_B_camshaft_notch, 
            (int) AB1_shift.unit_B_camshaft_notch);
        switch (_secondary_camshaft_target_notch)
        {
            case 0:
                break;

            case unit_a_sim.roll_over_to_1:
                _secondary_controller.roll_over_move(to_1: true);
                break;

            case unit_a_sim.roll_over_to_full:
                _secondary_controller.roll_over_move(to_1: false);
                break;

            default:
                assert.test(_secondary_camshaft_target_notch >= 1 && _secondary_camshaft_target_notch <= unit_a_sim.camshaft_notches);
                _secondary_controller.target_notch = _secondary_camshaft_target_notch;
                break;
        }
    }

    private void simulate()
    {
        check_if_disposed();
        _pantograph.simulate(_total_load.Value);
        set_port_signal(_control_BA1, (int) BA1_signals.unit_B_camshaft_notch, (int) BA1_shift.unit_B_camshaft_notch,
            _secondary_controller.current_notch);
    }

    public override void Dispose()
    {
        if (!disposed)
        { 
            base.Dispose();
            _secondary_controller.Dispose();
            _pantograph.Dispose();
            _roof_bus.Dispose();
            _battery_cabinet.Dispose();
            _simulation.SimulationFlow.TickEvent -= simulate;
            _control_AB1.ValueUpdatedInternally  -= MU_AB1_control;
        }
    }
}
