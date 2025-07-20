using Autodesk.Revit.DB;
using System.Collections.Generic;
using System.Linq;

public static class ViewCollector
{
    public static List<View> GetDraftingViews(Document doc)
    {
        return new FilteredElementCollector(doc)
            .OfClass(typeof(View))
            .Cast<View>()
            .Where(v =>
                v.ViewType == ViewType.DraftingView && // Corrected ViewType
                !v.IsTemplate &&
                v.Name != null)
            .OrderBy(v => v.Name)
            .ToList();
    }
}
