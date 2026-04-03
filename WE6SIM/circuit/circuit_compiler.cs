// Distributed under terms and conditions of CC0 licence. See LICENCE_CC0.txt for details.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

using static WE6SIM.circuit_sim.circuit_builder;

namespace WE6SIM.circuit_sim;

internal class circuit_compiler
{
    enum element_type { unknown, untracked_resistor, named_branch, named_branch_reversed, contactor };
    
    static readonly string[] __node_extents =
    {
        @"\|/",
        @"- -",
        @"/|\"
    };

    private static readonly HashSet<(int row, int column)> _visited = [];
    private static readonly Dictionary<(int row, int column), node_builder> _nodes = [];

    private static void get_symbol_dirtection(char symbol, out int row_dir, out int column_dir)
    {
        switch (symbol)
        {
            case '-':
                row_dir = 0;
                column_dir = 1;
                break;

            case '|':
                row_dir = 1;
                column_dir = 0;
                break;

            case '/':
                //row_dir    = 1;
                //column_dir = -1;
                //break;

            case '\\':
                row_dir = column_dir = 1;
                break;

            default:
                throw new ArgumentException("Path is not traceable");
        }
    }
    
    private static int trace_element(Dictionary<string, float> elements, string[] diagram, int row, int column, int direction, 
        branch_builder branch_definition)
    {
        element_type element = element_type.unknown;
        char element_symbol = diagram[row][column], termination_symbol = '\0';
        switch (element_symbol)
        {
            case '[':
                if (direction < 0)
                    throw new MalformedCircuitException($"Resistor symbol turned backwards at {row}, {column}");
                element = element_type.untracked_resistor;
                termination_symbol = ']';
                break;

            case ']':
                if (direction > 0)
                    throw new MalformedCircuitException($"Resistor symbol turned backwards at {row}, {column}");
                element = element_type.untracked_resistor;
                termination_symbol = '[';
                break;

            case '@':
                element = element_type.named_branch;
                termination_symbol = (direction > 0) ? '>' : '<';
                break;

            case '<':
                if (direction < 0)
                    throw new MalformedCircuitException($"Branch direction symbol turned backwards at {row}, {column}");
                element = element_type.named_branch_reversed;
                termination_symbol = '@';
                break;

            case '>':
                if (direction > 0)
                    throw new MalformedCircuitException($"Branch direction symbol turned backwards at {row}, {column}");
                element = element_type.named_branch_reversed;
                termination_symbol = '@';
                break;

            case '#':
                element = element_type.contactor;
                termination_symbol = '#';
                break;

            default:
                throw new MalformedCircuitException($"Unrecognised element at {row}, {column}");
        }
        Debug.Assert(element != element_type.unknown && termination_symbol != '\0');

        string name;
        int    termination_position;
        if (direction > 0)
        {
			//string search_part = diagram[row][(column + 1)..];
			string search_part = diagram[row].Substring(column + 1);
			termination_position = search_part.IndexOf(termination_symbol);
            if (termination_position < 0)
                throw new MalformedCircuitException($"Unrecognised element at {row}, {column}");
			//name = search_part[..termination_position];
			name = search_part.Substring(0, termination_position);
        }
        else
        {
            //string search_part = diagram[row][..column];
			string search_part = diagram[row].Substring(0, column);
			termination_position = search_part.LastIndexOf(termination_symbol);
            if (termination_position < 0)
                throw new MalformedCircuitException($"Unrecognised element at {row}, {column}");
            //name = search_part[(termination_position + 1)..];
			name = search_part.Substring(termination_position + 1);
		}

		if (!elements.TryGetValue(name, out float resistance))
            throw new ArgumentException($"Element {name} not listed");
        switch (element)
        {
            case element_type.untracked_resistor:
                branch_definition.add_resistance(name, resistance);
                break;

            case element_type.named_branch:
                if (branch_definition.branch_name != null)
                    throw new MalformedCircuitException("$Branch with multiple names at {row}, {column}");
                branch_definition.branch_name  = name;
                branch_definition.reversed_EMF = false;
                branch_definition.add_resistance(name, resistance);
                break;

            case element_type.named_branch_reversed:
                if (branch_definition.branch_name != null)
                    throw new MalformedCircuitException("$Branch with multiple names at {row}, {column}");
                branch_definition.branch_name  = name;
                branch_definition.reversed_EMF = true;
                branch_definition.add_resistance(name, resistance);
                break;

            case element_type.contactor:
                branch_definition.add_contactor(name);
                break;
        }

        return name.Length;
    }
    
