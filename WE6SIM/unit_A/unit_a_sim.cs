// Distributed under terms and conditions of CC0 licence. See LICENCE_CC0.txt for details.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using DV.Simulation.Cars;

using LocoSim.Implementations;

using UnityEngine;

using WE6SIM.catenary;
using WE6SIM.circuit_sim;
using WE6SIM.devices;
using WE6SIM.utilities;

using static WE6SIM.utilities.sensor_grabber;
using static WE6SIM.utilities.signal_cable;

namespace WE6SIM.unit_A;

internal partial class unit_a_sim: electric_device
{
    const int   motors = 6;
    const float max_exciter_voltage = 120.0f, min_exciter_voltage = 10.0f, max_exciter_current = 2000.0f;
    const float max_exciter_power = max_exciter_voltage * max_exciter_current;

    private readonly Dictionary<string, circuit.branch_user> _named_branches, _contactor_locations;
    private readonly Dictionary<string, float> _currents = [], _element_resistances = [];

    private readonly Fuse _appliances, _overhead_power, _control_air;
    private readonly Port _torque_a, _wheel_RPM, _traction_motor_load, _traction_motor_RPM, _traction_motor_EMF;
    private readonly Port _contactor_on_sound, _contactor_off_sound;
    private readonly Port _reverse_current_lamp;
    private readonly Port _independent_brake, _sander;
    private readonly Port _total_load;

    private readonly Port _control_AB1, _control_BA1, _torque_B, _wheel_RPM_B;

    private readonly SimController _simulation;
    private readonly circuit       _circuit;

    private readonly pantograph                 _pantograph;
    private readonly roof_busbar                _roof_bus;
    private readonly traction_motor[]           _traction_motors;
    private readonly blower_controller          _blowers;
    private readonly throttle_controller        _throttle_controller;
    private readonly control_stand              _control_stand;
    private readonly red_ditch_light_controller _red_light_controller;
    
    private readonly TrainCar _unit;

    private readonly Action<float> set_primary_notch, set_seconday_notch, set_supply_volts, set_motors_volts;
    private readonly Action<float>[] set_motor_group_load, set_motor_group_field;

    private contactors _contactors;

    private bool _fast_notching_enabled = false;
    private int  _throttle = 0, _secondary_camshaft_notch, _selector = 3, _field_position = 0;
    private Task? _single_notch_movement;
    private float _reverser_position = 0.5f, _motors_volts;

    public const int camshaft_notches = 7, roll_over_to_1 = camshaft_notches + 1, roll_over_to_full = camshaft_notches + 2;

