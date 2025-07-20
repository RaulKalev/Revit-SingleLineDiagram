using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Linq;

namespace EliteSheets.ExternalEvents
{
    public class PlaceControllerHandler : IExternalEventHandler
    {
        public UIDocument UiDoc { get; set; }
        public string OffsetText { get; set; } // Bind from UI
        public ElementId SelectedSymbolId { get; set; }

        public void Execute(UIApplication app)
        {
            if (UiDoc == null) return;

            Document doc = UiDoc.Document;

            View activeView = doc.ActiveView;
            if (!(activeView is ViewDrafting draftingView))
            {
                TaskDialog.Show("Invalid View", "Please run this command in a drafting view.");
                return;
            }

            // 1. Find the electrical equipment
            var ats = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_ElectricalEquipment)
                .WhereElementIsNotElementType()
                .OfType<FamilyInstance>()
                .FirstOrDefault(e =>
                    e.Symbol.FamilyName == "EN_ATS-Keskseade" &&
                    e.Symbol.Name == "ATS keskseade");

            if (ats == null)
            {
                TaskDialog.Show("Not Found", "ATS keskseade not found in the model.");
                return;
            }

            // 2. Get horizontal level line positions (Detail Lines with style "xx_Level")
            var levelLines = new FilteredElementCollector(doc, draftingView.Id)
                .OfCategory(BuiltInCategory.OST_Lines)
                .WhereElementIsNotElementType()
                .OfType<DetailLine>()
                .Where(dl =>
                {
                    GraphicsStyle style = doc.GetElement(dl.LineStyle.Id) as GraphicsStyle;
                    return style != null && style.Name == "xx_Level";
                })
                .ToList();

            if (!levelLines.Any())
            {
                TaskDialog.Show("No Level Lines", "No 'xx_Level' detail lines found in the current drafting view.");
                return;
            }

            // Get bottom-most Y value from all level lines
            double targetY = levelLines
                .Select(dl => dl.GeometryCurve.GetEndPoint(0).Y)
                .Concat(levelLines.Select(dl => dl.GeometryCurve.GetEndPoint(1).Y))
                .Min();

            // 3. Find the Detail Item family symbol
            var targetSymbol = doc.GetElement(SelectedSymbolId) as FamilySymbol;
            if (targetSymbol == null) return;


            // 4. Activate symbol if needed
            if (!targetSymbol.IsActive)
            {
                using (Transaction tx = new Transaction(doc, "Activate Symbol"))
                {
                    tx.Start();
                    targetSymbol.Activate();
                    tx.Commit();
                }
            }

            // 5. Place detail item at bottom center of the view crop box (updated)
            // Get the longest level line (in case there are multiple)
            var longestLine = levelLines
                .OrderByDescending(dl => dl.GeometryCurve.Length)
                .First();

            Curve levelCurve = longestLine.GeometryCurve;
            double midX = (levelCurve.GetEndPoint(0).X + levelCurve.GetEndPoint(1).X) / 2;


            double offsetMm = 150; // default
            if (!string.IsNullOrWhiteSpace(OffsetText) && double.TryParse(OffsetText, out double parsed))
                offsetMm = parsed;

            double offset = UnitUtils.ConvertToInternalUnits(offsetMm, UnitTypeId.Millimeters);

            XYZ placementPoint = new XYZ(midX, targetY + offset, 0);

            // 6. Delete existing instances of the same controller symbol in the view
            // 6. Delete existing controller detail items (any KS-related) from the view
            var existingControllerInstances = new FilteredElementCollector(doc, draftingView.Id)
                .OfCategory(BuiltInCategory.OST_DetailComponents)
                .WhereElementIsNotElementType()
                .OfType<FamilyInstance>()
                .Where(fi =>
                {
                    string familyName = fi.Symbol.FamilyName;
                    string typeName = fi.Symbol.Name;
                    return (familyName.Contains("KS") || typeName.Contains("KS"));
                })
                .Select(e => e.Id)
                .ToList();


            if (existingControllerInstances.Any())
            {
                using (Transaction tx = new Transaction(doc, "Delete Existing Controller Detail Item"))
                {
                    tx.Start();
                    doc.Delete(existingControllerInstances);
                    tx.Commit();
                }
            }

            // 7. Place the detail item
            using (Transaction tx = new Transaction(doc, "Place Controller Detail Item"))
            {
                tx.Start();
                doc.Create.NewFamilyInstance(placementPoint, targetSymbol, draftingView);
                tx.Commit();
            }

        }

        public string GetName() => "Place Controller Detail Item Handler";
    }
}
