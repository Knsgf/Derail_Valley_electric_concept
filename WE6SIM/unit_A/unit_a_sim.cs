// Distributed under terms and conditions of CC0 licence. See LICENCE_CC0.txt for details.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using DV.Simulation.Cars;

using LocoSim.Implementations;
using LocoSim.Implementations.Wheels;

using UnityEngine;

using WE6SIM.catenary;
using WE6SIM.circuit_sim;
using WE6SIM.devices;
using WE6SIM.unit_B;
using WE6SIM.utilities;

using static WE6SIM.circuit_sim.circuit;
using static WE6SIM.devices.control_stand;
using static WE6SIM.utilities.sensor_grabber;
using static WE6SIM.utilities.signal_cable;

namespace WE6SIM.unit_A;

internal partial class unit_A_sim: electric_device
{
    const int   motors           = 6;
    const float compressor_power = 50.0E+3f;

    private readonly Dictionary<string, branch_user> _named_branches, _contactor_locations;
    private readonly Dictionary<string, float> _currents = [], _element_resistances = [];

    private readonly Fuse _appliances, _control_air, _main_breaker_closed, _compressor_on;
    private readonly Port _torque_A, _wheel_RPM, _traction_motor_load, _traction_motor_RPM, _traction_motor_EMF, _jog_volts;
    private readonly Port _contactor_on_sound, _contactor_off_sound;
    private readonly Port _total_load, _relative_voltage, _compressor_power, _traction_motor_heat_B;
    private readonly Port _reverser_handle, _selector_handle;

    private readonly Port _control_AB1, _control_BA1, _control_BA2, _torque_B, _wheel_RPM_B, _traction_motor_load_B;

    private readonly SimController _simulation;
    private readonly circuit       _circuit;
    private readonly resistor_heat _resistor_grid;

    private readonly pantograph                 _pantograph;
    private readonly roof_busbar                _roof_bus;
    private readonly main_circuit_breaker       _main_breaker;
    private readonly traction_motor[]           _traction_motors;
    private readonly exciter                    _regenerative_field;
    private readonly blower_controller          _blowers;
    private readonly throttle_controller        _throttle_controller;
    private readonly control_stand              _control_stand;
    private readonly red_ditch_light_controller _red_light_controller;
    private readonly dummy_voltage_regulator    _traction_motor_temperature;
    
    private readonly TrainCar _unit;

    private readonly Action<float>   set_primary_notch, set_seconday_notch, set_supply_volts, set_motors_volts;
    private readonly Action<float>[] set_motor_group_load, set_motor_group_field;
    private readonly Action<float>   set_reverse_current_lamp;
    private readonly Action<float>   set_independent_brake, toggle_sander;

    private readonly contactors _contactors;

    private bool _fast_notching_enabled = false, _jogging_mode_on = false, _jog = false, _cab_active = false;
    private int  _throttle = -1, _secondary_camshaft_notch, _selector = -1, _field_position = -1;
    private Task? _single_notch_movement;
    private float _reverser_position = 0.5f, _fast_notching_current_limit = 250.0f;

    public const int camshaft_notches = 7, roll_over_to_1 = camshaft_notches + 1, roll_over_to_full = camshaft_notches + 2;

