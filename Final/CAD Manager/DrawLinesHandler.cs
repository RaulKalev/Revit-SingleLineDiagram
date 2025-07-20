using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;

namespace EliteSheets.ExternalEvents
{
    public class DrawLinesHandler : IExternalEventHandler
    {
        public UIDocument UiDoc { get; set; }
        public View TargetView { get; set; }
        public List<Level> SelectedLevels { get; set; } = new List<Level>();
        public double MarginMm { get; set; } = 10;
        public double SpacingMm { get; set; } = 10;

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

            var detailItems = new FilteredElementCollector(doc, draftingView.Id)
                .OfCategory(BuiltInCategory.OST_DetailComponents)
                .WhereElementIsNotElementType()
                .OfType<FamilyInstance>()
                .Where(fi =>
                {
                    var comments = fi.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString();
                    return !string.IsNullOrWhiteSpace(comments) && int.TryParse(comments.Trim(), out _);
                })
                .ToList();

            if (!detailItems.Any())
            {
                TaskDialog.Show("No Items", "No placed fire alarm detail items found.");
                return;
            }

            double threshold = UnitUtils.ConvertToInternalUnits(5, UnitTypeId.Millimeters);
            var groups = new List<List<FamilyInstance>>();

            foreach (var item in detailItems)
            {
                var pt = (item.Location as LocationPoint)?.Point;
                if (pt == null) continue;

                var existingGroup = groups.FirstOrDefault(g =>
                    Math.Abs((g.First().Location as LocationPoint).Point.Y - pt.Y) < threshold);

                if (existingGroup != null)
                    existingGroup.Add(item);
                else
                    groups.Add(new List<FamilyInstance> { item });
            }

            // Look for custom line style "xx_ATS"
            var lineStyle = new FilteredElementCollector(doc)
                .OfClass(typeof(GraphicsStyle))
                .Cast<GraphicsStyle>()
                .FirstOrDefault(gs =>
                    gs.GraphicsStyleCategory != null &&
                    gs.GraphicsStyleCategory.Parent != null &&
                    gs.GraphicsStyleCategory.Parent.Id == doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines).Id &&
                    gs.Name.Equals("xx_ATS", StringComparison.OrdinalIgnoreCase));

            if (lineStyle == null)
            {
                TaskDialog.Show("Error", "Line style 'xx_ATS' not found. Please create it in the project.");
                return;
            }

            ElementId lineStyleId = lineStyle.Id;

            double spacingInternal = UnitUtils.ConvertToInternalUnits(SpacingMm, UnitTypeId.Millimeters);

            using (Transaction tx = new Transaction(doc, "Delete & Draw Row Lines"))
            {
                tx.Start();

                // Delete only lines marked by this plugin
                var oldLines = new FilteredElementCollector(doc, draftingView.Id)
                    .OfClass(typeof(CurveElement))
                    .OfType<DetailLine>()
                    .Where(dl => dl.LineStyle.Id == lineStyleId)
                    .ToList();


                foreach (var line in oldLines)
                    doc.Delete(line.Id);

                // Draw new lines
                foreach (var group in groups)
                {
                    var points = group
                        .Select(i => (i.Location as LocationPoint)?.Point)
                        .Where(p => p != null)
                        .ToList();

                    if (points.Count == 0) continue;

                    double y = points.First().Y;
                    double z = points.First().Z;

                    double minX, maxX;

                    if (points.Count == 1)
                    {
                        double x = points.First().X;
                        minX = x - spacingInternal;
                        maxX = x + spacingInternal;
                    }
                    else
                    {
                        minX = points.Min(p => p.X) - spacingInternal;
                        maxX = points.Max(p => p.X) + spacingInternal;
                    }

                    XYZ start = new XYZ(minX, y, z);
                    XYZ end = new XYZ(maxX, y, z);

                    Line line = Line.CreateBound(start, end);
                    DetailLine detailLine = doc.Create.NewDetailCurve(draftingView, line) as DetailLine;

                    if (detailLine != null && lineStyleId != null)
                        detailLine.LineStyle = doc.GetElement(lineStyleId);

                }

                tx.Commit();
            }
        }

        public string GetName() => "Draw Lines at Controller Rows";
    }
}
