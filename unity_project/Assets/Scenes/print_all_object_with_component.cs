using System.Collections;
using System.Collections.Generic;

using CCL.Types.Proxies.Controls;

using UnityEngine;

public class print_all_object_with_component: MonoBehaviour
{
    // Start is called before the first frame update
    void Start()
    {
        var objects = FindObjectsOfType<LeverProxy>();
        foreach (var current_object in objects)
            print(current_object.name);
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
