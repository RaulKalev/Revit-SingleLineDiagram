using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;

namespace Schema.Helpers
{
    public static class DetailMappingManager
    {
        private static string _mappingsFilePath;
        private static Dictionary<string, string> _mappings;

        private static string GetFilePath()
        {
            if (_mappingsFilePath == null)
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string folder = Path.Combine(appData, "Schema");
                if (!Directory.Exists(folder))
                    Directory.CreateDirectory(folder);
                _mappingsFilePath = Path.Combine(folder, "DetailMappings.json");
            }
            return _mappingsFilePath;
        }

        public static void Load()
        {
            try
            {
                string path = GetFilePath();
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    _mappings = JsonConvert.DeserializeObject<Dictionary<string, string>>(json) 
                                ?? new Dictionary<string, string>();
                }
                else
                {
                    _mappings = new Dictionary<string, string>();
                }
            }
            catch
            {
                _mappings = new Dictionary<string, string>();
            }
        }

        public static void SaveMapping(string family, string type, string detailSymbolName)
        {
            try 
            {
                // Debug Log
                string debugPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "schema_debug.txt");
                File.AppendAllText(debugPath, $"SaveMapping called for {family}:{type} -> {detailSymbolName}\n");

                if (_mappings == null) Load();

                string key = $"{family}:{type}";
                
                if (_mappings.ContainsKey(key))
                    _mappings[key] = detailSymbolName;
                else
                    _mappings.Add(key, detailSymbolName);

                SaveToFile();
            }
            catch (Exception ex)
            {
                 string debugPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "schema_debug.txt");
                 File.AppendAllText(debugPath, $"Error in SaveMapping: {ex.Message}\n");
            }
        }

        public static string GetMappedSymbol(string family, string type)
        {
            if (_mappings == null) Load();

            string key = $"{family}:{type}";
            if (_mappings.TryGetValue(key, out string symbol))
                return symbol;

            return null;
        }

        private static void SaveToFile()
        {
            try
            {
                string json = JsonConvert.SerializeObject(_mappings, Formatting.Indented);
                File.WriteAllText(GetFilePath(), json);
                
                string debugPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "schema_debug.txt");
                File.AppendAllText(debugPath, $"Successfully saved to {GetFilePath()}\n");
            }
            catch (Exception ex)
            {
                string debugPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "schema_debug.txt");
                File.AppendAllText(debugPath, $"Failed to save mappings to file: {ex.Message}\n");
            }
        }
    }
}
