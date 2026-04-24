namespace VirtualFaceTracking.Module.Runtime;

public sealed class ModulePaths
{
    public string CustomLibsRoot { get; init; } = string.Empty;
    public string DeploymentDirectory { get; init; } = string.Empty;
    public string GuiExePath { get; init; } = string.Empty;
    public string DefaultsPath { get; init; } = string.Empty;
    public string StatePath { get; init; } = string.Empty;
    public string LogPath { get; init; } = string.Empty;
    public string PipeName { get; init; } = string.Empty;

    public static ModulePaths Resolve()
    {
        var customLibsRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "VRCFaceTracking",
            "CustomLibs");
        var deploymentDirectory = Path.Combine(customLibsRoot, "VirtualFaceTracking");

        return new ModulePaths
        {
            CustomLibsRoot = customLibsRoot,
            DeploymentDirectory = deploymentDirectory,
            GuiExePath = Path.Combine(deploymentDirectory, "VirtualFaceTracking.Gui.exe"),
            DefaultsPath = Path.Combine(deploymentDirectory, "virtual-tracker.defaults.json"),
            StatePath = Path.Combine(deploymentDirectory, "virtual-tracker.state.json"),
            LogPath = Path.Combine(deploymentDirectory, "virtual-tracker.log"),
            PipeName = Shared.IPC.PipeProtocol.DefaultPipeName
        };
    }
}
