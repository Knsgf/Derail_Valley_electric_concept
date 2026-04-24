// Distributed under terms and conditions of CC0 licence. See LICENCE_CC0.txt for details.

using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

using WE6SIM.catenary_editor;
using WE6SIM.utilities;
using static WE6SIM.utilities.world_position;

namespace WE6SIM.catenary;

interface catenary_object_user
{
    int template_index { get; }
    Vector3 get_relative_position();
    Quaternion get_orientation();
}

internal static partial class catenary_visual
{
    [JsonObject]
    private class catenary_object: catenary_object_user
    {
        [JsonIgnore]
        private int _template_index;

        [JsonIgnore]
        public GameObject? entity = null;
        [JsonIgnore]
        public bool is_visible = false, placed_procedurally = false;

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

        [JsonConstructor]
        public catenary_object(int template_index, int x, int z, float y, Quaternion orientation)
        {
            this.template_index = template_index;
            this.template       = _templates[template_index]; //  Stupid analyzer
            this.orientation    = orientation;
            this.x = x;
            this.z = z;
            this.y = y;
        }

        public Vector3 get_relative_position() => world_position.get_relative_position(x, z, y);

        public Quaternion get_orientation() => orientation;

        public virtual void reveal()
        {
            is_visible = true;
            entity   ??= GameObject.Instantiate(template, world_position.get_relative_position(x, z, y), orientation);
        }

        public void hide_when_out_of_view()
        {
            if (!is_visible && entity is not null)
            {
                GameObject.Destroy(entity);
                entity = null;
            }
        }

        public static Func<int, int, float, Quaternion, catenary_object> wrap_constructor(int template_index)
        {
            return (int x, int z, float y, Quaternion orientation) => new catenary_object(template_index, x, z, y, orientation);
        }
    }
}