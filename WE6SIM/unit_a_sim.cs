// Distributed under terms and conditions of CC0 licence. See LICENCE_CC0.txt for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

using DV.Simulation.Cars;
using LocoSim.Implementations;
using WE6SIM.circuit_sim;
using WE6SIM.utilities;

using static WE6SIM.utilities.signal_cable;
using static WE6SIM.utilities.sensor_grabber;

namespace WE6SIM;

internal partial class unit_a_sim: electric_device
{
    const int nb = 6, mb = 6 / nb;
	const float max_exciter_voltage = 120.0f, min_exciter_voltage = 10.0f;

    private readonly GameObject _test_pole_prefab;
	private GameObject? _test_pole;

	private readonly Dictionary<string, circuit.branch_user> _named_branches, _contactor_locations;
	private readonly Dictionary<string, float> _currents = [];

	private readonly Fuse _appliances;
	private readonly Port _throttle_handle, _reverser_handle, _field_handle, _selector_handle; 
	private readonly Port _torque_a, _wheel_RPM, _traction_motor_load, _traction_motor_RPM, _traction_motor_EMF;
	private readonly Port _front_pantograph_switch;
	private readonly Port _primary_notch_hand, _secondary_notch_hand;
	private readonly Port _supply_volts, _motor_volts;
    private readonly Port[] _load_meter_groups, _field_meter_groups;
	private readonly Port _contactor_on_sound, _contactor_off_sound;
    private readonly Port? _switch;

	private readonly Port _control_AB1, _control_BA1, _torque_b;

	private readonly SimController _simulation;
	private readonly circuit       _circuit;

	private readonly pantograph             _pantograph;
	private readonly blower_controller      _blowers;
	private readonly camshaft_motor         _reverser, _primary_controller;
	private readonly camshaft_contactor_set _reverser_shaft, _primary_camshaft, _secondary_camshaft;
	private readonly throttle_controller    _throttle_controller;
	private readonly contactor              _line_contactor, _dynamic_brake_contactor, _regenerative_contactor;
	private readonly contactor[]            _field_shunt_contactors;
	private readonly TrainCar               _unit;

	private bool _traction_on = false;
	private int  _throttle = 0, _secondary_camshaft_notch, _selector = 3, _field_position = 0;
	private Task? _single_notch_movement;
	private float _line_voltage = 1650.0f, _reverser_position = 0.5f;

	public const int camshaft_notches = 7, roll_over_to_1 = camshaft_notches + 1, roll_over_to_full = camshaft_notches + 2;

