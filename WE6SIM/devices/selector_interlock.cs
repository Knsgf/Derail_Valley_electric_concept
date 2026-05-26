// Distributed under terms and conditions of CC0 licence. See LICENCE_CC0.txt for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WE6SIM.devices;

internal class selector_interlock(Action<float> unit_selector_handler)
{
    private readonly Action<float> unit_selector_handler = unit_selector_handler;

    public void interlocked_handler(float raw_selector)
    {
        unit_selector_handler(raw_selector);
    }
}
