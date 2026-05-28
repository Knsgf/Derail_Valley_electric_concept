// Distributed under terms and conditions of CC0 licence. See LICENCE_CC0.txt for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using UnityEngine;

using static UnityEngine.EventSystems.EventTrigger;

namespace WE6SIM.utilities;

internal static class hierarchy_traversal
{
    public static IEnumerable<GameObject> AllChildren(this GameObject top)
    {
        foreach (Transform child in top.transform)
        {
            GameObject child_object = child.gameObject;
            yield return child_object;
            foreach (GameObject deep_child in child_object.AllChildren())
                yield return deep_child;
        }
    }
}
