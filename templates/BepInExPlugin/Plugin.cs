using BepInEx;
using BepInEx.Logging;

namespace __PLUGIN_NAMESPACE__;

[BepInPlugin("__PLUGIN_ID__", "__PLUGIN_NAME__", "__PLUGIN_VERSION__")]
public sealed class __PLUGIN_CLASS__ : BaseUnityPlugin {
    static ManualLogSource? log;

    void Awake() {
        log = Logger;
        log.LogInfo("Plugin loaded");
    }
}
