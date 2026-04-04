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

internal partial class unit_a_sim: IDisposable
{
	private readonly GameObject _test_pole_prefab;
	private GameObject? _test_pole;

	private readonly Dictionary<string, circuit.branch_user> _named_branches, _contactor_locations;
	private readonly Dictionary<string, float> _currents = [];

	private readonly Fuse _appliances;
	private readonly Port _throttle_handle, _reverser_handle, _torque_a, _wheel_RPM;
	private readonly Port _front_pantograph_switch;
	private readonly Port? _switch;

	private readonly Port _control_AB1, _control_BA1, _torque_b;

	private readonly SimController       _simulation;
	private readonly circuit             _circuit;
	private readonly pantograph          _pantograph;
	private readonly camshaft_controller _primary_controller = new(7);

	private bool _traction_on = false, _camshaft_unlock = false;
	private int  _reverser = 0, _throttle = 0, _secondary_camshaft_notch;
	private Task? _single_notch_movement;

	private readonly TrainCar _unit;

	public unit_a_sim(Dictionary<string, Fuse> fuses, Dictionary<string, Port> ports, TrainCar unit)
	{
		SimController? simulation = unit.SimController ?? throw new ArgumentNullException("No simulation component");

		_appliances = get_fuse(fuses, "fusebox.ELECTRICS_MAIN");
		_appliances.StateUpdated += appliances_toggle;

		_reverser_handle = get_port(ports, "[Reverser].EXT_IN");
		_throttle_handle = get_port(ports, "[Throttle].EXT_IN");
		_throttle_handle.ValueUpdatedInternally += throttle_handler;

		_front_pantograph_switch = get_port(ports, "[FrontPantographSwitch].EXT_IN");
		_front_pantograph_switch.ValueUpdatedInternally += toggle_pole;

		_torque_a = get_port(ports, "traction.TORQUE_IN");
		_wheel_RPM = get_port(ports, "traction.WHEEL_RPM_EXT_IN");
		//_switch = new_unit_a.traced_switch;

		_torque_b = get_port(ports, "internal_MU.TM4-6");
		_control_AB1 = get_port(ports, "internal_MU.CONTROL_AB1");
		_control_BA1 = get_port(ports, "internal_MU.CONTROL_BA1");
		_control_BA1.ValueUpdatedInternally += MU_BA1_control;

		_test_pole_prefab = Main.catenary_parts.pole;

		_circuit = circuit_compiler.trace(_element_resistances, circuit_diagram).set_up_simulation(out _named_branches, out _contactor_locations);
		foreach (string branch_name in _named_branches.Keys)
			_currents[branch_name] = 0.0f;

		_unit       = unit;
		_simulation = simulation;
		simulation.SimulationFlow.TickEvent += simulate;
		_pantograph = new pantograph(unit.gameObject);
	}

	private void toggle_pole(float port_value)
	{
		//Main.log($"toggle_pole(): {_test != null}");
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

	/*
	private bool _disposed = false;
	public async void port_watch_test()
	{
		Main.log("port_watch_test() started");
		while (!_disposed)
		{
			await port_value_change.watch(_reverser_handle);
			Main.log($"port_watch_test() = {_reverser_handle.Value}");
		}
	}
	*/

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
		if (turn_on)
		{
			throttle_handler(_throttle / 5.0f);
		}
	}

	private async Task finish_secondary_movement(int target_notch)
	{
		set_secondary_camshaft_target_notch(target_notch);
		if (target_notch == 8)
			target_notch = 1;
		else if (target_notch == 9)
			target_notch = 7;
		while (get_secondary_camshaft_current_notch(_control_BA1.Value) != target_notch)
			await Task.Delay(200);
	}

	private async Task notch_down()
	{
		bool secondary_at_1 = _secondary_camshaft_notch == 1;
		int current_primary_notch = _primary_controller.current_notch;
		if (!_camshaft_unlock || secondary_at_1 && current_primary_notch == 1)
			return;
		_camshaft_unlock = false;
		if (!secondary_at_1)
			await finish_secondary_movement(_secondary_camshaft_notch - 1);
		else
		{
			_primary_controller.target_notch = current_primary_notch - 1;
			await _primary_controller.finish_movement();
			await finish_secondary_movement(9);
		}
	}

	private async Task notch_up()
	{
		bool secondary_at_7 = _secondary_camshaft_notch == 7;
		int current_primary_notch = _primary_controller.current_notch;
		if (!_camshaft_unlock || secondary_at_7 && current_primary_notch == 7)
			return;
		_camshaft_unlock = false;
		if (!secondary_at_7)
			await finish_secondary_movement(_secondary_camshaft_notch + 1);
		else
		{
			await finish_secondary_movement(8);
			_primary_controller.target_notch = current_primary_notch + 1;
			await _primary_controller.finish_movement();
		}
	}

