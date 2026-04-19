// Distributed under terms and conditions of CC0 licence. See LICENCE_CC0.txt for details.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WE6SIM.utilities;

namespace WE6SIM.catenary;

internal static partial class catenary_visual
{
    private class quad_tree
    {
        const int node_objects_limit = 64;

        private class tree_node
        {
            public tree_node[]?         quadrants         = null;
            public catenary_object?      division_object   = null;
            public List<catenary_object> remaining_objects = [];
        }

        private readonly tree_node _root = new();

        private void divide_node(tree_node node)
        {
            List<catenary_object> remaining_objects       = node.remaining_objects;
            int                  remaining_objects_count = remaining_objects.Count;

            if (remaining_objects_count <= node_objects_limit)
                return;
            long sum_x = 0, sum_z = 0;
            foreach (catenary_object current_object in remaining_objects)
            {
                sum_x += current_object.x;
                sum_z += current_object.z;
            }
            int average_x = (int) (sum_x / remaining_objects_count);
            int average_z = (int) (sum_z / remaining_objects_count);

            int minimum_offset = int.MaxValue, closest_object = -1;
            for (int object_index = remaining_objects_count - 1; object_index >= 0; --object_index)
            {
                int offset_from_average = Math.Abs(remaining_objects[object_index].x - average_x)
                                        + Math.Abs(remaining_objects[object_index].z - average_z);
                if (minimum_offset > offset_from_average)
                {
                    minimum_offset = offset_from_average;
                    closest_object = object_index;
                }
            }
            catenary_object division_object = remaining_objects[closest_object];
            node.division_object           = division_object;
            remaining_objects.FastRemoveAt(closest_object);

            tree_node           [] quadrants      = node.quadrants = new tree_node[4];
            List<catenary_object>[] quadrant_lists = new List<catenary_object>[4];
            for (int quadrant_index = 3; quadrant_index >= 0; --quadrant_index)
            {
                quadrants     [quadrant_index] = new();
                quadrant_lists[quadrant_index] = quadrants[quadrant_index].remaining_objects;
            }
            foreach (catenary_object current_object in remaining_objects)
            {
                int quadrant_index = 0;
                if (current_object.x > division_object.x)
                    ++quadrant_index;
                if (current_object.z > division_object.z)
                    quadrant_index += 2;
                quadrant_lists[quadrant_index].Add(current_object);
            }
            remaining_objects.Clear();

            for (int quadrant_index = 3; quadrant_index >= 0; --quadrant_index)
                divide_node(quadrants[quadrant_index]);
        }

        public quad_tree(List<catenary_object> objects)
        {
            _root.remaining_objects.AddRange(objects);
            divide_node(_root);
        }

        private void search_node(tree_node current_node, List<catenary_object> found_objects, bool do_bounds_check, 
            int left, int top, int right, int bottom)
        {
            if (!do_bounds_check)
            {
                foreach (catenary_object current_object in current_node.remaining_objects)
                {
                    found_objects.Add(current_object);
                }
            }
            else
            {
                foreach (catenary_object current_object in current_node.remaining_objects)
                {
                    if (current_object.x >= left && current_object.x <= right && current_object.z >= top && current_object.z <= bottom)
                    {
                        found_objects.Add(current_object);
                    }
                }
            }

            tree_node[]?    quadrants       = current_node.quadrants;
            catenary_object? division_object = current_node.division_object;
            if (division_object != null && quadrants != null)
            {
                if (   division_object.x >= left && division_object.x <= right
                    && division_object.z >=  top && division_object.z <= bottom)
                {
                    //division_object.is_visible = true;
                    found_objects.Add(division_object);
                }
                if (left <= division_object.x)
                {
                    if (top <= division_object.z)
                        search_node(quadrants[0], found_objects, do_bounds_check, left, top, right, bottom);
                    if (bottom > division_object.z)
                        search_node(quadrants[2], found_objects, do_bounds_check, left, top, right, bottom);
                }
                if (right > division_object.x)
                {
                    if (top <= division_object.z)
                        search_node(quadrants[1], found_objects, do_bounds_check, left, top, right, bottom);
                    if (bottom > division_object.z)
                        search_node(quadrants[3], found_objects, do_bounds_check, left, top, right, bottom);
                }
            }
        }

        public void find_objects(List<catenary_object> found_objects, bool do_bounds_check, int left, int top, int right, int bottom)
        {
            search_node(_root, found_objects, do_bounds_check, left, top, right, bottom);
        }
    }
}