// Distributed under terms and conditions of CC0 licence. See LICENCE_CC0.txt for details.

using System;
using System.Collections.Generic;

using Newtonsoft.Json;
using UnityEngine;

using electric_sim.utilities;

namespace electric_sim.catenary;

interface gantry_user: catenary_object_user
{
#if DEBUG
    bool  is_truss { get; }
    float stretch  { get; set; }
    Vector3? cross_point(Vector3 travel_relative_start, Vector3 travel_vector);
    void change_orientation(Quaternion new_orientation);
#endif
}

internal partial class overhead_equipment
{
    [JsonObject]
    private class gantry: catenary_object, gantry_user
    {
#if DEBUG
        [JsonIgnore]
        private readonly int  _gantry_closer_end_x, _gantry_closer_end_z;
        [JsonIgnore]
        private          int _gantry_further_end_x, _gantry_further_end_z;
        [JsonIgnore]
        private line_cross _mow_movement_intersection;
#endif
        
        [JsonIgnore]
        private static readonly float[] _gantry_lengths = [8.86f, 13.26f, 17.56f, 0.0f, 26.235f];

        [JsonProperty]
        private readonly int tracks;
        [JsonIgnore]
        private float _stretch;
        [JsonIgnore]
        public readonly pole _further_pole;

        [JsonProperty]
        public float stretch
        {
            get => _stretch;
#if DEBUG
            set
            {
                _stretch   = value;
                is_visible = false;
                hide_when_out_of_view();
                reposition_further_pole();
                system.handle_scenery_visibility(PlayerManager.PlayerTransform.position);
            }
#endif
        }

        [JsonIgnore]
        public bool is_truss => tracks == 6;

        private static Vector3 get_frame_relative_position(int x, int z, float y, Quaternion orientation, float stretch)
        {
            return world_position.get_relative_position(x, z, y) + orientation * Vector3.left * (default_pole_offset * (stretch - 1.0f));
        }

        private static string get_template(int tracks)
        {
            if (tracks == 6)
                return "GantryTruss6Tracks";
            return (tracks is >= 2 and <= 4) ? $"Gantry{tracks}Tracks"
                : throw new ArgumentOutOfRangeException("Gantries should cover 2, 3, 4 or 6 tracks");
        }

        private static (int x, int z) further_pole_position(int x, int z, int tracks, float stretch, Quaternion orientation)
        {
            Vector3 offset_to_further_pole = orientation * Vector3.left * (_gantry_lengths[tracks - 2] * stretch);
            int further_pole_x = x + world_position.float_to_fixed(offset_to_further_pole.x);
            int further_pole_z = z + world_position.float_to_fixed(offset_to_further_pole.z);
            return (further_pole_x, further_pole_z);
        }

        [JsonConstructor]
        public gantry(int tracks, int x, int z, float y, Quaternion orientation, float stretch = 1.0f)
            : base(get_template(tracks), x, z, y, orientation)
        {
            this.tracks = tracks;
            _stretch    = stretch;
            (int further_pole_x, int further_pole_z) = further_pole_position(x, z, tracks, stretch, orientation);
            _further_pole = system.add_scenery_object((int x, int z, float y, Quaternion orientation) 
                => new pole(pole_kind.Ground, is_siding_anchor_pole: false, x, z, y, orientation), further_pole_x, further_pole_z, y, orientation);
            _further_pole.placed_procedurally = _further_pole.cantilever_on_far_side = true;

#if DEBUG
            catenary_object arrow = system.add_scenery_object(miscellaneous_object.build_generic("GantryArrow"), x, z, y, orientation);
            arrow.placed_procedurally = true;
            ( _gantry_closer_end_x,  _gantry_closer_end_z) = world_position.get_absolute_position(
                get_relative_position() + orientation * Vector3.right * default_pole_offset);
            (_gantry_further_end_x, _gantry_further_end_z) = world_position.get_absolute_position(
                get_relative_position() + orientation * Vector3.left  * (_gantry_lengths[tracks - 2] * stretch));
            _mow_movement_intersection = new line_cross(_gantry_closer_end_x,  _gantry_closer_end_z, 
                                                       _gantry_further_end_x, _gantry_further_end_z, 0.01f);
#endif
        }

        public override bool reveal()
        {
            assert.test(template is not null);
            is_visible = true;
            entity ??= GameObject.Instantiate(template, get_frame_relative_position(x, z, y, orientation, _stretch), orientation);
            entity.transform.localScale = new Vector3(_stretch, 1.0f, 1.0f);
            return true;
        }

#if DEBUG
        private void reposition_further_pole()
        {
            _further_pole.is_visible = false;
            _further_pole.hide_when_out_of_view();
            (_further_pole.x, _further_pole.z) = further_pole_position(x, z, tracks, _stretch, orientation);
            (_gantry_further_end_x, _gantry_further_end_z) = world_position.get_absolute_position(
                get_relative_position() + orientation * Vector3.left  * (_gantry_lengths[tracks - 2] * _stretch));
            _mow_movement_intersection = new line_cross(_gantry_closer_end_x,  _gantry_closer_end_z, 
                                                       _gantry_further_end_x, _gantry_further_end_z, 0.01f);
            system.reconstruct_tree_after_moving_object(_further_pole);
        }

        public Vector3? cross_point(Vector3 travel_relative_start, Vector3 travel_vector)
        {
            (int travel_start_x, int travel_start_z) = world_position.get_absolute_position(travel_relative_start                );
            (int   travel_end_x, int   travel_end_z) = world_position.get_absolute_position(travel_relative_start + travel_vector);
            float? gantry_cross = _mow_movement_intersection.crossed_line_parameter(travel_start_x, travel_start_z,
                                                                                    travel_end_x,   travel_end_z);
            if (gantry_cross == null)
                return null;
            var gantry_cross_point = (float) gantry_cross;
            Vector3 gantry_closer_end  = world_position.get_relative_position( _gantry_closer_end_x,  _gantry_closer_end_z, y);
            Vector3 gantry_further_end = world_position.get_relative_position(_gantry_further_end_x, _gantry_further_end_z, y);
            return Vector3.LerpUnclamped(gantry_closer_end, gantry_further_end, gantry_cross_point);
        }

        public void change_orientation(Quaternion new_orientation)
        {
            if (Quaternion.Angle(orientation, new_orientation) < 1.0f)
                return;
            List<catenary_object_user> nearby_objects = [];
            system.get_objects_in_area(nearby_objects, get_relative_position(), 0.1f);
            catenary_object? base_pole = null;
            for (int index = nearby_objects.Count - 1; index >= 0; --index)
            { 
                if (nearby_objects[index] is pole regular_pole && regular_pole.pole_type == pole_kind.Ground)
                {
                    base_pole = regular_pole;
                    break;
                }
            }
            
            assert.test(base_pole != null);
            base_pole.is_visible = _further_pole.is_visible = is_visible = false;
            hide_when_out_of_view();
            base_pole.hide_when_out_of_view();
            _further_pole.hide_when_out_of_view();
            base_pole.orientation = _further_pole.orientation = orientation = new_orientation;
            reposition_further_pole();
            system.handle_scenery_visibility(PlayerManager.PlayerTransform.position);
        }
#endif
    }
}
