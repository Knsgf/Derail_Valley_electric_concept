// Distributed under terms and conditions of CC0 licence. See LICENCE_CC0.txt for details.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using WE6SIM.utilities;

namespace WE6SIM;

internal partial class unit_a_sim: IDisposable
{
	private class throttle_controller
	{
		private readonly unit_a_sim _host;

		private bool _camshaft_unlock = false, _interrupt_movement = false, _roll_over = false;

		public throttle_controller(unit_a_sim host)
		{
			_host = host;
		}

		public async void roll_camshafts_over()
		{
			unit_a_sim host = _host;
			if (!host.is_powered)
				return;

			_interrupt_movement = _roll_over = true;
			host._primary_controller.roll_over_move(to_1: true);
			host.set_secondary_camshaft_target_notch(roll_over_to_1);
			while (host.is_powered && (host._primary_controller.current_notch != 1
				|| host.get_secondary_camshaft_current_notch(host._control_BA1.Value) != 1
				|| host._single_notch_movement != null && !host._single_notch_movement.IsCompleted))
			{
				await Task.Delay(300);
			}
			_roll_over = _interrupt_movement = false;
		}

		public async Task finish_secondary_movement(int target_notch)
		{
			unit_a_sim host = _host;

			assert.test(target_notch >= 1 && target_notch <= roll_over_to_full);
			host.set_secondary_camshaft_target_notch(target_notch);
			if (target_notch == roll_over_to_1)
				target_notch = 1;
			else if (target_notch == roll_over_to_full)
				target_notch = camshaft_notches;
			while (!_interrupt_movement && host.is_powered
				&& host.get_secondary_camshaft_current_notch(host._control_BA1.Value) != target_notch)
			{
				await Task.Delay(300);
			}
		}

		public async Task notch_down()
		{
			unit_a_sim host = _host;
			if (_interrupt_movement || !host.is_powered)
				return;

			bool secondary_at_1 = host._secondary_camshaft_notch == 1;
			int current_primary_notch = host._primary_controller.current_notch;
			assert.test(current_primary_notch >= 1 && _host._secondary_camshaft_notch >= 1);
			if (!_camshaft_unlock || secondary_at_1 && current_primary_notch == 1)
				return;
			_camshaft_unlock = false;
			if (!secondary_at_1)
				await finish_secondary_movement(host._secondary_camshaft_notch - 1);
			else
			{
				host._primary_controller.target_notch = current_primary_notch - 1;
				await host._primary_controller.finish_movement();
				if (!_interrupt_movement)
					await finish_secondary_movement(roll_over_to_full);
			}
		}

		public async Task notch_up()
		{
			unit_a_sim host = _host;
			if (_interrupt_movement || !host.is_powered)
				return;

			bool secondary_at_7 = host._secondary_camshaft_notch == camshaft_notches;
			int current_primary_notch = host._primary_controller.current_notch;
			assert.test(current_primary_notch <= camshaft_notches && _host._secondary_camshaft_notch <= camshaft_notches);
			if (!_camshaft_unlock || secondary_at_7 && current_primary_notch == camshaft_notches)
				return;
			_camshaft_unlock = false;
			if (!secondary_at_7)
				await finish_secondary_movement(host._secondary_camshaft_notch + 1);
			else
			{
				await finish_secondary_movement(roll_over_to_1);
				if (!_interrupt_movement)
				{
					host._primary_controller.target_notch = current_primary_notch + 1;
					await host._primary_controller.finish_movement();
				}
			}
		}

		public async Task unlock_camshafts(bool continuous_run)
		{
			unit_a_sim host = _host;
			if (_interrupt_movement || !host.is_powered)
				return;

			if (host._single_notch_movement != null && !host._single_notch_movement.IsCompleted)
				await host._single_notch_movement;
			if ((continuous_run || host._throttle == 3) && _host.is_powered)
				_camshaft_unlock = true;
		}

		public async void run_down()
		{
			unit_a_sim host = _host;
			if (!host.is_powered)
				return;

			if (host._single_notch_movement != null && !host._single_notch_movement.IsCompleted)
			{
				_interrupt_movement = true;
				await host._single_notch_movement;
				_interrupt_movement = _roll_over;
			}
			while (!_roll_over && host._throttle == 1 && _host.is_powered
				&& (host._secondary_camshaft_notch > 1 || host._primary_controller.current_notch > 1))
			{
				await unlock_camshafts(continuous_run: true);
				host._single_notch_movement = notch_down();
			}
		}

		public async void run_up()
		{
			unit_a_sim host = _host;
			if (!host.is_powered)
				return;

			if (host._single_notch_movement != null && !host._single_notch_movement.IsCompleted)
			{
				_interrupt_movement = true;
				await host._single_notch_movement;
				_interrupt_movement = _roll_over;
			}
			while (!_roll_over && host._throttle == 5 && _host.is_powered
				&& (host._secondary_camshaft_notch < camshaft_notches || host._primary_controller.current_notch < camshaft_notches))
			{
				await unlock_camshafts(continuous_run: true);
				host._single_notch_movement = notch_up();
			}
		}
	}
}