    public unit_A_sim(Dictionary<string, Fuse> fuses, Dictionary<string, Port> ports, TrainCar unit, int random_seed)
        : base("unit_A_sim")
    {
        SimController? simulation = unit.SimController ?? throw new ArgumentNullException("No simulation component");

        _appliances          = grab_fuse(fuses, "fusebox.ELECTRONICS_MAIN"              );
        _control_air         = grab_fuse(fuses, "fusebox.CONTROL_AIR"                   );
        _main_breaker_closed = grab_fuse(fuses, "[MainBreakerContacts].CLOSED"          );
        _compressor_on       = grab_fuse(fuses, "[MainBreakerContacts].COMPRESSOR_POWER");
        set_up_fuses(_appliances);
        _compressor_on.StateUpdated += compressor_power_toggle;

        _torque_A            = grab_port(ports, "traction.TORQUE_IN"                        );
        _wheel_RPM           = grab_port(ports, "traction.WHEEL_RPM_EXT_IN"                 );
        _traction_motor_load = grab_port(ports, "[CustomSimulation].MOTOR_LOAD"             );
        _traction_motor_RPM  = grab_port(ports, "[CustomSimulation].MOTOR_RPM"              );
        _traction_motor_EMF  = grab_port(ports, "[CustomSimulation].MOTOR_EMF"              );
        _total_load          = grab_port(ports, "[CustomGauges].CURRENT_DRAW"               );
        _jog_volts           = grab_port(ports, "[CustomSimulation].JOG_VOLTS"              );
        _relative_voltage    = grab_port(ports, "[CustomSimulation].RELATIVE_SUPPLY_VOLTAGE");
        _compressor_power    = grab_port(ports, "compressor.POWER_CONSUMPTION"              );

        const float variation = 0.1f;
        UnityEngine.Random.State old_state = UnityEngine.Random.state;
        UnityEngine.Random.InitState(random_seed);
        foreach (KeyValuePair<string, float> element in _base_element_resistances)
            _element_resistances[element.Key] = element.Value * UnityEngine.Random.Range(1.0f - variation, 1.0f + variation);
        UnityEngine.Random.state = old_state;
        _circuit = circuit_compiler.trace(_element_resistances, circuit_diagram).set_up_simulation(out _named_branches, out _contactor_locations, _currents);
        _resistor_grid = new(ports, _element_resistances);
        foreach (string branch_name in _named_branches.Keys)
            _currents[branch_name] = 0.0f;
        _named_branches["BAT"].EMF = battery_panel.battery_EMF;

        _torque_B              = grab_port(ports, "[internal_MU].TM4-6"            );
        _wheel_RPM_B           = grab_port(ports, "[internal_MU].WHEEL_RPM_FROM_B" );
        _traction_motor_load_B = grab_port(ports, "[CustomSimulation].MOTOR_LOAD_B");
        _traction_motor_heat_B = grab_port(ports, "[CustomSimulation].MOTOR_HEAT_B");
        _control_AB1           = grab_port(ports, "[internal_MU].CONTROL_AB1"      );
        _control_BA1           = grab_port(ports, "[internal_MU].CONTROL_BA1"      );
        _control_BA2           = grab_port(ports, "[internal_MU].CONTROL_BA2"      );

        _contactor_on_sound  = grab_port(ports, "[CustomSimulation].CONTACTOR_ON" );
        _contactor_off_sound = grab_port(ports, "[CustomSimulation].CONTACTOR_OFF");
        _contactors   = new(unit, this, _appliances, _control_air, _main_breaker_closed, _contactor_locations, _contactor_on_sound, _contactor_off_sound);
        _roof_bus     = new(ports, is_unit_A: true);
        _pantograph   = new(unit.gameObject, _roof_bus, _appliances, _control_air, ports);
        _main_breaker = new(_appliances, _control_air, ports, this);

        _traction_motors = new traction_motor[motors];
        for (int motor_number = 1; motor_number <= motors / 2; ++motor_number)
            _traction_motors[motor_number - 1] = new(motor_number, _wheel_RPM  , _named_branches);
        for (int motor_number = motors / 2 + 1; motor_number <= motors; ++motor_number)
            _traction_motors[motor_number - 1] = new(motor_number, _wheel_RPM_B, _named_branches);
        _traction_motor_temperature = new(ports);
        _regenerative_field = new(this, ports);
        _blowers = new(
            _main_breaker_closed, 
            grab_port(ports, "[Blowers].BLOWERS_RELATIVE_SPEED"), 
            grab_port(ports, "tmHeat.TEMPERATURE"              ), 
            grab_port(ports, "[Blowers].MOTOR_COOLING_RATE"    ), 
            grab_port(ports, "[ResistorHeat].TEMPERATURE"      ), 
            grab_port(ports, "[Blowers].RESISTOR_COOLING_RATE" ), 
            _contactor_on_sound, _contactor_off_sound
        );

        _control_stand       = new(_appliances, ports);
        _throttle_controller = new(this);
        _reverser_handle     = grab_port(ports, "[Reverser].CONTROL_EXT_IN");
        _selector_handle     = grab_port(ports, "[Selector].EXT_IN"        );
        _control_stand.register_handler("reverser_handle",      reverser_handler);
        _control_stand.register_handler("throttle_handle",      throttle_handler);
        _control_stand.register_handler(   "field_handle", field_control_handler);
        _control_stand.register_handler("selector_handle",      selector_handler);

        _control_stand.register_handler("front_pantograph_switch", toggle_front_pantograph );
        _control_stand.register_handler( "back_pantograph_switch", toggle_back_pantograph  );
        _control_stand.register_handler(    "left_sidepan_switch", toggle_left_sidepan     );
        _control_stand.register_handler(   "right_sidepan_switch", toggle_right_sidepan    );
        _control_stand.register_handler( "main_breaker_on_button", enable_main_breaker     );
        _control_stand.register_handler("main_breaker_off_button", _main_breaker.toggle_off);
        _control_stand.register_handler(   "fast_notching_switch", fast_notching_toggle    );
        _control_stand.register_handler(    "blower_speed_switch", blower_speed_toggle     );

        _control_stand.register_handler(      "independent_brake", synchronise_independent_brake, needs_power: false);
        _control_stand.register_handler(           "brake_cutout",                cab_activation);
        _control_stand.register_handler(                 "sander",            synchronise_sander);
        set_independent_brake = _control_stand.create_setter("independent_brake");
        toggle_sander         = _control_stand.create_setter(           "sander");
        _red_light_controller = new red_ditch_light_controller(_appliances, ports);

        _control_stand.register_handler("primary_notch_hand", signal_primary_camshaft_target_notch);
        set_supply_volts   = _control_stand.create_setter(        "supply_volts");
        set_motors_volts   = _control_stand.create_setter(        "motors_volts");
        set_primary_notch  = _control_stand.create_setter(  "primary_notch_hand");
        set_seconday_notch = _control_stand.create_setter("secondary_notch_hand");
        set_motor_group_load  = new Action<float>[3];
        set_motor_group_field = new Action<float>[3];
        for (int group = 1; group <= 3; ++group)
        {
            set_motor_group_load [group - 1] = _control_stand.create_setter( $"load_meter_{group}");
            set_motor_group_field[group - 1] = _control_stand.create_setter($"field_meter_{group}");
        }

        set_reverse_current_lamp        = _control_stand.create_setter("reverse_current_lamp");
        _contactors.set_transition_lamp = _control_stand.create_setter(     "transition_lamp");

        _unit       = unit;
        _simulation = simulation;
        simulation.SimulationFlow.TickEvent += simulate;
        _control_BA1.ValueUpdatedInternally += MU_BA1_control;

        //circuit_telemetry.set_up(_circuit, _named_branches);
    }

