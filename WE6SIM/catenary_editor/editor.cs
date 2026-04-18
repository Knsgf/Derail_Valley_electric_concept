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
    public enum placement { Disabled, Left, Right, Gantry2, Gantry3, Gantry4 };

    private static readonly Quaternion flip_horizontal = Quaternion.AngleAxis(180.0f, Vector3.up);

    private static int  _last_pole_x, _last_pole_z;
    //private static float _remaining_time = 1.0f;
    private static bool _first_pole = true;
    private static Quaternion _last_pole_orientation = Quaternion.identity;
    
    public static float pole_height_offset { get; set; }
    public static placement pole_placement { get; set; }
    public static bool skip_first { get; set; }
    public static float distance_between_poles { get; set; }
    public static float maximum_sweep { get; set; }
    public static bool erase_scenery { get; set; }

    private static void place_pole(Vector3 relative_position, Quaternion orientation)
    {
        (_last_pole_x, _last_pole_z) = get_absolute_position(relative_position);
        _last_pole_orientation       = orientation;
        if (pole_placement == placement.Left)
            orientation *= flip_horizontal;
        catenary_visual.add_scenery_object(11, relative_position, orientation);
        catenary_visual.add_scenery_object(10, relative_position, orientation);
    }

    private static void place_many_poles_in_a_row(Vector3 relative_position, Quaternion orientation)
    {
        relative_position -= 1.05f * Vector3.up;
        if (_first_pole)
        {
            _first_pole = false;
            if (!skip_first)
                place_pole(relative_position, orientation);
        }
        else
        {
            (int x, int z)     = get_absolute_position(relative_position);
            float half_angle   = (Mathf.Deg2Rad / 2.0f) * Quaternion.Angle(orientation, _last_pole_orientation);
            float chord_length = Mathf.Sqrt(get_distance_squared(x, z, _last_pole_x, _last_pole_z));
            float arc_radius   = (chord_length / 2.0f) / Mathf.Sin(half_angle);
            float chord_offset = arc_radius * (1.0f - Mathf.Cos(half_angle));
            if (chord_length >= distance_between_poles || chord_offset >= maximum_sweep)
                place_pole(relative_position, orientation);
        }
    }

    private static GameObject? get_closest(List<GameObject> objects, Vector3 position)
    {
        GameObject? closest_object = null;
        float minimum_distance_squared = float.MaxValue;
        foreach (GameObject current_object in objects)
        {
            float distance_squared = (position - current_object.transform.position).sqrMagnitude;
            if (minimum_distance_squared > distance_squared)
            {
                minimum_distance_squared = distance_squared;
                closest_object = current_object;
            }
        }
        return closest_object;
    }

    private static void place_gantry(Vector3 relative_position)
    {
        List<GameObject> poles = [];
        catenary_visual.get_objects_of_type(poles, 10, relative_position, (int) (5.0f * fixed_multiplier));
        GameObject? closest_pole = get_closest(poles, relative_position);
        if (closest_pole != null)
        {
            int   object_type        = 1;
            float second_pole_offset = 8.86f;
            if (pole_placement == placement.Gantry3)
            {
                object_type        = 2;
                second_pole_offset = 13.26f;
            }
            else if (pole_placement == placement.Gantry4)
            { 
                object_type        = 3;
                second_pole_offset = 17.56f;
            }
            Main.reset_placement_mode();
            Transform closest_pole_location = closest_pole.transform;
            place_pole(closest_pole_location.position - closest_pole_location.right * second_pole_offset,
                closest_pole_location.rotation);
            catenary_visual.add_scenery_object(object_type, closest_pole_location.position, closest_pole_location.rotation);
        }
    }

    public static void process_location(Vector3 relative_position, Quaternion orientation)
    {
        if (erase_scenery)
        {
            catenary_visual.erase_nearby_objects(PlayerManager.PlayerTransform.position);
            return;
        }

        switch (pole_placement)
        {
            case placement.Disabled:
                _first_pole = true;
                break;

            case placement.Left:
            case placement.Right:
                place_many_poles_in_a_row(relative_position, orientation);
                break;

            case placement.Gantry2:
            case placement.Gantry3:
            case placement.Gantry4:
                place_gantry(relative_position);
                break;
        }
    }
}

#endif