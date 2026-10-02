// Distributed under terms and conditions of CC0 licence. See LICENCE_CC0.txt for details.

using System;
using System.Collections.Generic;

using Newtonsoft.Json;
using UnityEngine;

using electric_sim.utilities;

namespace electric_sim.catenary;

interface side_rail_pole_user: pole_user, cantilever_user
{

}

public partial class overhead_equipment
{
    private struct side_pole_template: catenary_object_template
    {
        public readonly string template_name => template;
        public readonly string asset_path    => asset;
        
        [CSV_column(0)]
        public string template;
        [CSV_column(1)]
        public string asset;
        [CSV_column(2)]
        public string kind;
        [CSV_column(3)]
        public float offset;
        [CSV_column(4)]
        public float conductor_offset;
    }
    
    [JsonObject]
    private class side_rail_pole: catenary_object, side_rail_pole_user
    {
        const string default_kind = "SideRail";

        private static readonly Dictionary<string, side_pole_template> _side_pole_templates = [];
        
        private readonly Vector3 _pole_offset, _conductor_attachment_point_offset;
        
        [JsonIgnore]
        public bool cantilever_on_near_side 
        { 
            get => true; 
            set 
            {} 
        }
        [JsonIgnore]
        public bool cantilever_on_far_side
        { 
            get => true; 
            set 
            {} 
        }
        [JsonIgnore]
        public bool anchored
        { 
            get => false; 
            set 
            {} 
        }
        [JsonIgnore]
        public bool dual_wire => false;
        [JsonIgnore]
        public bool siding_anchor
        {
            get => false;
            set
            {}
        }
        
        [JsonProperty]
        public string kind          { get; private set; }
        [JsonProperty]
        public bool   wire_attached { get;         set; }

        [JsonIgnore]
        public float offset { get; private set; }
        [JsonIgnore]
        public bool is_ground   => true;
        [JsonIgnore]
        public bool is_tunnel   => false;
        [JsonIgnore]
        public bool is_bridge   => false;
        [JsonIgnore]
        public bool is_bracket  => false;
        [JsonIgnore]
        public bool is_siderail => true;

        public static void set_up_templates(CSV_struct<side_pole_template> templates)
        {
            for (int row = templates.row_count; row > 0; --row)
            {
                side_pole_template side_pole_definition         = templates.get_row(row);
                _side_pole_templates[side_pole_definition.kind] = side_pole_definition;
            }
        }

        private static string get_template(string? kind)
        {
            kind ??= default_kind;
            if (!_side_pole_templates.TryGetValue(kind, out side_pole_template definition))
                throw new ArgumentException($"Unknown side pole kind {kind}");
            return definition.template_name;
        }
        
        public side_rail_pole(string? kind, int x, int z, float y, Quaternion orientation)
            : base(get_template(kind), x, z, y, orientation)
        {
            kind    ??= default_kind;
            this.kind = kind;
            side_pole_template definition      = _side_pole_templates[kind];
            Vector3            pole_direction  = orientation    * Vector3.right;
            _pole_offset                       = pole_direction * definition.offset;
            _conductor_attachment_point_offset = pole_direction * definition.conductor_offset;
            offset = definition.offset;
        }

        public Vector3 get_pole_true_position()         => get_relative_position() + _pole_offset;
        
        public Vector3 relative_wire_attachment_point() => get_relative_position() + _conductor_attachment_point_offset;
        
        public (string?, string?) matching_cantilever(cantilever_kind direction) => (null, null);
    }
}
