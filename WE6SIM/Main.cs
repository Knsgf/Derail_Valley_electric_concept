// Distributed under terms and conditions of MIT licence. See LICENCE_MIT.txt for details.

using System;
using System.Reflection;

using HarmonyLib;

using static UnityModManagerNet.UnityModManager;

using LocoSim.Implementations;

using electric_sim.catenary_editor;
using System.Diagnostics;

namespace electric_sim;

public static class Main
{
    // Unity Mod Manage Wiki: https://wiki.nexusmods.com/index.php/Category:Unity_Mod_Manager

    private static ModEntry.ModLogger? _logger;
    
    public static Port? diagnostics  { get; set; }
    public static Port? diagnostics2 { get; set; }
    public static ModEntry? mod_info { get; private set; }
    
    [Conditional("DEBUG")]
    public static void log(string message)
    {
        _logger?.Log(message);
    }

    // UMM's logger is not thread-safe!
    [Conditional("DEBUG")]
    public static void background_log(string message)
    {
        Console.WriteLine($"[Catenary-DC background] {message}");
    }

    public static void release_log(string message)
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
            log("Catenary-DC started");
            mod_info = mod;
            editor_settings.set_up(mod);
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
