namespace VRChatTranslator.Core.Services;

/// <summary>
/// Every folder and file the app keeps on disk, in one place. Nothing here is inside the install folder, so the portable
/// app can sit in a read-only location and an update never touches the user's settings or downloaded models.
/// </summary>
public static class AppPaths
{
    public const string CompanyFolder = "TheLevelEverythingCompany";
    public const string AppFolder = "VRChatUniversalTranslator";

    /// <summary>%LOCALAPPDATA%\TheLevelEverythingCompany\VRChatUniversalTranslator, or the folder in <paramref name="localAppData"/>.</summary>
    public static string GetDataRoot(string? localAppData = null) => Path.Combine(
        localAppData ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), CompanyFolder, AppFolder);

    public static string DataRoot => GetDataRoot();
    public static string ConfigPath => Path.Combine(DataRoot, "config.json");
    public static string ModelsDirectory => Path.Combine(DataRoot, "models");
    public static string LogsDirectory => Path.Combine(DataRoot, "logs");
    public static string SpeakerModelPath => Path.Combine(ModelsDirectory, "speaker-eres2net.onnx");

    /// <summary>The optional fast English recogniser's two files (model and token list) live here.</summary>
    public static string FastEnglishDirectory => Path.Combine(ModelsDirectory, "parakeet-110m-int8");
}
