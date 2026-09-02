// Distributed under terms and conditions of CC0 licence. See LICENCE_CC0.txt for details.

using System;
using System.Collections.Generic;

using HarmonyLib;
using UnityEngine;

using DV.ServicePenalty;
using DV.Simulation.Cars;
using DV.ThingTypes;
using DV.Utils;
using LocoSim.Implementations;

using electric_sim.catenary_editor;
using electric_sim.utilities;

namespace electric_sim.unit_A;

[HarmonyPatch(typeof(SimulatedCarDebtTracker))]
internal class electricity_meter
{
    [HarmonyPatch(typeof(SimController), "OnLogicCarInitialized")]
    private static class logic_car_nitialiser
    {
        public static void Postfix(TrainCar? ___train, SimulatedCarDebtTracker? ___debt)
        {
            add_new_tracker(___train, ___debt);
        }
    }

    [HarmonyPatch(typeof(OwnedCarsStateController), "RegisterCarStateTracker")]
    private static class private_vehicle_registar
    {
        public static void Postfix(TrainCar? car)
        {
            if (car != null)
                reassign_tracker(car, null, is_private: true);
        }
    }

    [HarmonyPatch(typeof(LocoDebtController), "RegisterLocoDebtTracker")]
    private static class company_vehicle_registar
    {
        public static void Postfix(TrainCar? car, LocoDebtTrackerBase? locoDebtTracker)
        {
            if (car != null)
                reassign_tracker(car, locoDebtTracker as SimulatedCarDebtTracker, is_private: false);
        }
    }

    private class private_electricity_tracker: LocoDebtTrackerBase
    {
        const float start_value = 262143.0f;
        
        public private_electricity_tracker(TrainCar unit_A)
        {
            debtData = new(unit_A.ID, unit_A.carType, InitializeDebtComponents());
        }
        
        public override DebtComponent[] InitializeDebtComponents()
        {
            return [new(start_value, ResourceType.ElectricCharge)];
        }

        public override bool IsDebtOnlyEnvironmental() => false;

        public override void ResetState()
        {
            if (_fee_trackers.TryGetValue(this, out electricity_meter meter))
            {
                meter._energy_used            = 0.0;
                meter._game_save_energy.Value = 0.0f;
            }
        }

        public override void TurnOffDebtSources()
        {
            if (_fee_trackers.TryGetValue(this, out electricity_meter meter))
                meter._unit_A.shut_down();
        }

        public override void UpdateDebtValues()
        {
            if (_fee_trackers.TryGetValue(this, out electricity_meter meter))
            {
                foreach (DebtComponent current_fee in GetTrackedDebts())
                {
                    if (current_fee.Type == ResourceType.ElectricCharge)
                    { 
                        current_fee.UpdateEndValue(Mathf.Min(start_value - (float) meter._energy_used, start_value));
                        break;
                    }
                }
            }
        }
    }

    const float minimum_current = 10.0f, energy_unit_price = 7.5f * 2.0f;

    private static readonly Dictionary<           TrainCar,   electricity_meter> _new_meters   = [];
    private static readonly Dictionary<           TrainCar, LocoDebtTrackerBase> _new_trackers = [];
    private static readonly Dictionary<LocoDebtTrackerBase,   electricity_meter> _fee_trackers = [];
    
    private readonly TrainCar   _unit;
    private readonly unit_A_sim _unit_A;
    private readonly Port       _game_save_energy;
    private readonly float      _usage_factor;

    private LocoDebtTrackerBase? _fee_tracker;

    private double _energy_used = 0.0;
    private bool   _energy_read = false;

    private static void add_new_tracker(TrainCar? vehicle, SimulatedCarDebtTracker? company_tracker)
    {
        if (vehicle == null || vehicle.playerSpawnedCar)
            return;
        (bool is_WE, bool is_unit_A) = car_spawn_handler.is_unit_WE(vehicle);
        if (!is_WE || !is_unit_A)
            return;
        if (vehicle.uniqueCar)
        {
            _new_trackers[vehicle] = new private_electricity_tracker(vehicle);
            SingletonBehaviour<LocoDebtController>.Instance.RegisterLocoDebtTracker(vehicle, _new_trackers[vehicle]);
            try_set_up_fee_tracker(vehicle);
        }
        else if (company_tracker != null)
        {
            _new_trackers[vehicle] = company_tracker;
            try_set_up_fee_tracker(vehicle);
        }
    }

