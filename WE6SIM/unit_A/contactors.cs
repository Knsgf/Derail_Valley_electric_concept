// Distributed under terms and conditions of CC0 licence. See LICENCE_CC0.txt for details.

using System;
using System.Collections.Generic;

using UnityEngine;

using LocoSim.Implementations;
using LocoSim.Implementations.Wheels;

using electric_sim.circuit_sim;
using electric_sim.devices;
using electric_sim.utilities;

using static electric_sim.devices.control_stand;
using static electric_sim.utilities.signal_cable;

namespace electric_sim.unit_A;

internal partial class unit_A_sim
{
    private class contactors: IDisposable
    {
        private readonly unit_A_sim              _unit;
        private readonly PoweredWheelsManager    _driving_axles;

        public readonly camshaft_motor           _reverser, _primary_controller, _selector_motor;
        public readonly camshaft_contactor_set   _reverser_shaft, _primary_camshaft, _secondary_camshaft;
        public readonly camshaft_contactor_set   _selector_traction_shaft, _selector_regenerative_shaft;
        public readonly camshaft_contactor_set   _jogging_switch;
        public readonly camshaft_contactor_set[] _motor_cutouts;
        public readonly binary_contactor         _line_contactor, _line_contactor2,_shunting_contactors, _voltmeter;
        public readonly contactor[]              _field_shunt_contactors;
        public readonly tri_state_contactor      _dynamic_brake_contactor;

        public Action<float>? set_transition_lamp;

