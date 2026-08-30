// Distributed under terms and conditions of CC0 licence. See LICENCE_CC0.txt for details.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using DV.ServicePenalty;
using DV.Simulation.Cars;
using DV.ThingTypes;
using DV.Utils;

using electric_sim.catenary_editor;
using electric_sim.utilities;

using HarmonyLib;

using LocoSim.Implementations;

using UnityEngine;

using static UnityEngine.UI.CanvasScaler;

namespace electric_sim.unit_A;

[HarmonyPatch(typeof(SimulatedCarDebtTracker))]
internal class electricity_meter: IDisposable
{
    [HarmonyPatch(typeof(SimController), "OnLogicCarInitialized")]
    private static class LogicCarInitializer
    {
        public static void Postfix(SimController __instance, TrainCar? ___train, SimulatedCarDebtTracker? ___debt)
        {
            if (___train == null || ___train.playerSpawnedCar)
                return;
            (bool is_WE, bool is_unit_A) = car_spawn_handler.is_unit_WE(___train);
            if (!is_WE || !is_unit_A)
                return;
            Main.log($"EMTR {___train.uniqueCar} {___debt?.ToString() ?? "<null>"}");
            if (___train.uniqueCar)
            {
                _new_trackers[___train] = new private_electricity_tracker(___train);
                SingletonBehaviour<LocoDebtController>.Instance.RegisterLocoDebtTracker(___train, _new_trackers[___train]);
                try_set_up_fee_tracker(___train);
            }
            else if (___debt != null)
            {
                _new_trackers[___train] = ___debt;
                try_set_up_fee_tracker(___train);
            }
        }
    }

    private class private_electricity_tracker: LocoDebtTrackerBase
    {
        private TrainCar _unit_A;
        
        public private_electricity_tracker(TrainCar unit_A)
        {
            _unit_A = unit_A;
            debtData = new(unit_A.ID, unit_A.carType, InitializeDebtComponents());
        }
        
        public override DebtComponent[] InitializeDebtComponents()
        {
            Main.log($"OWN IDC {_unit_A.ID}");
            return [new(1.0f, ResourceType.ElectricCharge)];
        }

        public override bool IsDebtOnlyEnvironmental() => false;

        public override void ResetState()
        {
            Main.log($"OWN RS {_unit_A.ID}");
        }

        public override void TurnOffDebtSources()
        {
            Main.log($"OWN TODR {_unit_A.ID}");
        }

        public override void UpdateDebtValues()
        {
            Main.log($"OWN UDV {_unit_A.ID}");
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

    public electricity_meter(TrainCar unit, unit_A_sim unit_A, Dictionary<string, Port> ports)
    {
        _unit             = unit;
        _unit_A           = unit_A;
        _game_save_energy = sensor_grabber.grab_port(ports, "[LeftoverMeter].EXT_IN");
        _usage_factor     = (editor_settings.kWh_price / energy_unit_price) / (1000.0f * 3600.0f);
        _new_meters[unit] = this;
        try_set_up_fee_tracker(unit);
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

    public void Dispose()
    {
        if (_fee_tracker != null && _fee_trackers.ContainsKey(_fee_tracker))
        {
            if (_fee_tracker is private_electricity_tracker)
            {
                Main.log($"Staging energy fees for {_unit.ID}");
                SingletonBehaviour<LocoDebtController>.Instance.StageLocoDebtOnLocoDestroy(_fee_tracker);
            }
            _fee_trackers.Remove(_fee_tracker);
            _fee_tracker = null;
        }
        
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
