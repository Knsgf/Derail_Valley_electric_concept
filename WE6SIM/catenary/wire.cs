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
    int substation_index { get; }
}

internal partial class overhead_equipment
{
    [JsonObject]
    private class wire: catenary_object, wire_user
    {
        const float default_section_length = 40.0f;
        
        [JsonIgnore]
        private static readonly float y_scale = Mathf.Sqrt(2.0f);
        [JsonIgnore]
        private static readonly Quaternion secondary_vertical_orientation = Quaternion.Euler(45.0f, 0.0f, 0.0f);

        [JsonIgnore]
        private readonly Quaternion _primary_vertical_orientation;
        [JsonIgnore]
        private readonly Vector3 _primary_vertical_scale, _secondary_vertical_scale;
        
        private GameObject? _primary_transform, _secondary_transform;

        [JsonProperty]
        public wire_kind wire_type { get; private set; }
        public int substation_index { get; private set; }
        [JsonProperty]
        public float length { get; private set; }
        [JsonProperty]
        public float previous_pole_vertical_offset { get; private set; }

        private static string wire_template(wire_kind wire_type)
        {
            return wire_type switch
            {
                wire_kind.plain_dual        => "WireDual",
                wire_kind.plain_single      => "WireSingle",
                wire_kind.end_anchor_dual   => "WireDualEnd",
                wire_kind.end_anchor_single => "WireSingleEnd",
                _ => throw new ArgumentOutOfRangeException($"Invalid wire type {wire_type}")
            };
        }
        
        [JsonConstructor]
        public wire(wire_kind wire_type, int substation_index, float length, float previous_pole_vertical_offset, 
            int x, int z, float y, Quaternion orientation): base(wire_template(wire_type), x, z, y, orientation)
        {
            assert.test(length > 0.0f);
            this.wire_type                     = wire_type;
            this.substation_index              = substation_index;
            this.length                        = length;
            this.previous_pole_vertical_offset = previous_pole_vertical_offset;
            
            float shear_angle               = Mathf.Atan(previous_pole_vertical_offset / length);
            float primary_orientation_angle = Mathf.Deg2Rad * 45.0f + shear_angle / 2.0f;
            _primary_vertical_orientation   = Quaternion.Euler(Mathf.Rad2Deg * (-primary_orientation_angle), 0.0f, 0.0f);
            _primary_vertical_scale         = new Vector3(1.0f, Mathf.Cos(primary_orientation_angle), Mathf.Sin(primary_orientation_angle));
            _secondary_vertical_scale       = new Vector3(1.0f, y_scale, length / default_section_length * y_scale / Mathf.Cos(shear_angle));
            Main.log($"wire {shear_angle} {primary_orientation_angle} {_primary_vertical_scale} {_secondary_vertical_scale}");
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
    }
}
