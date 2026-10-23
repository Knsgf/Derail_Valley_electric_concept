//#define INPUT_OFF

using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;

using electric_sim.circuit_sim;
using electric_sim.devices;
using System.Threading.Tasks;

namespace Tests
{
    internal class TM_rig
    {
        const int    motors         = 1;
        const float  wheel_diameter = 1.12f;
        const string TM_test_rig    = "\n"
            + "*---@EPS>------------------------------------------------*   \n"
            + "|                                                        |   \n"
            + "|                                   *-------#RR1a#-------*   \n"
            + "|                                   |                    |   \n"
            + "*--<MF1b@--*--<MF1a@--*--*--#RF1b#--*--<MA1@--*--#RF1a#--*   \n"
            + "|          |          |  |                    |              \n"
            + "*--<WF1b@--*--<WF1a@--*  *--------#RR1b#------*              \n";
        static readonly (float ratio_a, float ratio_b)[] _field_ratios = new (float, float)[]
        {
            (1.0f, 1.00f),
            (0.4f, 1.00f),
            (0.2f, 1.00f),
            (0.0f, 1.00f),
            (0.0f, 0.53f),
            (0.0f, 0.47f),
            (0.0f, 0.40f)
        };
        static readonly float[] _WF_a_resistances = new float[_field_ratios.Length], _WF_b_resistances = new float[_field_ratios.Length];

        readonly Dictionary<string, float> _resistances = new Dictionary<string, float>()
        {
            ["EPS"] = 0.0f
        };
        readonly Dictionary<string, circuit.branch_user> _named_branches, _contactor_locations;

        readonly object         _simulation_sync = new object();
        readonly circuit        _TM_rig;
        readonly traction_motor _motor1/*, _motor2*/;
        readonly IEnumerator    _simulation_step;

        float _supply_voltage, _supply_resistance, _speed;
        int   _WF;
        bool  _reversed;
        Task  _simulation_progress;

        static float shunt_resistance(float field_resistance, float ratio)
        {
            if (ratio >= 1.0f)
                return 1.0E+6f;
            if (ratio <= 0.0f)
                return 0.0f;
            return field_resistance * ratio / (1.0f - ratio);
        }
        
        static TM_rig()
        {
            for (int WF_index = 0; WF_index < _field_ratios.Length; ++WF_index)
            {
                (float ratio_a, float ratio_b) = _field_ratios[WF_index];
                _WF_a_resistances[WF_index] = shunt_resistance(traction_motor.base_field1_resistance, ratio_a);
                _WF_b_resistances[WF_index] = shunt_resistance(traction_motor.base_field2_resistance, ratio_b);
            }
        }

        internal TM_rig(float starting_voltage, float starting_resistance, float starting_speed, int starting_WF, bool start_reversed)
        {
            float multiplier = 1.0f;
            for (int motor_branch = 1; motor_branch <= motors; ++motor_branch)
            {
                _resistances[$"MA{motor_branch}" ] = traction_motor.base_armature_resistance *  multiplier;
                _resistances[$"MF{motor_branch}a"] = traction_motor.base_field1_resistance   * (multiplier + 0.001f);
                _resistances[$"MF{motor_branch}b"] = traction_motor.base_field2_resistance   * (multiplier + 0.002f);
                _resistances[$"WF{motor_branch}a"] = _WF_a_resistances[starting_WF];
                _resistances[$"WF{motor_branch}b"] = _WF_b_resistances[starting_WF];
                multiplier += 0.003f;
            }
            _TM_rig = circuit_compiler.trace(_resistances, TM_test_rig).set_up_simulation(out _named_branches, out _contactor_locations, _simulation_sync);
            _motor1 = new traction_motor(1, 1.0f, multiplier, _named_branches);
            circuit.telemetry.set_up(_TM_rig, _named_branches, Console.WriteLine);
            traction_motor.print = Console.WriteLine;

            _supply_voltage    = Mathf.Clamp(starting_voltage, 0.0f, 2000.0f);
            _supply_resistance = Mathf.Max(starting_resistance, 0.0f);
            _speed             = starting_speed;
            _WF                = Math.Max(0, Math.Min(_field_ratios.Length - 1, starting_WF));
            _reversed          = start_reversed;

            set_reverser(start_reversed);
            _named_branches["EPS"].EMF               = _supply_voltage;
            _named_branches["EPS"].closed_resistance = _supply_resistance;
            _simulation_step = simulation_cycle().GetEnumerator();
        }

        static float wheel_RPM(float speed) => (speed / 3.6f) / (Mathf.PI * wheel_diameter) * 60.0f;
        
        void set_reverser(bool reversed)
        {
            for (int motor_branch = 1; motor_branch <= motors; ++motor_branch)
            {
                _contactor_locations["RF1a"].toggle_contactor("RF1a", !reversed);
                _contactor_locations["RF1b"].toggle_contactor("RF1b", !reversed);
                _contactor_locations["RR1a"].toggle_contactor("RR1a",  reversed);
                _contactor_locations["RR1b"].toggle_contactor("RR1b",  reversed);
            }
        }

        private IEnumerable simulation_cycle()
        {
            while (true)
            {
                _simulation_progress = _TM_rig.simulate(permit_topology_changes: true);
                while (!_simulation_progress.IsCompleted)
                    yield return null;
                _motor1.estalblish_EMFs(false, wheel_RPM(_speed));
                _simulation_progress = _TM_rig.simulate(permit_topology_changes: false);
                while (!_simulation_progress.IsCompleted)
                    yield return null;
                _motor1.establish_currents();
                yield return null;
            }
        }
        
        internal void run()
        {
            while (true)
            {
                lock (_simulation_sync)
                    circuit.telemetry.log_sorted_voltages(_TM_rig); 

#if !INPUT_OFF            
                switch(char.ToLowerInvariant(Console.ReadKey().KeyChar))
                {
                    case 'q':
                        return;

                    case '7':
                        _speed += 1.0f;
                        continue;
                
                    case '1':
                        _speed -= 1.0f;
                        continue;

                    case '8':
                        _supply_voltage = Mathf.Min(_supply_voltage + 50.0f, 1500.0f);
                        _named_branches["EPS"].EMF = _supply_voltage;
                        continue;
                
                    case '2':
                        _supply_voltage = Mathf.Max(_supply_voltage - 50.0f, 0.0f);
                        _named_branches["EPS"].EMF = _supply_voltage;
                        continue;

                    case '9':
                        _supply_resistance += 0.1f;
                        continue;
                
                    case '3':
                        _supply_resistance = Mathf.Max(_supply_resistance - 0.1f, 0.0f);
                        continue;

                    case '/':
                        _reversed = !_reversed;
                        set_reverser(_reversed);
                        continue;

                    /*
                    case '.':
                        fields_crossed = !fields_crossed;
                        continue;
                    */

                    case '+':
                        _WF = Math.Min(_WF + 1, _field_ratios.Length - 1);
                        continue;

                    case '-':
                        _WF = Math.Max(_WF - 1, 0);
                        continue;
                }
#endif
                _simulation_step.MoveNext();
            }
        }
    }
}
