// Distributed under terms and conditions of CC0 licence. See LICENCE_CC0.txt for details.

using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

using DV.Utils;
using WE6SIM.utilities;

using static UnityModManagerNet.UnityModManager;
using static WE6SIM.utilities.world_position;

namespace WE6SIM.catenary;

internal static partial class catenary_visual
{
    public enum placement { left, right };

    [JsonObject]
    private class scenery_object
    {
        private int _template_index;

        [JsonIgnore]
        public GameObject? entity     = null;
        [JsonIgnore]
        public bool        is_visible = false;

        [JsonProperty]
        public Quaternion orientation = Quaternion.identity;
        [JsonProperty]
        public int   x, z;
        [JsonProperty]
        public float y;

        [JsonIgnore]
        public GameObject template { get; private set; }

        [JsonProperty]
        public int template_index 
        {
            get => _template_index;
            set
            {
                if (value >= _templates.Length)
                    throw new ArgumentOutOfRangeException($"Prefab index {value} exceeds total number of prefabs {_templates.Length}");
                _template_index = value;
                template        = _templates[value];
            }
        }

        public scenery_object(int template_index, int x, int z, float y, Quaternion orientation)
        {
            this.template_index = template_index;
            this.template       = _templates[template_index]; //  Stupid analyzer
            this.orientation    = orientation;
            this.x = x;
            this.z = z;
            this.y = y;
        }
    }

    private static int  _last_x, _last_z;
    //private static float _remaining_time = 1.0f;
    private static bool _singleton_handlers_set = false, _scenery_changed = true, _store_scenery = false;

    private static string? file_path;
    private static readonly List<scenery_object> _freshly_added_objects = [];
    private static List<scenery_object> _all_objects = [], _previously_visible_objects = [], _currently_visible_objects = [];
    private static quad_tree? object_tree;
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
        file_path = mod.Path;
        AssetBundle catenary_assets = AssetBundle.LoadFromFile(Path.Combine(file_path, "catenary"))
                ?? throw new FileNotFoundException("Not found " + Path.Combine(file_path, "catenary"));
        string[] all_assets = catenary_assets.GetAllAssetNames();
        foreach (string name in all_assets)
            Main.log(name);
        for (int asset_index = 0; asset_index < _template_names.Length; ++asset_index)
        {
            _templates[asset_index] = catenary_assets.LoadAsset<GameObject>($"Assets/Catenary/{_template_names[asset_index]}.prefab")
                ?? throw new FileNotFoundException($"No {_template_names[asset_index]} prefab");
        }

