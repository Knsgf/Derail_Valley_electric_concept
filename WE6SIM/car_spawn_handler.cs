// Distributed under terms and conditions of CC0 licence. See LICENCE_CC0.txt for details.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

using HarmonyLib;
using UnityEngine;

using DV.ThingTypes;
using LocoSim.Implementations;

using electric_sim.catenary;
using electric_sim.unit_A;
using electric_sim.unit_B;

#if DEBUG
using electric_sim.catenary_editor;
#endif

namespace electric_sim;

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

    private static readonly Dictionary<TrainCar, unit_A_sim> _all_A_units = [];
    private static readonly Dictionary<TrainCar, unit_B_sim> _all_B_units = [];

#if DEBUG
    private static TrainCar?     _mow_vehicle = null;
    private static mow_follower? _mow_tracker = null;
#endif

    public static void Postfix(CarSpawner __instance)
    {
        //Main.log("car_spawn_handler.Postfix");
        if (Main.mod_info == null)
            throw new Exception("Run-time mod information unavailable");
        overhead_equipment.set_up(Main.mod_info);
        __instance.CarSpawned -= on_car_spawned;
        __instance.CarSpawned += on_car_spawned;
        UnloadWatcher.UnloadRequested -= session_end;
        UnloadWatcher.UnloadRequested += session_end;
    }

    [Conditional("DEBUG")]
    private static void print_hierarchy(GameObject entity, int indent = 0)
    {
        if (indent <= 0)
            Main.log($"{entity.name} ({entity.transform.childCount}) '{entity.tag}'");
        else
            Main.log(string.Format($"{{0,{indent}}} {{1}} ({{2}}) '{{3}}'", " ", entity.name, entity.transform.childCount, entity.tag));
        foreach (Transform child in entity.transform)
            print_hierarchy(child.gameObject, indent + 4);
    }

    static internal (bool is_custom, bool is_unit_A) is_unit_WE(TrainCar vehicle)
    {
        string car_type = vehicle.carLivery.id;
        if (car_type.Length >= "WE6981A".Length)
        {
            car_type = car_type.Substring(0, "WE6981A".Length);
            if (string.Equals(car_type, "WE6981A", StringComparison.Ordinal))
                return (true, true);
            if (string.Equals(car_type, "WE6981B", StringComparison.Ordinal))
                return (true, false);
        }
        return (false, false);
    }
    
    private static void on_car_spawned(TrainCar vehicle)
    {
        if (vehicle == null || !vehicle.IsLoco)
            return;
        //Main.log("Spawn " + vehicle.ID + " " + vehicle.carLivery.id);

#if DEBUG
        if ((vehicle.carType == TrainCarType.LocoDM1U || vehicle.carType == TrainCarType.LocoMicroshunter) && _mow_tracker == null)
        {
            Main.log($"MOW vehicle {vehicle.ID}");
            _mow_vehicle = vehicle;
            _mow_tracker = new mow_follower(overhead_equipment.system, vehicle);
            vehicle.OnDestroyCar += on_car_purge;
            return;
        }
#endif

        (bool is_WE, bool is_unit_A) = is_unit_WE(vehicle);
        if (!is_WE)
            return;
        int random_seed = 0;
        if (is_unit_A)
        {
            for (int letter_index = 0; letter_index < vehicle.ID.Length; ++letter_index)
                random_seed += vehicle.ID[letter_index] << (letter_index & 0x7);
        }

        print_hierarchy(vehicle.gameObject);
        Dictionary<string, Fuse> all_fuses = [];
        foreach (Fuse? fuse in vehicle.SimController.SimulationFlow.AllFuses)
        {
            if (fuse != null)
            {
                //Main.log(fuse.id);
                all_fuses[fuse.id] = fuse;
            }
        }

        Dictionary<string, Port> all_ports = [];
        foreach (Port? port in vehicle.SimController.SimulationFlow.AllPorts)
        {
            if (port == null)
                continue;
            //Main.log($"{port.id} {port.type} {port.valueType}");
            all_ports[port.id] = port;
#if DEBUG
            if (is_unit_A)
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
#endif
        }

        //if (vehicle.gameObject != null)
        //	print_hierarchy(vehicle.gameObject);
        if (is_unit_A)
            _all_A_units[vehicle] = new unit_A_sim(all_fuses, all_ports, vehicle, random_seed);
        else
            _all_B_units[vehicle] = new unit_B_sim(all_fuses, all_ports, vehicle);
        vehicle.OnDestroyCar += on_car_purge;
    }

    private static void purge_vehicle(TrainCar vehicle, bool session_end)
    {
        vehicle.OnDestroyCar -= on_car_purge;

#if DEBUG
        if (_mow_vehicle == vehicle)
        {
            Main.log("Remove MOW " + vehicle.ID);
            _mow_tracker?.Dispose();
            _mow_vehicle = null;
            _mow_tracker = null;
        }
#endif

        if (_all_A_units.TryGetValue(vehicle, out unit_A_sim disposed_unit_a))
        {
            Main.log("Remove A " + vehicle.ID + " " + vehicle.carLivery.id);
            disposed_unit_a.purge(session_end);
            _all_A_units.Remove(vehicle);
            Main.diagnostics = Main.diagnostics2 = null;
        }
        else if (_all_B_units.TryGetValue(vehicle, out unit_B_sim disposed_unit_b))
        {
            Main.log("Remove B " + vehicle.ID + " " + vehicle.carLivery.id);
            disposed_unit_b.Dispose();
            _all_B_units.Remove(vehicle);
        }
    }

    private static void on_car_purge(TrainCar vehicle)
    {
        purge_vehicle(vehicle, session_end: false);
    }

    private static void session_end()
    {
#if DEBUG
        if (_mow_vehicle != null)
            purge_vehicle(_mow_vehicle, session_end: true);
#endif
        foreach (TrainCar unit_A in _all_A_units.Keys.ToArray())
            purge_vehicle(unit_A, session_end: true);
        foreach (TrainCar unit_B in _all_B_units.Keys.ToArray())
            purge_vehicle(unit_B, session_end: true);
    }
}
