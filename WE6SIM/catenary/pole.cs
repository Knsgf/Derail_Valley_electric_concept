// Distributed under terms and conditions of CC0 licence. See LICENCE_CC0.txt for details.

using System;

using Newtonsoft.Json;
using UnityEngine;

namespace electric_sim.catenary;

interface pole_user: catenary_object_user
{
    overhead_equipment.pole_kind pole_type { get; }
    bool cantilever_on_near_side { get; set; }
    bool cantilever_on_far_side  { get; set; }
    bool anchored                { get; set; }
    bool siding_anchor           { get; set; }
    Vector3 get_pole_true_position();
}

internal partial class overhead_equipment
{
    [JsonObject]
    private class pole: catenary_object, pole_user
    {
        [JsonIgnore]
        public bool erased = false;
        
        [JsonProperty]
        public pole_kind pole_type { get; private set; }
        [JsonProperty]
        public bool cantilever_on_near_side { get; set; }
        [JsonProperty]
        public bool cantilever_on_far_side  { get; set; }
        [JsonProperty]
        public bool anchored { get; set; }
        [JsonProperty]
        public bool siding_anchor { get; set; }

        private static string pole_template(pole_kind pole_type)
        {
            return pole_type switch
            {
                pole_kind.Ground       => "Pole",
                pole_kind.Bridge       => "BridgePortal",
                pole_kind.Tunnel       => "TunnelPole",
                pole_kind.Bracket      => "RegistrationBracket",
                pole_kind.TrussBracket => "TrussGantryBracket",
                _ => throw new ArgumentOutOfRangeException($"Unknown pole type {pole_type}")
            };
        }

        [JsonConstructor]
        public pole(pole_kind pole_type, bool is_siding_anchor_pole, int x, int z, float y, Quaternion orientation): base(pole_template(pole_type), x, z, y, orientation)
        {
            this.pole_type     = pole_type;
            this.siding_anchor = is_siding_anchor_pole;
            if (pole_type == pole_kind.Ground)
            {
                catenary_object foundation     = system.add_scenery_object(miscellaneous_object.build_generic("PoleFoundation"), x, z, y, orientation);
                foundation.placed_procedurally = true;
            }
        }

        public Vector3 get_pole_true_position()
        { 
            return get_relative_position() + orientation 
                * ((pole_type is pole_kind.Bracket or pole_kind.TrussBracket) ? Vector3.left : Vector3.right) 
                * default_pole_offset;
        }

        public void sink_pole(float height_change)
        {
            y -= height_change;
        }
    }
}
