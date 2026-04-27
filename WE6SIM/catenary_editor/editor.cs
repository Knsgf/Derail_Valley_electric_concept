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
using UnityModManagerNet;

using WE6SIM.catenary;

using static UnityModManagerNet.UnityModManager;
using static WE6SIM.catenary.overhead_equipment;
using static WE6SIM.utilities.world_position;

namespace WE6SIM.catenary_editor;

internal static class editor
{
    public enum placement 
    { 
        Disabled, Left, Right, Front, Gantry2, Gantry3, Gantry4, GantryStretch, GantryAlign, Bracket, FlippedBracket,
        Cantilever, GantryRegistrationArm 
    };
    const float mow_vehicle_length = 14.4f, overhang = 4.1f, wheelbase = mow_vehicle_length - overhang * 2.0f;
    const float vehicle_half_length_squared = mow_vehicle_length * mow_vehicle_length / 4.0f;
    const float half_wheelbase_squared      =          wheelbase *          wheelbase / 4.0f;
    const float default_pole_offset         = 2.2f;

    private static readonly Quaternion flip_around_vertical                  = Quaternion.AngleAxis(180.0f, Vector3.up);
    private static readonly Quaternion turn_by_90_counter_clockwise_vertical = Quaternion.AngleAxis(-90.0f, Vector3.up);

    private static int              _last_pole_x, _last_pole_z, _last_x, _last_z;
    private static float            _remaining_cantilevers_distance;
    private static bool             _first_cantilever      = true, _first_pole = true;
    private static Quaternion       _last_pole_orientation = Quaternion.identity;
    private static editor_settings? _settings;
    private static pole_user?       _movable_pole = null;
    
    public static placement       part_placement                   { get; set; }
    public static pole_kind       pole_type                        { get; set; }
    public static float           pole_horizontal_offset           { get; set; }
    public static float           pole_height_offset               { get; set; }
    public static bool            skip_first                       { get; set; }
    public static int             distance_between_poles           { get; set; }
    public static float           maximum_sweep                    { get; set; }
    public static float           gantry_stretch                   { get; set; }
    public static cantilever_kind cantilever_type                  { get; set; }
    public static bool            zigzag                           { get; set; }
    public static bool            dual_wire                        { get; set; }
    public static bool            automatic_cantilever_termination { get; set; }
    public static int             cantilever_termination_distance  { get; set; }
    public static bool            suspend_cantilever_distance      { get; set; }
    public static bool            erase_scenery                    { get; set; }
    public static bool            use_DM1U                         { get; set; }

	private static void show_editor_controls(ModEntry mod)
	{
		_settings?.Draw(mod);
	}

    public static void set_up(ModEntry mod)
    {
		if (_settings == null)
        {
            _settings = editor_settings.Load<editor_settings>(mod);
		    mod.OnGUI = show_editor_controls;
        }
    }

    private static void store_last_pole_location(Vector3 relative_position, Quaternion orientation)
    {
        (_last_pole_x, _last_pole_z) = get_absolute_position(relative_position);
        _last_pole_orientation       = orientation;
    }
    
