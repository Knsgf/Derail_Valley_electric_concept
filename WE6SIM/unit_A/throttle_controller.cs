// Distributed under terms and conditions of CC0 licence. See LICENCE_CC0.txt for details.

using System.Threading.Tasks;
using UnityEngine;

using WE6SIM.utilities;

using static WE6SIM.devices.control_stand;

namespace WE6SIM.unit_A;

internal partial class unit_A_sim
{
    private class throttle_controller
    {
        const int skipped_notch = 3;
        
        private readonly unit_A_sim _unit;

        private bool _camshaft_unlocked = false, _interrupt_movement = false, _roll_over = false;

        public throttle_controller(unit_A_sim unit)
        {
            _unit = unit;
        }

        public async void roll_camshafts_over()
        {
            unit_A_sim unit = _unit;
            if (!unit.is_powered)
                return;

            _interrupt_movement = _roll_over = true;
            contactors all_contactors = unit._contactors;
            while (unit.is_powered && (all_contactors._line_contactor.engaged 
                                    || all_contactors._line_contactor2.engaged
                                    || all_contactors._voltmeters.engaged))
            {
                all_contactors._line_contactor.toggle(false);
                all_contactors._line_contactor2.toggle(false);
                all_contactors._voltmeters.toggle(false);
                await Task.Delay(300);
            }
            all_contactors.toggle_traction_motors(turn_on: false);
            all_contactors._primary_controller.roll_over_move(to_1: true);
            unit.set_secondary_camshaft_target_notch(roll_over_to_1);
            while (unit.is_powered && all_contactors._primary_controller.current_notch != 1)
            {
                await Task.Delay(300);
                if (all_contactors._primary_controller.current_notch == 1)
                    break;
                all_contactors._primary_controller.roll_over_move(to_1: true);	// restart if previous call terminated before a fuse switched on
            }
            while (unit.is_powered && unit.get_secondary_camshaft_current_notch(unit._control_BA1.Value) != 1)
            {
                await Task.Delay(300);
                unit.set_secondary_camshaft_target_notch(roll_over_to_1);
            }
            while (unit.is_powered && (unit._single_notch_movement != null && !unit._single_notch_movement.IsCompleted))
                await Task.Delay(300);
            _roll_over = _interrupt_movement = false;
        }

        public async Task finish_secondary_movement(int target_notch)
        {
            unit_A_sim unit = _unit;

            assert.test(target_notch >= 1 && target_notch <= roll_over_to_full);
            unit.set_secondary_camshaft_target_notch(target_notch);
            if (target_notch == roll_over_to_1)
                target_notch = 1;
            else if (target_notch == roll_over_to_full)
                target_notch = camshaft_notches;
            while (!_interrupt_movement && unit.is_powered
                && unit.get_secondary_camshaft_current_notch(unit._control_BA1.Value) != target_notch)
            {
                await Task.Delay(300);
                unit.set_secondary_camshaft_target_notch(target_notch);
            }
        }

        private static int next_down_notch(int notch)
        {
            if (notch == skipped_notch + 1)
                return 2;
            return (notch > 1) ? (notch - 1) : 1;
        }

        public async Task notch_down()
        {
            unit_A_sim unit = _unit;
            if (_interrupt_movement || !unit.is_powered)
                return;

            int current_primary_notch   = unit._contactors._primary_controller.current_notch; 
            int current_secondary_notch = unit.get_secondary_camshaft_current_notch(unit._control_BA1.Value);
            bool secondary_at_1 = current_secondary_notch == 1;
            assert.test(current_primary_notch >= 1 && current_secondary_notch >= 1);
            if (!_camshaft_unlocked || secondary_at_1 && current_primary_notch == 1)
                return;
            _camshaft_unlocked = false;
            if (unit._fast_notching_enabled)
            {
                unit._contactors._primary_controller.target_notch = next_down_notch(current_primary_notch);
                await finish_secondary_movement(next_down_notch(current_secondary_notch));
                await unit._contactors._primary_controller.finish_movement();
            }
            else if (!secondary_at_1)
                await finish_secondary_movement(next_down_notch(current_secondary_notch));
            else
            {
                unit._contactors._primary_controller.target_notch = next_down_notch(current_primary_notch);
                await unit._contactors._primary_controller.finish_movement();
                if (!_interrupt_movement)
                    await finish_secondary_movement(roll_over_to_full);
            }
        }

        private static int next_up_notch(int notch)
        {
            if (notch == skipped_notch - 1)
                return 4;
            return (notch < camshaft_notches) ? (notch + 1) : camshaft_notches;
        }

