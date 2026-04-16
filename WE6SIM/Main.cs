// Distributed under terms and conditions of MIT licence. See LICENCE_MIT.txt for details.

using System;
using System.IO;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityModManagerNet;
using static UnityModManagerNet.UnityModManager;

using LocoSim.Implementations;
using WE6SIM.catenary_editor;

namespace WE6SIM;

public static class Main
{
	// Unity Mod Manage Wiki: https://wiki.nexusmods.com/index.php/Category:Unity_Mod_Manager

	private static ModEntry.ModLogger? _logger;
	private static test_settings?      _settings;
	
	public static Port? diagnostics { get; set; }
	public static Port? diagnostics2 { get; set; }
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

			editor.load_assets(mod);
			_settings = test_settings.Load<test_settings>(mod);
			mod.OnGUI = show_test_configuration;

			log("WE6SIM started");
		}
		catch (Exception ex)
		{
			_logger.LogException($"Failed to load {mod.Info.DisplayName}:", ex);
			code_injector?.UnpatchAll(mod.Info.Id);
			return false;
		}

		return true;
	}

	private static void show_test_configuration(ModEntry mod)
	{
		_settings.Draw(mod);
	}
}