    private static void reassign_tracker(TrainCar vehicle, SimulatedCarDebtTracker? tracker, bool is_private)
    {
        TrainCar?          unit            = null;
        electricity_meter? meter           = null;
        bool               replace_tracker = false;
        foreach (KeyValuePair<LocoDebtTrackerBase, electricity_meter> current_tracker in _fee_trackers)
        {
            meter = current_tracker.Value;
            unit  = meter._unit;
            if (unit == vehicle 
                && unit.uniqueCar == is_private 
                && (current_tracker.Key is private_electricity_tracker) != is_private)
            {
                replace_tracker = true;
                break;
            }
        }
        if (replace_tracker)
        {
            assert.test(meter != null && unit != null);
            meter.deregister_tracker(ownership_change: true, session_end: false);
            _new_meters[unit] = meter;
            add_new_tracker(unit, tracker);
        }
    }
    
    private static void try_set_up_fee_tracker(TrainCar unit)
    {
        if (!_new_meters.ContainsKey(unit) || !_new_trackers.ContainsKey(unit))
            return;
        electricity_meter setting_meter           = _new_meters  [unit];
        setting_meter._fee_tracker                = _new_trackers[unit];
        _fee_trackers[setting_meter._fee_tracker] = setting_meter;
        Main.log($"Set up a fee tracker <{setting_meter._fee_tracker.GetType()}> for car {unit.ID}");
        _new_meters.Remove  (unit);
        _new_trackers.Remove(unit);
    }

    public electricity_meter(TrainCar unit, unit_A_sim unit_A, Dictionary<string, Port> ports)
    {
        _unit             = unit;
        _unit_A           = unit_A;
        _game_save_energy = sensor_grabber.grab_port(ports, "[LeftoverMeter].EXT_IN");
        _usage_factor     = (editor_settings.kWh_price / energy_unit_price) / (1000.0f * 3600.0f);
        _new_meters[unit] = this;
        try_set_up_fee_tracker(unit);
    }

    public void count_energy(float voltage, float current)
    {
        if (_fee_tracker == null)
            return;
        if (!_energy_read)
        {
            _energy_read = true;
            _energy_used = _game_save_energy.Value;
            _fee_tracker.UpdateDebtValues();
        }
        if (Mathf.Abs(current) >= minimum_current)
        {
            _energy_used           += voltage * current * _usage_factor * Time.deltaTime;
            _game_save_energy.Value = (float) _energy_used;
        }
    }

    public void deregister_tracker(bool ownership_change, bool session_end)
    {
        if (_fee_tracker != null && _fee_trackers.ContainsKey(_fee_tracker))
        {
            if (!session_end && _fee_tracker is private_electricity_tracker)
            {
                Main.log($"Staging electricity fees for {_unit.ID}");
                SingletonBehaviour<LocoDebtController>.Instance.StageLocoDebtOnLocoDestroy(_fee_tracker);
                if (ownership_change)
                {
                    _energy_used            = 0.0;
                    _game_save_energy.Value = 0.0f;
                }
            }
            _fee_trackers.Remove(_fee_tracker);
        }
        _fee_tracker = null;

        // Remove entries for player-spawned vehicles without a tracker
        if (_new_meters.ContainsKey(_unit))
            _new_meters.Remove(_unit);
        if (_new_trackers.ContainsKey(_unit))
            _new_trackers.Remove(_unit);
    }
    
    [HarmonyPatch("UpdateDebtValues"), HarmonyPostfix]
    public static void UpdateDebtValuesPostfix(SimulatedCarDebtTracker? __instance)
    {
        if (__instance == null)
            return;
        foreach (DebtComponent current_fee in __instance.GetTrackedDebts())
        {
            if (current_fee.Type == ResourceType.ElectricCharge && _fee_trackers.TryGetValue(__instance, out electricity_meter meter))
            { 
                current_fee.UpdateEndValue(current_fee.EndValue - (float) meter._energy_used);
                break;
            }
        }
    }

    [HarmonyPatch("ResetState"), HarmonyPostfix]
    public static void ResetStatePostfix(SimulatedCarDebtTracker? __instance)
    {
        if (__instance != null && _fee_trackers.TryGetValue(__instance, out electricity_meter meter))
        {
            meter._energy_used            = 0.0;
            meter._game_save_energy.Value = 0.0f;
        }
    }

    [HarmonyPatch("TurnOffDebtSources"), HarmonyPostfix]
    public static void TurnOffDebtSourcesPostfix(SimulatedCarDebtTracker? __instance)
    {
        if (__instance != null && _fee_trackers.TryGetValue(__instance, out electricity_meter meter))
            meter._unit_A.shut_down();
    }
}
