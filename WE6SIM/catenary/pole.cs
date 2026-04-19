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
    catenary_visual.pole_kind pole_type { get; }
}

internal static partial class catenary_visual
{
    [JsonObject]
    private class pole: catenary_object, pole_user
    {
        [JsonProperty]
        public pole_kind pole_type { get; private set; }

        [JsonConstructor]
        public pole(pole_kind pole_type, int x, int z, float y, Quaternion orientation): base(10, x, z, y, orientation)
        {
            this.pole_type = pole_type;
            if (pole_type == pole_kind.Ground)
            {
                catenary_object foundation = add_scenery_object(wrap_constructor(11), x, z, y, orientation);
                foundation.do_not_store    = true;
            }
        }
    }
}
