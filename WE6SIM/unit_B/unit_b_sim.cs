// Distributed under terms and conditions of CC0 licence. See LICENCE_CC0.txt for details.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using DV.Simulation.Brake;
using DV.Simulation.Cars;

using LocoSim.Implementations;
using LocoSim.Implementations.Wheels;

using UnityEngine;

using WE6SIM.devices;
using WE6SIM.unit_A;
using WE6SIM.utilities;

using static WE6SIM.utilities.sensor_grabber;
using static WE6SIM.utilities.signal_cable;

namespace WE6SIM.unit_B;

internal class unit_B_sim: electric_device
{
    private readonly pantograph              _pantograph;
    private readonly roof_busbar             _roof_bus;
    private readonly battery_panel           _battery_cabinet;
    private readonly dummy_voltage_regulator _traction_motor_temperature;
    private readonly PoweredWheelsManager    _driving_axles;

    private readonly TrainCar       _unit;
    private readonly SimController  _simulation;
    private readonly camshaft_motor _secondary_controller;
    private readonly control_stand  _control_stand;

    private readonly Fuse _appliances, _main_breaker, _compressor_on, _control_air;
    private readonly Port _control_AB1, _control_BA1, _control_BA2;
    private readonly Port _total_load, _wheel_RPM, _traction_motor_RPM, _relative_voltage, _traction_motor_heat;
    private readonly Port _blower_speed, _motor_cooling, _motor_current_temperature;
    private readonly Port _contactor_on_sound, _contactor_off_sound;
    private readonly Port _reverser_handle, _selector_handle, _throttle_handle;

    private readonly Action<float> set_primary_notch, set_seconday_notch;
    private readonly Action<float> set_independent_brake, set_sander;
    private readonly Action<float> throttle_relay, field_handle_relay, selector_relay;

    private int  _secondary_camshaft_target_notch = 1;
    private bool _cab_active = false;

