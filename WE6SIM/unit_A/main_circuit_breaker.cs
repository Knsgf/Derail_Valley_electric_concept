// Distributed under terms and conditions of CC0 licence. See LICENCE_CC0.txt for details.

using System.Collections.Generic;
using System.Threading.Tasks;

using LocoSim.Implementations;

using electric_sim.devices;
using electric_sim.utilities;

using static electric_sim.devices.control_stand;
using static electric_sim.utilities.signal_cable;

namespace electric_sim.unit_A;

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
#if DEBUG
            if (pantograph.infinite_power)
                return true;
#endif
            unit_A_sim unit = _unit;
            if (unit._selector is (int) selector_modes.rheostatic_brake)
                return true;
            pantograph unit_A_pantograph = unit._pantograph;
            float      BA1               = unit._control_BA1.Value;
            return (!unit_A_pantograph.stowed || !unit_A_pantograph.sidepan_stowed) 
                || port_value_signal_active(BA1, (int) BA1_signals.pantograph_up);
        }
        
        public async void toggle_on(float button_press)
        {
            unit_A_sim unit = _unit;
            if (button_press < 0.5f || port_value_signal_active(unit._control_BA1.Value, (int) BA1_signals.breaker_trip) 
                || unit._main_breaker_closed.State || _engaging || !is_powered 
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
            toggle_port_signal(_unit._control_AB1, (int) AB1_signals.main_breaker, true);
            _engaging    = false;
            _switched_on = true;
        }

        public void toggle_off(float button_press)
        {
            if (button_press >= 0.5f)
                trip();
        }

        public async void trip()
        {
            if (!_engaging && !_switched_on)
                return;
            _engaging = _switched_on = false;
            _unit._main_breaker_closed.ChangeState(false);
            toggle_port_signal(_unit._control_AB1, (int) AB1_signals.main_breaker, false);
            _unit._contactors.toggle_traction_motors(turn_on: false);
            _trip_sound.Value = 1.0f;
            await Task.Delay(500);
            _trip_sound.Value = 0.0f;
        }

        public void trip_if_operating_parameters_exceeded(float supply_voltage, float motor_voltage, float motor_load, float total_draw)
        {
            if (_switched_on && supply_voltage >= 2000.0f || motor_voltage >= 2000.0f || motor_load >= 850.0f || total_draw >= 4800.0f)
            {
                Main.log($"TU {supply_voltage} {motor_voltage} {motor_load} {total_draw}");
                //trip();
                _trip_sound.Value = 1.0f;   // Trigger instant fuse blow in vanilla TractionMotorSet via "water" detection
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