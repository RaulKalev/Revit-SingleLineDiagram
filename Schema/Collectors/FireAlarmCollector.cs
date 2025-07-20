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
            // Load detail mapping database from the executing assembly location
            AccessDetailMappingReader.Load();

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

            // ✅ Group by Level + Family + Type
            var flatData = collector
                .Select(fi => new
                {
                    Level = doc.GetElement(fi.LevelId)?.Name ?? "Unknown",
                    Family = fi.Symbol?.Family?.Name ?? "<null>",
                    Type = fi.Symbol?.Name ?? "<null>",
                    AhelaNr = fi.LookupParameter("Ahela nr.")?.AsString() ?? "",
                    Instance = fi
                })
                .ToList(); // 👈 Forces immediate evaluation

            var grouped = flatData
                .GroupBy(x => new { x.Level, x.Family, x.Type, x.AhelaNr })
                .OrderBy(g => g.Key.Level, new LevelNameComparer())
                .ThenBy(g => g.Key.Family)
                .ThenBy(g => g.Key.Type)
                .ThenBy(g => g.Key.AhelaNr);



            foreach (var group in grouped)
            {
                string defaultSymbolName = AccessDetailMappingReader.GetDetailSymbol(group.Key.Family, group.Key.Type);
                string ahelaNr = group.Key.AhelaNr;

                var defaultOption = allDetailTypes.FirstOrDefault(opt => opt.DisplayName == defaultSymbolName);

                entries.Add(new FireAlarmEntry
                {
                    LevelName = group.Key.Level,
                    FamilyName = group.Key.Family,
                    TypeName = group.Key.Type,
                    Count = group.Count(),
                    AvailableDetailTypes = new ObservableCollection<DetailTypeOption>(allDetailTypes),
                    SelectedDetailType = defaultOption ?? allDetailTypes.FirstOrDefault(),
                    AhelaNr = ahelaNr
                });
            }


            return entries;
        }


    }
}
