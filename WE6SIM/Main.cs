// Distributed under terms and conditions of MIT licence. See LICENCE_MIT.txt for details.

using System;
using System.IO;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityModManagerNet;
using static UnityModManagerNet.UnityModManager;

using LocoSim.Implementations;
using WE6SIM.catenary;
using WE6SIM.catenary_editor;

namespace WE6SIM;

public static class Main
{
	// Unity Mod Manage Wiki: https://wiki.nexusmods.com/index.php/Category:Unity_Mod_Manager

	private static ModEntry.ModLogger? _logger;
	
	public static Port? diagnostics  { get; set; }
	public static Port? diagnostics2 { get; set; }
	public static ModEntry? mod_info { get; private set; }
	
	[Obsolete]
	public static float pole_height_offset { get; set; }

	public static void log(string message)
	{
		_logger?.Log(message);
	}

	public static bool Load(ModEntry mod)
	{
		Harmony? code_injector = null;

		_logger = mod.Logger;
		try
		{
			code_injector = new Harmony(mod.Info.Id);
			code_injector.PatchAll(Assembly.GetExecutingAssembly());

			// Other plugin startup logic
			editor.set_up(mod);
			log("WE6SIM started");
			mod_info = mod;
		}
		catch (Exception ex)
		{
			_logger.LogException($"Failed to load {mod.Info.DisplayName}:", ex);
			code_injector?.UnpatchAll(mod.Info.Id);
			return false;
		}

		return true;
	}
}