        public contactors(TrainCar unit, unit_A_sim simulation, Fuse appliances, Fuse air_supply, Fuse main_breaker, 
            Dictionary<string, circuit.branch_user> contactor_locations, 
            Port contactor_on_sound, Port contactor_off_sound)
        {
            _unit = simulation;
            foreach (GameObject current_object in unit.gameObject.AllChildren())
            {
                PoweredWheelsManager? driving_axles = current_object.GetComponent<PoweredWheelsManager>();
                if (driving_axles is not null)
                {
                    /*
                    Main.log($"PoweredWheelsManager {driving_axles.poweredWheels.Length}");
                    foreach (PoweredWheel? axle in driving_axles.poweredWheels)
                    {
                        if (axle is not null)
                            Main.log($"{axle.index} {axle.state}");
                    }
                    */
                    _driving_axles = driving_axles;
                    break;
                }
            }
            if (_driving_axles is null)
                throw new Exception("No powered wheels manager");

            Action<bool> contactor_click_on_A = delegate (bool engage)
            {
                if (engage)
                    contactor_on_sound.Value = 1.0f;
                else
                    contactor_off_sound.Value = 1.0f;
            };
            Action<bool> contactor_click_on_B = delegate (bool engage)
            {
                toggle_port_signal(_unit._control_AB1, (int) (engage ? AB1_signals.contactor_on : AB1_signals.contactor_off), true);
            };
            Action<bool> contactor_click_on_both = delegate (bool engage)
            {
                contactor_click_on_A(engage);
                contactor_click_on_B(engage);
            };
            
            _primary_controller          = new(camshaft_notches, appliances, drop_to_1_on_power_loss: false);
            _primary_camshaft            = new(_primary_contactor_toggles, contactor_locations, null, contactor_click_on_A);
            _secondary_camshaft          = new(_secondary_contactor_toggles, contactor_locations, null, contactor_click_on_B);
            _reverser                    = new(2, appliances, drop_to_1_on_power_loss: false);
            _reverser_shaft              = new(_reverser_toggles, contactor_locations, _reverser, contactor_click_on_both);
            _selector_motor              = new(8, appliances, drop_to_1_on_power_loss: false);
            _selector_traction_shaft     = new(_selector_traction_toggles, contactor_locations, _selector_motor, contactor_click_on_both);
            _selector_regenerative_shaft = new(_selector_regenerative_toggles, contactor_locations, _selector_motor, contactor_click_on_both);
            _jogging_switch              = camshaft_contactor_set.on_off(["JOG1", "JOG2"], null, contactor_locations, null, null);
            _selector_motor.notch_changed     += extinguish_transition_lamp;
            _primary_controller.notch_changed += switch_primary_contactors;
            
            _line_contactor      = new(["LC1"                 ], null, contactor_locations, contactor_click_on_A   , main_breaker, air_supply);
            _line_contactor2     = new([ "LC2", "LC3"         ], null, contactor_locations, contactor_click_on_A   , main_breaker, air_supply);
            _shunting_contactors = new(["CR1S", "CR2S", "CR3S"], null, contactor_locations, contactor_click_on_A   , main_breaker, air_supply);
            _voltmeter           = new(["VMC34"               ], null, contactor_locations, contactor_click_on_both, main_breaker, air_supply);
            
            _motor_cutouts = new camshaft_contactor_set[motors];
            for (int motor = 1; motor <= motors; ++motor)
                _motor_cutouts[motor - 1] = camshaft_contactor_set.on_off([$"CO{motor}"], null, contactor_locations, null, null);

            string[] dynamic_brake_closed_contacts = new string[motors + 5], dynamic_brake_open_contacts = new string[motors + 3];
            dynamic_brake_closed_contacts[0] = "DB16c";
            dynamic_brake_closed_contacts[1] = "DB34c";
            dynamic_brake_closed_contacts[2] = "DB56c";
            dynamic_brake_closed_contacts[3] = "DB12Gc";
            dynamic_brake_closed_contacts[4] = "DB34Gc";
            dynamic_brake_open_contacts[0] = "DB12o";
            dynamic_brake_open_contacts[1] = "DB34o";
            dynamic_brake_open_contacts[2] = "DB56o";
            for (int motor = 1; motor <= motors; ++motor)
            {
                dynamic_brake_closed_contacts[motor + 4] = $"DB{motor}c";
                dynamic_brake_open_contacts  [motor + 2] = $"DB{motor}o";
            }
            _dynamic_brake_contactor = new(dynamic_brake_closed_contacts, null, dynamic_brake_open_contacts,
                contactor_locations, contactor_click_on_both, appliances);

            _field_shunt_contactors = new contactor[6];
            for (int field_contactor = 1; field_contactor <= 6; ++field_contactor)
            {
                if (field_contactor == 3)
                    continue;
                string[] contacts = new string[motors];
                for (int motor = 1; motor <= motors; ++motor)
                    contacts[motor - 1] = $"FS{motor}.{field_contactor}";
                _field_shunt_contactors[field_contactor - 1] = new binary_contactor(contacts, null, contactor_locations, 
                    contactor_click_on_both, appliances, air_supply);
            }
            string[] open_contacts   = new string[motors], intermediate_contacts = new string[motors * 2], 
                     closed_contacts = new string[motors];
            for (int motor = 1; motor <= motors; ++motor)
            {
                int motor_index = motor - 1;
                open_contacts  [motor_index] = intermediate_contacts[motor_index * 2    ] = $"FS{motor}.3o";
                closed_contacts[motor_index] = intermediate_contacts[motor_index * 2 + 1] = $"FS{motor}.3c";
            }
            _field_shunt_contactors[3 - 1] = new tri_state_contactor(closed_contacts, intermediate_contacts, open_contacts, 
                contactor_locations, contactor_click_on_both, appliances, air_supply);
        }

        public void switch_primary_contactors(int primary_notch)
        {
            bool to_haul_notch = _primary_controller.target_notch != 1;
            _shunting_contactors.toggle(to_haul_notch);
            _primary_camshaft.switch_contactors(to_haul_notch ? primary_notch : _secondary_camshaft.current_notch);
            //Main.log($"SPC {to_haul_notch} {primary_notch} {_secondary_camshaft.current_notch}");
        }

