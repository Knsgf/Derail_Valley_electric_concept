using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

using WE6SIM.catenary_editor;

namespace WE6SIM.catenary;

interface pole_user: catenary_object_user
{
    overhead_equipment.pole_kind pole_type { get; }
    bool gantry_attached         { get; set; }
    bool cantilever_on_near_side { get; set; }
    bool cantilever_on_far_side  { get; set; }
}

internal partial class overhead_equipment
{
    [JsonObject]
    private class pole: catenary_object, pole_user
    {
        [JsonProperty]
        public pole_kind pole_type { get; private set; }
        [JsonProperty]
        public bool gantry_attached         { get; set; }
        [JsonProperty]
        public bool cantilever_on_near_side { get; set; }
        [JsonProperty]
        public bool cantilever_on_far_side  { get; set; }

        private static string pole_template(pole_kind pole_type)
        {
            return pole_type switch
            {
                pole_kind.Ground  => "Pole",
                pole_kind.Bridge  => "Pole",
                pole_kind.Tunnel  => "Pole",
                pole_kind.Bracket => "RegistrationBracket",
                _ => throw new ArgumentOutOfRangeException($"Unknown pole type {pole_type}")
            };
        }

        [JsonConstructor]
        public pole(pole_kind pole_type, int x, int z, float y, Quaternion orientation): base(pole_template(pole_type), x, z, y, orientation)
        {
            this.pole_type = pole_type;
            if (pole_type == pole_kind.Ground)
            {
                catenary_object foundation     = system.add_scenery_object(wrap_constructor("PoleFoundation"), x, z, y, orientation);
                foundation.placed_procedurally = true;
            }
        }
    }
}
