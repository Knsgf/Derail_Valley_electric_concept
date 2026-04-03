// Distributed under terms and conditions of CC0 licence. See LICENCE_CC0.txt for details.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace WE6SIM.circuit_sim;

internal partial class circuit_builder
{
    internal class branch_builder
    {
        private static int __next_id = 0;
        
        private int _num_contactors = 0, _id;

        public bool    reversed_EMF { get; set; }
        public string? branch_name  { get; set; }
        public Dictionary<string, float> resistances { get; private set; } = [];
        public Dictionary<string,   int> contactors  { get; private set; } = [];
        public bool is_empty => resistances.Count + contactors.Count == 0;


        public branch_builder(float intial_resistance = 0.0f, string? branch_name = null, bool is_EMF_reversed = false)
        {
            if (__setup_done)
                throw new InvalidOperationException($"Circuit set-up finished");
            _id = __next_id++;
            this.branch_name  = branch_name;
            reversed_EMF      = is_EMF_reversed;
            ++__branch_count;
            if (intial_resistance > 0.0f)
                add_resistance(branch_name ?? "", intial_resistance);
        }

        public void add_resistance(string name, float ohms)
        {
            if (__setup_done)
                throw new InvalidOperationException($"Circuit set-up finished");
			if (ohms < 0.0f)
				throw new ArgumentOutOfRangeException("Negative resistance");
            if (resistances.ContainsKey(name))
                throw new InvalidOperationException($"{name} is already present");
            resistances[name] = ohms;
        }

        public void add_contactor(string name)
        {
            if (__setup_done)
                throw new InvalidOperationException($"Circuit set-up finished");
            if (_num_contactors >= 30)
                throw new InvalidOperationException($"No more than 30 contactors permitted per branch");
			if (string.IsNullOrWhiteSpace(name))
				throw new ArgumentException("Null or blank contactor name");
            if (contactors.ContainsKey(name))
                throw new InvalidOperationException($"{name} is already present");
            contactors[name] = _num_contactors++;
        }
    }
}