    private void toggle_front_pantograph(float port_value)
    {
        if (!_pantograph.sidepan_stowed || port_value_signal_active(_control_AB1.Value, (int) AB1_signals.unit_B_sidepan))
            return;
        _pantograph.toggle(port_value < 0.5f);
    }

    private void toggle_back_pantograph(float port_value)
    {
        if (!_pantograph.sidepan_stowed || port_value_signal_active(_control_AB1.Value, (int) AB1_signals.unit_B_sidepan))
            return;
        toggle_port_signal(_control_AB1, (int) AB1_signals.unit_B_pantograph, port_value >= 0.5f);
    }

    private void toggle_right_sidepan(float port_value)
    {
        if (!_pantograph.stowed || port_value_signal_active(_control_AB1.Value, (int) AB1_signals.unit_B_pantograph))
            return;
        _pantograph.sidepan_toggle(port_value < 0.5f);
    }
    
    private void toggle_left_sidepan(float port_value)
    {
        if (!_pantograph.stowed || port_value_signal_active(_control_AB1.Value, (int) AB1_signals.unit_B_pantograph))
            return;
        toggle_port_signal(_control_AB1, (int) AB1_signals.unit_B_sidepan, port_value >= 0.5f);
    }

    private void enable_main_breaker(float button_port)
    {
        if (button_port >= 0.5f && _cab_active)
            _main_breaker.toggle_on();
    }

