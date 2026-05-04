// Distributed under terms and conditions of CC0 licence. See LICENCE_CC0.txt for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Newtonsoft.Json;

using UnityEngine;

using WE6SIM.utilities;

namespace WE6SIM.catenary;

interface wire_user: catenary_object_user
{
}

internal partial class overhead_equipment
{
    [JsonObject]
    private class wire: catenary_object, wire_user
    {
        const float default_section_length = 40.0f, default_side_rail_length = 10.0f, default_wire_height = 6.0f, end_anchor_dead_length = 4.0f;
        
        [JsonIgnore]
        private static readonly float y_scale = Mathf.Sqrt(2.0f);
        [JsonIgnore]
        private static readonly Quaternion secondary_vertical_orientation = Quaternion.Euler(45.0f, 0.0f, 0.0f);

        [JsonIgnore]
        private readonly Quaternion _primary_vertical_orientation;
        [JsonIgnore]
        private readonly Vector3 _primary_vertical_scale, _secondary_vertical_scale;
        [JsonIgnore]
        private readonly line_cross _pantograph_strip_intersection;
        [JsonIgnore]
        private readonly float _other_end_y;
        
        private GameObject? _primary_transform, _secondary_transform;

        [JsonProperty]
        public wire_kind wire_type;
        [JsonProperty]
        public float length;
        [JsonProperty]
        public float previous_pole_vertical_offset;
        [JsonProperty]
        public string substation;

        private static string wire_template(wire_kind wire_type)
        {
            return wire_type switch
            {
                wire_kind.plain_dual           => "WireDual",
                wire_kind.plain_single         => "WireSingle",
                wire_kind.middle_anchor_dual   => "WireMidpointAnchorDual",
                wire_kind.middle_anchor_single => "WireMidpointAnchorSingle",
                wire_kind.end_anchor_dual      => "WireDualEnd",
                wire_kind.end_anchor_single    => "WireSingleEnd",
                wire_kind.wall_anchor_single   => "WireSingleWallEnd",
                wire_kind.side_rail            => "SideRail",
                wire_kind.termination_rail     => "SideRailEnd",
                _ => throw new ArgumentOutOfRangeException($"Invalid wire type {wire_type}")
            };
        }
        
        [JsonConstructor]
        public wire(wire_kind wire_type, string substation, float length, float previous_pole_vertical_offset, 
            int x, int z, float y, Quaternion orientation): base(wire_template(wire_type), x, z, y, orientation)
        {
            assert.test(length > 0.0f);
            this.wire_type                     = wire_type;
            this.substation                    = substation;
            this.length                        = length;
            this.previous_pole_vertical_offset = previous_pole_vertical_offset;
            
            float shear_angle               = Mathf.Atan(previous_pole_vertical_offset / length);
            float primary_orientation_angle = Mathf.Deg2Rad * 45.0f + shear_angle / 2.0f;
            float template_section_length   = (wire_type is wire_kind.side_rail or wire_kind.termination_rail) 
                ? default_side_rail_length : default_section_length;
            _primary_vertical_orientation = Quaternion.Euler(Mathf.Rad2Deg * (-primary_orientation_angle), 0.0f, 0.0f);
            _primary_vertical_scale       = new Vector3(1.0f, Mathf.Cos(primary_orientation_angle), Mathf.Sin(primary_orientation_angle));
            _secondary_vertical_scale     = new Vector3(1.0f, y_scale, length / template_section_length * y_scale / Mathf.Cos(shear_angle));
            Main.log($"wire {shear_angle} {primary_orientation_angle} {_primary_vertical_scale} {_secondary_vertical_scale}");

            bool    end_achor     = wire_type is wire_kind.end_anchor_dual or wire_kind.end_anchor_single or wire_kind.wall_anchor_single;
            Vector3 wire_top_view = orientation * Vector3.forward * (end_achor ? (length - end_anchor_dead_length) : length);
            _pantograph_strip_intersection = new line_cross(x, z, 
                x + world_position.float_to_fixed(wire_top_view.x), z + world_position.float_to_fixed(wire_top_view.z), 0.01f);
            _other_end_y = y + previous_pole_vertical_offset;
            Main.log($"WTV = {wire_top_view} h0 = {y} h1 = {_other_end_y}");
        }

        public override void reveal()
		{
            is_visible = true;
            if (entity is null)
            {
                entity = new GameObject("Wire");
                Transform entity_location = entity.transform;
                entity_location.position  = get_relative_position();
                entity_location.rotation  = orientation;

                _primary_transform = new GameObject("Shear A");
                Transform primary_location = _primary_transform.transform;
                primary_location.SetParent(entity_location, worldPositionStays: false);
                primary_location.localRotation = _primary_vertical_orientation;
                primary_location.localScale    = _primary_vertical_scale;

                _secondary_transform = GameObject.Instantiate(template, primary_location);
                Transform secondary_location     = _secondary_transform.transform;
                secondary_location.localRotation = secondary_vertical_orientation;
                secondary_location.localScale    = _secondary_vertical_scale;
            }
		}

		public override void hide_when_out_of_view()
		{
			if (!is_visible && entity is not null)
            {
                assert.test(_primary_transform is not null && _secondary_transform is not null);
                GameObject.Destroy(_secondary_transform);
                GameObject.Destroy(  _primary_transform);
                GameObject.Destroy(              entity);
                entity = _primary_transform = _secondary_transform = null;
            }
		}

        public float? contact_height(int strip_end1_x, int strip_end1_z, int strip_end2_x, int strip_end2_z, 
            float pantograph_base_y)
        {
            float? intersection_parameter = _pantograph_strip_intersection.crossed_line_parameter(strip_end1_x, strip_end1_z,
                                                                                                  strip_end2_x, strip_end2_z);
            if (intersection_parameter == null)
                return null;
            float contact_height = Mathf.LerpUnclamped(y, _other_end_y, (float) intersection_parameter) + default_wire_height;
            return (contact_height > pantograph_base_y) ? contact_height : null;
        }
    }
}
