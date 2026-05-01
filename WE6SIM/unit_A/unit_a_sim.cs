// Distributed under terms and conditions of CC0 licence. See LICENCE_CC0.txt for details.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

using DV.Simulation.Cars;
using LocoSim.Implementations;
using WE6SIM.circuit_sim;
using WE6SIM.devices;

using static WE6SIM.utilities.signal_cable;
using static WE6SIM.utilities.sensor_grabber;

namespace WE6SIM.unit_A;

internal partial class unit_a_sim: electric_device
{
    //const int nb = 3/*, mb = 6 / nb*/;
    const int motors = 6;
	const float max_exciter_voltage = 120.0f, min_exciter_voltage = 10.0f, max_exciter_current = 2000.0f;
	const float max_exciter_power = max_exciter_voltage * max_exciter_current;

	private readonly Dictionary<string, circuit.branch_user> _named_branches, _contactor_locations;
	private readonly Dictionary<string, float> _currents = [], _element_resistances = [];

	private readonly Fuse   _appliances;
	private readonly Port   _torque_a, _wheel_RPM, _traction_motor_load, _traction_motor_RPM, _traction_motor_EMF;
	private readonly Port   _contactor_on_sound, _contactor_off_sound;

	private readonly Port _control_AB1, _control_BA1, _torque_b;

	private readonly SimController _simulation;
	private readonly circuit       _circuit;

	private readonly pantograph             _pantograph;
	private readonly traction_motor[]       _traction_motors;
	private readonly blower_controller      _blowers;
	private readonly throttle_controller    _throttle_controller;
	private readonly control_stand          _control_stand;
	private readonly TrainCar               _unit;

	private readonly Action<float> set_primary_notch, set_seconday_notch, set_supply_volts, set_motors_volts;
	private readonly Action<float>[] set_motor_group_load, set_motor_group_field;

	private contactors _contactors;

	private bool _fast_notching_enabled = false;
	private int  _throttle = 0, _secondary_camshaft_notch, _selector = 3, _field_position = 0;
	private Task? _single_notch_movement;
	private float _line_voltage = 1650.0f, _reverser_position = 0.5f, _motors_volts;

	public const int camshaft_notches = 7, roll_over_to_1 = camshaft_notches + 1, roll_over_to_full = camshaft_notches + 2;

