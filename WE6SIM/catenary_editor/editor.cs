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

    private static readonly Quaternion flip_around_vertical = Quaternion.AngleAxis(180.0f, Vector3.up);

    private static int  _last_pole_x, _last_pole_z;
    //private static float _remaining_time = 1.0f;
    private static bool _first_pole = true;
    private static Quaternion _last_pole_orientation = Quaternion.identity;
    
    public static float pole_height_offset { get; set; }
    public static catenary_visual.pole_kind pole_type { get; set; }
    public static placement part_placement { get; set; }
    public static bool skip_first { get; set; }
    public static float distance_between_poles { get; set; }
    public static float maximum_sweep { get; set; }
    public static bool erase_scenery { get; set; }

    private static void store_last_pole_location(Vector3 relative_position, Quaternion orientation)
    {
        (_last_pole_x, _last_pole_z) = get_absolute_position(relative_position);
        _last_pole_orientation       = orientation;
    }
    
    public static void place_pole(Vector3 relative_position, Quaternion orientation)
    {
        store_last_pole_location(relative_position, orientation);
        if (part_placement == placement.Left)
            orientation *= flip_around_vertical;
        catenary_visual.add_pole(pole_type, relative_position, orientation);
    }

    private static void place_many_poles_in_succession(Vector3 relative_position, Quaternion orientation)
    {
        relative_position -= 1.05f * Vector3.up;
        if (_first_pole)
        {
            _first_pole = false;
            if (!skip_first)
                place_pole(relative_position, orientation);
            else
                store_last_pole_location(relative_position, orientation);
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

    private static _type_? get_closest<_type_>(List<catenary_object_user> objects, Vector3 position) 
        where _type_: catenary_object_user
    {
        _type_? closest_object = default;
        float minimum_distance_squared = float.MaxValue;
        foreach (catenary_object_user current_object in objects)
        {
            if (current_object is _type_ object_of_right_type)
            {
                float distance_squared = (position - current_object.get_relative_position()).sqrMagnitude;
                if (minimum_distance_squared > distance_squared)
                {
                    minimum_distance_squared = distance_squared;
                    closest_object           = object_of_right_type;
                }
            }
        }
        return closest_object;
    }

    private static void place_gantry(Vector3 relative_position)
    {
        List<catenary_object_user> poles = [];
        catenary_visual.get_objects_in_area(poles, relative_position, 5.0f);
        pole_user? closest_pole = get_closest<pole_user>(poles, relative_position);
        if (closest_pole != null)
        {
            int tracks = part_placement switch
            {
                placement.Gantry2 => 2,
                placement.Gantry3 => 3,
                placement.Gantry4 => 4,
                _ => throw new InvalidOperationException($"Gantry placement routine called in {part_placement} mode")
            };
            Main.reset_placement_mode();
            catenary_visual.add_gantry(tracks, closest_pole.get_relative_position(), closest_pole.get_orientation());
        }
    }

    public static void process_location(Vector3 relative_position, Quaternion orientation)
    {
        if (erase_scenery)
        {
            catenary_visual.erase_nearby_objects(PlayerManager.PlayerTransform.position);
            return;
        }

        switch (part_placement)
        {
            case placement.Disabled:
                _first_pole = true;
                break;

            case placement.Left:
            case placement.Right:
                place_many_poles_in_succession(relative_position, orientation);
                break;

            case placement.Gantry2:
            case placement.Gantry3:
            case placement.Gantry4:
                place_gantry(PlayerManager.PlayerTransform.position);
                break;
        }
    }
}

#endif