// Distributed under terms and conditions of CC0 licence. See LICENCE_CC0.txt for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

using LocoSim.Implementations;

namespace WE6SIM;

internal class camshaft_motor: electric_device
{
	const int   notch_change_time_ms = 500, notch_change_stages = 2;
	const float notch_lock_threshold = 1.0f / (notch_change_stages * 5.0f);

	private readonly int    _num_notches;
	private readonly object _blocker = new();
	private readonly bool   _drop_to_1_on_power_loss;

	private Task? _regular_movement;
	private int   _target_notch = 1;
	private bool  _camshaft_in_motion = false, _finish_movement_at_next_notch = false;
	private bool  _roll_over          = false;

	public int target_notch
	{
		get
		{
			check_if_disposed();
			return _roll_over ? 0 : _target_notch;
		}
		set
		{
			check_if_disposed();
			if (!_roll_over && is_powered)
			{
				lock (_blocker)
				{
					_target_notch = Math.Max(1, Math.Min(_num_notches, value));
					if (!_camshaft_in_motion && _target_notch != current_notch)
						_regular_movement = regular_move();
				}
			}
		}
	}
	public float current_position { get; private set; } = 1.0f;
	public int current_notch => Mathf.RoundToInt(Mathf.Clamp(current_position, 1.0f, _num_notches));

	public event Action<int>? notch_changed;

	public camshaft_motor(int notches, Fuse power_supply, bool drop_to_1_on_power_loss): base("camshaft_motor", power_supply)
	{
		_num_notches             = notches;
		_drop_to_1_on_power_loss = drop_to_1_on_power_loss;
		power_supply_toggled    += power_supply_changed;

		current_position = drop_to_1_on_power_loss ? 1.0f : (UnityEngine.Random.value * (notches - 1) + 1.0f);
		_target_notch = current_notch;
	}

	private void power_supply_changed(bool power_on)
	{
		if (power_on)
			target_notch = (int) current_position;
		else if (_drop_to_1_on_power_loss && !_roll_over)
			roll_over_move(to_1: true);
	}

	private async Task single_notch_motion(int target_notch, bool power_loss_drop)
	{
		bool up_motion        = target_notch > current_position, down_motion = target_notch < current_position;
		int next_target_notch;
		if (up_motion)
			next_target_notch = Mathf.FloorToInt(current_position + 1.0f);
		else if (down_motion)
			next_target_notch = Mathf.CeilToInt(current_position - 1.0f);
		else
			next_target_notch = Mathf.RoundToInt(current_position);
		while ((power_loss_drop || is_powered) && !disposed
			&& ( up_motion && next_target_notch -  current_position >= 0.5f / notch_change_stages
			|| down_motion && current_position  - next_target_notch >= 0.5f / notch_change_stages))
		{
			current_position += up_motion ? (1.0f / notch_change_stages) : (-1.0f / notch_change_stages);
			await Task.Delay(notch_change_time_ms / notch_change_stages);
		}
		if (!disposed && Mathf.Abs(current_position - next_target_notch) <= 0.5f / notch_change_stages)
		{
			current_position = next_target_notch;   // Eliminate round-off errors
			if (next_target_notch >= 1 && next_target_notch <= _num_notches)
				notch_changed?.Invoke(next_target_notch);
		}
	}

	private async Task regular_move()
	{
		if (_camshaft_in_motion || _roll_over || !is_powered || disposed)
			return;
		_camshaft_in_motion = true;
		while (true)
		{
			_target_notch = Math.Max(1, Math.Min(_num_notches, _target_notch));
			await single_notch_motion(_target_notch, power_loss_drop: false);
			lock (_blocker)
			{
				if (!is_powered || disposed)
				{
					_camshaft_in_motion = _finish_movement_at_next_notch = false;
					break;
				}
				if (_finish_movement_at_next_notch || Mathf.Abs(current_position - _target_notch) <= notch_lock_threshold)
				{
					_target_notch = current_notch;    // Ensure that current and target are the same
												      // if movement was cancelled via _finish_movement_at_next_notch
					_finish_movement_at_next_notch = false;
					_camshaft_in_motion            = _roll_over;
					break;
				}
			}
		}
	}

	public async void roll_over_move(bool to_1)
	{
		check_if_disposed();
		if (_roll_over)
			return;
		_roll_over = true;
		if (_camshaft_in_motion)
		{
			_finish_movement_at_next_notch = true;
			if (_regular_movement != null && !_regular_movement.IsCompleted)
				await _regular_movement;
			_finish_movement_at_next_notch = false;
		}

		if (!is_powered || disposed)
		{
			if (!_drop_to_1_on_power_loss || disposed)
			{
				_roll_over = _camshaft_in_motion = false;
				return;
			}
			to_1 = true;
		}

		bool signal_on_completion = false;
		if (    to_1 && Mathf.Abs(current_position -         1.0f) > notch_lock_threshold 
			|| !to_1 && Mathf.Abs(current_position - _num_notches) > notch_lock_threshold)
		{
			_camshaft_in_motion = true;
			int target_notch;
			if (to_1)
				target_notch = ((current_notch << 1) <= _num_notches + 2) ? 1 : (_num_notches + 1);
			else
				target_notch = ((current_notch << 1) <= _num_notches    ) ? 0 : (_num_notches    );
			do
			{
				await single_notch_motion(target_notch, _drop_to_1_on_power_loss);
				if (!is_powered || disposed)
				{
					if (!_drop_to_1_on_power_loss || disposed)
					{
						_roll_over = _camshaft_in_motion = false;
						return;
					}
					to_1         = true;
					target_notch = 1;
				}
			}
			while (Mathf.Abs(current_position - target_notch) > notch_lock_threshold);
			current_position = _target_notch = to_1 ? 1 : _num_notches;
			if (target_notch != 1 && target_notch != _num_notches)
				signal_on_completion = true;
		}
		_roll_over = _camshaft_in_motion = false;
		if (signal_on_completion)
			notch_changed?.Invoke(_target_notch);
	}

	public async Task finish_movement()
	{
		check_if_disposed();
		if (_camshaft_in_motion && is_powered && _regular_movement != null && !_regular_movement.IsCompleted)
			await _regular_movement;
	}
}
