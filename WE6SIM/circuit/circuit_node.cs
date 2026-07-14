// Distributed under terms and conditions of CC0 licence. See LICENCE_CC0.txt for details.

using System;
using System.Collections.Generic;

namespace electric_sim.circuit_sim;

internal partial class circuit
{
    private struct node
    {
        private static int __next_available_id = 0;

        private readonly branch[] _incoming_branches, _outgoing_branches;
        private readonly bool _is_base_node;

        public int id { get; private set; }

        private branch[] attach_branches(circuit simulation, bool connect_at_start, HashSet<circuit_builder.branch_builder> branches,
            Dictionary<string, branch_user> named_branches, Dictionary<string, branch_user> contactor_locations)
        {
            branch[] new_branches = new branch[branches.Count];
            int index = 0;
            foreach (circuit_builder.branch_builder current in branches)
                new_branches[index++] = simulation.set_up_branch(current, connect_at_start, named_branches, contactor_locations);
            return new_branches;
        }

        public node(circuit simulation, bool is_base_node, HashSet<circuit_builder.branch_builder> incoming, HashSet<circuit_builder.branch_builder> outgoing,
            Dictionary<string, branch_user> named_branches, Dictionary<string, branch_user> contactor_locations)
        {
            id = __next_available_id++;
            _is_base_node = is_base_node;
            _incoming_branches = attach_branches(simulation, false, incoming, named_branches, contactor_locations);
            _outgoing_branches = attach_branches(simulation,  true, outgoing, named_branches, contactor_locations);
        }

        private void set_potential_step(float potential, bool on_outgoing_branches)
        {
            branch[] branches = on_outgoing_branches ? _outgoing_branches : _incoming_branches;
            for (int index = branches.Length - 1; index >= 0; --index)
                branches[index].set_node_potential(on_outgoing_branches, potential);
        }

        public void set_potential(float potential)
        {
            if (_is_base_node && potential != 0.0f)
                throw new InvalidOperationException("Base node's potential must be zero");
            set_potential_step(potential, true);
            set_potential_step(potential, false);
        }

        public void fill_connections_row(sparse_matrix connections)
        {
            for (int index = 0; index < _incoming_branches.Length; ++index)
                connections[id, _incoming_branches[index].id] = -1.0f;
            for (int index = 0; index < _outgoing_branches.Length; ++index)
                connections[id, _outgoing_branches[index].id] = 1.0f;
        }

        public static void circuit_setup_finished()
        {
            __next_available_id = 0;
        }
    }
}