    public unit_B_sim(Dictionary<string, Fuse> fuses, Dictionary<string, Port> ports, TrainCar unit)
        : base("unit_B_sim")
    {
        SimController? simulation = unit.SimController ?? throw new ArgumentNullException("No simulation component");

        _main_breaker   = grab_fuse(fuses, "[MainBreakerContacts].CLOSED"          );
        _compressor_on  = grab_fuse(fuses, "[MainBreakerContacts].COMPRESSOR_POWER");
        _control_air    = grab_fuse(fuses, "fusebox.CONTROL_AIR"                   );
        _appliances     = grab_fuse(fuses, "fusebox.ELECTRONICS_MAIN"              );
        set_up_fuses(_appliances);
        _main_breaker.StateUpdated += main_breaker_trip_relay;

        _control_AB1 = grab_port(ports, "[internal_MU].CONTROL_AB1");
        _control_BA1 = grab_port(ports, "[internal_MU].CONTROL_BA1");
        _control_BA2 = grab_port(ports, "[internal_MU].CONTROL_BA2");
        
        _total_load                = grab_port(ports, "[internal_MU].PANTOGRAPHS_LOAD"            );
        _wheel_RPM                 = grab_port(ports, "traction.WHEEL_RPM_EXT_IN"                 );
        _traction_motor_RPM        = grab_port(ports, "[CustomSimulation].MOTOR_RPM"              );
        _relative_voltage          = grab_port(ports, "[CustomSimulation].RELATIVE_SUPPLY_VOLTAGE");
        _traction_motor_heat       = grab_port(ports, "[CustomSimulation].MOTOR_HEAT_B"           );
        _motor_current_temperature = grab_port(ports, "tmHeat.TEMPERATURE"                        );
        _blower_speed              = grab_port(ports, "[CustomSimulation].BLOWERS_RELATIVE_SPEED" );
        _motor_cooling             = grab_port(ports, "[CustomSimulation].MOTOR_COOLING_B"        );
        foreach (GameObject current_object in unit.gameObject.AllChildren())
        {
            PoweredWheelsManager? driving_axles = current_object.GetComponent<PoweredWheelsManager>();
            if (driving_axles is not null)
            {
                /*
                Main.log($"PoweredWheelsManager {driving_axles.poweredWheels.Length}");
                foreach (PoweredWheel? axle in driving_axles.poweredWheels)
                {
                    if (axle is not null)
                        Main.log($"{axle.index} {axle.state}");
                }
                */
                _driving_axles = driving_axles;
                break;
            }
        }
        if (_driving_axles is null)
            throw new Exception("No powered wheels manager");
        assert.test(_driving_axles.poweredWheels.Length == 3);
        int motor_status = 1 << ((int) BA2_shift.unit_B_active_motors);
        for (int axle = 0; axle < 3; ++axle)
        {
            toggle_port_signal(_control_BA2, motor_status, _driving_axles.poweredWheels[axle].IsPowered);
            motor_status <<= 1;
        }
        _driving_axles.PoweredWheelKilled    += motor_cut_out;
        _driving_axles.PoweredWheelSetOnFire += motor_cut_out;
        _driving_axles.PoweredWheelRepaired  += motor_cut_in;

        _contactor_on_sound  = grab_port(ports, "[CustomSimulation].CONTACTOR_ON" );
        _contactor_off_sound = grab_port(ports, "[CustomSimulation].CONTACTOR_OFF");

        _secondary_controller = new camshaft_motor(unit_A_sim.camshaft_notches, _appliances, drop_to_1_on_power_loss: false);

        _battery_cabinet            = new(fuses, ports, unit.brakeSystem);
        _roof_bus                   = new(ports, is_unit_A: false);
        _pantograph                 = new(unit.gameObject, _roof_bus, _appliances, _control_air, ports);
        _traction_motor_temperature = new(ports);
        
        throttle_relay     = handle_relay(BA1_signals.throttle, BA1_shift.throttle, control_stand.throttle_notches    );
        field_handle_relay = handle_relay(BA1_signals.field   , BA1_shift.field   , control_stand.field_handle_notches);
        selector_relay     = handle_relay(BA1_signals.selector, BA1_shift.selector, control_stand.selector_notches    );
        _control_stand = new(_appliances, ports);
        _control_stand.register_handler("reverser_handle",     reverser_relay, default_setting: 0.5f);
        _control_stand.register_handler("throttle_handle",     throttle_relay);
        _control_stand.register_handler(   "field_handle", field_handle_relay);
        _control_stand.register_handler("selector_handle",     selector_relay);
        _reverser_handle = grab_port(ports, "[Reverser].CONTROL_EXT_IN");
        _selector_handle = grab_port(ports, "[Selector].EXT_IN"        );
        _throttle_handle = grab_port(ports, "[Throttle].EXT_IN"        );

        _control_stand.register_handler( "main_breaker_on_button", enable_main_breaker);
        _control_stand.register_handler("main_breaker_off_button", (float button_port) 
            => toggle_port_signal(_control_BA1, (int) BA1_signals.breaker_trip, button_port >= 0.5f), needs_power: false);

        _control_stand.register_handler(     "brake_cutout",                cab_activation);
        _control_stand.register_handler("independent_brake", synchronise_independent_brake, needs_power: false, default_setting: 1.0f);
        _control_stand.register_handler(           "sander",            synchronise_sander);
        set_independent_brake = _control_stand.create_setter("independent_brake");
        set_sander            = _control_stand.create_setter(           "sander");

        _control_stand.register_handler("supply_volts", (float voltage) => _relative_voltage.Value = voltage / 1500.0f, needs_power: false);
        set_primary_notch  = _control_stand.create_setter(  "primary_notch_hand");
        set_seconday_notch = _control_stand.create_setter("secondary_notch_hand");
        
        _unit       = unit;
        _simulation = simulation;
        _control_AB1.ValueUpdatedInternally += MU_AB1_control;
        simulation.SimulationFlow.TickEvent += simulate;
        
    }

    private void synchronise_independent_brake(float raw_handle_position)
    {
        set_port_signal(_control_BA1, (int) BA1_signals.independent_brake, (int) BA1_shift.independent_brake, 
            Mathf.RoundToInt(raw_handle_position * control_stand.independent_brake_last_notch));
    }
    
    private void synchronise_sander(float sander_switch)
    {
        toggle_port_signal(_control_BA1, (int) BA1_signals.sander, sander_switch >= 0.5f);
    }

    private void reverser_relay(float raw_reverser)
    {
        toggle_port_signal(_control_BA1, (int) BA1_signals.reverser, raw_reverser >= 0.5f);
    }

    private Action<float> handle_relay(BA1_signals signal, BA1_shift signal_shift, int notches)
    {
        float multiplier = notches - 1.0f;
        return delegate (float port_value)
        {
            set_port_signal(_control_BA1, (int) signal, (int) signal_shift, Mathf.RoundToInt(port_value * multiplier));
        };
    }

    private void enable_main_breaker(float button_port)
    {
        toggle_port_signal(_control_BA1, (int) BA1_signals.breaker_engage, 
            button_port >= 0.5f && _cab_active && _throttle_handle.Value < 1.0f / control_stand.throttle_notches);
    }

