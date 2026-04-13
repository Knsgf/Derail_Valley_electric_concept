// Distributed under terms and conditions of CC0 licence. See LICENCE_CC0.txt for details.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

using WE6SIM.utilities;

namespace WE6SIM.circuit_sim;

internal partial class circuit_builder
{
    private static readonly HashSet<node_builder> __all_nodes = [];
    private static node_builder? __base_node = null;

    private static bool __setup_done = true;
    private static int  __branch_count = 0;

    public int node_count   => __all_nodes.Count;
    public int branch_count => __branch_count;
    public HashSet<node_builder> all_nodes => __all_nodes;

    public circuit_builder()
    {
        if (!__setup_done)
            throw new InvalidOperationException("Previous circuit setup unfinished");
        assert.test(__branch_count == 0 && __all_nodes.Count == 0 && __base_node == null);
        __setup_done = false;
    }

    public circuit set_up_simulation(out Dictionary<string, circuit.branch_user> named_branches,
        out Dictionary<string, circuit.branch_user> contactor_locations, object thread_blocker)
    {
        named_branches      = [];
        contactor_locations = [];
        circuit simulation = new(this, named_branches, contactor_locations, thread_blocker);
        assert.test(__setup_done && __all_nodes.Count == 0 && __branch_count == 0 && __base_node == null);
        return simulation;
    }

    public void finish_set_up()
    {
        __setup_done   = true;
        __branch_count = 0;
        __base_node    = null;
        __all_nodes.Clear();
    }
}

