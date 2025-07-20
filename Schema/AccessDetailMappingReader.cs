using System.Collections.Generic;
using System.Data.OleDb;

namespace Schema.Helpers
{
    public static class AccessDetailMappingReader
    {
        private static readonly Dictionary<string, string> _mapping = new Dictionary<string, string>();
        private static bool _isLoaded = false;

        public static void Load(string dbPath = null)
        {
            if (_isLoaded) return;
            _isLoaded = true;

            if (string.IsNullOrEmpty(dbPath))
            {
                string assemblyDir = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                dbPath = System.IO.Path.Combine(assemblyDir ?? string.Empty, "DetailMappings.accdb");
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

        public static string GetDetailSymbol(string family, string type)
        {
            string key = $"{family} : {type}";
            return _mapping.TryGetValue(key, out var detail) ? detail : "EN_ATS-AspiratsiooniAndur : EN_ATS-AspiratsiooniAndur";
        }
    }
}
