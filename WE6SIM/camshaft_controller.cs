using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace WE6SIM;

internal class camshaft_controller
{
	const int notch_change_time_ms = 500, notch_change_stages = 5;

	private readonly int _num_notches;

	private Task? _regular_movement;
	private int _target_notch = 1;
	private bool _camshaft_in_motion = false, _finish_movement_at_next_notch = false, _single_notch_movement = false;
	private bool _roll_over        = false;

	public float current_position { get; private set; } = 1.0f;
	public int current_notch => Mathf.RoundToInt(current_position);

	public event Action<int>? switched;

	public camshaft_controller(int notches)
	{
		_num_notches = notches;
	}

	private async Task single_notch_motion(int target_notch)
	{
		bool up_motion        = target_notch > current_notch, down_motion = target_notch < current_notch;
		int next_target_notch = current_notch;
		if (up_motion)
			++next_target_notch;
		else if (down_motion)
			--next_target_notch;
		while (up_motion && next_target_notch - current_position >= 0.5f / notch_change_stages
			|| down_motion && current_position - next_target_notch >= 0.5f / notch_change_stages)
		{
			current_position += up_motion ? (1.0f / notch_change_stages) : (-1.0f / notch_change_stages);
			await Task.Delay(notch_change_time_ms / notch_change_stages);
		}
		current_position = next_target_notch;
		if (next_target_notch >= 1 && next_target_notch <= _num_notches)
			switched?.Invoke(next_target_notch);
	}

	private async Task regular_move()
	{
		if (_camshaft_in_motion)
			return;
		_camshaft_in_motion = true;
		do
		{
			_target_notch = Math.Max(1, Math.Min(_num_notches, _target_notch));
			await single_notch_motion(_target_notch);
		}
		while (!_finish_movement_at_next_notch && current_notch != _target_notch);
		_target_notch                  = current_notch;    // Ensure that current and target are the same if _finish_movement_at_next_notch was true
		_finish_movement_at_next_notch = false;
		_camshaft_in_motion            = _roll_over;
	}

	public async void roll_over(bool to_1)
	{
		_roll_over = true;
		if (_camshaft_in_motion)
		{
			_finish_movement_at_next_notch = true;
			if (_regular_movement != null && !_regular_movement.IsCompleted)
				await _regular_movement;
			_finish_movement_at_next_notch = false;
		}
		if (to_1 && current_notch != 1 || !to_1 && current_notch != _num_notches)
		{
			_camshaft_in_motion = true;
			int target_notch;
			if (to_1)
				target_notch = ((current_notch << 1) <= _num_notches + 2) ? 1 : (_num_notches + 1);
			else
				target_notch = 0;
			_target_notch = target_notch;
			do
			{
				await single_notch_motion(target_notch);
			}
			while (current_notch != target_notch);
			current_position    = _target_notch = to_1 ? 1 : _num_notches;
			_camshaft_in_motion = _roll_over    = _finish_movement_at_next_notch = false;
			if (target_notch != 1)
				switched?.Invoke(_target_notch);
		}
		_camshaft_in_motion = _roll_over = _finish_movement_at_next_notch = false;
	}

	public void run_down()
	{
		if (_roll_over || current_notch == 1)
			return;
		_target_notch                  = 1;
		_finish_movement_at_next_notch = _single_notch_movement = false;
		if (!_camshaft_in_motion)
			_regular_movement = regular_move();
	}

	public void notch_down()
	{
		if (_roll_over || _single_notch_movement || current_notch == 1)
			return;
		_single_notch_movement         = true;
		_target_notch                  = current_notch - 1;
		_finish_movement_at_next_notch = false;
		if (!_camshaft_in_motion)
			_regular_movement = regular_move();
	}

	public void hold()
	{
		if (_roll_over)
			return;
		if (_camshaft_in_motion)
			_finish_movement_at_next_notch = true;
		_single_notch_movement = false;
	}

	public void notch_up()
	{
		Main.log($"NU {_roll_over} {_single_notch_movement} {current_notch}");
		if (_roll_over || _single_notch_movement || current_notch == _num_notches)
			return;
		_single_notch_movement         = true;
		_target_notch                  = current_notch + 1;
		_finish_movement_at_next_notch = false;
		if (!_camshaft_in_motion)
			_regular_movement = regular_move();
	}

	public void run_up()
	{
		if (_roll_over || current_notch == _num_notches)
			return;
		_target_notch                  = _num_notches;
		_finish_movement_at_next_notch = _single_notch_movement = false;
		if (!_camshaft_in_motion)
			_regular_movement = regular_move();
	}
}
