using Autodesk.Revit.DB;
using System.Collections.ObjectModel;
using Schema.ExternalEvents;

namespace Schema.Helpers
{
    public interface ISubsystemEntry
    {
        Element Element { get; set; }
        string FamilyName { get; set; }
        string TypeName { get; set; }
        string LevelName { get; set; }
        string AhelaNr { get; set; }
        int Count { get; set; }
        bool IsSelected { get; set; }
        int SectionNumber { get; set; }
        ObservableCollection<DetailTypeOption> AvailableDetailTypes { get; set; }
        DetailTypeOption SelectedDetailType { get; set; }
    }
}
