// Distributed under terms and conditions of CC0 licence. See LICENCE_CC0.txt for details.

using System;
using System.Collections.Generic;

namespace electric_sim.utilities;

internal static class list_extension
{
    public static void FastRemoveAt<_type_>(this List<_type_> list, int index)
    {
        int last_element = list.Count - 1;
        if (last_element < index || index < 0)
            throw new IndexOutOfRangeException($"List index {index} out of range 0..{last_element}");
        list[index] = list[last_element];
        list.RemoveAt(last_element);
    }
}