    public unit_a_sim(Dictionary<string, Fuse> fuses, Dictionary<string, Port> ports, TrainCar unit, int random_seed)
        : base("unit_A_sim")
    {
        SimController? simulation = unit.SimController ?? throw new ArgumentNullException("No simulation component");

        _appliances     = grab_fuse(fuses, "fusebox.ELECTRONICS_MAIN");
        _overhead_power = grab_fuse(fuses, "fusebox.OVERHEAD_POWER"  );
        _control_air    = grab_fuse(fuses, "fusebox.CONTROL_AIR"     );
        set_up_fuses(_appliances);
        _overhead_power.StateUpdated += overhead_power_toggle;

        _torque_a            = grab_port(ports, "traction.TORQUE_IN");
        _wheel_RPM           = grab_port(ports, "traction.WHEEL_RPM_EXT_IN");
        _traction_motor_load = grab_port(ports, "[CustomSimulation].MOTOR_LOAD");
        _traction_motor_RPM  = grab_port(ports, "[CustomSimulation].MOTOR_RPM" );
        _traction_motor_EMF  = grab_port(ports, "[CustomSimulation].MOTOR_EMF" );
        _total_load          = grab_port(ports, "[CustomGauges].CURRENT_DRAW");

        const float variation = 0.1f;
        UnityEngine.Random.State old_state = UnityEngine.Random.state;
        UnityEngine.Random.InitState(random_seed);
        foreach (KeyValuePair<string, float> element in _base_element_resistances)
            _element_resistances[element.Key] = element.Value * UnityEngine.Random.Range(1.0f - variation, 1.0f + variation);
        UnityEngine.Random.state = old_state;
        _circuit = circuit_compiler.trace(_element_resistances, circuit_diagram).set_up_simulation(out _named_branches, out _contactor_locations, _currents);
        foreach (string branch_name in _named_branches.Keys)
            _currents[branch_name] = 0.0f;

        _torque_B    = grab_port(ports, "[internal_MU].TM4-6");
        _wheel_RPM_B = grab_port(ports, "[internal_MU].WHEEL_RPM_FROM_B");

        _contactor_on_sound  = grab_port(ports, "[CustomSimulation].CONTACTOR_ON" );
        _contactor_off_sound = grab_port(ports, "[CustomSimulation].CONTACTOR_OFF");
        _contactors = new contactors(_appliances, _control_air, _contactor_locations, _contactor_on_sound, _contactor_off_sound);
        _roof_bus   = new roof_busbar(ports, is_unit_A: true);
        _pantograph = new pantograph(unit.gameObject, _roof_bus, _appliances, _control_air);
        _traction_motors = new traction_motor[motors];
        for (int motor_number = 1; motor_number <= motors / 2; ++motor_number)
            _traction_motors[motor_number - 1] = new traction_motor(motor_number, _wheel_RPM);
        for (int motor_number = motors / 2 + 1; motor_number <= motors; ++motor_number)
            _traction_motors[motor_number - 1] = new traction_motor(motor_number, _wheel_RPM_B);
        _blowers = new blower_controller(_appliances, grab_port(ports, "[CustomSimulation].BLOWERS_RELATIVE_SPEED"), _contactor_on_sound, _contactor_off_sound);

        _control_AB1 = grab_port(ports, "[internal_MU].CONTROL_AB1");
        _control_BA1 = grab_port(ports, "[internal_MU].CONTROL_BA1");
        _control_BA1.ValueUpdatedInternally += MU_BA1_control;

        _control_stand       = new control_stand(_appliances, ports);
        _throttle_controller = new throttle_controller(this);
        _control_stand.register_handler("reverser_handle",      reverser_handler);
        _control_stand.register_handler("throttle_handle",      throttle_handler);
        _control_stand.register_handler(   "field_handle", field_control_handler);
        _control_stand.register_handler("selector_handle",      selector_handler);

        _control_stand.register_handler("front_pantograph_switch", toggle_front_pantograph);
        _control_stand.register_handler( "back_pantograph_switch",  toggle_back_pantograph);
        _control_stand.register_handler(    "left_sidepan_switch",     toggle_left_sidepan);
        _control_stand.register_handler(   "right_sidepan_switch",    toggle_right_sidepan);
        _control_stand.register_handler(   "fast_notching_switch",    fast_notching_toggle);
        _red_light_controller = new red_ditch_light_controller(_appliances, ports);
        _reverse_current_lamp = grab_port(ports, "[CustomGauges].REVERSE_CURRENT");
        _independent_brake    = grab_port(ports, "[IndependentBrake].EXT_IN");
        _independent_brake.ValueUpdatedInternally += synchronise_independent_brake;
        _sander = grab_port(ports, "[Sander].CONTROL_EXT_IN");
        _sander.ValueUpdatedInternally += synchronise_sander;

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

        _unit = unit;
        _simulation = simulation;
        simulation.SimulationFlow.TickEvent += simulate;
    }

    private void toggle_front_pantograph(float port_value)
    {
        _pantograph.toggle(port_value < 0.5f);
    }

    private void toggle_back_pantograph(float port_value)
    {
        toggle_port_signal(_control_AB1, (int) AB1_signals.unit_B_pantograph, port_value >= 0.5f);
    }

    private void toggle_right_sidepan(float port_value)
    {
        _pantograph.sidepan_toggle(port_value < 0.5f);
    }
    
    private void toggle_left_sidepan(float port_value)
    {
        toggle_port_signal(_control_AB1, (int) AB1_signals.unit_B_sidepan, port_value >= 0.5f);
    }

