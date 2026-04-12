// Distributed under terms and conditions of CC0 licence. See LICENCE_CC0.txt for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using LocoSim.Implementations;
using WE6SIM.circuit_sim;

namespace WE6SIM;

internal partial class unit_a_sim
{
    private struct contactors: IDisposable
    {
        public readonly camshaft_motor         _reverser, _primary_controller;
        public readonly camshaft_contactor_set _reverser_shaft, _primary_camshaft, _secondary_camshaft;
        public readonly contactor              _line_contactor, _dynamic_brake_contactor, _regenerative_contactor;
        public readonly contactor[]            _field_shunt_contactors;

        public contactors(Fuse appliances, Dictionary<string, circuit.branch_user> contactor_locations, 
            Port contactor_on_sound, Port contactor_off_sound)
        {
            _primary_controller     = new camshaft_motor(camshaft_notches, appliances, drop_to_1_on_power_loss: false);
            _primary_camshaft       = new camshaft_contactor_set(_primary_contactor_toggles, contactor_locations, _primary_controller, contactor_on_sound, contactor_off_sound);
            _secondary_camshaft     = new camshaft_contactor_set(_secondary_contactor_toggles, contactor_locations, null, contactor_on_sound, contactor_off_sound);
            _reverser               = new camshaft_motor(2, appliances, drop_to_1_on_power_loss: false);
            _reverser_shaft         = new camshaft_contactor_set(_reverser_toggles, contactor_locations, _reverser, contactor_on_sound, contactor_off_sound);
            _line_contactor         = new contactor(["LC1"], null, contactor_locations, contactor_on_sound, contactor_off_sound, appliances);
            _field_shunt_contactors = new contactor[6];
            const int motors = 1;
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
                open_contacts[motor - 1] = $"FS{motor}.3o";
                closed_contacts[motor - 1] = $"FS{motor}.3c";
            }
            _field_shunt_contactors[3 - 1] = new contactor(open_contacts, closed_contacts, contactor_locations, contactor_on_sound, contactor_off_sound, appliances);
            _dynamic_brake_contactor = new contactor(["DBo"], ["DBc"], contactor_locations, contactor_on_sound, contactor_off_sound, appliances);
            _regenerative_contactor  = new contactor(["RB1.3o"], ["RB1.1c", "RB1.2c"], contactor_locations, contactor_on_sound, contactor_off_sound, appliances);
        }

        public void switch_selector_contactors(int selector)
        {
            switch (selector)
            {
                case 0:
                    _dynamic_brake_contactor.toggle(turn_on: false);
                    _regenerative_contactor.toggle(turn_on: true);
                    _field_shunt_contactors[0].toggle(turn_on: true);
                    _field_shunt_contactors[1].toggle(turn_on: true);
                    _field_shunt_contactors[2].toggle(turn_on: false);
                    _field_shunt_contactors[3].toggle(turn_on: true);
                    _field_shunt_contactors[4].toggle(turn_on: true);
                    _field_shunt_contactors[5].toggle(turn_on: true);
                    break;

                case 1:
                    _dynamic_brake_contactor.toggle(turn_on: false);
                    _regenerative_contactor.toggle(turn_on: true);
                    _field_shunt_contactors[0].toggle(turn_on: true);
                    _field_shunt_contactors[1].toggle(turn_on: true);
                    _field_shunt_contactors[2].toggle(turn_on: false);
                    _field_shunt_contactors[3].toggle(turn_on: true);
                    _field_shunt_contactors[4].toggle(turn_on: true);
                    _field_shunt_contactors[5].toggle(turn_on: true);
                    break;

                case 2:
                    _regenerative_contactor.toggle(turn_on: false);
                    _dynamic_brake_contactor.toggle(turn_on: true);
                    break;

                case 3:
                case 4:
                case 5:
                    _regenerative_contactor.toggle(turn_on: false);
                    _dynamic_brake_contactor.toggle(turn_on: false);
                    break;
            }
        }

        public void Dispose()
        {
            _primary_controller.Dispose();
            _primary_camshaft.Dispose();
            _secondary_camshaft.Dispose();
            _reverser.Dispose();
            _reverser_shaft.Dispose();
            _line_contactor.Dispose();
            _dynamic_brake_contactor.Dispose();
            _regenerative_contactor.Dispose();
            for (int field_contactor = 0; field_contactor < 6; ++field_contactor)
                _field_shunt_contactors[field_contactor].Dispose();
        }
    }
}
