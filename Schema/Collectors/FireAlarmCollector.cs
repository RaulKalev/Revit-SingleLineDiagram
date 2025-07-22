using Autodesk.Revit.DB;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using Schema.ExternalEvents;
using Schema.Helpers;

namespace Schema.Collectors
{
    public class FireAlarmEntry : INotifyPropertyChanged
    {
        public Element Element { get; set; }
        public string FamilyName { get; set; }
        public string TypeName { get; set; }
        public int Count { get; set; }
        public string LevelName { get; set; }
        public bool IsSelected { get; set; } = true;
        private int _sectionNumber = 1;
        public int SectionNumber
        {
            get => _sectionNumber;
            set
            {
                if (_sectionNumber != value)
                {
                    _sectionNumber = value;
                    OnPropertyChanged(nameof(SectionNumber));
                }
            }
        }

        private string _ahelaNr;
        public string AhelaNr
        {
            get => _ahelaNr;
            set
            {
                _ahelaNr = value;
                OnPropertyChanged(nameof(AhelaNr));
            }
        }

        // Add this property for Seadme Nr (Aadress)
        private string _aadress;
        public string Aadress
        {
            get => _aadress;
            set
            {
                _aadress = value;
                OnPropertyChanged(nameof(Aadress));
            }
        }

        public ObservableCollection<DetailTypeOption> AvailableDetailTypes { get; set; }

        private DetailTypeOption _selectedDetailType;
        public DetailTypeOption SelectedDetailType
        {
            get => _selectedDetailType;
            set
            {
                _selectedDetailType = value;
                OnPropertyChanged(nameof(SelectedDetailType));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }


    public static class FireAlarmCollector
    {
        public static List<FireAlarmEntry> GetFireAlarmElements(Document doc)
        {
            // Get current user's username and build the path dynamically
            string userName = System.Environment.UserName;
            string dbPath = $@"C:\Users\{userName}\EULE Dropbox\Raul Kalev\Me\Plugins\Schema\DetailMappings.accdb";
            AccessDetailMappingReader.Load(dbPath);

            var entries = new List<FireAlarmEntry>();

            var collector = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_FireAlarmDevices)
                .WhereElementIsNotElementType()
                .Cast<FamilyInstance>();

            // Get all detail symbols
            var allDetailTypes = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(BuiltInCategory.OST_DetailComponents)
                .Cast<FamilySymbol>()
                .Select(f => new DetailTypeOption
                {
                    DisplayName = $"{f.Family.Name} : {f.Name}",
                    Id = f.Id.ToString()
                })
                .OrderBy(o => o.DisplayName)
                .ToList();

            // Instead of grouping, create one FireAlarmEntry per FamilyInstance
            foreach (var fi in collector)
            {
                string level = doc.GetElement(fi.LevelId)?.Name ?? "Unknown";
                string family = fi.Symbol?.Family?.Name ?? "<null>";
                string type = fi.Symbol?.Name ?? "<null>";
                string ahelaNr = fi.LookupParameter("Ahela nr.")?.AsString() ?? "";
                string seadmeNr = fi.LookupParameter("Seadme Nr.")?.AsString() ?? "";

                string defaultSymbolName = AccessDetailMappingReader.GetDetailSymbol(family, type);
                var defaultOption = allDetailTypes.FirstOrDefault(opt => opt.DisplayName == defaultSymbolName);

                entries.Add(new FireAlarmEntry
                {
                    Element = fi,
                    LevelName = level,
                    FamilyName = family,
                    TypeName = type,
                    Count = 1, // Each entry is a single instance
                    AvailableDetailTypes = new ObservableCollection<DetailTypeOption>(allDetailTypes),
                    SelectedDetailType = defaultOption ?? allDetailTypes.FirstOrDefault(),
                    AhelaNr = ahelaNr,
                    Aadress = seadmeNr // Unique per instance
                });
            }

            return entries;
        }


    }
}
