using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI;
using Schema.ExternalEvents;
using Schema.Helpers;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;

namespace CAD_Manager.Collectors
{
    public static class VVSCollector
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
            protected void OnPropertyChanged(string propertyName)
                => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        public static List<SecurityDeviceEntry> GetSecurityDeviceElements(Document doc)
        {
            // 1. Collect all detail symbols
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

            // 2. Group elements and build the entry list
            return new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_SecurityDevices)
                .WhereElementIsNotElementType()
                .OfType<FamilyInstance>()
                .Where(e => e.Symbol.FamilyName != null &&
                            e.Symbol.FamilyName.IndexOf("VVS", System.StringComparison.OrdinalIgnoreCase) >= 0)
                .GroupBy(e =>
                {
                    // Try to get the shared type parameter "ELENEA_ÜLD 001_Nimetus" from the FamilySymbol (type)
                    string parameterValue = null;

                    // Access the type parameter for "ELENEA_ÜLD 001_Nimetus"
                    var typeParam = e.Symbol.LookupParameter("ELENEA_ÜLD 001_Nimetus");
                    if (typeParam != null)
                    {
                        parameterValue = typeParam.AsString();
                    }

                    // Default to "N/A" if the parameter is missing or empty
                    parameterValue = parameterValue ?? "N/A";

                    // Group by the ELENEA_ÜLD 001_Nimetus parameter from the type
                    return new
                    {
                        e.Symbol.FamilyName,
                        ELENEA_ÜLD_001_Nimetus = parameterValue,  // Grouping by ELENEA_ÜLD 001_Nimetus
                        LevelName = doc.GetElement(e.LevelId)?.Name ?? "N/A",
                        AhelaNr = e.LookupParameter("Panel")?.AsString() ?? ""
                    };
                })
                .Select(g =>
                {
                    // Get the default symbol name from the mapping reader
                    string defaultSymbolName = AccessDetailMappingReader.GetDetailSymbol(g.Key.FamilyName, g.Key.ELENEA_ÜLD_001_Nimetus);
                    var defaultOption = allDetailTypes.FirstOrDefault(opt => opt.DisplayName == defaultSymbolName);

                    return new SecurityDeviceEntry
                    {
                        FamilyName = g.Key.FamilyName,
                        TypeName = g.Key.ELENEA_ÜLD_001_Nimetus,  // Using ELENEA_ÜLD_001_Nimetus here instead of TypeName
                        LevelName = g.Key.LevelName,
                        AhelaNr = g.Key.AhelaNr,
                        Count = g.Count(),
                        Element = g.First(),
                        AvailableDetailTypes = new ObservableCollection<DetailTypeOption>(allDetailTypes),
                        SelectedDetailType = defaultOption ?? allDetailTypes.FirstOrDefault()
                    };
                })
                // Use LevelNameComparer for sorting levels numerically
                .OrderBy(e => e.LevelName, new LevelNameComparer())  // Sort levels numerically
                .ThenBy(e => e.AhelaNr)  // Then by panel (AhelaNr)
                .ThenBy(e => e.TypeName)  // Then by type name (ELENEA_ÜLD 001_Nimetus)
                .ToList();

        }

    }
    }
