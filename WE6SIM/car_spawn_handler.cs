// Distributed under terms and conditions of CC0 licence. See LICENCE_CC0.txt for details.

using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

using LocoSim.Implementations;
using DV.ThingTypes;
using DV.Utils;
using WE6SIM.catenary;
using WE6SIM.unit_A;

#if DEBUG
using WE6SIM.catenary_editor;
#endif

namespace WE6SIM;

[HarmonyPatch(typeof(CarSpawner), "Awake")]
internal static class car_spawn_handler
{
	/*
	static GameObject? _cube;
	static TrainCar? _test;
	static Fuse? _fuse;
	static Port? _port_x, _port_y, _port_z, _port1, _port2, _port3;
	static readonly List<GameObject> _spheres = [];
	static Vector3 _last_position;
	*/

	private static readonly Dictionary<TrainCar, unit_a_sim> _all_a_units = [];
	private static readonly Dictionary<TrainCar, unit_b_sim> _all_b_units = [];

#if DEBUG
    private static TrainCar?     _mow_vehicle = null;
	private static mow_follower? _mow_tracker = null;
#endif

	public static void Postfix(CarSpawner __instance)
	{
		Main.log("car_spawn_handler.Postfix");
        catenary_visual.set_up();
        __instance.CarSpawned -= on_car_spawned;
		__instance.CarSpawned += on_car_spawned;
		__instance.CarAboutToBeDeleted -= on_car_purged;
		__instance.CarAboutToBeDeleted += on_car_purged;
	}

#if DEBUG   
	private static void print_hierarchy(GameObject entity, int indent = 0)
	{
		if (indent <= 0)
			Main.log($"{entity.name} ({entity.transform.childCount}) '{entity.tag}'");
		else
			Main.log(string.Format($"{{0,{indent}}} {{1}} ({{2}}) '{{3}}'", " ", entity.name, entity.transform.childCount, entity.tag));
		foreach (Transform child in entity.transform)
			print_hierarchy(child.gameObject, indent + 4);
	}
#endif

	private static void on_car_spawned(TrainCar vehicle)
	{
		if (vehicle == null || !vehicle.IsLoco)
			return;
		Main.log("Spawn " + vehicle.ID + " " + vehicle.carLivery.id);

#if DEBUG
        if ((vehicle.carType == TrainCarType.LocoDM1U || vehicle.carType == TrainCarType.LocoMicroshunter) && _mow_tracker == null)
		{
			Main.log($"MOW vehicle {vehicle.ID}");
			_mow_vehicle = vehicle;
			_mow_tracker = new mow_follower(vehicle);
			return;
		}
#endif

		bool is_unit_a   = false;
        int  random_seed = 0;
        if (string.Equals(vehicle.carLivery.id.Substring(0, "WE6981A".Length), "WE6981A", StringComparison.Ordinal))
		{
			is_unit_a = true;
			for (int letter_index = 0; letter_index < vehicle.ID.Length; ++letter_index)
				random_seed += vehicle.ID[letter_index] << (letter_index & 0x7);
		}
		else if (!string.Equals(vehicle.carLivery.id.Substring(0, "WE6981B".Length), "WE6981B", StringComparison.Ordinal))
			return;

		Dictionary<string, Fuse> all_fuses = [];
		foreach (Fuse? fuse in vehicle.SimController.SimulationFlow.AllFuses)
		{
			if (fuse != null)
			{
				Main.log(fuse.id);
				//if (string.Equals(fuse.id, "fusebox.ELECTRICS_MAIN", StringComparison.Ordinal))
				//	new_unit_a.appliances = fuse;
				all_fuses[fuse.id] = fuse;
			}
		}

		Dictionary<string, Port> all_ports = [];
		foreach (Port? port in vehicle.SimController.SimulationFlow.AllPorts)
		{
			if (port == null)
				continue;
			Main.log($"{port.id} {port.type} {port.valueType}");
			all_ports[port.id] = port;
			if (is_unit_a)
			{
				switch (port.id)
				{
					case "diagnostics.DISPLAY":
						if (Main.diagnostics == null)
						{
							Main.log($"Diagnostics display connected for {vehicle.ID}");
							Main.diagnostics = port;
						}
						break;

					case "diagnostics.DISPLAY2":
						if (Main.diagnostics2 == null)
						{
							Main.log($"Diagnostics display 2 connected for {vehicle.ID}");
							Main.diagnostics2 = port;
						}
						break;
				}
			}
		}

		//if (vehicle.gameObject != null)
		//	print_hierarchy(vehicle.gameObject);
		if (is_unit_a)
			_all_a_units[vehicle] = new unit_a_sim(all_fuses, all_ports, vehicle, random_seed);
		else
			_all_b_units[vehicle] = new unit_b_sim(all_fuses, all_ports, vehicle);
	}

