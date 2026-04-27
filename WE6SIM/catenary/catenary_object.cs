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
    Vector3 get_relative_position();
    Quaternion get_orientation();
    void set_relative_position(Vector3 new_position);
}

internal partial class overhead_equipment
{
    [JsonObject]
    private class catenary_object: catenary_object_user
    {
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
        protected GameObject template { get; private set; }

        protected catenary_object(string template_name, int x, int z, float y, Quaternion orientation)
        {
            if (!system._templates.TryGetValue(template_name, out GameObject template))
                throw new ArgumentException($"{template_name} not defined");
            this.template    = template;
            this.orientation = orientation;
            this.x = x;
            this.z = z;
            this.y = y;
            if (Mathf.Abs(orientation.x) > 0.01f || Mathf.Abs(orientation.z) > 0.01f)
                Main.log($"x={x} z={z} ax={orientation.x} ay={orientation.y} az={orientation.z}");
        }

        public Vector3 get_relative_position() => world_position.get_relative_position(x, z, y);

        public Quaternion get_orientation() => orientation;

        public void set_relative_position(Vector3 new_position)
        {
            is_visible = false;
            hide_when_out_of_view();
            (x, z) = get_absolute_position(new_position);
            y = new_position.y;
            system.reconstruct_tree_after_moving_object(this);
            system.handle_scenery_visibility(PlayerManager.PlayerTransform.position);
        }

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

        public static Func<int, int, float, Quaternion, catenary_object> wrap_constructor(string template_name)
        {
            return (int x, int z, float y, Quaternion orientation) 
                => new catenary_object(template_name, x, z, y, orientation);
        }
    }
}