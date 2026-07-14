// Distributed under terms and conditions of CC0 licence. See LICENCE_CC0.txt for details.

using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace electric_sim.utilities;

internal static class assert
{
    [Conditional("DEBUG")]
    public static void test([DoesNotReturnIf(false)] bool passed)
    {
        if (!passed)
            throw new assertion_failed_exception();
    }
}

internal class assertion_failed_exception: Exception
{
    public assertion_failed_exception(): base()
    {}

    public assertion_failed_exception(string message): base(message)
    {}
}
