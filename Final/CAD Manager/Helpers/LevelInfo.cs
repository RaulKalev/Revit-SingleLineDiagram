using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Autodesk.Revit.DB;

namespace Schema.Helpers
{
    public class LevelEntry : INotifyPropertyChanged
    {
        private bool _isSelected = true;
        private string _name;
        private double _elevation;

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged(nameof(IsSelected));
                }
            }
        }

        public string Name
        {
            get => _name;
            set
            {
                if (_name != value)
                {
                    _name = value;
                    OnPropertyChanged(nameof(Name));
                }
            }
        }

        public double Elevation
        {
            get => _elevation;
            set
            {
                if (_elevation != value)
                {
                    _elevation = value;
                    OnPropertyChanged(nameof(Elevation));
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }


public static class LevelInfo
    {
        public static List<LevelEntry> GetLevelEntries(Document doc)
        {
            if (doc == null) return new List<LevelEntry>();

            return new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(l => l.Elevation)
                .Select(l => new LevelEntry
                {
                    Name = l.Name,
                    Elevation = UnitUtils.ConvertFromInternalUnits(l.Elevation, UnitTypeId.Millimeters)
                })
                .ToList();
        }
    }
}