        List<scenery_object>? loaded_objects = null;
        try
        {
            string raw_scenery = File.ReadAllText(Path.Combine(file_path, "scenery.json"));
            loaded_objects = JsonConvert.DeserializeObject<List<scenery_object>>(raw_scenery);
        }
        catch (Exception error)
        {
            Main.log($"Exception occured when loading scenery: {error}");
        }
        if (loaded_objects != null)
        {
            Main.log($"Loaded objects: {loaded_objects.Count}");
            _all_objects     = loaded_objects;
            object_tree      = new quad_tree(loaded_objects);
            _scenery_changed = true;
        }
    }

    private static void find_objects_within_region(List<scenery_object> found_objects, quad_tree? all_objects, 
        bool do_bounds_check, int left, int top, int right, int bottom)
    {
        found_objects.Clear();
        all_objects?.find_objects(found_objects, do_bounds_check, left, top, right, bottom);
        foreach (scenery_object current_object in _freshly_added_objects)
        {
            if (current_object.x >= left && current_object.x <= right && current_object.z >= top && current_object.z <= bottom)
                found_objects.Add(current_object);
        }
    }

    public static void handle_scenery_visibility(Vector3 relative_postion)
    {
        const float visible_distance       = 100.0f;
        const int   visible_distance_fixed = (int) (visible_distance * fixed_multiplier);

        /*
        _remaining_time -= Time.deltaTime;
        if (_remaining_time > 0.0f)
            return;
        _remaining_time = 1.0f;
        */

        (int x, int z) = get_absolute_position(relative_postion);
        if (!_scenery_changed && Math.Abs(x - _last_x) + Math.Abs(z - _last_z) <= visible_distance_fixed >> 3)
            return;
        _last_x = x;
        _last_z = z;
        _scenery_changed = false;

        (_previously_visible_objects, _currently_visible_objects) = (_currently_visible_objects, _previously_visible_objects);
        List<scenery_object> visible_objects = _previously_visible_objects;
        for (int object_index = visible_objects.Count - 1; object_index >= 0; --object_index)
            visible_objects[object_index].is_visible = false;
        visible_objects = _currently_visible_objects;
        /*
        visible_objects.Clear();
        object_tree?.find_objects(visible_objects, x - visible_distance_fixed, z - visible_distance_fixed,
                                                   x + visible_distance_fixed, z + visible_distance_fixed);
        foreach (scenery_object current_object in _freshly_added_objects)
        {
            if (get_manhattan_distance(x, z, current_object.x, current_object.z) <= visible_distance_fixed + (visible_distance_fixed >> 1))
            {
                // current_object.is_visible = true;
                visible_objects.Add(current_object);
            }
        }
        */
        find_objects_within_region(visible_objects, object_tree, do_bounds_check: false,
            x - visible_distance_fixed, z - visible_distance_fixed,x + visible_distance_fixed, z + visible_distance_fixed);
        for (int object_index = visible_objects.Count - 1; object_index >= 0; --object_index)
        {
            scenery_object current_object = visible_objects[object_index];
            current_object.is_visible = true;
            current_object.entity   ??= GameObject.Instantiate(current_object.template,
                get_relative_position(current_object.x, current_object.z, current_object.y), current_object.orientation);
        }

        visible_objects = _previously_visible_objects;
        for (int object_index = visible_objects.Count - 1; object_index >= 0; --object_index)
        {
            scenery_object current_object = visible_objects[object_index];
            if (!current_object.is_visible && current_object.entity is not null)
            { 
                GameObject.Destroy(current_object.entity);
                current_object.entity = null;
            }
        }
    }

    private static void floating_origin_shift(WorldMover floating_origin, Vector3 shift)
    {
        if (_freshly_added_objects.Count > 16)
        {
            object_tree = new quad_tree(_all_objects);
            _freshly_added_objects.Clear();
        }
        
        List<scenery_object> visible_objects = _currently_visible_objects;
        for (int index = visible_objects.Count - 1; index >= 0; --index)
            visible_objects[index].entity!.transform.position -= shift;
    }

    private static void track_player_movement()
    {
        handle_scenery_visibility(PlayerManager.PlayerTransform.position);
    }

    public static void set_up()
    {
        Main.log("catenary_visual.set_up()");
        if (!_singleton_handlers_set)
        {
            WorldMover floating_origin = SingletonBehaviour<WorldMover>.Instance;
            if (floating_origin != null)
            {
                floating_origin.WorldMoved           += floating_origin_shift;
                UnloadWatcher.UnloadRequested        += remove_all_scenery;
                PlayerManager.PlayerTeleportFinished += track_player_movement;
                _singleton_handlers_set = true;
            }
        }
    }

    public static void remove_all_scenery()
    {
        Main.log("catenary_visual.remove_all_scenery()");
        WorldMover floating_origin            = SingletonBehaviour<WorldMover>.Instance;
        floating_origin?.WorldMoved          -= floating_origin_shift;
        UnloadWatcher.UnloadRequested        -= remove_all_scenery;
        PlayerManager.PlayerTeleportFinished -= track_player_movement;
        _singleton_handlers_set = false;
        for (int index = _currently_visible_objects.Count - 1; index >= 0; --index)
        {
            scenery_object current_object = _currently_visible_objects[index];

            GameObject.Destroy(current_object.entity);
            current_object.entity = null;
        }
        _currently_visible_objects.Clear();
        _previously_visible_objects.Clear();
    }
}