	private async Task unlock_camshafts(bool continuous_run)
	{
		if (_single_notch_movement != null && !_single_notch_movement.IsCompleted)
			await _single_notch_movement;
		if (continuous_run || _throttle == 3)
			_camshaft_unlock = true;
	}

	private async void run_down()
	{
		while (_throttle == 1 && (_secondary_camshaft_notch > 1 || _primary_controller.current_notch > 1))
		{
			await unlock_camshafts(continuous_run: true);
			_single_notch_movement = notch_down();
		}
	}

	private async void run_up()
	{
		while (_throttle == 5 && (_secondary_camshaft_notch < 7 || _primary_controller.current_notch < 7))
		{
			await unlock_camshafts(continuous_run: true);
			_single_notch_movement = notch_up();
		}
	}

	private void throttle_handler(float normalised_throttle)
	{
		_throttle = Mathf.RoundToInt(normalised_throttle * 5.0f);
		if (!_appliances.State)
			return;
		switch (_throttle)
		{
			case 0:
				_primary_controller.roll_over_move(to_1: true);
				set_secondary_camshaft_target_notch(8);
				break;

			case 1:
				run_down();
				break;

			case 2:
				if (_single_notch_movement == null || _single_notch_movement.IsCompleted)
					_single_notch_movement = notch_down();
				break;

			case 3:
				_ = unlock_camshafts(continuous_run: false);
				break;

			case 4:
				if (_single_notch_movement == null || _single_notch_movement.IsCompleted)
					_single_notch_movement = notch_up();
				break;

			case 5:
				run_up();
				break;
		}
		//set_port_signal(_control_AB1, (int) AB1_signals.unit_b_camshaft_notch,
		//	(int) AB1_shift.unit_b_camshaft_lsb, _throttle + 1);
	}

	private void MU_BA1_control(float BA1)
	{
		Main.diagnostics2?.Value = _secondary_camshaft_notch = get_secondary_camshaft_current_notch(BA1);
	}


	private void traction_toggle(bool enable)
	{
		_traction_on = enable;
	}

	public void simulate()
	{
		_pantograph.move();

		int reverser = 0 /*(_reverser_handle.Value >= 0.5f) ? 1 : ((_reverser_handle.Value <= -0.5f) ? -1 : 0)*/;
		int throttle = 0 /*Mathf.RoundToInt(_throttle_handle.Value * 5.0f)*/;

		//_throttle.throttle_handler(reverser, throttle);

		const int nb = 1, mb = 6 / nb;
		const float max_flux = 300.0f, min_flux = 10.0f, flux_top = max_flux - min_flux, torque_factor = 0.186f, EMF_factor = 0.0195f;
		_named_branches["EPS"].EMF = 50.0f;
		foreach (KeyValuePair<string, circuit.branch_user> branch in _named_branches)
			_currents[branch.Key] = _currents[branch.Key] * 0.95f + branch.Value.current * 0.05f;
		_named_branches["MA1"].EMF = mb * (-EMF_factor) * (min_flux + Mathf.Max(-flux_top, Mathf.Min(flux_top, _currents["MF1"] / nb))) * _wheel_RPM.Value;

		_contactor_locations["RF1.1"].toggle_contactor("RF1.1", reverser > 0);
		_contactor_locations["RF1.2"].toggle_contactor("RF1.2", reverser > 0);
		_contactor_locations["RR1.1"].toggle_contactor("RR1.1", reverser < 0);
		_contactor_locations["RR1.2"].toggle_contactor("RR1.2", reverser < 0);

		_contactor_locations["CP1"].toggle_contactor("CP1", /*_traction_on &&*/ (throttle == 0 || throttle == 1 || throttle == 2 || throttle == 5));
		_contactor_locations["CP2"].toggle_contactor("CP2", /*_traction_on &&*/ (throttle == 1 || throttle == 2 || throttle == 4 || throttle == 5));
		_contactor_locations["CP3"].toggle_contactor("CP3", /*_traction_on &&*/ (throttle == 2 || throttle == 3 || throttle == 4 || throttle == 5));
		_contactor_locations["CP4"].toggle_contactor("CP4", /*_traction_on &&*/ (throttle == 3 || throttle == 4 || throttle == 5));

		_circuit.simulate();
		Main.diagnostics?.Value = _primary_controller.current_notch;
		//Main.diagnostics2?.Value = ;

		float torque = 3.0f * torque_factor * (_currents["MA1"] / nb) * (min_flux + Mathf.Max(-flux_top, Mathf.Min(flux_top, _currents["MF1"] / nb)));
		_torque_a.Value = _torque_b.Value = torque;
	}

	public void Dispose()
	{
		//_disposed = true;
		_simulation.SimulationFlow.TickEvent            -= simulate;
		_appliances.StateUpdated                        -= appliances_toggle;
		_throttle_handle.ValueUpdatedInternally         -= throttle_handler;
		_control_BA1.ValueUpdatedInternally             -= MU_BA1_control;
		_front_pantograph_switch.ValueUpdatedInternally -= toggle_pole;
	}
}
