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

using static UnityModManagerNet.UnityModManager;
using static WE6SIM.utilities.world_position;

namespace WE6SIM.catenary_editor;

internal static class editor
{
    public enum placement { left, right };

    private class scenery_object
    {
        public GameObject template;
        public GameObject? entity;
        public Quaternion orientation;
        public int x, z;
        public float y;
    }
    
    private static int  _last_pole_x, _last_pole_z;
    private static float _remaining_time = 1.0f;
    private static bool _first_pole = true, _floating_origin_handler_set = false;
    private static Quaternion _last_orientation = Quaternion.identity;
    
    private static readonly List<scenery_object> _hidden_poles = [], _visible_poles = [];
    private static readonly GameObject[] _templates;
    private static readonly string[] _template_names = 
    {
        "EndPoleAnchor",
        "Gantry2Tracks",
        "Gantry3Tracks",
        "Gantry4Tracks",
        "InnerCantileverDual",
        "InnerCantileverSingle",
        "MiddleCantileverDual",
        "MiddleCantileverSingle",
        "OuterCantileverDual",
        "OuterCantileverSingle",
        "Pole",
        "PoleFoundation",
        "RegistrationBracket",
        "RegistrationArmInnerDual",
        "RegistrationArmInnerSingle",
        "RegistrationArmMiddleDual",
        "RegistrationArmMiddleSingle",
        "RegistrationArmOuterDual",
        "RegistrationArmOuterSingle",
        "WireDual",
        "WireDualEnd",
        "WireSingle",
        "WireSingleEnd"
    };

    static editor()
    {
        _templates = new GameObject[_template_names.Length];
    }

    public static float pole_height_offset { get; set; }
    public static placement pole_placement { get; set; }
    public static bool auto_pole_placement { get; set; }
    public static float distance_between_poles { get; set; }
    public static float maximum_sweep { get; set; }

    public static void load_assets(ModEntry mod)
    {
        AssetBundle catenary_assets = AssetBundle.LoadFromFile(Path.Combine(mod.Path, "catenary"))
                ?? throw new FileNotFoundException("Not found " + Path.Combine(mod.Path, "catenary"));
        string[] all_assets = catenary_assets.GetAllAssetNames();
        foreach (string name in all_assets)
            Main.log(name);
        for (int asset_index = 0; asset_index < _template_names.Length; ++asset_index)
        {
            _templates[asset_index] = catenary_assets.LoadAsset<GameObject>($"Assets/Catenary/{_template_names[asset_index]}.prefab")
                ?? throw new FileNotFoundException($"No {_template_names[asset_index]} prefab");
        }
    }

    private static void add_scenery(int item_index, int x, int z, float y, Quaternion orientation)
    {
        scenery_object new_pole = new()
		{
			template = _templates[item_index],
			entity = null,
            x = x,
            z = z,
            y = y - 1.05f,
            orientation = (pole_placement == placement.left) ? (orientation * Quaternion.AngleAxis(180.0f, Vector3.up)) : orientation
		};
        _hidden_poles.Add(new_pole);
        Main.log($"x={x / fixed_multiplier} z={z / fixed_multiplier} y={y} c={_hidden_poles.Count}");
        _last_pole_x = x;
        _last_pole_z = z;
        _last_orientation = orientation;
    }

    private static void handle_pole_visibility(int x, int z)
    {
        const float visible_distance = 500.0f;

        _remaining_time -= Time.deltaTime;
        if (_remaining_time > 0.0f)
            return;
        _remaining_time = 1.0f;

        for (int index = _hidden_poles.Count - 1; index >= 0; --index)
        {
            scenery_object current_pole = _hidden_poles[index];

            if (get_manhattan_distance(x, z, current_pole.x, current_pole.z) <= visible_distance * fixed_multiplier)
            {
                _hidden_poles[index] = _hidden_poles[_hidden_poles.Count - 1];
                _hidden_poles.RemoveAt(_hidden_poles.Count - 1);
                current_pole.entity = GameObject.Instantiate(current_pole.template,
                    get_relative_position(current_pole.x, current_pole.z, current_pole.y), current_pole.orientation);
                _visible_poles.Add(current_pole);
            }
        }
        for (int index = _visible_poles.Count - 1; index >= 0; --index)
        {
            scenery_object current_pole = _visible_poles[index];

            if (get_manhattan_distance(x, z, current_pole.x, current_pole.z) > visible_distance * fixed_multiplier)
            {
                _visible_poles[index] = _visible_poles[_visible_poles.Count - 1];
                _visible_poles.RemoveAt(_visible_poles.Count - 1);
                GameObject.Destroy(current_pole.entity);
                current_pole.entity = null;
                _hidden_poles.Add(current_pole);
            }
        }
    }

    private static void floating_origin_shift(WorldMover floating_origin, Vector3 shift)
    {
        for (int index = _visible_poles.Count - 1; index >= 0; --index)
        {
            scenery_object current_pole = _visible_poles[index];

            //    current_pole.entity = GameObject.Instantiate(current_pole.template,
            //        get_relative_position(current_pole.x, current_pole.z, current_pole.y), current_pole.orientation);
            current_pole.entity!.transform.position -= shift;
        }
    }

    public static void set_up_floating_origin()
    {
        if (!_floating_origin_handler_set)
        {
            WorldMover floating_origin = SingletonBehaviour<WorldMover>.Instance;
            if (floating_origin != null)
            {
                floating_origin.WorldMoved += floating_origin_shift;
                _floating_origin_handler_set = true;
            }
        }
    }

    public static void process_location(int x, int z, float y, Quaternion orientation)
    {
        handle_pole_visibility(x, z);
        
        if (!auto_pole_placement)
            _first_pole = true;
        else if (_first_pole)
        {
            _first_pole = false;
            add_scenery(11, x, z, y, orientation);
            add_scenery(10, x, z, y, orientation);
            add_scenery(5, x, z, y, orientation);
        }
        else
        {
            float half_angle = (Mathf.Deg2Rad / 2.0f) * Quaternion.Angle(orientation, _last_orientation);
            float chord_length = Mathf.Sqrt(get_distance_squared(x, z, _last_pole_x, _last_pole_z));
            float arc_radius = (chord_length / 2.0f) / Mathf.Sin(half_angle);
            float chord_offset = arc_radius * (1.0f - Mathf.Cos(half_angle));
            if (chord_length >= distance_between_poles || chord_offset >= maximum_sweep)
            {
                add_scenery(11, x, z, y, orientation);
                add_scenery(10, x, z, y, orientation);
                add_scenery(5, x, z, y, orientation);
            }
        }
    }

    public static void remove_all_scenery()
    {
        WorldMover floating_origin = SingletonBehaviour<WorldMover>.Instance;
        floating_origin?.WorldMoved -= floating_origin_shift;
        for (int index = _visible_poles.Count - 1; index >= 0; --index)
        {
            scenery_object current_pole = _visible_poles[index];

            GameObject.Destroy(current_pole.entity);
            current_pole.entity = null;
        }
    }
}

#endif