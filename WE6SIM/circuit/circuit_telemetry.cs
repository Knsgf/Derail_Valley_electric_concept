// Distributed under terms and conditions of CC0 licence. See LICENCE_CC0.txt for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using UnityEngine;

using WE6SIM.utilities;

namespace WE6SIM.circuit_sim;

internal partial class circuit
{
    internal static class circuit_telemetry
    {
        private static readonly List<branch> _branches_sorted_by_current = [];
        private static readonly Dictionary<branch_user, string> _branch_names      = [];
        private static readonly Dictionary<     string,   bool> _branch_contactors = [];
        private static circuit? _monitored_circuit;

        public static void set_up(circuit monitored_circuit, Dictionary<string, branch_user> named_branches)
        {
            if (_monitored_circuit == null)
            {
                _monitored_circuit = monitored_circuit;
                foreach (KeyValuePair<string, branch_user> current_branch in named_branches)
                    _branch_names[current_branch.Value] = current_branch.Key;
                _branches_sorted_by_current.AddRange(monitored_circuit._branches);
            }
        }

        public static bool log_sorted_currents(circuit monitored_circuit, float minimum_current, float minimum_log_current)
        {
            if (_monitored_circuit == null || _monitored_circuit != monitored_circuit)
                return false;
            _branches_sorted_by_current.Sort(
                delegate (branch first, branch second)
                {
                    float first_current = first.current, second_current = second.current;
                    if (first_current > second_current)
                        return -1;
                    if (first_current < second_current)
                        return 1;
                    return 0;
                }
            );
            if (_branches_sorted_by_current.Count <= 0 || Mathf.Abs(_branches_sorted_by_current[0].current) < minimum_current)
                return false;

            bool log_started = false;
            float heat_power = 0.0f, EMF_power = 0.0f;
            for (int index = 0; index < _branches_sorted_by_current.Count; ++index)
            {
                branch current_branch = _branches_sorted_by_current[index];
                assert.test(current_branch.conductance >= 0.0f);
                float branch_heat_power = 0.0f;
                if (current_branch.conductance > 0.0f)
                {
                    branch_heat_power = current_branch.current * current_branch.current / current_branch.conductance;
                    heat_power += branch_heat_power;
                }
                float branch_EMF_power = current_branch.EMF * current_branch.current;
                EMF_power += branch_EMF_power;
                
                if (Mathf.Abs(current_branch.current) < minimum_log_current)
                    continue;
                if (!log_started)
                {
                    log_started = true;
                    Main.log("----------------------------------------------------------------------------------------------------------");
                }
                bool has_name = _branch_names.TryGetValue(current_branch, out string? branch_name);
                if (!has_name)
                    branch_name = "<UNNAMED>";
                string output = $"Branch '{branch_name}'; I = {current_branch.current} A; P1 = {current_branch.start_potential} V; P2 = {current_branch.end_potential} V; E = {current_branch.EMF} V; HE = {branch_heat_power} W; EP = {branch_EMF_power} W";
                current_branch.contactor_telemetry(_branch_contactors);
                foreach (KeyValuePair<string, bool> current_contactor in _branch_contactors)
                    output += $"; {current_contactor.Key}: {(current_contactor.Value ? "ON" : "off")}";
                Main.log(output);
            }
            if (log_started)
                Main.log("----------------------------------------------------------------------------------------------------------");

            Main.diagnostics?.Value = heat_power;
            Main.diagnostics2?.Value = heat_power - EMF_power;
            return true;
        }
    }
}