	public unit_a_sim(Dictionary<string, Fuse> fuses, Dictionary<string, Port> ports, TrainCar unit)
		: base("unit_A_sim")
	{

        SimController? simulation = unit.SimController ?? throw new ArgumentNullException("No simulation component");

		_appliances = grab_fuse(fuses, "fusebox.ELECTRICS_MAIN");
		set_up_fuses(_appliances);
		power_supply_toggled += appliances_toggle;

		_reverser_handle = grab_port(ports, "[Reverser].EXT_IN");
		_reverser_handle.ValueUpdatedInternally += reverser_handler;
		_throttle_controller = new throttle_controller(this);
		_throttle_handle = grab_port(ports, "[Throttle].EXT_IN");
		_throttle_handle.ValueUpdatedInternally += throttle_handler;
		_field_handle = grab_port(ports, "[FieldControl].EXT_IN");
		_field_handle.ValueUpdatedInternally += field_control_handler;
		_selector_handle = grab_port(ports, "[Selector].EXT_IN");
		_selector_handle.ValueUpdatedInternally += selector_handler;

        _front_pantograph_switch = grab_port(ports, "[FrontPantographSwitch].EXT_IN");
		_front_pantograph_switch.ValueUpdatedInternally += toggle_pole;

		_primary_notch_hand   = grab_port(ports, "[CustomSimulation].PRIMARY_NOTCH"  );
        _secondary_notch_hand = grab_port(ports, "[CustomSimulation].SECONDARY_NOTCH");

        _torque_a  = grab_port(ports, "traction.TORQUE_IN");
		_wheel_RPM = grab_port(ports, "traction.WHEEL_RPM_EXT_IN");
        _traction_motor_load = grab_port(ports, "[CustomSimulation].MOTOR_LOAD");
        _traction_motor_RPM  = grab_port(ports, "[CustomSimulation].MOTOR_RPM" );
        _traction_motor_EMF  = grab_port(ports, "[CustomSimulation].MOTOR_EMF" );

        _torque_b = grab_port(ports, "internal_MU.TM4-6");
		_control_AB1 = grab_port(ports, "internal_MU.CONTROL_AB1");
		_control_BA1 = grab_port(ports, "internal_MU.CONTROL_BA1");
		_control_BA1.ValueUpdatedInternally += MU_BA1_control;

		_test_pole_prefab = Main.catenary_parts.pole;

		_circuit = circuit_compiler.trace(_element_resistances, circuit_diagram).set_up_simulation(out _named_branches, out _contactor_locations);
		foreach (string branch_name in _named_branches.Keys)
			_currents[branch_name] = 0.0f;

        _contactor_on_sound  = grab_port(ports, "[CustomSimulation].CONTACTOR_ON" );
        _contactor_off_sound = grab_port(ports, "[CustomSimulation].CONTACTOR_OFF");
        _pantograph = new pantograph(unit.gameObject, _appliances);
		_blowers = new blower_controller(_appliances, grab_port(ports, "[CustomSimulation].BLOWERS_RELATIVE_SPEED"), _contactor_on_sound, _contactor_off_sound);
		_primary_controller = new camshaft_motor(camshaft_notches, _appliances, drop_to_1_on_power_loss: false);
		_primary_camshaft = new camshaft_contactor_set(_primary_contactor_toggles, _contactor_locations, _primary_controller, _contactor_on_sound, _contactor_off_sound);
		_secondary_camshaft = new camshaft_contactor_set(_secondary_contactor_toggles, _contactor_locations, null, _contactor_on_sound, _contactor_off_sound);
		_reverser = new camshaft_motor(2, _appliances, drop_to_1_on_power_loss: false);
		_reverser_shaft = new camshaft_contactor_set(_reverser_toggles, _contactor_locations, _reverser, _contactor_on_sound, _contactor_off_sound);
		_line_contactor = new contactor(["LC1"], null, _contactor_locations, _contactor_on_sound, _contactor_off_sound, _appliances);
		_field_shunt_contactors = new contactor[6];
		const int motors = 1;
		for (int field_contactor = 1; field_contactor <= 6; ++field_contactor)
		{
			if (field_contactor == 3)
				continue;
			string[] contacts = new string[motors];
			for (int motor = 1; motor <= motors; ++motor)
				contacts[motor - 1] = $"FS{motor}.{field_contactor}";
			_field_shunt_contactors[field_contactor - 1] = new contactor(contacts, null, _contactor_locations, _contactor_on_sound, _contactor_off_sound, _appliances);
		}
		string[] open_contacts = new string[motors], closed_contacts = new string[motors];
		for (int motor = 1; motor <= motors; ++motor)
		{
            open_contacts[motor - 1] = $"FS{motor}.3o";
            closed_contacts[motor - 1] = $"FS{motor}.3c";
        }
		_field_shunt_contactors[3 - 1] = new contactor(open_contacts, closed_contacts, _contactor_locations, _contactor_on_sound, _contactor_off_sound, _appliances);
		_dynamic_brake_contactor = new contactor(["DBo"], ["DBc"], _contactor_locations, _contactor_on_sound, _contactor_off_sound, _appliances);
		_regenerative_contactor = new contactor(["RB1.3o"], ["RB1.1c", "RB1.2c"], _contactor_locations, _contactor_on_sound, _contactor_off_sound, _appliances);

        _supply_volts = grab_port(ports, "[CustomGauges].SUPPLY"            );
		_motor_volts  = grab_port(ports, "[CustomGauges].ALL_MOTOR_TERMINAL");
        _load_meter_groups  = new Port[3];
        _field_meter_groups = new Port[3];
		for (int group = 1; group <= 3; ++group)
		{
			_load_meter_groups [group - 1] = grab_port(ports, $"[CustomGauges].LOAD_GROUP{group}" );
            _field_meter_groups[group - 1] = grab_port(ports, $"[CustomGauges].FIELD_GROUP{group}");
        }

        _unit = unit;
		_simulation = simulation;
		simulation.SimulationFlow.TickEvent += simulate;
	}

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
				_test_pole = GameObject.Instantiate<GameObject>(_test_pole_prefab, /*front_pos + offset*/ pole_position, front_rot);
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
		check_if_disposed();
		toggle_port_signal(_control_AB1, (int) AB1_signals.battery, turn_on);
		if (turn_on)
		{
			reverser_handler(_reverser_handle.Value);
			throttle_handler(_throttle_handle.Value);
			field_control_handler(_field_handle.Value);
		}
	}

	private void reverser_handler(float raw_reverser)
	{
		if (!is_powered || disposed)
			return;
		_reverser_position = raw_reverser;
		if (_selector == 2)
			raw_reverser = 1.0f - raw_reverser;
		if (raw_reverser >= 0.7f)
			_reverser.target_notch = 1;
		else if (raw_reverser <= 0.3f)
			_reverser.target_notch = 2;
	}

	private void throttle_handler(float raw_throttle)
	{
		_throttle = Mathf.RoundToInt(raw_throttle * 5.0f);
		if (!is_powered || disposed)
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
		//set_port_signal(_control_AB1, (int) AB1_signals.unit_b_camshaft_notch,
		//	(int) AB1_shift.unit_b_camshaft_lsb, _throttle + 1);
	}

	private void field_control_handler(float raw_field_position)
	{
        if (!is_powered || disposed)
            return;
        int handle_postion = Mathf.RoundToInt(raw_field_position * 6.0f);
		_field_position = handle_postion;
		if (_selector < 2)
			_named_branches["EXT"].EMF = min_exciter_voltage * (1.0f - raw_field_position) + max_exciter_voltage * raw_field_position;
		else
        {
            _named_branches["EXT"].EMF = 0.0f;
            for (int field_contactor_on = 0; field_contactor_on < handle_postion; ++field_contactor_on)
			_field_shunt_contactors[field_contactor_on].toggle(turn_on: true);
			for (int field_contactor_off = handle_postion; field_contactor_off < 6; ++field_contactor_off)
				_field_shunt_contactors[field_contactor_off].toggle(turn_on: false);
		}
    }

	private void selector_handler(float raw_selector_position)
	{
        if (!is_powered || disposed)
            return;
        int handle_postion = Mathf.RoundToInt(raw_selector_position * 5.0f);
		_selector = handle_postion;
		switch (handle_postion)
		{
			case 0:
                _line_voltage = 825.0f;
                reverser_handler(_reverser_position);
                _dynamic_brake_contactor.toggle(turn_on: false);
                _regenerative_contactor.toggle(turn_on: true);
                _field_shunt_contactors[0].toggle(turn_on: true);
                _field_shunt_contactors[1].toggle(turn_on: true);
                _field_shunt_contactors[2].toggle(turn_on: false);
                _field_shunt_contactors[3].toggle(turn_on: true);
                _field_shunt_contactors[4].toggle(turn_on: true);
                _field_shunt_contactors[5].toggle(turn_on: true);
                break;

            case 1:
				_line_voltage = 1650.0f;
                reverser_handler(_reverser_position);
                _dynamic_brake_contactor.toggle(turn_on: false);
				_regenerative_contactor.toggle(turn_on: true);
				_field_shunt_contactors[0].toggle(turn_on: true);
                _field_shunt_contactors[1].toggle(turn_on: true);
                _field_shunt_contactors[2].toggle(turn_on: false);
                _field_shunt_contactors[3].toggle(turn_on: true);
                _field_shunt_contactors[4].toggle(turn_on: true);
                _field_shunt_contactors[5].toggle(turn_on: true);
                break;

			case 2:
                _line_voltage = 0.0f;
                reverser_handler(_reverser_position);
                _regenerative_contactor.toggle(turn_on: false);
                _dynamic_brake_contactor.toggle(turn_on: true);
				field_control_handler(_field_position);
				break;

			case 3:
                _line_voltage = 275.0f;
                reverser_handler(_reverser_position);
                _regenerative_contactor.toggle(turn_on: false);
                _dynamic_brake_contactor.toggle(turn_on: false);
                field_control_handler(_field_position);
				break;
            
			case 4:
                _line_voltage = 825.0f;
                reverser_handler(_reverser_position);
                _regenerative_contactor.toggle(turn_on: false);
                _dynamic_brake_contactor.toggle(turn_on: false);
                field_control_handler(_field_position);
                break;

            case 5:
                _line_voltage = 1650.0f;
				reverser_handler(_reverser_position);
                _regenerative_contactor.toggle(turn_on: false);
                _dynamic_brake_contactor.toggle(turn_on: false);
                field_control_handler(_field_position);
                break;
        }
    }

    private void MU_BA1_control(float BA1)
	{
		if (disposed)
			return;
		/*Main.diagnostics2?.Value =*/ _secondary_camshaft_notch = get_secondary_camshaft_current_notch(BA1);
		_secondary_camshaft.switch_contactors(_secondary_camshaft_notch);
	}

	private void traction_toggle(bool enable)
	{
		_traction_on = enable;
	}

	private void simulate()
	{
		check_if_disposed();
		_pantograph.move();
		_contactor_on_sound.Value = _contactor_off_sound.Value = 0.0f;

		_primary_notch_hand.Value   = _primary_controller.current_position;
		_secondary_notch_hand.Value = _secondary_camshaft_notch;

		//int reverser = 0 /*(_reverser_handle.Value >= 0.5f) ? 1 : ((_reverser_handle.Value <= -0.5f) ? -1 : 0)*/;
		//int throttle = 0 /*Mathf.RoundToInt(_throttle_handle.Value * 5.0f)*/;

		//_throttle.throttle_handler(reverser, throttle);

		const float max_flux = 300.0f, min_flux = 1.0f;
		const float gear_ratio = 5.36f, torque_factor = 0.0347f, EMF_factor = 0.003634f;
		float voltage = is_powered ? _line_voltage : 0.0f;
		if (_currents["EPS"] < 0.0f)
			voltage += _currents["EPS"] * _element_resistances["EPS"];
        _named_branches["EPS"].EMF = _named_branches["EPS"].EMF * 0.9f + voltage * 0.1f;
		_supply_volts.Value = _named_branches["EPS"].EMF - _currents["EPS"] * _element_resistances["EPS"];
        foreach (KeyValuePair<string, circuit.branch_user> branch in _named_branches)
			_currents[branch.Key] = _currents[branch.Key] * 0.95f + branch.Value.current * 0.05f;
		float motor_RPM = _wheel_RPM.Value * gear_ratio;
		float magnetic_flux1 = (min_flux + Mathf.Clamp(Mathf.Abs(_currents["MF1a"] / nb), 0.0f, max_flux - min_flux)) * (1.0f - 0.63f);
        float magnetic_flux2 = (min_flux + Mathf.Clamp(Mathf.Abs(_currents["MF1b"] / nb), 0.0f, max_flux - min_flux)) * 0.63f;
		float magnetic_flux = magnetic_flux1 + magnetic_flux2;
        if (_currents["MF1b"] < 0.0f)
			magnetic_flux = -magnetic_flux;
        float EMF = mb * (-EMF_factor) * magnetic_flux * motor_RPM;
        _named_branches["MA1"].EMF = _named_branches["MA1"].EMF * 0.7f + EMF * 0.3f;
		_motor_volts.Value = _currents["VM"] * _element_resistances["VM"];
		_blowers.active = _selector == 2 || /*_primary_controller.current_notch > 1*/ _throttle >= 1;
		//_blowers.full_speed_mode = true;
		
        _circuit.simulate();
		_blowers.simulate((_selector == 2) ? _motor_volts.Value : (1650.0f - _currents["EPS"] * _element_resistances["EPS"]), _currents["MA1"] / nb);

        _traction_motor_RPM.Value = motor_RPM;
		_traction_motor_load.Value = _load_meter_groups[0].Value = _currents["MA1"] / nb;
		_field_meter_groups[0].Value = _currents["MF1b"] / nb;
		_traction_motor_EMF.Value = _named_branches["MA1"].EMF / (-mb);

		Main.diagnostics?.Value = _named_branches["EPS"].EMF;
        //Main.diagnostics2?.Value = _currents["MF1b"] / nb;

        float half_torque = (6.0f / 2.0f) * torque_factor * gear_ratio * (_currents["MA1"] / nb) * magnetic_flux;
		_torque_a.Value = _torque_b.Value = half_torque;
	}

	public override void Dispose()
	{
		if (!disposed)
		{
			base.Dispose();
			_pantograph.Dispose();
			_blowers.Dispose();
            _primary_controller.Dispose();
			_primary_camshaft.Dispose();
			_secondary_camshaft.Dispose();
			_reverser.Dispose();
			_reverser_shaft.Dispose();
            _line_contactor.Dispose();
			_dynamic_brake_contactor.Dispose();
			_regenerative_contactor.Dispose();
			for (int field_contactor = 0; field_contactor < 6; ++field_contactor)
				_field_shunt_contactors[field_contactor].Dispose();
			_simulation.SimulationFlow.TickEvent            -= simulate;
            _reverser_handle.ValueUpdatedInternally			-= reverser_handler;
            _throttle_handle.ValueUpdatedInternally         -= throttle_handler;
            _field_handle.ValueUpdatedInternally            -= field_control_handler;
            _selector_handle.ValueUpdatedInternally			-= selector_handler;
            _control_BA1.ValueUpdatedInternally             -= MU_BA1_control;
			_front_pantograph_switch.ValueUpdatedInternally -= toggle_pole;
		}
	}
}