        public void switch_field_contactors(int field_handle)
        {
            if (field_handle > 6)
                field_handle = 6;
            int field_contactor;
            for (field_contactor = 0; field_contactor < field_handle; ++field_contactor)
                _field_shunt_contactors[field_contactor].toggle(turn_on: true);
            for (; field_contactor < 6; ++field_contactor)
                _field_shunt_contactors[field_contactor].toggle(turn_on: false);
        }

        public void switch_selector_contactors(int selector)
        {
            if (selector is (int) selector_modes.series_regenerative or (int) selector_modes.parallel_regenerative)
            {
                _field_shunt_contactors[0].toggle(turn_on: true);
                _field_shunt_contactors[1].toggle(turn_on: true);
                _field_shunt_contactors[2].toggle(turn_on: false);
                _field_shunt_contactors[3].toggle(turn_on: true);
                _field_shunt_contactors[4].toggle(turn_on: true);
                _field_shunt_contactors[5].toggle(turn_on: true);
            }
            _dynamic_brake_contactor.toggle(selector is (int) selector_modes.rheostatic_brake);
            _selector_motor.target_notch = (selector is (int) selector_modes.parallel_power  ) ? 8 : (selector + 1);
            toggle_transition_lamp(_selector_motor.current_notch != _selector_motor.target_notch);
        }

        private void extinguish_transition_lamp(int selector_notch)
        {
            if (selector_notch == _selector_motor.target_notch)
                toggle_transition_lamp(light_up: false);
        }

        private void toggle_transition_lamp(bool light_up)
        {
            set_transition_lamp?.Invoke(light_up ? 0.5f : 0.0f);
            toggle_port_signal(_unit._control_AB1, (int) AB1_signals.transition, light_up);
        }

        public void toggle_traction_motors(bool turn_on)
        {
            int notch = turn_on ? 2 : 1, motor_status = 0;
            for (int motor = 0; motor <= 2; ++motor)
            {
                if (_driving_axles.poweredWheels[motor].IsPowered)
                    motor_status |= 1 << motor;
            }
            int   unit_B_motor_status = 1 << ((int) BA2_shift.unit_B_active_motors);
            float BA2                 = _unit._control_BA2.Value;
            for (int motor = 5; motor >= 3; --motor)
            { 
                if (port_value_signal_active(BA2, unit_B_motor_status))
                    motor_status |= 1 << motor;
                unit_B_motor_status <<= 1;
            }
            
            if (_unit._selector == (int) selector_modes.rheostatic_brake)
            {
                for (int motor_group = 0x3; motor_group <= (0x3 << 4); motor_group <<= 2)
                {
                    if ((motor_status & motor_group) != motor_group)
                        motor_status &= ~motor_group;
                }
            }

            for (int motor = 0; motor < 6; ++motor)
                _motor_cutouts[motor].switch_contactors(((motor_status & (1 << motor)) != 0) ? notch : 1);
        }

        public void toggle_jogging(bool turn_on)
        {
            toggle_traction_motors(turn_on);
            _jogging_switch.switch_contactors(turn_on ? 2 : 1);
        }

        public void Dispose()
        {
            _primary_controller.Dispose();
            _primary_camshaft.Dispose();
            _secondary_camshaft.Dispose();
            _reverser.Dispose();
            _reverser_shaft.Dispose();
            _selector_motor.Dispose();
            _selector_traction_shaft.Dispose();
            _selector_regenerative_shaft.Dispose();
            _line_contactor.Dispose();
            _line_contactor2.Dispose();
            _shunting_contactors.Dispose();
            _dynamic_brake_contactor.Dispose();
            _jogging_switch.Dispose();
            _voltmeter.Dispose();
            for (int motor = 0; motor < 6; ++motor)
                _motor_cutouts[motor].Dispose();
            for (int field_contactor = 0; field_contactor < 6; ++field_contactor)
                _field_shunt_contactors[field_contactor].Dispose();
        }
    }
}
