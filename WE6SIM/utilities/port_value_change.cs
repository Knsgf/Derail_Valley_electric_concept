// Distributed under terms and conditions of CC0 licence. See LICENCE_CC0.txt for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

using LocoSim.Implementations;

namespace WE6SIM.utilities;

internal struct port_value_change
{
	public class port_value_watcher: INotifyCompletion
	{
		private readonly Port _watched_port;

		private Action? _resuming_code;

		public bool IsCompleted { get; private set; }

		private void port_value_changed(float _)
		{
			_watched_port.ValueUpdatedInternally -= port_value_changed;
			IsCompleted = true;
			_resuming_code?.Invoke();
		}

		public port_value_watcher(Port watched_port)
		{
			_watched_port = watched_port;
			watched_port.ValueUpdatedInternally += port_value_changed;
			IsCompleted = false;
		}

		public void OnCompleted(Action resuming_code)
		{
			_resuming_code = resuming_code;
		}

		public void GetResult()
		{}
	}

	private readonly Port _port_to_watch;

	private port_value_change(Port port_to_watch)
	{
		_port_to_watch = port_to_watch;
	}

	public readonly port_value_watcher GetAwaiter() => new(_port_to_watch);

	public static port_value_change watch(Port port_to_watch) => new(port_to_watch);
}
