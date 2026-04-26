// Distributed under terms and conditions of CC0 licence. See LICENCE_CC0.txt for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using UnityEngine;

using WE6SIM.catenary_editor;
using WE6SIM.utilities;

namespace WE6SIM.catenary;

interface gantry_user: catenary_object_user
{
    float stretch { get; set; }
    Vector3? cross_point(Vector3 travel_relative_start, Vector3 travel_vector);
    void change_orientation(Quaternion new_orientation);
}

internal partial class overhead_equipment
{
    [JsonObject]
    private class gantry: catenary_object, gantry_user
    {
        const float gantry_shift = 2.2f;
        
        [JsonIgnore]
        private static readonly float[] _gantry_lengths = [8.86f, 13.26f, 17.56f];

        [JsonProperty]
        private readonly int tracks;
        [JsonIgnore]
        private float _stretch;
        [JsonIgnore]
        private readonly pole _further_pole;

        [JsonProperty]
        public float stretch
        {
            get => _stretch;
            set
            {
                _stretch = value;
                reposition_further_pole();
            }
        }

        private void reposition_further_pole()
        {
            (int further_pole_x, int further_pole_z) = further_pole_position(x, z, tracks, _stretch, orientation);
            _further_pole.x = further_pole_x;
            _further_pole.z = further_pole_z;
            _further_pole.entity?.transform.position = world_position.get_relative_position(further_pole_x, further_pole_z, y);
            if (entity is not null)
            {
                entity.transform.position   = get_frame_relative_position(x, z, y, orientation, _stretch);
                entity.transform.localScale = new Vector3(_stretch, 1.0f, 1.0f);
            }
            system.reconstruct_tree();
        }

        private static Vector3 get_frame_relative_position(int x, int z, float y, Quaternion orientation, float stretch)
        {
            return world_position.get_relative_position(x, z, y) + orientation * Vector3.left * (gantry_shift * (stretch - 1.0f));
        }

        private static string get_template(int tracks)
        {
            return (tracks >= 2 && tracks <= 4) ? $"Gantry{tracks}Tracks"
                : throw new ArgumentOutOfRangeException("Gantries should cover 2, 3 or 4 tracks");
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
                => new pole(pole_kind.Ground, x, z, y, orientation), further_pole_x, further_pole_z, y, orientation);
            _further_pole.placed_procedurally = true;
        }

		public override void reveal()
		{
            is_visible = true;
            entity ??= GameObject.Instantiate(template, get_frame_relative_position(x, z, y, orientation, _stretch), orientation);
            entity.transform.localScale = new Vector3(_stretch, 1.0f, 1.0f);
		}

        private float calculate_2x2_determinant(float top_left, float top_right, float bottom_left, float bottom_right)
        {
            return top_left * bottom_right - top_right * bottom_left;
        }
        
        public Vector3? cross_point(Vector3 travel_relative_start, Vector3 travel_vector)
        {
            Vector3 gantry_closer_end = world_position.get_relative_position(x, z, y) + orientation * (Vector3.right * gantry_shift);
            Vector3 gantry_span       = orientation * Vector3.left * (_gantry_lengths[tracks - 2] * stretch);
            float divider = calculate_2x2_determinant(gantry_span.x, -travel_vector.x, gantry_span.z, -travel_vector.z);
            if (divider == 0.0f)
                return null;
            float origins_difference_x = travel_relative_start.x - gantry_closer_end.x, origins_difference_z = travel_relative_start.z - gantry_closer_end.z;
            float gantry_cross_point = calculate_2x2_determinant(origins_difference_x, -travel_vector.x, origins_difference_z, -travel_vector.z) / divider;
            if (gantry_cross_point < 0.0f || gantry_cross_point > 1.0f)
                return null;
            float travel_vector_cross_point = calculate_2x2_determinant(gantry_span.x, origins_difference_x, gantry_span.z, origins_difference_z) / divider;
            if (travel_vector_cross_point < 0.0f || travel_vector_cross_point > 1.0f)
                return null; 
            Vector3 braket_position = travel_relative_start + travel_vector * travel_vector_cross_point;
            braket_position.y = y;
            return braket_position;
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
            bool visible = is_visible;
            base_pole.is_visible = _further_pole.is_visible = is_visible = false;
            hide_when_out_of_view();
            base_pole.hide_when_out_of_view();
            _further_pole.hide_when_out_of_view();
            base_pole.orientation = _further_pole.orientation = orientation = new_orientation;
            reposition_further_pole();
            if (visible)
            {
                base_pole.reveal();
                _further_pole.reveal();
                reveal();
            }
        }
	}
}
