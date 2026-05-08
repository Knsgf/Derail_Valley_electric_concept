// Distributed under terms and conditions of CC0 licence. See LICENCE_CC0.txt for details.

using System;

using UnityEngine;

namespace WE6SIM.utilities;

internal class line_cross
{
    private readonly int   _crossed_origin_x, _crossed_origin_z, _crossed_direction_x, _crossed_direction_z;
    private readonly float _absolute_extent, _crossed_relative_extent_lower, _crossed_relative_extent_upper;
    
    private static float square(int x)
    {
        return ((long) x * x) / (world_position.fixed_multiplier * world_position.fixed_multiplier);
    }
    
    private static long calculate_2x2_determinant(int top_left, int top_right, int bottom_left, int bottom_right)
    {
        return ((long) top_left * bottom_right - (long) top_right * bottom_left) / world_position.fixed_divider;
    }

    public line_cross(int crossed_origin_x, int crossed_origin_z, int crossed_end_x, int crossed_end_z, float absolute_extent)
    {
        _crossed_origin_x    = crossed_origin_x;
        _crossed_origin_z    = crossed_origin_z;
        _crossed_direction_x = crossed_end_x  - crossed_origin_x;
        _crossed_direction_z = crossed_end_z  - crossed_origin_z;
        if (_crossed_direction_x == 0 && _crossed_direction_z == 0)
            throw new ArgumentException("Zero length fixed line segment not permitted");
        
        _absolute_extent               = absolute_extent;
        float crossed_length           = Mathf.Sqrt(square(_crossed_direction_x) + square(_crossed_direction_z));
        float crossed_relative_extent  = absolute_extent / crossed_length;
        _crossed_relative_extent_lower =       -crossed_relative_extent;
        _crossed_relative_extent_upper = 1.0f + crossed_relative_extent;
    }
        
    public float? crossed_line_parameter(int crossing_origin_x, int crossing_origin_z, int crossing_end_x, int crossing_end_z)
    {
        int crossing_direction_x = crossing_end_x - crossing_origin_x;
        int crossing_direction_z = crossing_end_z - crossing_origin_z;
        float main_determinant = calculate_2x2_determinant(
            _crossed_direction_x, -crossing_direction_x, 
            _crossed_direction_z, -crossing_direction_z
        );
        if (main_determinant == 0.0f)
            return null;
        
        int   origins_difference_x     = crossing_origin_x - _crossed_origin_x;
        int   origins_difference_z     = crossing_origin_z - _crossed_origin_z;
        float crossing_length          = Mathf.Sqrt(square(crossing_direction_x) + square(crossing_direction_z));
        float crossing_relative_extent = _absolute_extent / crossing_length;

        float crossing_parameter = calculate_2x2_determinant(
            _crossed_direction_x, origins_difference_x, 
            _crossed_direction_z, origins_difference_z
        ) / main_determinant;
        if (crossing_parameter < -crossing_relative_extent || crossing_parameter > 1.0f + crossing_relative_extent) 
            return null;
        float crossed_parameter = calculate_2x2_determinant(
            origins_difference_x, -crossing_direction_x, 
            origins_difference_z, -crossing_direction_z
        ) / main_determinant;
        return (crossed_parameter < _crossed_relative_extent_lower || crossed_parameter > _crossed_relative_extent_upper)
            ? null : crossed_parameter;
    }
}