    private void fast_notching_toggle(float port_value)
    {
        _fast_notching_enabled = port_value >= 0.5f;
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

    private void overhead_power_toggle(bool turn_on)
    {
        toggle_port_signal(_control_AB1, (int) AB1_signals.unit_B_overhead_power, turn_on);
    }

    private void synchronise_independent_brake(float raw_handle_position)
    {
        set_port_signal(_control_AB1, (int) AB1_signals.unit_B_independent_brake, (int) AB1_shift.unit_B_independent_brake, 
            Mathf.RoundToInt(raw_handle_position * 5.0f));
    }
    
    private void synchronise_sander(float sander_switch)
    {
        toggle_port_signal(_control_AB1, (int) AB1_signals.unit_B_sander, sander_switch >= 0.5f);
    }

    private void reverser_handler(float raw_reverser)
    {
        if (disposed)
            return;
        _reverser_position = raw_reverser;
        if (_selector == 2)
            raw_reverser = 1.0f - raw_reverser;
        if (raw_reverser >= 0.7f)
            _contactors._reverser.target_notch = 1;
        else if (raw_reverser <= 0.3f)
            _contactors._reverser.target_notch = 2;
    }

    private void throttle_handler(float raw_throttle)
    {
        _throttle = Mathf.RoundToInt(raw_throttle * 5.0f);
        if (disposed)
            return;
        switch (_throttle)
        {
            case 0:
                //if (_single_notch_movement != null && !_single_notch_movement.IsCompleted)
                //	_interrupt_single_notch_movement = true;
                _throttle_controller.roll_camshafts_over();
                break;

            case 1:
                _throttle_controller.run_down();
                break;

            case 2:
                if (_single_notch_movement == null || _single_notch_movement.IsCompleted)
                    _single_notch_movement = _throttle_controller.notch_down();
                break;

            case 3:
                _ = _throttle_controller.unlock_camshafts(continuous_run: false);
                break;

            case 4:
                if (_single_notch_movement == null || _single_notch_movement.IsCompleted)
                    _single_notch_movement = _throttle_controller.notch_up();
                break;

            case 5:
                _throttle_controller.run_up();
                break;
        }
    }

    private void set_exciter_voltage(int field_handle_postion)
    {
        float line_voltage = _roof_bus.voltage, exciter_EMF;
        if (line_voltage < 1000.0f || !_overhead_power.State)
            exciter_EMF = 0.0f;
        else
        {
            float raw_field_position = field_handle_postion / 6.0f;
            float voltage_adjust = (1.0f - _motors_volts / line_voltage) * max_exciter_voltage;
            exciter_EMF = Mathf.Clamp(min_exciter_voltage * (1.0f - raw_field_position) 
                + max_exciter_voltage * raw_field_position + voltage_adjust, min_exciter_voltage, max_exciter_voltage);
            float exciter_power = _currents["EXT"] * exciter_EMF;
            if (exciter_power > max_exciter_power)
                exciter_EMF *= max_exciter_power / exciter_power;
        }
        _named_branches["EXT"].EMF = exciter_EMF;
    }

    private void field_control_handler(float raw_field_position)
    {
        if (disposed)
            return;
        int handle_postion = Mathf.RoundToInt(raw_field_position * 6.0f);
        _field_position = handle_postion;
        if (_selector < 2)
            set_exciter_voltage(handle_postion);
        else
        {
            _named_branches["EXT"].EMF = 0.0f;
            _contactors.switch_field_contactors(handle_postion);
        }
    }

    private void selector_handler(float raw_selector_position)
    {
        if (disposed)
            return;
        int handle_postion = Mathf.RoundToInt(raw_selector_position * 5.0f);
        _selector = handle_postion;
        reverser_handler(_reverser_position);
        _contactors.switch_selector_contactors(handle_postion);
        if (handle_postion >= 2)
            _contactors.switch_field_contactors(_field_position);
    }

    private void MU_BA1_control(float BA1)
    {
        if (disposed)
            return;
        _appliances.ChangeState (port_value_signal_active(BA1, (int) BA1_signals.battery           ));
        _control_air.ChangeState(port_value_signal_active(BA1, (int) BA1_signals.control_air_usable));
        _secondary_camshaft_notch = get_secondary_camshaft_current_notch(BA1);
        _contactors._secondary_camshaft.switch_contactors(_secondary_camshaft_notch);
    }

    private void simulate()
    {
        check_if_disposed();
        overhead_equipment.system.handle_scenery_visibility(_unit.transform.position);
        _total_load.Value = _currents["EPS"];
        _pantograph.simulate(_total_load.Value);
        _contactor_on_sound.Value = _contactor_off_sound.Value = 0.0f;

        set_primary_notch(_contactors._primary_controller.current_position);
        set_seconday_notch(_secondary_camshaft_notch);

        lock (_currents)
        {
            foreach (KeyValuePair<string, circuit.branch_user> branch in _named_branches)
                _currents[branch.Key] = _currents[branch.Key] * 0.95f + branch.Value.current * 0.05f;
        }
        bool  rheostatic_brake_on = _selector == 2;
        float voltage = rheostatic_brake_on ? 0.0f : _roof_bus.voltage;
        _overhead_power.ChangeState(!rheostatic_brake_on && voltage >= 1000.0f);
        _named_branches["EPS"].EMF = _named_branches["EPS"].EMF * 0.9f + voltage * 0.1f;
        if (_selector < 2)
            set_exciter_voltage(_field_position);
        traction_motor[] traction_motors = _traction_motors;
        for (int motor_index = motors - 1; motor_index >= 0; --motor_index)
            traction_motors[motor_index].simulate(rheostatic_brake_on, _currents, _named_branches);
        _circuit.simulate();    // Must be called after all EMFs have been set

        set_supply_volts(_named_branches["EPS"].EMF - _currents["EPS"] * _element_resistances["EPS"]);
        if (_selector == 3)
        {
            _motors_volts = Mathf.Abs(_currents["VM12"] * _element_resistances["VM12"])
                          + Mathf.Abs(_currents["VM34"] * _element_resistances["VM34"])
                          + Mathf.Abs(_currents["VM56"] * _element_resistances["VM56"]);
        }
        else
        {
            float motor_volts_1_4 = Mathf.Max(Mathf.Abs(_currents["VM12"] * _element_resistances["VM12"]), 
                                              Mathf.Abs(_currents["VM34"] * _element_resistances["VM34"]));
            _motors_volts         = Mathf.Max(Mathf.Abs(_currents["VM56"] * _element_resistances["VM56"]), motor_volts_1_4);
        }
        set_motors_volts(_motors_volts);
        float average_RPM = 0.0f, average_load = 0.0f, maximum_load = 0.0f, average_field = 0.0f, average_EMF = 0.0f, total_torque = 0.0f;
        for (int motor_index = motors - 1; motor_index >= 0; --motor_index)
        {
            traction_motor motor = traction_motors[motor_index];
            total_torque  += motor.wheel_torque;
            average_RPM   += motor.RPM;
            average_load  += motor.load_current;
            maximum_load   = Mathf.Max(maximum_load, Mathf.Abs(motor.load_current));
            average_field += motor.field_current;
            average_EMF   += motor.EMF;
        }
        average_RPM   /= traction_motors.Length;
        average_EMF   /= traction_motors.Length;
        average_load  /= traction_motors.Length;
        average_field /= traction_motors.Length;
        _traction_motor_RPM.Value   = average_RPM;
        _traction_motor_load.Value  = average_load;
        if (maximum_load < 10.0f)
            _reverse_current_lamp.Value = 0.0f;
        else
            _reverse_current_lamp.Value = (average_load * average_field * (_reverser_position - 0.5f) < 0.0f) ? 1.0f : 0.0f;
        for (int group_index = 2; group_index >= 0; --group_index)
        {
            set_motor_group_load [group_index](traction_motors[group_index << 1].load_current );
            set_motor_group_field[group_index](traction_motors[group_index << 1].field_current);
        }
        _traction_motor_EMF.Value = average_EMF;
        
        _blowers.active                = rheostatic_brake_on || /*_primary_controller.current_notch > 1*/ _throttle >= 1;
        _blowers.rheostatic_braking_on = rheostatic_brake_on;
        _blowers.motor_current         = maximum_load;
        _blowers.line_voltage          = rheostatic_brake_on ? _motors_volts : voltage;
        //_blowers.full_speed_mode = true;
        _blowers.simulate();

        _torque_a.Value = _torque_B.Value = total_torque / 2.0f;
    }

    public override void Dispose()
    {
        if (!disposed)
        {
            base.Dispose();
            _pantograph.Dispose();
            _roof_bus.Dispose();
            _blowers.Dispose();
            _contactors.Dispose();
            _control_stand.Dispose();
            _red_light_controller.Dispose();
            _simulation.SimulationFlow.TickEvent      -= simulate;
            _control_BA1.ValueUpdatedInternally       -= MU_BA1_control;
            _independent_brake.ValueUpdatedInternally -= synchronise_independent_brake;
        }
    }
}
