// Distributed under terms and conditions of CC0 licence. See LICENCE_CC0.txt for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace electric_sim.devices;

internal class throttle_HUD(Action<float> HUD_setter)
{
    private Action<float> HUD_setter = HUD_setter;
    private int _last_primary = -1, _last_secondary = -1;

    private int skip_T_notch(int notch) => (notch <= 3) ? notch : (notch - 1);
    
    public void update(int primary_notch, int secodary_notch)
    {
        if (primary_notch != _last_primary || secodary_notch != _last_secondary)
        {
            _last_primary   = primary_notch;
            _last_secondary = secodary_notch;
            HUD_setter(skip_T_notch(primary_notch) * 10 + skip_T_notch(secodary_notch));
        }
    }
}