    public static void place_pole(Vector3 relative_position, Quaternion orientation)
    {
        if (part_placement == placement.Front && _movable_pole != null)
        {
            _movable_pole.set_relative_position(relative_position + _movable_pole.get_orientation() * Vector3.right * pole_horizontal_offset);
            return;
        }

        store_last_pole_location(relative_position, orientation);
        if (part_placement == placement.Left)
            orientation *= flip_around_vertical;
        else if (part_placement == placement.Front)
            orientation *= turn_by_90_counter_clockwise_vertical;
        pole_user last_pole = system.add_pole((part_placement == placement.Bracket) ? pole_kind.Bracket : pole_type, 
            relative_position + orientation * Vector3.right * pole_horizontal_offset, orientation);
        if (part_placement == placement.Front && _movable_pole == null)
            _movable_pole = last_pole;
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

            float arc_radius, chord_offset;
            if (half_angle == 0.0f)
                arc_radius = chord_offset = 0.0f;
            else
            {
                arc_radius   = (chord_length / 2.0f) / Mathf.Sin(half_angle);
                chord_offset = arc_radius * (1.0f - Mathf.Cos(half_angle));
            }
            if (chord_length >= distance_between_poles || chord_offset >= maximum_sweep)
            {
                var lateral_offset = Vector3.zero;
                if (use_DM1U && arc_radius > mow_vehicle_length / 2.0f)
                {
                    float arc_radius_squared         = arc_radius * arc_radius;
                    float distance_to_vehicle_centre =                              Mathf.Sqrt(arc_radius_squared -      half_wheelbase_squared);
                    float overhang_shift             = distance_to_vehicle_centre - Mathf.Sqrt(arc_radius_squared - vehicle_half_length_squared);
                    Vector3 previous_forward = _last_pole_orientation * Vector3.forward, current_forward = orientation * Vector3.forward;
                    var   arc_axis = Vector3.Cross(previous_forward, current_forward);
                    lateral_offset = Vector3.Cross(        arc_axis, current_forward).normalized * overhang_shift;
                }
                place_pole(relative_position + lateral_offset, orientation);
            }
        }
    }

    private static _type_? get_closest<_type_>(List<catenary_object_user> objects, Vector3 position, float x_shift = 0.0f,
        Func<_type_, bool>? filter = null)
        where _type_: catenary_object_user
    {
        _type_? closest_object           = default;
        float   minimum_distance_squared = float.MaxValue;
        filter ??= (_) => true;
        foreach (catenary_object_user current_object in objects)
        {
            if (current_object is _type_ object_of_specified_type && filter(object_of_specified_type))
            {
                Vector3 shifted_position = current_object.get_relative_position() 
                    + current_object.get_orientation() * Vector3.right * x_shift;
                float distance_squared = (position - shifted_position).sqrMagnitude;
                if (minimum_distance_squared > distance_squared)
                {
                    minimum_distance_squared = distance_squared;
                    closest_object           = object_of_specified_type;
                }
            }
        }
        return closest_object;
    }

    private static List<catenary_object_user> grab_nearby_objects(Vector3 relative_position, float area_half_size)
    {
        List<catenary_object_user> nearby_objects = [];
        system.get_objects_in_area(nearby_objects, relative_position, area_half_size);
        return nearby_objects;
    }

    private static void place_gantry(Vector3 relative_position)
    {
        List<catenary_object_user> nearby_objects = grab_nearby_objects(relative_position, 25.0f);
        if (part_placement == placement.GantryStretch)
        {
            gantry_user? closest_gantry = get_closest<gantry_user>(nearby_objects, relative_position);
            closest_gantry?.stretch = gantry_stretch;
        }
        else
        {
            pole_user? closest_pole = get_closest<pole_user>(nearby_objects, relative_position);
            if (closest_pole != null && !closest_pole.cantilever_on_near_side)
            {
                int tracks = part_placement switch
                {
                    placement.Gantry2 => 2,
                    placement.Gantry3 => 3,
                    placement.Gantry4 => 4,
                    _ => throw new InvalidOperationException($"Gantry placement routine called in {part_placement} mode")
                };
                closest_pole.cantilever_on_near_side = true;
                system.add_gantry(tracks, closest_pole.get_relative_position(), closest_pole.get_orientation());
            }
        }
    }

    private static void align_gantry(Vector3 relative_position, Quaternion orientation)
    {
        List<catenary_object_user> nearby_objects = grab_nearby_objects(relative_position, 5.0f);
        gantry_user? closest_gantry = get_closest<gantry_user>(nearby_objects, relative_position);
        closest_gantry?.change_orientation(orientation);
    }

    private static void place_gantry_braket(Vector3 relative_position)
    {
        Vector3                    last_relative_position = get_relative_position(_last_x, _last_z, relative_position.y);
        List<catenary_object_user> nearby_objects         = grab_nearby_objects(relative_position, 25.0f);
        foreach (catenary_object_user current_object in nearby_objects)
        {
            if (current_object is gantry_user gantry)
            {
                Vector3? bracket_position = gantry.cross_point(last_relative_position, relative_position - last_relative_position);
                if (bracket_position == null)
                    continue;
                pole_user? closest_pole          = get_closest<pole_user>(nearby_objects, (Vector3) bracket_position);
                Vector3?   closest_pole_position = closest_pole?.get_relative_position();
                if (   closest_pole          == null || closest_pole.pole_type != pole_kind.Bracket
                    || closest_pole_position == null || ((Vector3) closest_pole_position - (Vector3) bracket_position).sqrMagnitude > 0.01f)
                {
                    Quaternion orientation = gantry.get_orientation();
                    if (part_placement == placement.FlippedBracket)
                        orientation *= flip_around_vertical;
                    place_pole((Vector3) bracket_position, orientation);
                }
            }
        }
    }

    private static (pole_user?, Vector3) get_nearest_pole_position(List<catenary_object_user> nearby_objects, 
        Vector3 relative_position, bool look_for_gantry_brackets)
    {
        pole_user? closest_pole;
        if (look_for_gantry_brackets)
        {
            closest_pole = get_closest(nearby_objects, relative_position, -0.9f * default_pole_offset, 
                (pole_user pole) => pole.pole_type == pole_kind.Bracket);
        }
        else
        {
            closest_pole = get_closest(nearby_objects, relative_position, default_pole_offset,
                (pole_user pole) => pole.pole_type != pole_kind.Bracket);
        }
        if (closest_pole == null)
            return (null, Vector3.zero);
        return (closest_pole, closest_pole.get_relative_position() + closest_pole.get_orientation()
            * (look_for_gantry_brackets ? Vector3.left : Vector3.right) * default_pole_offset);
    }
    
    private static bool place_cantilever(bool is_gantry_registration_arm, ref cantilever_kind cantilever_type, 
        Vector3 relative_position)
    {
        List<catenary_object_user> nearby_objects = grab_nearby_objects(relative_position, 10.0f);
        (pole_user? pole, Vector3 pole_position)  = get_nearest_pole_position(nearby_objects, relative_position, is_gantry_registration_arm);
        if (pole == null)
            return false;

        Quaternion pole_orientation           = pole.get_orientation();
        Vector3    offset_to_pole             = pole_position - relative_position;
        Vector3    registration_arm_direction = pole_orientation * (is_gantry_registration_arm ? Vector3.right : Vector3.left);
        bool       place_on_near_side         = false, place_on_far_side = false;
        if (offset_to_pole.sqrMagnitude < 16.0f)
        {
            place_on_near_side = Vector3.Dot(offset_to_pole, registration_arm_direction) < 0.0f;
            if (place_on_near_side)
                place_on_near_side = !pole.cantilever_on_near_side;
            else
                place_on_far_side  = !pole.cantilever_on_far_side;
        }
        if (!place_on_near_side && !place_on_far_side)
            return false;

        if (_first_cantilever)
        {
            _first_cantilever = false;
            store_last_pole_location(pole_position, pole_orientation);
        }
        float angle_between_poles = Quaternion.Angle(_last_pole_orientation, pole_orientation);
        if (angle_between_poles > 90.0f)
            cantilever_type = (cantilever_type == cantilever_kind.Inner) ? cantilever_kind.Outer : cantilever_kind.Inner;
        else
        { 
            float sweep_angle = Mathf.Rad2Deg * Mathf.Atan(maximum_sweep / distance_between_poles);
            if (angle_between_poles > 3.0f * sweep_angle)
            {
                var     turn_axis  = Vector3.Cross(_last_pole_orientation * Vector3.forward, pole_orientation * Vector3.forward);
                Vector3 pole_chord = pole_position - get_relative_position(_last_pole_x, _last_pole_z, pole_position.y);
                var     turn_into  = Vector3.Cross(turn_axis, pole_chord);
                cantilever_type = (Vector3.Dot(turn_into, pole_orientation * Vector3.right) > 0.0f) 
                    ? cantilever_kind.Outer : cantilever_kind.Inner;
            }
        }

        if (place_on_near_side)
        {
            system.add_cantilever(cantilever_type, is_gantry_registration_arm,
                dual_wire, pole.get_relative_position(), pole_orientation);
            pole.cantilever_on_near_side = true;
            Main.log($"Near {pole_position} {pole.get_relative_position()} {relative_position}");
        }
        else
        {
            system.add_cantilever(cantilever_type, is_gantry_registration_arm, dual_wire, 
                pole.get_relative_position() - registration_arm_direction * (default_pole_offset * 2.0f),
                pole_orientation * flip_around_vertical);
            pole.cantilever_on_far_side = true;
            Main.log($"Far {pole_position} {pole.get_relative_position()} {relative_position}");
        }
        store_last_pole_location(pole_position, pole_orientation);
        return true;
    }

    public static void process_location(Vector3 relative_position, Vector3 forward_direction)
    {
        if (erase_scenery)
        {
            system.erase_nearby_objects(PlayerManager.PlayerTransform.position);
            return;
        }

        Quaternion orientation;
        switch (part_placement)
        {
            case placement.Disabled:
                _movable_pole                   = null;
                _first_cantilever               = _first_pole = true;
                _remaining_cantilevers_distance = cantilever_termination_distance;
                break;

            case placement.Left:
            case placement.Right:
                _movable_pole                   = null;
                _first_cantilever               = true;
                _remaining_cantilevers_distance = cantilever_termination_distance;
                forward_direction.y = 0.0f;
                if (forward_direction.sqrMagnitude > 0.9f)
                {
                    orientation = Quaternion.FromToRotation(Vector3.forward, forward_direction);
                    place_many_poles_in_succession(relative_position, orientation);
                }
                break;

            case placement.Front:
                _first_cantilever               = _first_pole = true;
                _remaining_cantilevers_distance = cantilever_termination_distance;
                forward_direction.y = 0.0f;
                if (forward_direction.sqrMagnitude > 0.9f)
                {
                    orientation = Quaternion.FromToRotation(Vector3.forward, forward_direction);
                    place_pole(relative_position - 1.05f * Vector3.up, orientation);
                }
                break;

            case placement.Gantry2:
            case placement.Gantry3:
            case placement.Gantry4:
            case placement.GantryStretch:
                _movable_pole                   = null;
                _first_cantilever               = _first_pole = true;
                _remaining_cantilevers_distance = cantilever_termination_distance;
                place_gantry(PlayerManager.PlayerTransform.position);
                break;

            case placement.GantryAlign:
                _movable_pole                   = null;
                _first_cantilever               = _first_pole = true;
                _remaining_cantilevers_distance = cantilever_termination_distance;
                orientation = Quaternion.FromToRotation(Vector3.forward, new Vector3(forward_direction.x, 0.0f, forward_direction.z));
                align_gantry(PlayerManager.PlayerTransform.position, orientation);
                break;

            case placement.Bracket:
            case placement.FlippedBracket:
                _movable_pole                   = null;
                _first_cantilever               = _first_pole = true;
                _remaining_cantilevers_distance = cantilever_termination_distance;
                place_gantry_braket(relative_position);
                break;

            case placement.Cantilever:
            case placement.GantryRegistrationArm:
                _first_pole   = true;
                _movable_pole = null;
                if (!automatic_cantilever_termination)
                    _remaining_cantilevers_distance = cantilever_termination_distance;
                else 
                {
                    if (_remaining_cantilevers_distance < 0.0f) 
                    {                    
                        part_placement = placement.Disabled;
                        _settings?.reset_placement_mode();
                        break;
                    }                    
                    if (!suspend_cantilever_distance)
                    {
                        (int x, int z) = get_absolute_position(relative_position);
                        _remaining_cantilevers_distance -= Mathf.Sqrt(get_distance_squared(x, z, _last_x, _last_z));
                    }
                }
                cantilever_kind _next_cantilever_type = cantilever_type;
				bool cantilever_placed = place_cantilever(part_placement == placement.GantryRegistrationArm, 
                    ref _next_cantilever_type, relative_position);
                if (cantilever_placed && zigzag)
                {
                    _next_cantilever_type = (_next_cantilever_type == cantilever_kind.Inner) 
                        ? cantilever_kind.Outer : cantilever_kind.Inner;
                }
                cantilever_type = _next_cantilever_type;
                _settings?.update_cantilever_type(_next_cantilever_type);
                break;
        }
        (_last_x, _last_z) = get_absolute_position(relative_position);
        _settings?.show_remaining_cantilever_placement_distance(_remaining_cantilevers_distance);
    }
}

#endif