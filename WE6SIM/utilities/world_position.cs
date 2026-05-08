// Distributed under terms and conditions of CC0 licence. See LICENCE_CC0.txt for details.

using System;

using UnityEngine;

using DV.OriginShift;

namespace WE6SIM.utilities;

internal static class world_position
{
    public const int   fixed_divider    = 1000;
    public const float fixed_multiplier = fixed_divider;
    
    public static int float_to_fixed(float value)
    {
        return Mathf.RoundToInt(value * fixed_multiplier);
    }
    
    public static (int x, int z) get_absolute_position(Vector3 relative_position)
    {
        int relative_x = float_to_fixed(relative_position.x);
        int relative_z = float_to_fixed(relative_position.z);
        Vector3 origin_shift = OriginShift.currentMove;
        int origin_x = float_to_fixed(origin_shift.x);
        int origin_z = float_to_fixed(origin_shift.z);
        return (relative_x - origin_x, relative_z - origin_z);
    }

    public static Vector3 get_relative_position(int x, int z, float y)
    {
        Vector3 origin_shift = OriginShift.currentMove;
        int origin_x = float_to_fixed(origin_shift.x);
        int origin_z = float_to_fixed(origin_shift.z);
        return new Vector3((x + origin_x) / fixed_multiplier, y, (z + origin_z) / fixed_multiplier);
    }

    public static float get_distance_squared(int x1, int z1, int x2, int z2)
    {
        long x_difference = x1 - x2, z_difference = z1 - z2;
        return (x_difference * x_difference + z_difference * z_difference) / (fixed_multiplier * fixed_multiplier);
    }

    public static int get_manhattan_distance(int x1, int z1, int x2, int z2)
    {
        return Math.Abs(x1 - x2) + Math.Abs(z1 - z2);
    }
}
