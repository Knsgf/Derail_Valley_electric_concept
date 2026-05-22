// Distributed under terms and conditions of CC0 licence. See LICENCE_CC0.txt for details.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using WE6SIM.utilities;

using static WE6SIM.circuit_sim.circuit_builder;

namespace WE6SIM.circuit_sim;

internal class MalformedCircuitException(string message): Exception(message)
{ }

internal partial class circuit
{
    private enum connection { not_connected = 0, at_start = 1, at_end = 2, at_both = 3 }
    
    private static readonly Dictionary<branch_builder,     branch> __built_branches      = [];
    private static readonly Dictionary<branch        , connection> __connected_terminals = [];

    private readonly   node[] _nodes;
    private readonly branch[] _branches;

    private readonly object        _background_blocker;
    private readonly sparse_matrix _incidence, _transposed_incidence, _negative_incidence;
    private readonly sparse_matrix _conductance, _EMFs, _conductance_staged, _EMFs_staged;
    private readonly sparse_matrix _left1 = new(), _left = new(), _virtual_currents = new();
    private readonly float[]       _potentials;
    private readonly int           _last_active_node;

    private sparse_matrix.linear_solver _solver, _background_solver;
    private sparse_matrix _right, _background_right;
    private HashSet<branch> _branch_conductances_changed, _branch_conductances_staged = [], _branch_EMFs_changed = [], _branch_EMFs_staged = [];
    
    private Task? _matrices_recalculation;
    private bool  _simulation_in_progress = false;
    private int   _last_branch            = 0;

    private void set_up_nodes(circuit_builder circuit_info, Dictionary<string, branch_user> named_branches,
        Dictionary<string, branch_user> contactor_locations)
    {
        node_builder? base_node = null;
        int index = 0;
        foreach (node_builder node_definition in circuit_info.all_nodes)
        {
            assert.test(!node_definition.squished);
            if (node_definition.is_base_node)
                base_node = node_definition;
            else
            {
                _nodes[index++] = new node(this, false, node_definition.incoming_branches,
                    node_definition.outgoing_branches, named_branches, contactor_locations);
            }
        }
        if (base_node == null)
            throw new MalformedCircuitException("Base node not defined");
        _nodes[index] = new node(this, true, base_node.incoming_branches, base_node.outgoing_branches, named_branches, contactor_locations);
    }

    private branch set_up_branch(branch_builder branch_definition, bool connect_at_start, 
        Dictionary<string, branch_user> named_branches, Dictionary<string, branch_user> contactor_locations)
    {
        bool branch_built = __built_branches.TryGetValue(branch_definition, out branch? current_branch);
        if (branch_built)
        {
            assert.test(current_branch != null && __connected_terminals.ContainsKey(current_branch));
            connection previous_status = __connected_terminals[current_branch];
            __connected_terminals[current_branch] |= connect_at_start ? connection.at_start : connection.at_end;
            if (__connected_terminals[current_branch] == previous_status)
                throw new MalformedCircuitException($"Branch attached to multiple nodes at the same end {current_branch.id}");
            return current_branch;
        }

        current_branch = new(branch_definition.resistances, branch_definition.reversed_EMF, branch_definition.contactors);
        if (!string.IsNullOrWhiteSpace(branch_definition.branch_name))
        {
            if (named_branches.ContainsKey(branch_definition.branch_name))  //  Someone forgot to tag that IsNullOrWhiteSpace() always returns true if argument is null
                throw new MalformedCircuitException($"Duplicate branch {branch_definition.branch_name}");
            named_branches[branch_definition.branch_name] = current_branch;
        }
        foreach (string contactor_name in branch_definition.contactors.Keys)
        {
            if (contactor_locations.ContainsKey(contactor_name))
                throw new MalformedCircuitException($"Duplicate contactor {contactor_name}");
            contactor_locations[contactor_name] = current_branch;
        }
        __connected_terminals[current_branch] = connect_at_start ? connection.at_start : connection.at_end;

        _branches[_last_branch++] = __built_branches[branch_definition] = current_branch;
        return current_branch;
    }