        public async Task notch_up()
        {
            unit_A_sim unit = _unit;
            if (_interrupt_movement || !unit.is_powered)
                return;

            int current_primary_notch   = unit._contactors._primary_controller.current_notch; 
            int current_secondary_notch = unit.get_secondary_camshaft_current_notch(unit._control_BA1.Value);
            bool secondary_at_7 = current_secondary_notch == camshaft_notches;
            assert.test(current_primary_notch <= camshaft_notches && current_secondary_notch <= camshaft_notches);
            if (!_camshaft_unlocked || secondary_at_7 && current_primary_notch == camshaft_notches)
                return;
            _camshaft_unlocked = false;
            if (unit._fast_notching_enabled && current_primary_notch < camshaft_notches && current_secondary_notch == 1 
                && Mathf.Abs(unit._traction_motor_load.Value) <= unit._fast_notching_current_limit)
            {
                unit._contactors._primary_controller.target_notch = next_up_notch(current_primary_notch);
                //await finish_secondary_movement(next_up_notch(current_secondary_notch));
                await unit._contactors._primary_controller.finish_movement();
            }
            else if (!secondary_at_7)
                await finish_secondary_movement(next_up_notch(current_secondary_notch));
            else
            {
                await finish_secondary_movement(roll_over_to_1);
                if (!_interrupt_movement)
                {
                    unit._contactors._primary_controller.target_notch = next_up_notch(current_primary_notch);
                    await unit._contactors._primary_controller.finish_movement();
                }
            }
        }

        public async Task unlock_camshafts(bool continuous_run)
        {
            unit_A_sim unit = _unit;
            if (!unit.is_powered || !unit._main_breaker_closed.State || _roll_over 
                || unit._reverser_position > 0.3f && unit._reverser_position < 0.7f)
            {
                return;
            }

            contactors all_contactors = unit._contactors;
            bool enable_line_contactor2 = unit._selector is not (int) selector_modes.yard_power;
            if (!all_contactors._line_contactor.engaged || enable_line_contactor2 && !!all_contactors._line_contactor2.engaged)
            {
                int primary_target_notch = (unit._selector is not (int) selector_modes.rheostatic_brake) ? 1 : 5;
                while (unit._throttle == 3 && !all_contactors._line_contactor.engaged 
                    || enable_line_contactor2 && !all_contactors._line_contactor2.engaged)
                {
                    if (all_contactors._primary_controller.current_notch == primary_target_notch
                        && unit.get_secondary_camshaft_current_notch(unit._control_BA1.Value) == 1)
                    {
                        all_contactors._line_contactor.toggle(true);
                        if (enable_line_contactor2)
                            all_contactors._line_contactor2.toggle(true);
                    }
                    else
                    {
                        all_contactors._primary_controller.target_notch = primary_target_notch;
                        unit.set_secondary_camshaft_target_notch(1);
                    }
                    await Task.Delay(300);
                    primary_target_notch = (unit._selector is not (int) selector_modes.rheostatic_brake) ? 1 : 5;
                }
            }

            if (_interrupt_movement)
                return;
            if (unit._single_notch_movement != null && !unit._single_notch_movement.IsCompleted)
                await unit._single_notch_movement;
            if ((continuous_run || unit._throttle == 3) && _unit.is_powered)
                _camshaft_unlocked = true;
        }

        public async void run_down()
        {
            unit_A_sim unit = _unit;
            if (!unit.is_powered)
                return;

            if (unit._single_notch_movement != null && !unit._single_notch_movement.IsCompleted)
            {
                _interrupt_movement = true;
                await unit._single_notch_movement;
                _interrupt_movement = _roll_over;
            }
            while (!_interrupt_movement && unit._throttle == 1 && _unit.is_powered
                && (unit.get_secondary_camshaft_current_notch(unit._control_BA1.Value) > 1 
                    || unit._contactors._primary_controller.current_notch > 1))
            {
                await unlock_camshafts(continuous_run: true);
                if (!_camshaft_unlocked)
                    return;
                unit._single_notch_movement = notch_down();
            }
        }

        public async void run_up()
        {
            unit_A_sim unit = _unit;
            if (!unit.is_powered)
                return;

            if (unit._single_notch_movement != null && !unit._single_notch_movement.IsCompleted)
            {
                _interrupt_movement = true;
                await unit._single_notch_movement;
                _interrupt_movement = _roll_over;
            }
            while (!_interrupt_movement && unit._throttle == 5 && _unit.is_powered
                && (unit.get_secondary_camshaft_current_notch(unit._control_BA1.Value) < camshaft_notches 
                    || unit._contactors._primary_controller.current_notch < camshaft_notches))
            {
                await unlock_camshafts(continuous_run: true);
                if (!_camshaft_unlocked)
                    return;
                unit._single_notch_movement = notch_up();
            }
        }
    }
}