    private void fast_notching_toggle(float port_value)
    {
        _fast_notching_enabled = port_value >= 0.5f;
    }

    private void blower_speed_toggle(float port_value)
    {
        _blowers.full_speed_mode = port_value >= 0.5f;
    }

    private void signal_primary_camshaft_target_notch(float target_notch)
    {
        set_port_signal(_control_AB1, (int) AB1_signals.unit_A_camshaft_notch,
            (int) AB1_shift.unit_A_camshaft_notch, Mathf.RoundToInt(Mathf.Clamp(target_notch, 1.0f, camshaft_notches)));
    }

    private void set_secondary_camshaft_target_notch(int target_notch)
    {
        set_port_signal(_control_AB1, (int) AB1_signals.unit_B_camshaft_notch,
            (int) AB1_shift.unit_B_camshaft_notch, target_notch);
    }

    private int get_secondary_camshaft_current_notch(float BA1)
    {
        return extract_signal_from_port_value(BA1, (int) BA1_signals.unit_B_camshaft_notch,
            (int) BA1_shift.unit_B_camshaft_notch);
    }

    private void compressor_power_toggle(bool turn_on)
    {
        toggle_port_signal(_control_AB1, (int) AB1_signals.compressor_power, turn_on);
    }

    private void synchronise_independent_brake(float raw_handle_position)
    {
        set_port_signal(_control_AB1, (int) AB1_signals.independent_brake, (int) AB1_shift.independent_brake, 
            Mathf.RoundToInt(raw_handle_position * independent_brake_last_notch));
    }
    
    private void synchronise_sander(float sander_switch)
    {
        toggle_port_signal(_control_AB1, (int) AB1_signals.sander, sander_switch >= 0.5f);
    }

    private void reverser_handler(float raw_reverser, bool selector_switched)
    {
        if (!selector_switched && Mathf.Abs(raw_reverser - _reverser_position) < 0.1f)
            return;
        _reverser_position = raw_reverser;
        if (_selector is (int) selector_modes.rheostatic_brake)
            raw_reverser = 1.0f - raw_reverser;
        if (raw_reverser >= 0.7f)
            _contactors._reverser.target_notch = 1;
        else if (raw_reverser <= 0.3f)
            _contactors._reverser.target_notch = 2;
    }

    private void reverser_handler(float raw_reverser)
    {
        reverser_handler(raw_reverser, selector_switched: false);
    }

    private void toggle_traction_motors(bool turn_on)
    {
        _contactors.toggle_traction_motors(turn_on);
        _contactors._voltmeter.toggle(turn_on && _selector is not (int) selector_modes.yard_power);
    }
    
    private void throttle_handler(float raw_throttle, bool cab_changed)
    {
        int wheel_position = Mathf.RoundToInt(raw_throttle * throttle_last_notch);
        if (!cab_changed && wheel_position == _throttle)
            return;
        _throttle = wheel_position;
        switch (wheel_position)
        {
            case 0:
                _throttle_controller.roll_camshafts_over();
                break;

            case 1:
                toggle_traction_motors(turn_on: _main_breaker_closed.State);
                _throttle_controller.run_down();
                break;

            case 2:
                toggle_traction_motors(turn_on: _main_breaker_closed.State);
                if (_single_notch_movement == null || _single_notch_movement.IsCompleted)
                    _single_notch_movement = _throttle_controller.notch_down();
                break;

            case 3:
                toggle_traction_motors(turn_on: _main_breaker_closed.State);
                _ = _throttle_controller.unlock_camshafts(continuous_run: false);
                break;

            case 4:
                toggle_traction_motors(turn_on: _main_breaker_closed.State);
                if (_single_notch_movement == null || _single_notch_movement.IsCompleted)
                    _single_notch_movement = _throttle_controller.notch_up();
                break;

            case 5:
                toggle_traction_motors(turn_on: _main_breaker_closed.State);
                _throttle_controller.run_up();
                break;
        }
    }