    public circuit(circuit_builder circuit_info, Dictionary<string, branch_user> named_branches, 
        Dictionary<string, branch_user> contactor_locations, object background_blocker)
    {
        _background_blocker = background_blocker;

        node_builder.remove_extraneous_nodes();
        _nodes    = new   node[circuit_info.node_count  ];
        _branches = new branch[circuit_info.branch_count];
        set_up_nodes(circuit_info, named_branches, contactor_locations);
        if (__connected_terminals.Count != circuit_info.branch_count)
            throw new MalformedCircuitException("Some branches are not connected to any node");
        foreach (KeyValuePair<branch, connection> branch_connection in __connected_terminals)
        {
            if (branch_connection.Value != connection.at_both)
                throw new MalformedCircuitException($"Disconnected branch found ({branch_connection.Key.id})");
        }
        __built_branches.Clear();
        __connected_terminals.Clear();
        Array.Sort(   _nodes, (node   left, node   right) => left.id - right.id);
        Array.Sort(_branches, (branch left, branch right) => left.id - right.id);

        _last_active_node = _nodes.Length - 2;
        assert.test(_last_active_node >= 0);

        _incidence        = new(_last_active_node + 1, _branches.Length);
        _right            = new(_last_active_node + 1, _branches.Length);
        _background_right = new(_last_active_node + 1, _branches.Length);
        for (int row = _last_active_node; row >= 0; --row)
            _nodes[row].fill_connections_row(_incidence);
        _transposed_incidence = new sparse_matrix().transpose_from(_incidence);
        _negative_incidence   = new sparse_matrix().negate_from   (_incidence);
            
        _conductance        = new(_branches.Length);
        _conductance_staged = new(_branches.Length);
        _last_branch = _branches.Length - 1;
        for (int index = _last_branch; index >= 0; --index)
        {
            _conductance[index, index] = _conductance_staged[index, index] = _branches[index].future_conductance;
            _branches[index].contactor_toggled += receive_contactor_toggle;
            _branches[index].EMF_changed       += handle_EMF_change;
        }
        _left1.multiply(_incidence,          _conductance);
        _left.multiply (    _left1, _transposed_incidence);
        _solver            = new(_left);
        _background_solver = new(_left);

        _EMFs        = new(_branches.Length, 1);
        _EMFs_staged = new(_branches.Length, 1);
        _potentials  = new float[_last_active_node + 1];
        
        node.circuit_setup_finished();
        branch.circuit_setup_finished();
        circuit_info.finish_set_up();
        _branch_conductances_changed = [.. _branches];
    }

    private void update_conductances()
    {
        _left1.multiply(_incidence, _conductance_staged  );
        _left.multiply (    _left1, _transposed_incidence);
        _background_solver.change_coeff_matrix(_left);
        _background_right.multiply(_negative_incidence, _conductance_staged);
    }

    private void receive_contactor_toggle(branch toggling_branch)
    {
        _conductance[toggling_branch.id, toggling_branch.id] = toggling_branch.future_conductance;
        _branch_conductances_changed.Add(toggling_branch);
    }

    private void handle_EMF_change(branch EMF_source)
    {
        _EMFs[EMF_source.id, 0] = EMF_source.future_EMF;
        _branch_EMFs_changed.Add(EMF_source);
    }


    private void run_solver()
    {
        _virtual_currents.multiply(_right, _EMFs_staged);
        _solver.solve(_potentials, _virtual_currents);
    }

    public async void simulate()
    {
        if (_simulation_in_progress)
            return;
        _simulation_in_progress = true;

        if (_matrices_recalculation != null && _matrices_recalculation.IsCompleted)
        {
            sparse_matrix conductances = _conductance_staged;
            foreach (branch current_branch in _branch_conductances_staged)
            {
                current_branch.conductance = conductances[current_branch.id, current_branch.id];
                Main.log($"{current_branch.id} {current_branch.conductance}");
            }
            _branch_conductances_staged.Clear();
            (_right , _background_right ) = (_background_right , _right );
            (_solver, _background_solver) = (_background_solver, _solver);
            _matrices_recalculation = null;
        }

        assert.test(_branch_EMFs_staged.Count == 0);
        (_branch_EMFs_staged, _branch_EMFs_changed) = (_branch_EMFs_changed, _branch_EMFs_staged);
        _EMFs_staged.copy_from(_EMFs);
        if (_branch_conductances_changed.Count > 0 && _branch_conductances_staged.Count == 0)
        {
            (_branch_conductances_staged, _branch_conductances_changed) = (_branch_conductances_changed, _branch_conductances_staged);
            assert.test(_branch_conductances_staged.Count > 0);
            _conductance_staged.copy_from(_conductance);
            _matrices_recalculation = Task.Run(update_conductances);
        }
        
        await Task.Run(run_solver);
        lock (_background_blocker)
        {
            node [] nodes           = _nodes;
            float[] node_potentials = _potentials;
            for (int node_index = _last_active_node; node_index >= 0; --node_index)
                nodes[node_index].set_potential(node_potentials[node_index]);
            sparse_matrix EMFs = _EMFs_staged;
            foreach (branch current_branch in _branch_EMFs_staged)
                current_branch.set_current_EMF_from_matrix(EMFs[current_branch.id, 0]);
            _branch_EMFs_staged.Clear();
        }
        _simulation_in_progress = false;
    }
}
