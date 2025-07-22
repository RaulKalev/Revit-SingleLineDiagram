using System.Collections.Generic;
using System.Data.OleDb;
using System;

namespace Schema
{
    public static class AccessDetailMappingReader
    {
        private static readonly Dictionary<string, string> _mapping = new Dictionary<string, string>();
        private static bool _isLoaded = false;

        public static void Load(string dbPath)
        {
            if (_isLoaded) return;
            _isLoaded = true;

            try
            {
                if (string.IsNullOrEmpty(dbPath))
                {
                    dbPath = Environment.GetEnvironmentVariable("SCHEMA_DB_PATH");
                    if (string.IsNullOrEmpty(dbPath))
                    {
                        string username = Environment.UserName;
                        dbPath = $@"C:\Users\{username}\EULE Dropbox\Raul Kalev\Me\Plugins\Schema\DetailMappings.accdb";
                    }
                }

                if (!System.IO.File.Exists(dbPath))
                {
                    Autodesk.Revit.UI.TaskDialog.Show("Database Error", $"DetailMappings.accdb not found:\n{dbPath}");
                    return;
                }

                string connectionString = $@"Provider=Microsoft.ACE.OLEDB.12.0;Data Source={dbPath};Persist Security Info=False;";
                using (var connection = new OleDbConnection(connectionString))
                {
                    connection.Open();
                    var command = new OleDbCommand("SELECT FamilyName, TypeName, DetailFamily, DetailType FROM ATS", connection);
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string key = $"{reader["FamilyName"]} : {reader["TypeName"]}";
                            string value = $"{reader["DetailFamily"]} : {reader["DetailType"]}";
                            if (!_mapping.ContainsKey(key))
                                _mapping.Add(key, value);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Autodesk.Revit.UI.TaskDialog.Show("Database Error", $"Failed to load detail mappings:\n{ex.Message}");
            }
        }

        public static string GetDetailSymbol(string family, string type)
        {
            string key = $"{family} : {type}";
            return _mapping.TryGetValue(key, out var detail) ? detail : "EN_ATS-AspiratsiooniAndur : EN_ATS-AspiratsiooniAndur";
        }
    }
}
