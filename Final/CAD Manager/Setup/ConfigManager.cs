using System.IO;
using Newtonsoft.Json;

namespace Schema.Setup
{
    public static class ConfigManager
    {
        private static readonly string ConfigPath = @"C:\ProgramData\RK Tools\Schema\config.json";

        public static void Save(ConfigModel config)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath));
            var json = JsonConvert.SerializeObject(config, Formatting.Indented);
            File.WriteAllText(ConfigPath, json);
        }

        public static ConfigModel Load()
        {
            if (!File.Exists(ConfigPath))
                return new ConfigModel();

            var json = File.ReadAllText(ConfigPath);
            return JsonConvert.DeserializeObject<ConfigModel>(json) ?? new ConfigModel();
        }
    }
}
