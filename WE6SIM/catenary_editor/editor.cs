// Distributed under terms and conditions of CC0 licence. See LICENCE_CC0.txt for details.

#if DEBUG

using DV.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

using WE6SIM.catenary;

using static UnityModManagerNet.UnityModManager;
using static WE6SIM.utilities.world_position;

namespace WE6SIM.catenary_editor;

internal static class editor
{
    public enum placement { left, right };

    private static int  _last_pole_x, _last_pole_z;
    //private static float _remaining_time = 1.0f;
    private static bool _first_pole = true;
    private static Quaternion _last_orientation = Quaternion.identity;
    
    public static float pole_height_offset { get; set; }
    public static placement pole_placement { get; set; }
    public static bool auto_pole_placement { get; set; }
    public static float distance_between_poles { get; set; }
    public static float maximum_sweep { get; set; }

    public static void process_location(int x, int z, float y, Quaternion orientation)
    {
        if (!auto_pole_placement)
            _first_pole = true;
        else if (_first_pole)
        {
            _first_pole = false;
            if (pole_placement == placement.left)
                orientation *= Quaternion.AngleAxis(180.0f, Vector3.up);
            catenary_visual.add_scenery_object(11, x, z, y, orientation);
            catenary_visual.add_scenery_object(10, x, z, y, orientation);
            catenary_visual.add_scenery_object(5, x, z, y, orientation);
            _last_orientation = orientation;
            _last_pole_x = x;
            _last_pole_z = z;
        }
        else
        {
            if (pole_placement == placement.left)
                orientation *= Quaternion.AngleAxis(180.0f, Vector3.up);
            float half_angle = (Mathf.Deg2Rad / 2.0f) * Quaternion.Angle(orientation, _last_orientation);
            float chord_length = Mathf.Sqrt(get_distance_squared(x, z, _last_pole_x, _last_pole_z));
            float arc_radius = (chord_length / 2.0f) / Mathf.Sin(half_angle);
            float chord_offset = arc_radius * (1.0f - Mathf.Cos(half_angle));
            if (chord_length >= distance_between_poles || chord_offset >= maximum_sweep)
            {
                catenary_visual.add_scenery_object(11, x, z, y, orientation);
                catenary_visual.add_scenery_object(10, x, z, y, orientation);
                catenary_visual.add_scenery_object(5, x, z, y, orientation);
                _last_orientation = orientation;
                _last_pole_x = x;
                _last_pole_z = z;
            }
        }
    }

}

#endif