	/*
	if (string.Equals(vehicle.carLivery.id, "WE6981A", StringComparison.Ordinal) || string.Equals(vehicle.carLivery.id, "WE6981B", StringComparison.Ordinal))
	{

		if (_cube is null)
		{
			_test = vehicle;
			vehicle.SimController.SimulationFlow.TickEvent += on_every_tick;
			_cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
			_cube.transform.position = 4.5f * Vector3.up;
			_cube.transform.SetParent(vehicle.gameObject.transform, false);
			_cube.transform.rotation = Quaternion.identity;

			foreach (Fuse? fuse in vehicle.SimController.SimulationFlow.AllFuses)
			{
				if (fuse != null)
				{
					Main.logger.Log(fuse.id);
					if (string.Equals(fuse.id, "fusebox.ELECTRICS_MAIN", StringComparison.Ordinal))
					{
						_fuse = fuse;
						fuse.StateUpdated += on_switch_toggle;
					}
				}
			}

			foreach (Port? port in vehicle.SimController.SimulationFlow.AllPorts)
			{
				if (port != null)
				{
					Main.logger.Log($"{port.id} {port.type} {port.valueType}");
					switch (port.id)
					{
						case "WPOS.X":
							_port_x = port;
							break;

						case "WPOS.Y":
							_port_y = port;
							break;

						case "WPOS.Z":
							_port_z = port;
							break;

						case "throttle.EXT_IN":
							Main.logger.Log($"Throttle connected");
							_port1 = port;
							break;

						case "reverser.REVERSER":
							Main.logger.Log($"Reverser connected");
							_port3 = port;
							break;

						//case "traction.TORQUE_IN":
						case "internal_MU.TM4-6":
							Main.logger.Log($"Torque output connected");
							_port2 = port;
							break;
					}
				}
			}

			WorldMover floating_origin = SingletonBehaviour<WorldMover>.Instance;
			if (floating_origin != null)
				floating_origin.WorldMoved += on_origin_shift;
		}
	}
	*/

	private static void on_car_purged(TrainCar vehicle)
	{
#if DEBUG
		if (_mow_vehicle == vehicle)
		{
            Main.log("Remove MOW " + vehicle.ID);
			_mow_tracker?.Dispose();
			_mow_vehicle = null;
			_mow_tracker = null;
        }
#endif

		if (_all_a_units.TryGetValue(vehicle, out unit_a_sim disposed_unit_a))
		{
			Main.log("Remove A " + vehicle.ID + " " + vehicle.carLivery.id);
			disposed_unit_a.Dispose();
			_all_a_units.Remove(vehicle);
			Main.diagnostics = Main.diagnostics2 = null;
		}
		else if (_all_b_units.TryGetValue(vehicle, out unit_b_sim disposed_unit_b))
		{
			Main.log("Remove B " + vehicle.ID + " " + vehicle.carLivery.id);
			disposed_unit_b.Dispose();
			_all_b_units.Remove(vehicle);
		}
	}
	/*
	if (vehicle == _test)
	{
		Main.logger.Log("Remove (2)");
		vehicle.SimController.SimulationFlow.TickEvent -= on_every_tick;
		WorldMover floating_origin = SingletonBehaviour<WorldMover>.Instance;
		if (floating_origin != null)
			floating_origin.WorldMoved -= on_origin_shift;
		if (_fuse != null)
			_fuse.StateUpdated -= on_switch_toggle;
		if (_cube != null)
			GameObject.Destroy(_cube);
		_cube = null;
		_test = null;
		foreach (GameObject sphere in _spheres)
			GameObject.Destroy(sphere);
		_spheres.Clear();
	}
	*/

	/*
	private static void on_switch_toggle(bool switch_state)
	{
		Main.logger.Log($"on_switch_toggle({switch_state}) <{_cube == null}> <{_port_x == null}> <{_port_y == null}> <{_port_z == null}>");
		_cube?.transform.rotation = switch_state ? Quaternion.AngleAxis(45.0f, Vector3.up) : Quaternion.identity;
		/*
		_port_x?.Value = switch_state ? 1.0f : 0.0f;
		_port_y?.Value = switch_state ? 2.0f : 0.0f;
		_port_z?.Value = switch_state ? 3.0f : 0.0f;
	}
	*/

	/*
	private static void on_every_tick()
	{
		if (_test != null && _port_x != null && _port_y != null && _port_z != null && _port1 != null && _port2 != null && _port3 != null)
		{
			Vector3 front_pos = _test.FrontCouplerAnchor.position;
			_port_x.Value = front_pos.x;
			_port_y.Value = front_pos.y;
			_port_z.Value = front_pos.z;

			_port2.Value = _port1.Value * _port3.Value * (100.0E+3f * 0.56f);

			/*
			_port_x.Value = PlayerManager.PlayerTransform.AbsolutePosition().x;
			_port_y.Value = PlayerManager.PlayerTransform.AbsolutePosition().y;
			_port_z.Value = PlayerManager.PlayerTransform.AbsolutePosition().z;
			*/

	//_port1.Value = OriginShift.currentMove.x;
	//_port2.Value = OriginShift.currentMove.z;

	/*
	if ((front_pos - _last_position).sqrMagnitude > 40.0f * 40.0f)
	{
		_last_position = front_pos;
		bool add_new = true;
		foreach (GameObject sphere in _spheres)
		{
			if ((front_pos - sphere.transform.position).sqrMagnitude <= 40.0f * 40.0f)
			{
				add_new = false;
				break;
			}
		}
		if (add_new)
		{
			GameObject new_sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
			new_sphere.transform.position = front_pos + 5.0f * Vector3.up;
			_spheres.Add(new_sphere);
		}
	}
	*/

	/*
	private static void on_origin_shift(WorldMover floating_origin, Vector3 shift)
	{
		Main.logger.Log("on_origin_shift " + shift.ToString());
		//for (int index = _spheres.Count - 1; index >= 0; --index)
		//	_spheres[index].transform.position -= shift;
	}
	*/
}