    private static branch_builder trace_branch(Dictionary<string, float> elements, string[] diagram, int row, int column, 
        int node_row_offset, int node_column_offset)
    {
        int start_row = row, start_column = column;
        char branch_symbol = diagram[row][column];
        get_symbol_dirtection(branch_symbol, out int row_dir, out int column_dir);
        /*
        if (_visited.Contains((row + row_dir, column + column_dir)))
        {
            row_dir    = -row_dir;
            column_dir = -column_dir;
        }
        */
        row_dir    *= node_row_offset;
        column_dir *= node_column_offset;
        Debug.Assert(!_visited.Contains((row, column)));
        _visited.Add((row, column));
        branch_builder new_branch = new();

        while (true)
        {
            int test_row = row + row_dir, test_column = column + column_dir;
            if (test_row < 0 || test_row >= diagram.Length || test_column < 0 || test_column > diagram[test_row].Length)
                throw new MalformedCircuitException($"Branch extends beyond diagram at {row}, {column}");
            switch (diagram[test_row][test_column])
            {
                case '+':
                    row    = test_row;
                    column = test_column;
                    break;

                case '[':
                case ']':
                case '<':
                case '>':
                case '@':
                case '#':
                    if (row_dir != 0)
                        throw new MalformedCircuitException($"Circuit element on non-horizontal line at {row}, {column}");
                    column += (trace_element(elements, diagram, test_row, test_column, column_dir, new_branch) + 2) * column_dir;
                    break;

                case '*':
                    Debug.Assert(row == start_row && column == start_column || !_visited.Contains((row, column)));
                    _visited.Add((row, column));
                    trace_node(elements, diagram, test_row, test_column, new_branch);
                    return new_branch;

                default:
                    if (diagram[test_row][test_column] != branch_symbol)
                        throw new MalformedCircuitException($"Unexpected branch termination at {row}, {column}");
                    row    = test_row;
                    column = test_column;
                    break;
            }
        }
    }
    
    private static void trace_node(Dictionary<string, float> elements, string[] diagram, int row, int column, 
        branch_builder? incoming_branch = null)
    {
        Debug.Assert(diagram[row][column] == '*');
        bool existing_node = _nodes.TryGetValue((row, column), out node_builder? node);
        if (!existing_node)
            _nodes[(row, column)] = node = new node_builder((row, column), _nodes.Count == 0);
        
        if (incoming_branch != null)
            node!.add_branch(incoming_branch, false);
        if (existing_node)
            return;

        _visited.Add((row, column));
        for (int row_offset = 1; row_offset >= -1; --row_offset)
        {
            int test_row = row + row_offset;
            if (test_row < 0 || test_row >= diagram.Length)
                continue;
            for (int column_offset = -1; column_offset <= 1; ++column_offset)
            {
                int test_column = column + column_offset;
                if (test_column >= 0 && test_column < diagram[test_row].Length 
                    && !_visited.Contains((test_row, test_column))
                    && diagram[test_row][test_column] == __node_extents[row_offset + 1][column_offset + 1])
                {
                    node!.add_branch(trace_branch(elements, diagram, test_row, test_column, row_offset, column_offset), true);
                }
            }
        }
    }

    public static circuit_builder trace(Dictionary<string, float> elements, string diagram)
    {
        string[] delineated_diagram = diagram.Split('\n');
        int row, column = -1;
        for (row = delineated_diagram.Length - 1; row >= 0; --row)
        {
            column = delineated_diagram[row].LastIndexOf('*');
            if (column >= 0)
                break;
        }
        if (column < 0)
            throw new ArgumentException("No nodes in diagram");
        circuit_builder new_circuit = new();
        trace_node(elements, delineated_diagram, row, column);
        _visited.Clear();
        _nodes.Clear();
        return new_circuit;
    }
}
