// Distributed under terms and conditions of CC0 licence. See LICENCE_CC0.txt for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using UnityEngine;
using WE6SIM.circuit_sim;
using static UnityEngine.UI.CanvasScaler;

using DV.Simulation.Cars;
using LocoSim.Implementations;
using WE6SIM.utilities;

using static WE6SIM.utilities.signal_cable;
using static WE6SIM.utilities.sensor_grabber;

namespace WE6SIM;

internal class unit_b_sim: electric_device
{
	private readonly pantograph _pantograph;

	private readonly TrainCar _unit;
	private readonly SimController _simulation;
	private readonly camshaft_motor _secondary_controller;

	private readonly Fuse _appliances;
	private readonly Port _control_AB1, _control_BA1;

	private int _secondary_camshaft_target_notch = 1;

	public unit_b_sim(Dictionary<string, Fuse> fuses, Dictionary<string, Port> ports, TrainCar unit)
		: base("unit_B_sim")
	{
		SimController? simulation = unit.SimController ?? throw new ArgumentNullException("No simulation component");

		_appliances = get_fuse(fuses, "fusebox.ELECTRICS_MAIN");

		//_throttle_handle = get_port(ports, "throttle.EXT_IN");
		//_reverser_handle = get_port(ports, "reverser.REVERSER");

		//_front_pantograph_switch = get_port(ports, "[FrontPantographSwitch].EXT_IN");
		//_front_pantograph_switch.ValueUpdatedInternally += toggle_pole;

		//_torque_b = get_port(ports, "internal_MU.TM4-6");
		_control_AB1 = get_port(ports, "internal_MU.CONTROL_AB1");
		_control_AB1.ValueUpdatedInternally += MU_AB1_control;
		_control_BA1 = get_port(ports, "internal_MU.CONTROL_BA1");

		_secondary_controller = new camshaft_motor(unit_a_sim.camshaft_notches, _appliances, drop_to_1_on_power_loss: false);

		_unit = unit;
		_simulation = simulation;
		simulation.SimulationFlow.TickEvent += simulate;
		_pantograph = new pantograph(unit.gameObject, _appliances);

		//_throttle = new throttle_controllers();
		//_throttle.traction_toggle += traction_toggle;
	}

	private void MU_AB1_control(float AB1)
	{
		if (disposed)
			return;
		if (port_value_signal_active(AB1, (int) AB1_signals.back_pantograph))
			_pantograph.set_target_height(6.0f + Main.pole_height_offset);
		else
			_pantograph.set_target_height(0.0f);
		_appliances.ChangeState(port_value_signal_active(AB1, (int) AB1_signals.battery));

		_secondary_camshaft_target_notch = extract_signal_from_port_value(AB1, (int) AB1_signals.unit_b_camshaft_notch, (int) AB1_shift.unit_b_camshaft_lsb);
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
		_pantograph.move();
		set_port_signal(_control_BA1, (int) BA1_signals.unit_b_camshaft_notch, (int) BA1_shift.unit_b_camshaft_lsb,
			_secondary_controller.current_notch);
	}

	public override void Dispose()
	{
		if (!disposed)
		{ 
			base.Dispose();
			_secondary_controller.Dispose();
			_pantograph.Dispose();
			_simulation.SimulationFlow.TickEvent -= simulate;
			_control_AB1.ValueUpdatedInternally  -= MU_AB1_control;
		}
	}
}
