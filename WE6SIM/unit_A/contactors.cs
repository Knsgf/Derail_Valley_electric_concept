// Distributed under terms and conditions of CC0 licence. See LICENCE_CC0.txt for details.

using System;
using System.Collections.Generic;

using LocoSim.Implementations;
using WE6SIM.circuit_sim;
using WE6SIM.devices;

namespace WE6SIM.unit_A;

internal partial class unit_a_sim
{
    private struct contactors: IDisposable
    {
        public readonly camshaft_motor         _reverser, _primary_controller, _selector_motor;
        public readonly camshaft_contactor_set _reverser_shaft, _primary_camshaft, _secondary_camshaft;
        public readonly camshaft_contactor_set _selector_traction_shaft, _selector_regenerative_shaft;
        public readonly contactor              _line_contactor, _line_contactor2, _dynamic_brake_contactor;
        public readonly contactor[]            _field_shunt_contactors;

        public contactors(Fuse appliances, Dictionary<string, circuit.branch_user> contactor_locations, 
            Port contactor_on_sound, Port contactor_off_sound)
        {
            _primary_controller          = new camshaft_motor(camshaft_notches, appliances, drop_to_1_on_power_loss: false);
            _primary_camshaft            = new camshaft_contactor_set(_primary_contactor_toggles, contactor_locations, _primary_controller, contactor_on_sound, contactor_off_sound);
            _secondary_camshaft          = new camshaft_contactor_set(_secondary_contactor_toggles, contactor_locations, null, contactor_on_sound, contactor_off_sound);
            _reverser                    = new camshaft_motor(2, appliances, drop_to_1_on_power_loss: false);
            _reverser_shaft              = new camshaft_contactor_set(_reverser_toggles, contactor_locations, _reverser, contactor_on_sound, contactor_off_sound);
            _selector_motor              = new camshaft_motor(8, appliances, drop_to_1_on_power_loss: false);
            _selector_traction_shaft     = new camshaft_contactor_set(_selector_traction_toggles, contactor_locations, _selector_motor, contactor_on_sound, contactor_off_sound);
            _selector_regenerative_shaft = new camshaft_contactor_set(_selector_regenerative_toggles, contactor_locations, _selector_motor, contactor_on_sound, contactor_off_sound);
            
            _line_contactor = new contactor(["LC1"], null, contactor_locations, contactor_on_sound, contactor_off_sound, appliances);
            _line_contactor2 = new contactor(["LC2", "LC3"], null, contactor_locations, contactor_on_sound, contactor_off_sound, appliances);

            string[] dynamic_brake_closed_contacts = new string[motors + 5], dynamic_brake_open_contacts = new string[motors + 3];
            dynamic_brake_closed_contacts[0] = "DB12c";
            dynamic_brake_closed_contacts[1] = "DB36c";
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
            _dynamic_brake_contactor = new contactor(dynamic_brake_open_contacts, dynamic_brake_closed_contacts, contactor_locations, contactor_on_sound, contactor_off_sound, appliances);

            _field_shunt_contactors = new contactor[6];
            for (int field_contactor = 1; field_contactor <= 6; ++field_contactor)
            {
                if (field_contactor == 3)
                    continue;
                string[] contacts = new string[motors];
                for (int motor = 1; motor <= motors; ++motor)
                    contacts[motor - 1] = $"FS{motor}.{field_contactor}";
                _field_shunt_contactors[field_contactor - 1] = new contactor(contacts, null, contactor_locations, contactor_on_sound, contactor_off_sound, appliances);
            }
            string[] open_contacts = new string[motors], closed_contacts = new string[motors];
            for (int motor = 1; motor <= motors; ++motor)
            {
                open_contacts  [motor - 1] = $"FS{motor}.3o";
                closed_contacts[motor - 1] = $"FS{motor}.3c";
            }
            _field_shunt_contactors[3 - 1] = new contactor(open_contacts, closed_contacts, contactor_locations, contactor_on_sound, contactor_off_sound, appliances);
        }

        public void switch_field_contactors(int field_handle)
        {
            for (int field_contactor_on = 0; field_contactor_on < field_handle; ++field_contactor_on)
                _field_shunt_contactors[field_contactor_on].toggle(turn_on: true);
            for (int field_contactor_off = field_handle; field_contactor_off < 6; ++field_contactor_off)
                _field_shunt_contactors[field_contactor_off].toggle(turn_on: false);
        }

        public void switch_selector_contactors(int selector)
        {
            if (selector == 0 || selector == 1)
            {
                _field_shunt_contactors[0].toggle(turn_on: true);
                _field_shunt_contactors[1].toggle(turn_on: true);
                _field_shunt_contactors[2].toggle(turn_on: false);
                _field_shunt_contactors[3].toggle(turn_on: true);
                _field_shunt_contactors[4].toggle(turn_on: true);
                _field_shunt_contactors[5].toggle(turn_on: true);
            }
            _dynamic_brake_contactor.toggle(selector == 2);
            _selector_motor.target_notch = (selector >= 5) ? 8 : (selector + 1);
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
            _dynamic_brake_contactor.Dispose();
            for (int field_contactor = 0; field_contactor < 6; ++field_contactor)
                _field_shunt_contactors[field_contactor].Dispose();
        }
    }
}