    private void throttle_handler(float raw_throttle)
    {
        throttle_handler(raw_throttle, cab_changed: false);
    }

    private void field_control_handler(float raw_field_position, bool cab_changed)
    {
        int handle_postion = Mathf.RoundToInt(raw_field_position * field_handle_last_notch);
        if (!cab_changed && _field_position == handle_postion)
            return;
        _field_position = handle_postion;
        if (_selector is not (int) selector_modes.series_regenerative and not (int) selector_modes.parallel_regenerative)
        {
            _named_branches["EXT"].EMF = 0.0f;
            _contactors.switch_field_contactors(handle_postion);
        }
    }

    private void field_control_handler(float raw_field_position)
    {
        field_control_handler(raw_field_position, cab_changed: false);
    }

    private void selector_handler(float raw_selector_position, bool cab_changed)
    {
        int handle_postion = Mathf.RoundToInt(raw_selector_position * selector_last_notch);
        if (!cab_changed && _selector == handle_postion)
            return;
        _selector = handle_postion;
        _main_breaker.trip_if_all_pantographs_retracted();
        reverser_handler(_reverser_handle.Value, selector_switched: true);
        _contactors.switch_selector_contactors(handle_postion);
        if (handle_postion >= 2)
            _contactors.switch_field_contactors(_field_position);
        _fast_notching_current_limit = handle_postion switch
        {
            (int) selector_modes.yard_power            => 400.0f,
            (int) selector_modes.series_power          => 300.0f,
            (int) selector_modes.parallel_power        => 250.0f,
            (int) selector_modes.rheostatic_brake      => 250.0f,
            (int) selector_modes.series_regenerative   => 250.0f,
            (int) selector_modes.parallel_regenerative => 250.0f,
            _ => throw new InvalidOperationException($"Improper selector position {handle_postion}")
        };
    }

    private void selector_handler(float raw_selector_position)
    {
        selector_handler(raw_selector_position, cab_changed: false);
    }

    private void cab_activation(float valve)
    {
        if (valve < 0.5f)
            _cab_active = false;
        else if (!_cab_active)
        {
            _cab_active = true;
            throttle_handler     (0.0f, cab_changed: true);
            field_control_handler(0.0f, cab_changed: true);
            selector_handler(_selector_handle.Value, cab_changed: true);
        }
    }

    private void handle_relay(float BA1, BA1_signals signal, BA1_shift signal_shift, float last_notch, 
        Action<float, bool> handle, bool cab_changed)
    {
        int notch = extract_signal_from_port_value(BA1, (int) signal, (int) signal_shift);
        handle(notch / last_notch, cab_changed);
    }

    private void MU_BA1_control(float BA1)
    {
        if (disposed)
            return;
        _appliances.ChangeState (port_value_signal_active(BA1, (int) BA1_signals.battery           ));
        _control_air.ChangeState(port_value_signal_active(BA1, (int) BA1_signals.control_air_usable));
        
        _jogging_mode_on = port_value_signal_active(BA1, (int) BA1_signals.jog);

        bool breaker_trip = port_value_signal_active(BA1, (int) BA1_signals.breaker_trip);
        if (breaker_trip)
            _main_breaker.trip();

        if (!_cab_active)
        {
            if (!breaker_trip && port_value_signal_active(BA1, (int) BA1_signals.breaker_engage))
                _main_breaker.toggle_on();
            
            bool cab_changed = port_value_signal_active(BA1, (int) BA1_signals.cab_change);
            reverser_handler(port_value_signal_active(BA1, (int) BA1_signals.reverser) ? 0.0f : 1.0f, cab_changed);
            handle_relay(BA1, BA1_signals.throttle, BA1_shift.throttle, throttle_last_notch    ,      throttle_handler, cab_changed);
            handle_relay(BA1, BA1_signals.field   , BA1_shift.field   , field_handle_last_notch, field_control_handler, cab_changed);
            handle_relay(BA1, BA1_signals.selector, BA1_shift.selector, selector_last_notch    ,      selector_handler, cab_changed);
        }
        set_independent_brake(extract_signal_from_port_value(BA1, (int) BA1_signals.independent_brake, 
            (int) BA1_shift.independent_brake) / independent_brake_last_notch);
        toggle_sander(port_value_signal_active(BA1, (int) BA1_signals.sander) ? 1.0f : 0.0f);

        _secondary_camshaft_notch = get_secondary_camshaft_current_notch(BA1);
        _contactors._secondary_camshaft.switch_contactors(_secondary_camshaft_notch);
        _contactors.switch_primary_contactors(_contactors._primary_controller.current_notch);   // Switch shunting notches at primary #1
        set_seconday_notch(_secondary_camshaft_notch);
    }

