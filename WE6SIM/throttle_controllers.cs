// Distributed under terms and conditions of CC0 licence. See LICENCE_CC0.txt for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WE6SIM;

internal class throttle_controllers
{
	const int notches = 8;

	private readonly camshaft_controller _primary_throttle = new(notches), _secondary_throttle = new(notches);

	private bool _notch_up_primary = false, _hold_primary = false, _roll_secondary_over = false;
	private bool _run_up           = false, _run_down     = false;
	private int  _last_throttle    = 0;

	public event Action<bool>? traction_toggle;

	public throttle_controllers()
	{
		_primary_throttle.switched   += primary_controller_logic;
		_secondary_throttle.switched += secondary_controller_logic;
	}

	private void primary_controller_logic(int current_notch)
	{
		//Main.diagnostics?.Value = current_notch;

		if (current_notch == 1 && _secondary_throttle.current_notch == 1)
		{
			if (_last_throttle <= 1)
				traction_toggle?.Invoke(false);
			else if (_last_throttle == 3)
				traction_toggle?.Invoke(true);
		}

		if (_hold_primary)
		{
			_hold_primary = false;
			_primary_throttle.hold();
		}
		if (_roll_secondary_over)
		{
			_roll_secondary_over = false;
			_secondary_throttle.roll_over_move(false);
		}
		else if (_run_up)
		{
			if (_primary_throttle.current_notch == notches && _secondary_throttle.current_notch == notches)
				_run_up = false;
			else
				_secondary_throttle.run_up();
		}
	}

	private void secondary_controller_logic(int current_notch)
	{
		//Main.diagnostics2?.Value = current_notch;

		if (current_notch == 1 && _primary_throttle.current_notch == 1)
		{
			if (_last_throttle <= 1)
				traction_toggle?.Invoke(false);
			else if (_last_throttle == 3)
				traction_toggle?.Invoke(true);
		}

		if (current_notch == 1)
		{
			if (_notch_up_primary)
			{
				_notch_up_primary = false;
				_hold_primary = true;
				_primary_throttle.notch_up();
			}
			else if (_run_down)
			{
				if (!secondary_rollover_check(false))
					_run_down = false;
			}
		}
		else if (current_notch == notches)
		{
			if (_run_up)
			{
				if (!secondary_rollover_check(true))
					_run_up = false;
			}
			else if (_run_down)
			{
				if (_primary_throttle.current_notch == 1 && _secondary_throttle.current_notch == 1)
					_run_down = false;
				else
					_secondary_throttle.run_down();
			}
		}
	}

	private bool secondary_rollover_check(bool notch_up)
	{
		if (notch_up)
		{
			if (_secondary_throttle.current_notch == notches && _primary_throttle.current_notch < notches)
			{
				_notch_up_primary = true;
				_secondary_throttle.roll_over_move(true);
				return true;
			}
		}
		else if (_secondary_throttle.current_notch == 1 && _primary_throttle.current_notch > 1)
		{
			_roll_secondary_over = _hold_primary = true;
			_primary_throttle.notch_down();
			return true;
		}
		return false;
	}

	private void shut_off_power()
	{
		_run_down = _run_up = false;
		traction_toggle?.Invoke(false);
		_primary_throttle.roll_over_move(true);
		_secondary_throttle.roll_over_move(true);
	}

	public void throttle_handler(int reverser, int throttle)
	{
		if (reverser == 0)
			shut_off_power();
		else if (throttle != _last_throttle)
		{
			bool rollover_occured;
			switch (throttle)
			{
				case 0:
					shut_off_power();
					break;

				case 1:
					_run_down = true;
					_run_up   = false;
					rollover_occured = secondary_rollover_check(false);
					if (!rollover_occured)
						_secondary_throttle.run_down();
					break;

				case 2:
					_run_down = _run_up = false;
					rollover_occured = secondary_rollover_check(false);
					if (!rollover_occured)
						_secondary_throttle.notch_down();
					break;

				case 3:
					_run_down = _run_up = false;
					traction_toggle?.Invoke(true);
					_secondary_throttle.hold();
					_primary_throttle.hold();
					break;

				case 4:
					_run_down = _run_up = false;
					rollover_occured = secondary_rollover_check(true);
					if (!rollover_occured)
						_secondary_throttle.notch_up();
					break;

				case 5:
					_run_down = false;
					_run_up   = true;
					rollover_occured = secondary_rollover_check(true);
					if (!rollover_occured)
						_secondary_throttle.run_up();
					break;
			}
		}
		_last_throttle = throttle;
	}
}
