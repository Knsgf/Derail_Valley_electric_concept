using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WE6SIM.utilities;

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
