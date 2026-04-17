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

namespace WE6SIM.catenary;

internal static class catenary_visual
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

    private static int  _last_x, _last_z;
    //private static float _remaining_time = 1.0f;
    private static bool _floating_origin_handler_set = false, _scenery_changed = true;

    private static readonly List<scenery_object> _all_objects = [], _visible_objects = [];
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

    static catenary_visual()
    {
        _templates = new GameObject[_template_names.Length];
    }

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

    public static void handle_scenery_visibility(int x, int z)
    {
        const float visible_distance = 500.0f;
        const int visible_distance_fixed = (int) (visible_distance * fixed_multiplier);

        /*
        _remaining_time -= Time.deltaTime;
        if (_remaining_time > 0.0f)
            return;
        _remaining_time = 1.0f;
        */
        //Main.log($"dx={x - _last_x} dz={z - _last_z}");
        if (!_scenery_changed && Math.Abs(x - _last_x) + Math.Abs(z - _last_z) <= visible_distance_fixed >> 3)
            return;
        Main.log($"x={x} z={z} {_scenery_changed}");
        _last_x = x;
        _last_z = z;
        _scenery_changed = false;

        List<scenery_object> visible_poles = _visible_objects;
        visible_poles.Clear();
        foreach (scenery_object current_object in _all_objects)
        {
            if (get_manhattan_distance(x, z, current_object.x, current_object.z) <= visible_distance_fixed)
            {
                current_object.entity ??= GameObject.Instantiate(current_object.template,
                        get_relative_position(current_object.x, current_object.z, current_object.y), current_object.orientation);
                visible_poles.Add(current_object);
            }
            else if (current_object.entity is not null)
            {
                GameObject.Destroy(current_object.entity);
                current_object.entity = null;
            }
        }
        /*
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
        */
    }

    private static void floating_origin_shift(WorldMover floating_origin, Vector3 shift)
    {
        List<scenery_object> visible_poles = _visible_objects;
        for (int index = visible_poles.Count - 1; index >= 0; --index)
        {
            scenery_object current_pole = visible_poles[index];

            //    current_pole.entity = GameObject.Instantiate(current_pole.template,
            //        get_relative_position(current_pole.x, current_pole.z, current_pole.y), current_pole.orientation);
            current_pole.entity!.transform.position -= shift;
        }
    }

    public static void set_up()
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

    public static void remove_all_scenery()
    {
        WorldMover floating_origin   = SingletonBehaviour<WorldMover>.Instance;
        floating_origin?.WorldMoved -= floating_origin_shift;
        _floating_origin_handler_set = false;
        for (int index = _visible_objects.Count - 1; index >= 0; --index)
        {
            scenery_object current_pole = _visible_objects[index];

            GameObject.Destroy(current_pole.entity);
            current_pole.entity = null;
        }
    }
    
    public static void add_scenery_object(int item_index, int x, int z, float y, Quaternion orientation)
    {
        scenery_object new_object = new()
        {
            template = _templates[item_index],
            entity = null,
            x = x,
            z = z,
            y = y - 1.05f,
            //orientation = (pole_placement == placement.left) ? (orientation * Quaternion.AngleAxis(180.0f, Vector3.up)) : orientation
            orientation = orientation
        };
        _all_objects.Add(new_object);
        Main.log($"x={x / fixed_multiplier} z={z / fixed_multiplier} y={y} c={_all_objects.Count}");
        _scenery_changed = true;
    }
}
