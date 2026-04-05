// Distributed under terms and conditions of CC0 licence. See LICENCE_CC0.txt for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using WE6SIM.circuit_sim;
using WE6SIM.utilities;

namespace WE6SIM;

internal class camshaft_contactor_set: electric_device
{
	const int max_notches = 31;

	private readonly Dictionary<string, int> _contactor_notch_patterns = [];
	private readonly Dictionary<string, circuit.branch_user> _contactor_locations;
	private readonly camshaft_motor _drive;

	private (string?, int, int) extract_token(string input, int starting_index)
	{
		int left_margin = starting_index;
		for (; left_margin < input.Length; ++left_margin)
		{
			if (!char.IsWhiteSpace(input[left_margin]))
				break;
		}
		if (left_margin >= input.Length)
			return (null, -1, -1);
		int right_margin = left_margin;
		for (; right_margin < input.Length; ++right_margin)
		{
			if (char.IsWhiteSpace(input[right_margin]))
				break;
		}
		assert.test(right_margin > left_margin && right_margin <= input.Length);
		return (input.Substring(left_margin, right_margin - left_margin), left_margin, right_margin);
	}

	public camshaft_contactor_set(string contactor_on_table, Dictionary<string, circuit.branch_user> contactor_locations,
		camshaft_motor drive): base("camshaft_contactor_set")
	{
		string[] lines = contactor_on_table.Split('\n');
		if (lines.Length < 2)
			throw new ArgumentException("At least 2 rows required in a table");
		if (lines.Length >= max_notches + 1)
			throw new ArgumentException($"Only up to {max_notches} allowed");
		if (lines[0].Length < 3)
			throw new ArgumentException("Top row cannont be empty");
		if (lines[0][0] != '#' && !char.IsWhiteSpace(lines[0][1]))
			throw new ArgumentException("Top-left cell must be a number sign");

		int name_left_margin = 2, rightmost_margin = 0;
		Dictionary<string, int> left_margins = [], right_margins = [];
		while (true)
		{
			(string? contactor_name, name_left_margin, int name_right_margin) = extract_token(lines[0], name_left_margin);
			if (contactor_name == null)
				break;
			if (!contactor_locations.ContainsKey(contactor_name))
				throw new ArgumentException($"{contactor_name} not present on circuit diagram");
			Main.log($"camshaft_contactor_set <{contactor_name}>");
			left_margins[contactor_name] = name_left_margin;
			right_margins[contactor_name] = name_right_margin;
			_contactor_notch_patterns[contactor_name] = 0;

			name_left_margin = rightmost_margin = name_right_margin;
		}

		for (int row = 1; row < lines.Length; ++row)
		{
			string current_line = lines[row];
			(string? row_index, _, _) = extract_token(current_line, 0);
			if (row_index == null || !int.TryParse(row_index, out int row_number) || row_number != row)
				throw new ArgumentException("Notch and row number mismatch");

			int closed_mask = 1 << (row - 1);
			foreach (KeyValuePair<string, int> left_margin in left_margins)
			{
				string contactor_name = left_margin.Key;
				if (left_margin.Value >= current_line.Length)
					continue;
				string on_cell = (right_margins[contactor_name] >= current_line.Length)
					? current_line.Substring(left_margin.Value)
					: current_line.Substring(left_margin.Value, right_margins[contactor_name] - left_margin.Value);
				if (!string.IsNullOrWhiteSpace(on_cell))
					_contactor_notch_patterns[contactor_name] |= closed_mask;
			}
		}

		_contactor_locations = contactor_locations;
		_drive               = drive;
		drive.notch_changed += switch_contactors;
	}

	private camshaft_contactor_set(string[] normally_open_contacts, string[] normally_closed_contacts,
		Dictionary<string, circuit.branch_user> contactor_locations, camshaft_motor drive): base("camshaft_contactor_set")
	{
		foreach (string open_contactor in normally_open_contacts)
		{
			if (!contactor_locations.ContainsKey(open_contactor))
				throw new ArgumentException($"{open_contactor} not present on circuit diagram");
			_contactor_notch_patterns[open_contactor] = 0x2;
		}
		foreach (string closed_contactor in normally_closed_contacts)
		{
			if (!contactor_locations.ContainsKey(closed_contactor))
				throw new ArgumentException($"{closed_contactor} not present on circuit diagram");
			_contactor_notch_patterns[closed_contactor] = 0x1;
		}

		_contactor_locations = contactor_locations;
		_drive               = drive;
		drive.notch_changed += switch_contactors;
	}

	private void switch_contactors(int notch)
	{
		check_if_disposed();
		int closed_mask = 1 << (notch - 1);
		foreach (KeyValuePair<string, int> contactor_close_pattern in _contactor_notch_patterns)
		{
			string contactor_name = contactor_close_pattern.Key;
			_contactor_locations[contactor_name].toggle_contactor(contactor_name, (contactor_close_pattern.Value & closed_mask) != 0);
		}
	}

	public override void Dispose()
	{
		if (!disposed)
		{
			base.Dispose();
			_drive.notch_changed -= switch_contactors;
			_drive.Dispose();
		}
	}
}
