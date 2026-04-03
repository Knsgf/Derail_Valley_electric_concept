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

namespace WE6SIM;

internal partial class unit_a_sim: IDisposable
{
	private readonly GameObject _test_pole_prefab;
	private GameObject? _test_pole;

	private readonly Dictionary<string, circuit.branch_user> _named_branches, _contactor_locations;
	private readonly Dictionary<string, float> _currents = [];

	private readonly Fuse                 _appliances;
	private readonly Port                 _throttle_handle, _reverser_handle, _torque_a, _torque_b, _wheel_RPM;
	private readonly Port                 _front_pantograph_switch;
	private readonly Port?                _switch;
	private readonly SimController        _simulation;
	private readonly throttle_controllers _throttle;
	private readonly circuit              _circuit;
	private readonly pantograph           _pantograph;

	private bool _traction_on = false;

	private readonly TrainCar _unit;

	private Fuse get_fuse(Dictionary<string, Fuse> fuses, string name)
	{
		if (!fuses.TryGetValue(name, out Fuse fuse))
			throw new ArgumentException("No fuse " + name);
		return fuse;
	}

	private Port get_port(Dictionary<string, Port> ports, string name)
	{
		if (!ports.TryGetValue(name, out Port port))
			throw new ArgumentException("No port " + name);
		return port;
	}

	public unit_a_sim(Dictionary<string, Fuse> fuses, Dictionary<string, Port> ports, TrainCar unit)
	{
		SimController? simulation = unit.SimController ?? throw new ArgumentNullException("No simulation component");

		_appliances = get_fuse(fuses, "fusebox.ELECTRICS_MAIN");

		_throttle_handle = get_port(ports, "throttle.EXT_IN");
		_reverser_handle = get_port(ports, "reverser.REVERSER");

		_front_pantograph_switch = get_port(ports, "[FrontPantographSwitch].EXT_IN");
		_front_pantograph_switch.ValueUpdatedInternally += toggle_pole;

		_torque_a = get_port(ports, "traction.TORQUE_IN");
		_torque_b = get_port(ports, "internal_MU.TM4-6");
		_wheel_RPM = get_port(ports, "traction.WHEEL_RPM_EXT_IN");
		//_switch = new_unit_a.traced_switch;

		_test_pole_prefab = Main.catenary_parts.pole;

		_circuit = circuit_compiler.trace(_element_resistances, circuit_diagram).set_up_simulation(out _named_branches, out _contactor_locations);
		foreach (string branch_name in _named_branches.Keys)
			_currents[branch_name] = 0.0f;

		_unit       = unit;
		_simulation = simulation;
		simulation.SimulationFlow.TickEvent += simulate;
		_pantograph = new pantograph(unit.gameObject);

		_throttle = new throttle_controllers();
		_throttle.traction_toggle += traction_toggle;
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
			}
		}
		else if (_test_pole is not null)
		{
			GameObject.Destroy(_test_pole);
			_test_pole = null;
			_pantograph.set_target_height(0.0f);
		}
	}

	private void traction_toggle(bool enable)
	{
		_traction_on = enable;
	}

	public void simulate()
	{
		_pantograph.move();

		int reverser = (_reverser_handle.Value >= 0.5f) ? 1 : ((_reverser_handle.Value <= -0.5f) ? -1 : 0);
		int throttle = Mathf.RoundToInt(_throttle_handle.Value * 5.0f);

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
		//Main.diagnostics?.Value = _currents["MA1"] / nb;
		//Main.diagnostics2?.Value = _named_branches["MA1"].EMF / mb;
		//Main.diagnostics2?.Value = _wheel_RPM.Value;

		float torque = 3.0f * torque_factor * (_currents["MA1"] / nb) * (min_flux + Mathf.Max(-flux_top, Mathf.Min(flux_top, _currents["MF1"] / nb)));
		_torque_a.Value = _torque_b.Value = torque;
	}

	public void Dispose()
	{
		_simulation.SimulationFlow.TickEvent -= simulate;
	}
}
