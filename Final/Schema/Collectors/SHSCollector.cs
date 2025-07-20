using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Schema.ExternalEvents;
using Schema.Helpers;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;

namespace CAD_Manager.Collectors
{
    public static class SHSCollector
    {
        public class SecurityDeviceEntry : ISubsystemEntry, INotifyPropertyChanged
        {
            public string FamilyName { get; set; }
            public string TypeName { get; set; }
            public string LevelName { get; set; }
            public string AhelaNr { get; set; }
            public int Count { get; set; }
            public Element Element { get; set; }

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

            public ObservableCollection<DetailTypeOption> AvailableDetailTypes { get; set; }

            private DetailTypeOption _selectedDetailType;
            public DetailTypeOption SelectedDetailType
            {
                get => _selectedDetailType;
                set
                {
                    if (_selectedDetailType != value)
                    {
                        _selectedDetailType = value;
                        OnPropertyChanged(nameof(SelectedDetailType));
                    }
                }
            }

            public event PropertyChangedEventHandler PropertyChanged;
            protected void OnPropertyChanged(string name) =>
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        public static List<SecurityDeviceEntry> GetSecurityDeviceElements(Document doc)

        {
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

            return new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_SecurityDevices)
                .WhereElementIsNotElementType()
                .OfType<FamilyInstance>()
                .Where(e => e.Symbol.FamilyName != null &&
                            e.Symbol.FamilyName.IndexOf("SHS", System.StringComparison.OrdinalIgnoreCase) >= 0)
                .GroupBy(e => new
                {
                    e.Symbol.FamilyName,
                    TypeName = e.Symbol.Name,
                    LevelName = doc.GetElement(e.LevelId)?.Name ?? "N/A",
                    AhelaNr = e.LookupParameter("Ahela NR")?.AsString() ?? ""
                })
                .Select(g =>
                {
                    string defaultSymbolName = AccessDetailMappingReader.GetDetailSymbol(g.Key.FamilyName, g.Key.TypeName);
                    var defaultOption = allDetailTypes.FirstOrDefault(opt => opt.DisplayName == defaultSymbolName);

                    return new SecurityDeviceEntry
                    {
                        FamilyName = g.Key.FamilyName,
                        TypeName = g.Key.TypeName,
                        LevelName = g.Key.LevelName,
                        AhelaNr = g.Key.AhelaNr,
                        Count = g.Count(),
                        Element = g.First(),
                        AvailableDetailTypes = new ObservableCollection<DetailTypeOption>(allDetailTypes),
                        SelectedDetailType = defaultOption ?? allDetailTypes.FirstOrDefault()
                    };
                })
                .OrderBy(e => e.LevelName, new LevelNameComparer())
                .ToList();
        }
    }
}