    private async void main_breaker_trip_relay(bool remain_on)
    {
        if (remain_on || port_value_signal_active(_control_AB1.Value, (int) BA1_signals.breaker_trip))
            return;
        toggle_port_signal(_control_BA1, (int) BA1_signals.breaker_trip, true);
        do
            await Task.Delay(500);
        while (_total_load.Value >= 10.0f);
        toggle_port_signal(_control_BA1, (int) BA1_signals.breaker_trip, false);
    }

    private void motor_cut_out(PoweredWheel axle)
    {
        assert.test(axle.index is >= 0 and < 3);
        toggle_port_signal(_control_BA2, 1 << ((int) BA2_shift.unit_B_active_motors + axle.index), false);
    }

    private void motor_cut_in(PoweredWheel axle)
    {
        assert.test(axle.index is >= 0 and < 3);
        toggle_port_signal(_control_BA2, 1 << ((int) BA2_shift.unit_B_active_motors + axle.index), true);
    }

    private void cab_activation(float valve)
    {
        if (valve < 0.5f)
            _cab_active = false;
        else if (!_cab_active)
        {
            _cab_active = true;
            toggle_port_signal(_control_BA1, (int) BA1_signals.cab_change, true);
            throttle_relay    (0.0f);
            field_handle_relay(0.0f);
            reverser_relay(_reverser_handle.Value);
            selector_relay(_selector_handle.Value);
            toggle_port_signal(_control_BA1, (int) BA1_signals.cab_change, false);
        }
    }

    private void MU_AB1_control(float AB1)
    {
        if (disposed)
            return;
        
        _pantograph.toggle        (!port_value_signal_active(AB1, (int) AB1_signals.unit_B_pantograph));
        _pantograph.sidepan_toggle(!port_value_signal_active(AB1, (int) AB1_signals.unit_B_sidepan   ));
        
        _main_breaker.ChangeState (port_value_signal_active(AB1, (int) AB1_signals.main_breaker    ));
        _compressor_on.ChangeState(port_value_signal_active(AB1, (int) AB1_signals.compressor_power));

        set_independent_brake(extract_signal_from_port_value(AB1, (int) AB1_signals.independent_brake, 
            (int) AB1_shift.independent_brake) / control_stand.independent_brake_last_notch);
        set_sander(port_value_signal_active(AB1, (int) AB1_signals.sander) ? 1.0f : 0.0f);

        _secondary_camshaft_target_notch = extract_signal_from_port_value(AB1, (int) AB1_signals.unit_B_camshaft_notch, 
            (int) AB1_shift.unit_B_camshaft_notch);
        switch (_secondary_camshaft_target_notch)
        {
            case 0:
                break;

            case unit_A_sim.roll_over_to_1:
                _secondary_controller.roll_over_move(to_1: true);
                break;

            case unit_A_sim.roll_over_to_full:
                _secondary_controller.roll_over_move(to_1: false);
                break;

            default:
                assert.test(_secondary_camshaft_target_notch >= 1 && _secondary_camshaft_target_notch <= unit_A_sim.camshaft_notches);
                _secondary_controller.target_notch = _secondary_camshaft_target_notch;
                break;
        }

        set_primary_notch(extract_signal_from_port_value(AB1, (int) AB1_signals.unit_A_camshaft_notch, 
            (int) AB1_shift.unit_A_camshaft_notch));

        _contactor_on_sound.Value  = port_value_signal_active(AB1, (int) AB1_signals.contactor_on ) ? 1.0f : 0.0f;
        _contactor_off_sound.Value = port_value_signal_active(AB1, (int) AB1_signals.contactor_off) ? 1.0f : 0.0f;
    }

    private void simulate()
    {
        check_if_disposed();
        _contactor_on_sound.Value = _contactor_off_sound.Value = 0.0f;
        _traction_motor_RPM.Value = _wheel_RPM.Value * traction_motor.gear_ratio;
        _traction_motor_temperature.simulate(_traction_motor_heat.Value, _traction_motor_RPM.Value);
        _motor_cooling.Value = _blower_speed.Value * (blower_controller.ambient_temperature_C 
            - _motor_current_temperature.Value) * blower_controller.full_motor_cooling_power_at_1C;
        _pantograph.simulate(_total_load.Value);

        set_seconday_notch(_secondary_controller.current_position);
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
            _control_stand.Dispose();
            _simulation.SimulationFlow.TickEvent -= simulate;
            _control_AB1.ValueUpdatedInternally  -= MU_AB1_control;
            _main_breaker.StateUpdated           -= main_breaker_trip_relay;
            _driving_axles.PoweredWheelKilled    -= motor_cut_out;
            _driving_axles.PoweredWheelSetOnFire -= motor_cut_out;
            _driving_axles.PoweredWheelRepaired  -= motor_cut_in;
        }
    }
}
