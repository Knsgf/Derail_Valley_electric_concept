// Distributed under terms and conditions of CC0 licence. See LICENCE_CC0.txt for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using LocoSim.Implementations;

using WE6SIM.devices;
using WE6SIM.utilities;

using static UnityEngine.UI.CanvasScaler;
using static WE6SIM.devices.control_stand;
using static WE6SIM.utilities.signal_cable;

namespace WE6SIM.unit_A;

internal partial class unit_A_sim
{
    private class main_circuit_breaker: electric_device
    {
        private readonly unit_A_sim _unit;
        
        private readonly Port _arm_sound, _engage_sound, _trip_sound;

        private bool _engaging = false, _switched_on = false;

        public main_circuit_breaker(Fuse electric_supply, Fuse air_supply, Dictionary<string, Port> ports,
            unit_A_sim unit)
            : base("Main circuit braker", electric_supply, air_supply)
        { 
            _unit = unit;

            _arm_sound    = sensor_grabber.grab_port(ports, "[MainBreaker].ARMING" );
            _engage_sound = sensor_grabber.grab_port(ports, "[MainBreaker].ENGAGED");
            _trip_sound   = sensor_grabber.grab_port(ports, "[MainBreaker].TRIPPED");
            
            power_supply_toggled                     += trip_on_power_loss;
            unit._control_AB1.ValueUpdatedInternally += trip_if_all_pantographs_retracted;
            unit._pantograph.toggled                 += trip_if_all_pantographs_retracted;
            unit._pantograph.sidepan_toggled         += trip_if_all_pantographs_retracted;
            unit._main_breaker_closed.StateUpdated   += trip_on_external_trigger;
        }

        private void trip_on_power_loss(bool powered)
        {
            if (!powered)
                trip();
        }

        private bool ready_to_run()
        {
            unit_A_sim unit = _unit;
            if (unit._selector is (int) selector_modes.rheostatic_brake)
                return true;
            pantograph unit_A_pantograph = unit._pantograph;
            float      AB1               = unit._control_AB1.Value;
            if (!unit_A_pantograph.stowed || port_value_signal_active(AB1, (int) AB1_signals.unit_B_pantograph))
                return true;
            return !unit_A_pantograph.sidepan_stowed || port_value_signal_active(AB1, (int) AB1_signals.unit_B_sidepan);
        }
        
        public async void toggle_on(float button_press)
        {
            unit_A_sim unit = _unit;
            if (button_press < 0.5f || unit._main_breaker_closed.State || _engaging || !is_powered 
                || unit._throttle != 0 || !ready_to_run())
            {
                return;
            }
            _engaging         = true;
            _trip_sound.Value = _engage_sound.Value = 0.0f;
            _arm_sound.Value  = 1.0f;
            await Task.Delay(1000);

            _arm_sound.Value = 0.0f;
            if (!_engaging)
                return;
            _engage_sound.Value = 1.0f;
            unit._main_breaker_closed.ChangeState(true);
            _engaging    = false;
            _switched_on = true;
        }

        public void toggle_off(float button_press)
        {
            if (button_press >= 0.5f)
                trip();
        }

        public void trip()
        {
            if (!_engaging && !_switched_on)
                return;
            _engaging = _switched_on = false;
            _unit._main_breaker_closed.ChangeState(false);
            _unit._contactors.toggle_traction_motors(turn_on: false);
            _trip_sound.Value = 1.0f;
        }

        public void trip_if_operating_parameters_exceeded(float supply_voltage, float motor_voltage, float total_draw)
        {
            if (supply_voltage > 2000.0f || motor_voltage > 2000.0f || total_draw > 4500.0f)
            {
                Main.log($"TU {supply_voltage} {motor_voltage} {total_draw}");
                trip();
            }
        }
        
        public void trip_if_all_pantographs_retracted()
        {
            if (!ready_to_run())
                trip();
        }

        private void trip_if_all_pantographs_retracted(float _)
        {
            trip_if_all_pantographs_retracted();
        }

        private void trip_on_external_trigger(bool remain_on)
        {
            if (!remain_on)
                trip();
        }

		public override void Dispose()
		{
			base.Dispose();
            _unit._control_AB1.ValueUpdatedInternally -= trip_if_all_pantographs_retracted;
        }
    }
}