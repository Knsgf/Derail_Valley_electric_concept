// Distributed under terms and conditions of CC0 licence. See LICENCE_CC0.txt for details.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using HarmonyLib;
using UnityEngine;

using DV.ServicePenalty;
using DV.ThingTypes;
using DV.Utils;
using LocoSim.Implementations;

using electric_sim.catenary_editor;
using electric_sim.utilities;

namespace electric_sim.unit_A;

[HarmonyPatch(typeof(SimulatedCarDebtTracker))]
internal class electricity_meter: IDisposable
{
    const float minimum_current = 10.0f, energy_unit_price = 7.5f * 2.0f;

    private static readonly Dictionary<SimulatedCarDebtTracker, electricity_meter> _fee_trackers = [];
    
    private readonly object                  _initialization_interlock = new();
    private readonly CancellationTokenSource _initialisation_timeout   = new(60 * 1000);
    private readonly Task                    _deferred_initialisation;
    private readonly Port                    _game_save_energy;
    private readonly float                   _usage_factor;

    private SimulatedCarDebtTracker? _fee_tracker;

    private double _energy_used = 0.0;

    public electricity_meter(TrainCar unit, Dictionary<string, Port> ports)
    {
        _game_save_energy        = sensor_grabber.grab_port(ports, "[LeftoverMeter].EXT_IN");
        _energy_used             = _game_save_energy.Value;
        _usage_factor            = (editor_settings.kWh_price / energy_unit_price) / (1000.0f * 3600.0f);
        _deferred_initialisation = setup_fee_tracker(unit, _initialisation_timeout.Token);
    }

    private async Task setup_fee_tracker(TrainCar unit, CancellationToken interrupt)
    {
        while (!interrupt.IsCancellationRequested)
        {
            LocoDebtController? all_fees = SingletonBehaviour<LocoDebtController>.Instance;
            lock (_initialization_interlock)
            {
                if (interrupt.IsCancellationRequested)
                    return;
                if (all_fees?.trackedLocosDebts != null)
                {
                    foreach (ExistingLocoDebt? tracked_fee in all_fees.trackedLocosDebts)
                    {
                        if (tracked_fee != null && tracked_fee.car == unit 
                            && tracked_fee.locoDebtTracker is SimulatedCarDebtTracker fee_tracker)
                        {
                            Main.log($"Set up a fee tracker {tracked_fee.ID} for car {unit.ID}");
                            _fee_tracker               = fee_tracker;
                            _fee_trackers[fee_tracker] = this;
                            fee_tracker.UpdateDebtValues();
                            return;
                        }
                    }
                }
            }
            await Task.Delay(100, interrupt);
        }
    }

    public void count_energy(float voltage, float current)
    {
        if (Mathf.Abs(current) < minimum_current)
            return;
        _energy_used           += (voltage * current) * _usage_factor * Time.deltaTime;
        _game_save_energy.Value = (_fee_tracker == null) ? 0.0f : ((float) _energy_used);
    }

    public void Dispose()
    {
        if (!_deferred_initialisation.IsCompleted && !_deferred_initialisation.IsCanceled)
            _initialisation_timeout.Cancel();
        lock (_initialization_interlock)
        {
            if (_fee_tracker != null && _fee_trackers.ContainsKey(_fee_tracker))
            {
                _fee_trackers.Remove(_fee_tracker);
                _fee_tracker = null;
            }
        }
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
}
