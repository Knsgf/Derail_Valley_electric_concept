// Distributed under terms and conditions of CC0 licence. See LICENCE_CC0.txt for details.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace WE6SIM.circuit_sim;

internal partial class circuit
{
    public interface branch_user
    {
        float conductance { get; }
        float current     { get; }
        float EMF         { get; set; }
        void toggle_contactor(string designation, bool switch_on);
    }

    private class branch : branch_user
    {
        const float min_branch_resistance = 1.0E-4f;

        private static int __next_available_id = 0;

        private readonly Dictionary<string, int> _contactors = [];

        private int _contactors_off = int.MaxValue;

        private float _conductance = 0.0f;
        private float _start_ptential = 0.0f, _end_potential = 0.0f, _EMF = 0.0f;

        private readonly bool _reversed_EMF = false;

        public float conductance => (_contactors_off != 0) ? 0.0f : _conductance;
        public float matrix_EMF
        {
            get => _EMF;
            set
            {
                _EMF = _reversed_EMF ? -value : value;
                EMF_changed?.Invoke(this);
            }
        }
        public float EMF
        {
            get => _reversed_EMF ? -_EMF : _EMF;
            set
            {
                _EMF = _reversed_EMF ? -value : value;
                EMF_changed?.Invoke(this);
            }
        }
        public float current
        {
            get
            {
                float current = (_start_ptential - _end_potential + _EMF) * conductance;
                return _reversed_EMF ? -current : current;
            }
        }

        public int id { get; private set; }

        public event Action<branch>? contactor_toggled;
        public event Action<branch>? EMF_changed;

        private void copy_dict<_type_>(Dictionary<string, _type_> source, Dictionary<string, _type_> destination)
        {
            foreach (KeyValuePair<string, _type_> item in source)
                destination[item.Key] = item.Value;
        }

        public branch(Dictionary<string, float> resistances, bool reverse_EMF, Dictionary<string, int> contactors)
        {
            id = __next_available_id++;

            float total_resistance = 0.0f;
            foreach (float resistance in resistances.Values)
                total_resistance += resistance;
            if (total_resistance < min_branch_resistance)
                total_resistance = min_branch_resistance;
            _conductance = 1.0f / total_resistance;

            _reversed_EMF = reverse_EMF;

            copy_dict(contactors, _contactors);
            Debug.Assert(_contactors.Count <= 30);
            _contactors_off = (1 << _contactors.Count) - 1;
        }

        public void set_node_potential(bool at_start_node, float new_potential)
        {
            if (at_start_node)
                _start_ptential = new_potential;
            else
                _end_potential = new_potential;
        }

        public void toggle_contactor(string contactor_designation, bool switch_on)
        {
            bool contactor_present = _contactors.TryGetValue(contactor_designation, out int contactor_number);
            if (!contactor_present)
                throw new ArgumentException($"Non-existent contactor {contactor_designation}");
            Debug.Assert(contactor_number <= 30 && contactor_number < _contactors.Count);
            bool branch_was_closed = _contactors_off == 0;
            int contactor_mask = 1 << contactor_number;
            if (!switch_on)
                _contactors_off |= contactor_mask;
            else
                _contactors_off &= ~contactor_mask;
            bool branch_is_now_closed = _contactors_off == 0;
            if (branch_is_now_closed != branch_was_closed)
                contactor_toggled?.Invoke(this);
        }

        public static void circuit_setup_finished()
        {
            __next_available_id = 0;
        }
    }
}
