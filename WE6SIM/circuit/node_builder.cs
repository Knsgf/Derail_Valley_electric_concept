// Distributed under terms and conditions of CC0 licence. See LICENCE_CC0.txt for details.

using System;
using System.Collections.Generic;

using WE6SIM.utilities;

namespace WE6SIM.circuit_sim;

internal partial class circuit_builder
{
    internal class node_builder
    {
        private (int diagram_row, int diagram_column) _location;

        public HashSet<branch_builder> incoming_branches { get; } = [];
        public HashSet<branch_builder> outgoing_branches { get; } = [];
        public bool squished { get; private set; } = false;
        public bool is_base_node => this == __base_node;

        public node_builder((int diagram_row, int diagram_column) location, bool is_base_node = false)
        {
            if (__setup_done)
                throw new InvalidOperationException($"Circuit set-up finished");
            _location = location;
            if (is_base_node)
            {
                if (__base_node != null)
                    throw new InvalidOperationException($"Base node already designated");
                __base_node = this;
            }

            __all_nodes.Add(this);
        }

        public void add_branch(branch_builder new_branch, bool is_outgoing_branch)
        {
            if (__setup_done)
                throw new InvalidOperationException("Circuit set-up finished");
            if (squished)
                throw new InvalidOperationException($"Not permitted to add branches after optimisation {_location}");
            if (is_outgoing_branch)
            {
                //assert.test(!incoming_branches.Contains(new_branch));
                outgoing_branches.Add(new_branch);
            }
            else
            {
                //assert.test(!outgoing_branches.Contains(new_branch));
                incoming_branches.Add(new_branch);
            }
        }

        private void squish_empty_branches()
        {
            if (__setup_done)
                throw new InvalidOperationException($"Circuit set-up finished");
            while (true)
            {
                node_builder? connected_node = null;
                foreach (branch_builder branch in outgoing_branches)
                {
                    if (!branch.is_empty)
                        continue;
                    foreach (node_builder current in __all_nodes)
                    {
                        if (current.incoming_branches.Contains(branch))
                        {
                            connected_node = current;
                            assert.test(connected_node != this);
                            goto loop_exit;
                        }
                    }
                }
            loop_exit:
                if (connected_node == null)
                    return;

                connected_node.squished = true;
                if (connected_node == __base_node)
                    __base_node = this;
                foreach (branch_builder outgoing in connected_node.outgoing_branches)
                    add_branch(outgoing, true);
                connected_node.outgoing_branches.Clear();
                foreach (branch_builder incoming in connected_node.incoming_branches)
                    add_branch(incoming, false);
                connected_node.incoming_branches.Clear();
                __all_nodes.Remove(connected_node);

                HashSet<branch_builder> squished_branches = new(incoming_branches);
                squished_branches.IntersectWith(outgoing_branches);
                __branch_count -= squished_branches.Count;
                incoming_branches.RemoveWhere((branch_builder item) => squished_branches.Contains(item));
                outgoing_branches.RemoveWhere((branch_builder item) => squished_branches.Contains(item));
            }
        }

        public static void remove_extraneous_nodes()
        {
            if (__base_node == null)
                throw new InvalidOperationException("No base node defined");
            List<node_builder> node_builders = new(__all_nodes);
            int index;
            for (index = 0; index < node_builders.Count; ++index)
            {
                if (!node_builders[index].squished)
                    node_builders[index].squish_empty_branches();
            }
            assert.test(!__base_node.squished);
        }
    }
}
