using System.IO;
using Newtonsoft.Json;
using Autodesk.Revit.UI; // Add for TaskDialog

namespace Schema.Setup
{
    public static class ConfigManager
    {
        private static readonly string ConfigPath = @"C:\ProgramData\RK Tools\Schema\config.json";

        public static void Save(ConfigModel config)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath));
                var json = JsonConvert.SerializeObject(config, Formatting.Indented);
                File.WriteAllText(ConfigPath, json);
            }
            catch (IOException ex)
            {
                TaskDialog.Show("Config Save Error", $"Could not save config file:\n{ex.Message}");
            }
            catch (System.Exception ex)
            {
                TaskDialog.Show("Config Save Error", $"Unexpected error:\n{ex.Message}");
            }
        }

        public static ConfigModel Load()
        {
            try
            {
                if (!File.Exists(ConfigPath))
                    return new ConfigModel();

                var json = File.ReadAllText(ConfigPath);
                return JsonConvert.DeserializeObject<ConfigModel>(json) ?? new ConfigModel();
            }
            catch (IOException ex)
            {
                TaskDialog.Show("Config Load Error", $"Could not load config file:\n{ex.Message}");
                return new ConfigModel();
            }
            catch (System.Exception ex)
            {
                TaskDialog.Show("Config Load Error", $"Unexpected error:\n{ex.Message}");
                return new ConfigModel();
            }
        }
    }
}
