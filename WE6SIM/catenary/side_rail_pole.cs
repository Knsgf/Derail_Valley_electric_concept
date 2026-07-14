// Distributed under terms and conditions of CC0 licence. See LICENCE_CC0.txt for details.

using Newtonsoft.Json;
using UnityEngine;

namespace electric_sim.catenary;

interface side_rail_pole_user: pole_user, cantilever_user
{

}

internal partial class overhead_equipment
{
    [JsonObject]
    private class side_rail_pole: catenary_object, side_rail_pole_user
    {
        const float side_rail_pole_offset = 2.304f;

        private readonly Vector3 _rail_attachment_point_offset;
        
        [JsonIgnore]
        public pole_kind pole_type => pole_kind.SideRail;
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
        public bool wire_attached { get; set; }

        public side_rail_pole(int x, int z, float y, Quaternion orientation): base("SideRailPole", x, z, y, orientation)
        {
            _rail_attachment_point_offset = orientation * Vector3.right * side_rail_pole_offset;
        }

        public Vector3 get_pole_true_position() => get_relative_position() + _rail_attachment_point_offset;
        
        public Vector3 relative_wire_attachment_point() => get_pole_true_position();
    }
}
