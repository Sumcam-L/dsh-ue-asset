using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace DshUeAsset.Core.Config;

public sealed class ConfigStore
{
    private static readonly JsonSerializerSettings Settings = new()
    {
        Formatting = Formatting.Indented,
        NullValueHandling = NullValueHandling.Ignore,
        MissingMemberHandling = MissingMemberHandling.Ignore,
        ObjectCreationHandling = ObjectCreationHandling.Replace,
        ContractResolver = new CamelCasePropertyNamesContractResolver()
    };

    public ConfigStore(string? path = null)
    {
        Path = string.IsNullOrWhiteSpace(path) ? AppConfig.DefaultConfigPath() : System.IO.Path.GetFullPath(path);
    }

    public string Path { get; }

    public AppConfig Load()
    {
        AppConfig config;
        if (File.Exists(Path))
        {
            var json = File.ReadAllText(Path);
            config = JsonConvert.DeserializeObject<AppConfig>(json, Settings) ?? new AppConfig();
        }
        else
        {
            config = new AppConfig();
        }

        config.Profiles ??= new Dictionary<string, GameProfile>(StringComparer.OrdinalIgnoreCase);
        config.Limits ??= new Limits();
        config.UiState ??= new UiState();
        config.OutputRoot ??= AppConfig.DefaultOutputRoot();
        if (string.IsNullOrWhiteSpace(config.DefaultMeshFormat)) config.DefaultMeshFormat = "UEFormat";
        return config;
    }

    public void Save(AppConfig config)
    {
        var dir = System.IO.Path.GetDirectoryName(Path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var temp = Path + ".tmp";
        File.WriteAllText(temp, JsonConvert.SerializeObject(config, Settings));
        if (File.Exists(Path)) File.Replace(temp, Path, null);
        else File.Move(temp, Path);
    }
}