    private void calculate_combined_unit_motor_performance(bool is_unit_A, traction_motor[] traction_motors, 
        ref float RPM_sum, ref float load_sum, ref float maximum_load, ref float field_sum, ref float EMF_sum,
        out float total_torque, out float total_heat)
    {
        int first_motor = is_unit_A ? 0 : 3;
        int last_motor  = first_motor + 2;
        
        total_torque = total_heat = 0.0f;
        for (int motor_index = first_motor; motor_index <= last_motor; ++motor_index)
        {
            traction_motor motor = traction_motors[motor_index];
            total_torque += motor.wheel_torque;
            RPM_sum      += motor.RPM;
            load_sum     += motor.load_current;
            maximum_load  = Mathf.Max(maximum_load, Mathf.Abs(motor.load_current));
            field_sum    += motor.field_current;
            EMF_sum      += motor.EMF;
            total_heat   += motor.heat_emission;
        }
    }
    
    private void simulate()
    {
        check_if_disposed();
        
        blower_controller blowers = _blowers;
        bool                  jog = _jogging_mode_on && !is_powered;
        if (jog)
        {
            if (!_jog)
            {
                _contactors.toggle_jogging(turn_on: true);
                _jog = true;
            }
            _total_load.Value = _currents["BAT"];
            _jog_volts.Value  = battery_panel.battery_EMF - _total_load.Value * battery_panel.battery_internal_resistance;
            _pantograph.simulate(0.0f);
        }
        else
        {
            if (_jog)
            {
                _contactors.toggle_jogging(turn_on: false);
                _jog = false;
            }
            _total_load.Value = _currents["EPS"] + blowers.current_draw;
            if (_named_branches["EPS"].EMF > 1.0f)
                _total_load.Value += _compressor_power.Value / _named_branches["EPS"].EMF;
            _jog_volts.Value  = 0.0f;
            _pantograph.simulate(_total_load.Value);
        }
        
        set_primary_notch(_contactors._primary_controller.current_position);
        _contactor_on_sound.Value = _contactor_off_sound.Value = 0.0f;
        toggle_port_signal(_control_AB1, (int) AB1_signals.contactor_on , false);
        toggle_port_signal(_control_AB1, (int) AB1_signals.contactor_off, false);

        if (!jog && !_main_breaker_closed.State && _roof_bus.voltage < 10.0f && _named_branches["EPS"].EMF < 10.0f && _wheel_RPM.Value < 20.0f 
            && blowers.motor_current < 10.0f && blowers.relative_speed < 0.01f && _regenerative_field.relative_speed < 0.01f)
        {
            _torque_A.Value = _torque_B.Value = 0.0f;
            //_traction_motor_temperature.simulate(0.0f);
        }
        else
        {
            Dictionary<string, float> currents = _currents;
            lock (currents)
            {
                //circuit_telemetry.log_sorted_currents(_circuit, -1.0f, -1.0f);
                //circuit_telemetry.log_sorted_voltages(_circuit);
                foreach (KeyValuePair<string, branch_user> branch in _named_branches)
                    currents[branch.Key] = currents[branch.Key] * 0.95f + branch.Value.current * 0.05f;
            }
            float motors_volts = Mathf.Abs(currents["VM34"] * _element_resistances["VM34"]);
            _resistor_grid.simulate(currents);

            bool  rheostatic_brake_on = _selector is (int) selector_modes.rheostatic_brake;
            float voltage = rheostatic_brake_on ? 0.0f : _roof_bus.voltage;
            _named_branches["EPS"].EMF = _named_branches["EPS"].EMF * 0.9f + voltage * 0.1f;
            bool regenerative_on = _selector is (int) selector_modes.series_regenerative or (int) selector_modes.parallel_regenerative;
            if (regenerative_on || _regenerative_field.relative_speed >= 0.01f)
                _regenerative_field.simulate(regenerative_on, _field_position, _roof_bus.voltage, motors_volts);
        
            traction_motor[] traction_motors = _traction_motors;
            for (int motor_index = motors - 1; motor_index >= 0; --motor_index)
                traction_motors[motor_index].simulate(rheostatic_brake_on && _throttle >= 1, currents, _named_branches);
            _circuit.simulate();

            float voltmeter_reading = rheostatic_brake_on ? blowers.fan_voltage : (_named_branches["EPS"].EMF - currents["EPS"] * _element_resistances["EPS"]);
            set_supply_volts(voltmeter_reading);
            _relative_voltage.Value = voltmeter_reading / 1500.0f;
            set_motors_volts(motors_volts);
            
            _compressor_on.ChangeState(voltage >= 1000.0f && _main_breaker_closed.State);
            float average_RPM = 0.0f, average_load_A = 0.0f, average_load_B = 0.0f, maximum_load = 0.0f; 
            float average_field_A = 0.0f, average_field_B = 0.0f, average_EMF = 0.0f;
            float total_torque_A, total_heat_emission_A, total_torque_B, total_heat_emission_B;
            calculate_combined_unit_motor_performance(is_unit_A:  true, traction_motors, ref average_RPM, 
                ref average_load_A, ref maximum_load, ref average_field_A, ref average_EMF,
                out total_torque_A, out total_heat_emission_A);
            calculate_combined_unit_motor_performance(is_unit_A: false, traction_motors, ref average_RPM, 
                ref average_load_B, ref maximum_load, ref average_field_B, ref average_EMF,
                out total_torque_B, out total_heat_emission_B);
            _main_breaker.trip_if_operating_parameters_exceeded(voltage, motors_volts, maximum_load, _total_load.Value);
            average_RPM     /= motors;
            average_EMF     /= motors;
            average_load_A  /= (motors >> 1);
            average_load_B  /= (motors >> 1);
            average_field_A /= (motors >> 1);
            if (maximum_load < 10.0f)
                set_reverse_current_lamp(0.0f);
            else
                set_reverse_current_lamp((average_load_A * average_field_A * (_reverser_position - 0.5f) < 0.0f) ? 1.0f : 0.0f);
            for (int group_index = 2; group_index >= 0; --group_index)
            {
                set_motor_group_load [group_index](traction_motors[group_index << 1].load_current );
                set_motor_group_field[group_index](traction_motors[group_index << 1].field_current);
            }
            _traction_motor_load.Value   = average_load_A;
            _traction_motor_load_B.Value = average_load_B;
            _traction_motor_heat_B.Value = total_heat_emission_B;
            _traction_motor_RPM.Value    = average_RPM;
            _traction_motor_EMF.Value    = average_EMF;
        
            blowers.active                = rheostatic_brake_on || /*_primary_controller.current_notch > 1*/ _throttle >= 1;
            blowers.rheostatic_braking_on = rheostatic_brake_on;
            blowers.motor_current         = maximum_load;
            blowers.line_voltage          = rheostatic_brake_on ? motors_volts : voltage;
            blowers.simulate();
            _traction_motor_temperature.simulate(total_heat_emission_A, average_RPM);

            _torque_A.Value = total_torque_A;
            _torque_B.Value = total_torque_B;
        }
    }

    public override void Dispose()
    {
        if (!disposed)
        {
            base.Dispose();
            _pantograph.Dispose();
            _roof_bus.Dispose();
            _main_breaker.Dispose();
            _blowers.Dispose();
            _contactors.Dispose();
            _control_stand.Dispose();
            _red_light_controller.Dispose();
            _simulation.SimulationFlow.TickEvent -= simulate;
            _control_BA1.ValueUpdatedInternally  -= MU_BA1_control;
        }
    }
}
