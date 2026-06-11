// Distributed under terms and conditions of CC0 licence. See LICENCE_CC0.txt for details.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WE6SIM.utilities;

internal static class log_stack
{
    public static void print()
    {
        StackTrace trace = new(fNeedFileInfo: true);
        Main.log(trace.ToString());
    }
}
