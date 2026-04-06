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
	private readonly GameObject _test_pole_prefab;
	private GameObject? _test_pole;

	private readonly Dictionary<string, circuit.branch_user> _named_branches, _contactor_locations;
	private readonly Dictionary<string, float> _currents = [];

	private readonly Fuse _appliances;
	private readonly Port _throttle_handle, _reverser_handle, _torque_a, _wheel_RPM;
	private readonly Port _front_pantograph_switch;
	private readonly Port _primary_notch_hand, _secondary_notch_hand;
	private readonly Port? _switch;

	private readonly Port _control_AB1, _control_BA1, _torque_b;

	private readonly SimController _simulation;
	private readonly circuit       _circuit;

	private readonly pantograph             _pantograph;
	private readonly camshaft_motor         _reverser, _primary_controller;
	private readonly camshaft_contactor_set _reverser_shaft, _primary_camshaft;
	private readonly throttle_controller    _throttle_controller;
	private readonly contactor              _line_contactor;
	private readonly TrainCar               _unit;

	private bool _traction_on = false;
	private int  _throttle = 0, _secondary_camshaft_notch;
	private Task? _single_notch_movement;

	public const int camshaft_notches = 7, roll_over_to_1 = camshaft_notches + 1, roll_over_to_full = camshaft_notches + 2;

	public unit_a_sim(Dictionary<string, Fuse> fuses, Dictionary<string, Port> ports, TrainCar unit)
		: base("unit_A_sim")
	{
		SimController? simulation = unit.SimController ?? throw new ArgumentNullException("No simulation component");

		_appliances = get_fuse(fuses, "fusebox.ELECTRICS_MAIN");
		set_up_fuses(_appliances);
		power_supply_toggled += appliances_toggle;

		_reverser_handle = get_port(ports, "[Reverser].EXT_IN");
		_reverser_handle.ValueUpdatedInternally += reverser_handler;

		_throttle_controller = new throttle_controller(this);
		_throttle_handle = get_port(ports, "[Throttle].EXT_IN");
		_throttle_handle.ValueUpdatedInternally += throttle_handler;

		_front_pantograph_switch = get_port(ports, "[FrontPantographSwitch].EXT_IN");
		_front_pantograph_switch.ValueUpdatedInternally += toggle_pole;

		_primary_notch_hand   = get_port(ports, "[CustomSimulation].PRIMARY_NOTCH"  );
        _secondary_notch_hand = get_port(ports, "[CustomSimulation].SECONDARY_NOTCH");

        _torque_a = get_port(ports, "traction.TORQUE_IN");
		_wheel_RPM = get_port(ports, "traction.WHEEL_RPM_EXT_IN");

		_torque_b = get_port(ports, "internal_MU.TM4-6");
		_control_AB1 = get_port(ports, "internal_MU.CONTROL_AB1");
		_control_BA1 = get_port(ports, "internal_MU.CONTROL_BA1");
		_control_BA1.ValueUpdatedInternally += MU_BA1_control;

		_test_pole_prefab = Main.catenary_parts.pole;

		_circuit = circuit_compiler.trace(_element_resistances, circuit_diagram).set_up_simulation(out _named_branches, out _contactor_locations);
		foreach (string branch_name in _named_branches.Keys)
			_currents[branch_name] = 0.0f;

		_pantograph = new pantograph(unit.gameObject, _appliances);
		_primary_controller = new camshaft_motor(camshaft_notches, _appliances, drop_to_1_on_power_loss: false);
		_primary_camshaft = new camshaft_contactor_set(_primary_contactor_toggles, _contactor_locations, _primary_controller);
		_reverser = new camshaft_motor(2, _appliances, drop_to_1_on_power_loss: false);
		_reverser_shaft = new camshaft_contactor_set(_reverser_toggles, _contactor_locations, _reverser);
		_line_contactor = new contactor(["LC1"], null, _contactor_locations, _appliances);

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
		}
	}

	private void reverser_handler(float raw_reverser)
	{
		if (!is_powered)
			return;
		if (raw_reverser >= 0.7f)
			_reverser.target_notch = 1;
		else if (raw_reverser <= 0.3f)
			_reverser.target_notch = 2;
	}

	private void throttle_handler(float normalised_throttle)
	{
		check_if_disposed();
		_throttle = Mathf.RoundToInt(normalised_throttle * 5.0f);
		if (!is_powered)
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

	private void MU_BA1_control(float BA1)
	{
		if (disposed)
			return;
		/*Main.diagnostics2?.Value =*/ _secondary_camshaft_notch = get_secondary_camshaft_current_notch(BA1);
	}

	private void traction_toggle(bool enable)
	{
		_traction_on = enable;
	}

	private void simulate()
	{
		check_if_disposed();
		_pantograph.move();

		_primary_notch_hand.Value   = _primary_controller.current_position;
		_secondary_notch_hand.Value = _secondary_camshaft_notch;

		//int reverser = 0 /*(_reverser_handle.Value >= 0.5f) ? 1 : ((_reverser_handle.Value <= -0.5f) ? -1 : 0)*/;
		//int throttle = 0 /*Mathf.RoundToInt(_throttle_handle.Value * 5.0f)*/;

		//_throttle.throttle_handler(reverser, throttle);

		const int nb = 1, mb = 6 / nb;
		const float max_flux = 300.0f, min_flux = 1.0f, flux_top = max_flux - min_flux, torque_factor = 0.186f, EMF_factor = 0.0195f;
		_named_branches["EPS"].EMF = 1500.0f;
		foreach (KeyValuePair<string, circuit.branch_user> branch in _named_branches)
			_currents[branch.Key] = _currents[branch.Key] * 0.95f + branch.Value.current * 0.05f;
		float EMF = mb * (-EMF_factor) * (min_flux + Mathf.Max(-flux_top, Mathf.Min(flux_top, _currents["MF1"] / nb))) * _wheel_RPM.Value;
		_named_branches["MA1"].EMF = _named_branches["MA1"].EMF * 0.7f + EMF * 0.3f;

        /*
		_contactor_locations["RF1.1"].toggle_contactor("RF1.1", reverser > 0);
		_contactor_locations["RF1.2"].toggle_contactor("RF1.2", reverser > 0);
		_contactor_locations["RR1.1"].toggle_contactor("RR1.1", reverser < 0);
		_contactor_locations["RR1.2"].toggle_contactor("RR1.2", reverser < 0);
		*/

        //_contactor_locations["CP1"].toggle_contactor("CP1", /*_traction_on &&*/ (throttle == 0 || throttle == 1 || throttle == 2 || throttle == 5));
        //_contactor_locations["CP2"].toggle_contactor("CP2", /*_traction_on &&*/ (throttle == 1 || throttle == 2 || throttle == 4 || throttle == 5));
        //_contactor_locations["CP3"].toggle_contactor("CP3", /*_traction_on &&*/ (throttle == 2 || throttle == 3 || throttle == 4 || throttle == 5));
        //_contactor_locations["CP4"].toggle_contactor("CP4", /*_traction_on &&*/ (throttle == 3 || throttle == 4 || throttle == 5));

        _circuit.simulate();
		Main.diagnostics?.Value = _currents["MA1"] / nb;
		Main.diagnostics2?.Value = _named_branches["MA1"].EMF / mb;

		float torque = 3.0f * torque_factor * (_currents["MA1"] / nb) * (min_flux + Mathf.Max(-flux_top, Mathf.Min(flux_top, _currents["MF1"] / nb)));
		_torque_a.Value = _torque_b.Value = torque;
	}

	public override void Dispose()
	{
		if (!disposed)
		{
			base.Dispose();
			_pantograph.Dispose();
            _primary_controller.Dispose();
			_primary_camshaft.Dispose();
			_reverser.Dispose();
			_reverser_shaft.Dispose();
            _line_contactor.Dispose();
			_simulation.SimulationFlow.TickEvent            -= simulate;
            _reverser_handle.ValueUpdatedInternally			-= reverser_handler;
            _throttle_handle.ValueUpdatedInternally         -= throttle_handler;
			_control_BA1.ValueUpdatedInternally             -= MU_BA1_control;
			_front_pantograph_switch.ValueUpdatedInternally -= toggle_pole;
		}
	}
}