	public unit_a_sim(Dictionary<string, Fuse> fuses, Dictionary<string, Port> ports, TrainCar unit, int random_seed)
		: base("unit_A_sim")
	{
        SimController? simulation = unit.SimController ?? throw new ArgumentNullException("No simulation component");

		_appliances = grab_fuse(fuses, "fusebox.ELECTRICS_MAIN");
		set_up_fuses(_appliances);
		power_supply_toggled += appliances_toggle;

        _torque_a            = grab_port(ports, "traction.TORQUE_IN");
		_wheel_RPM           = grab_port(ports, "traction.WHEEL_RPM_EXT_IN");
        _traction_motor_load = grab_port(ports, "[CustomSimulation].MOTOR_LOAD");
        _traction_motor_RPM  = grab_port(ports, "[CustomSimulation].MOTOR_RPM" );
        _traction_motor_EMF  = grab_port(ports, "[CustomSimulation].MOTOR_EMF" );

        _torque_b    = grab_port(ports, "internal_MU.TM4-6");
		_control_AB1 = grab_port(ports, "internal_MU.CONTROL_AB1");
		_control_BA1 = grab_port(ports, "internal_MU.CONTROL_BA1");
		_control_BA1.ValueUpdatedInternally += MU_BA1_control;

		const float variation = 0.1f;
		UnityEngine.Random.State old_state = UnityEngine.Random.state;
		UnityEngine.Random.InitState(random_seed);
		foreach (KeyValuePair<string, float> element in _base_element_resistances)
			_element_resistances[element.Key] = element.Value * UnityEngine.Random.Range(1.0f - variation, 1.0f + variation);
		UnityEngine.Random.state = old_state;
        _circuit = circuit_compiler.trace(_element_resistances, circuit_diagram).set_up_simulation(out _named_branches, out _contactor_locations, _currents);
		foreach (string branch_name in _named_branches.Keys)
			_currents[branch_name] = 0.0f;

        _contactor_on_sound  = grab_port(ports, "[CustomSimulation].CONTACTOR_ON" );
        _contactor_off_sound = grab_port(ports, "[CustomSimulation].CONTACTOR_OFF");
		_contactors = new contactors(_appliances, _contactor_locations, _contactor_on_sound, _contactor_off_sound);
        _pantograph = new pantograph(unit.gameObject, _appliances);
		_traction_motors = new traction_motor[motors];
		for (int motor_number = 1; motor_number <= motors; ++motor_number)
			_traction_motors[motor_number - 1] = new traction_motor(motor_number, _wheel_RPM);
		_blowers = new blower_controller(_appliances, grab_port(ports, "[CustomSimulation].BLOWERS_RELATIVE_SPEED"), _contactor_on_sound, _contactor_off_sound);

		_control_stand       = new control_stand(_appliances, ports);
        _throttle_controller = new throttle_controller(this);
        _control_stand.register_handler("reverser_handle",      reverser_handler);
		_control_stand.register_handler("throttle_handle",      throttle_handler);
		_control_stand.register_handler(   "field_handle", field_control_handler);
        _control_stand.register_handler("selector_handle",      selector_handler);

		_control_stand.register_handler("front_pantograph_switch", toggle_pantograph);
        _control_stand.register_handler("fast_notching_switch", fast_notching_toggle);

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

	private void toggle_pantograph(float port_value)
	{
		_pantograph.toggle(port_value < 0.5f);
	}

	/*
	private void toggle_pole(float port_value)
	{
		//Main.log($"toggle_pole(): {_test != null}");
		if (disposed)
			return;
		if (port_value >= 0.5f)
		{
			if (_test_pole is null)
			{
				//Vector3 front_pos = _unit.FrontCouplerAnchor.position;
				Quaternion front_rot = _unit.FrontCouplerAnchor.rotation;
				//Vector3 offset = _unit.FrontCouplerAnchor.TransformDirection(new Vector3(0.0f, -1.125f, -5.5f));
				Vector3 pole_position = _unit.FrontCouplerAnchor.TransformPoint(new Vector3(0.0f, Main.pole_height_offset - 1.125f, -5.5f));
				_test_pole = GameObject.Instantiate<GameObject>(_test_pole_prefab, front_pos + offset pole_position, front_rot);
				_pantograph.set_target_height(6.0f + Main.pole_height_offset);
				toggle_port_signal(_control_AB1, (int) AB1_signals.back_pantograph, true);
			}
		}
		else if (_test_pole is not null)
		{
			GameObject.Destroy(_test_pole);
			_test_pole = null;
			_pantograph.set_target_height(0.0f);
			toggle_port_signal(_control_AB1, (int) AB1_signals.back_pantograph, false);
		}
	}
		*/

	private void fast_notching_toggle(float port_value)
	{
		_fast_notching_enabled = port_value >= 0.5f;
	}

	private void set_secondary_camshaft_target_notch(int target_notch)
	{
		set_port_signal(_control_AB1, (int) AB1_signals.unit_b_camshaft_notch,
			(int) AB1_shift.unit_b_camshaft_lsb, target_notch);
	}

	private int get_secondary_camshaft_current_notch(float BA1)
	{
		return extract_signal_from_port_value(BA1, (int) BA1_signals.unit_b_camshaft_notch,
			(int) BA1_shift.unit_b_camshaft_lsb);
	}

	private void appliances_toggle(bool turn_on)
	{
		toggle_port_signal(_control_AB1, (int) AB1_signals.battery, turn_on);
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
		float raw_field_position = field_handle_postion / 6.0f;
		float voltage_adjust = (1.0f - _motors_volts / _line_voltage) * max_exciter_voltage;
        float exciter_EMF = Mathf.Clamp(min_exciter_voltage * (1.0f - raw_field_position) 
			+ max_exciter_voltage * raw_field_position + voltage_adjust, min_exciter_voltage, max_exciter_voltage);
		float exciter_power = _currents["EXT"] * exciter_EMF;
		if (exciter_power > max_exciter_power)
			exciter_EMF *= max_exciter_power / exciter_power;
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
		switch (handle_postion)
        {
            case 0:
            case 1:
                _line_voltage = 1650.0f;
                break;

            case 2:
                _line_voltage = 0.0f;
				_contactors.switch_field_contactors(_field_position);
                break;

            case 3:
            case 4:
            case 5:
                _line_voltage = 1650.0f;
                _contactors.switch_field_contactors(_field_position);
                break;
        }
    }

    private void MU_BA1_control(float BA1)
	{
		if (disposed)
			return;
		/*Main.diagnostics2?.Value =*/ _secondary_camshaft_notch = get_secondary_camshaft_current_notch(BA1);
		_contactors._secondary_camshaft.switch_contactors(_secondary_camshaft_notch);
	}

	private void simulate()
	{
		check_if_disposed();
		_pantograph.move();
		_contactor_on_sound.Value = _contactor_off_sound.Value = 0.0f;

		set_primary_notch(_contactors._primary_controller.current_position);
		set_seconday_notch(_secondary_camshaft_notch);

		lock (_currents)
		{
			foreach (KeyValuePair<string, circuit.branch_user> branch in _named_branches)
				_currents[branch.Key] = _currents[branch.Key] * 0.95f + branch.Value.current * 0.05f;
		}
		float voltage = is_powered ? _line_voltage : 0.0f;
		if (_currents["EPS"] < 0.0f)
			voltage += _currents["EPS"] * _element_resistances["EPS"];
		_named_branches["EPS"].EMF = _named_branches["EPS"].EMF * 0.9f + voltage * 0.1f;
		if (_selector < 2)
			set_exciter_voltage(_field_position);
		traction_motor[] traction_motors = _traction_motors;
		for (int motor_index = motors - 1; motor_index >= 0; --motor_index)
			traction_motors[motor_index].simulate(_selector == 2, _currents, _named_branches);
		_circuit.simulate();    // Must be called after all EMFs have been set

		set_supply_volts(_named_branches["EPS"].EMF - _currents["EPS"] * _element_resistances["EPS"]);
		_motors_volts = _currents["VM"] * _element_resistances["VM"];
		set_motors_volts(_motors_volts);
		float average_RPM = 0.0f, average_load = 0.0f, maximum_load = 0.0f, average_EMF = 0.0f, total_torque = 0.0f;
		for (int motor_index = motors - 1; motor_index >= 0; --motor_index)
		{
			traction_motor motor = traction_motors[motor_index];
			total_torque += motor.wheel_torque;
			average_RPM  += motor.RPM;
			average_load += motor.load_current;
			maximum_load  = Mathf.Max(maximum_load, Mathf.Abs(motor.load_current));
			average_EMF  += motor.EMF;
		}
		average_RPM  /= traction_motors.Length;
		average_EMF  /= traction_motors.Length;
		average_load /= traction_motors.Length;
		_traction_motor_RPM.Value  = average_RPM;
		_traction_motor_load.Value = average_load;
		for (int group_index = 2; group_index >= 0; --group_index)
		{
			set_motor_group_load[group_index](traction_motors[group_index << 1].load_current);
			set_motor_group_field[group_index](traction_motors[group_index << 1].field_current);
		}
		_traction_motor_EMF.Value = average_EMF;
		_blowers.active = _selector == 2 || /*_primary_controller.current_notch > 1*/ _throttle >= 1;
		//_blowers.full_speed_mode = true;
		_blowers.simulate((_selector == 2) ? _motors_volts : (1650.0f - _currents["EPS"] * _element_resistances["EPS"]), maximum_load);

		Main.diagnostics?.Value = _currents["MA5"];
        Main.diagnostics2?.Value = _currents["MA6"];

        _torque_a.Value = _torque_b.Value = total_torque / 2.0f;
    }

    public override void Dispose()
	{
		if (!disposed)
		{
			base.Dispose();
			_pantograph.Dispose();
			_blowers.Dispose();
			_contactors.Dispose();
			_control_stand.Dispose();
			_simulation.SimulationFlow.TickEvent -= simulate;
            _control_BA1.ValueUpdatedInternally  -= MU_BA1_control;
		}
	}
}
