using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using LocoSim.Implementations;

namespace WE6SIM;

internal class electric_device: IDisposable
{
	private readonly string _device_name;

	private Fuse? _fuse1, _fuse2;

	protected bool disposed { get; private set; } = false;
	protected bool is_powered { get; private set; } = false;

	protected event Action<bool>? power_supply_toggled;

	private void power_status_checker(bool _ = false)
	{
		if (disposed)
			return;
		bool power_toggled_on = (_fuse1 != null && _fuse1.State) && (_fuse2 == null || _fuse2.State);
		if (is_powered != power_toggled_on)
		{
			is_powered = power_toggled_on;
			power_supply_toggled?.Invoke(power_toggled_on);
		}
	}

	private void set_up_fuse(ref Fuse? own_fuse, Fuse? supplied_fuse)
	{
		if (own_fuse == null)
		{
			own_fuse = supplied_fuse;
			own_fuse?.StateUpdated += power_status_checker;
		}
	}

	protected electric_device(string device_name, Fuse? fuse1 = null, Fuse? fuse2 = null)
	{
		_device_name = device_name;
		set_up_fuses(fuse1, fuse2);
	}

	protected void set_up_fuses(Fuse? fuse1, Fuse? fuse2 = null)
	{
		set_up_fuse(ref _fuse1, fuse1);
		set_up_fuse(ref _fuse2, fuse2);
		power_status_checker();
	}

	protected void check_if_disposed()
	{
		if (disposed)
			throw new ObjectDisposedException($"Attempt to use {_device_name} that has been disposed");
	}

	~electric_device()
	{
		Main.log($"{_device_name} has not been disposed properly");
	}

	public virtual void Dispose()
	{
		if (!disposed)
		{
			disposed = true;
			GC.SuppressFinalize(this);
			_fuse1?.StateUpdated -= power_status_checker;
			_fuse2?.StateUpdated -= power_status_checker;
		}
	}
}
