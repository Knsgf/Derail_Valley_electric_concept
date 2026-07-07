// Distributed under terms and conditions of CC0 licence. See LICENCE_CC0.txt for details.

using System.Diagnostics;

namespace WE6SIM.utilities;

internal static class log_stack
{
    public static void print()
    {
        StackTrace trace = new(fNeedFileInfo: true);
        Main.log(trace.ToString());
    }
}
