using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Schema
{
    public enum PlacementPattern
    {
        PP,
        TP
    }

    public class DrawLinesHandler : IExternalEventHandler
    {
        public UIDocument UiDoc { get; set; }
        public View TargetView { get; set; }
        public List<Level> SelectedLevels { get; set; } = new List<Level>();
        public double MarginMm { get; set; } = 10;
        public double SpacingMm { get; set; } = 10;
        public int ItemsInRow { get; set; } = 1;
        public double RowOffset { get; set; } = 10; // (in mm)

        public PlacementPattern CurrentPattern { get; set; } = PlacementPattern.PP;

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

            // Collect all detail items in the view with Tekst parameter set
            var detailItems = new FilteredElementCollector(doc, draftingView.Id)
                .OfCategory(BuiltInCategory.OST_DetailComponents)
                .WhereElementIsNotElementType()
                .OfType<FamilyInstance>()
                .Where(fi => fi.LookupParameter("Tekst")?.AsString() == "PlacedByScript")
                .ToList();

            if (!detailItems.Any())
            {
                TaskDialog.Show("No Items", "No placed fire alarm detail items found.");
                return;
            }

            double offset = UnitUtils.ConvertToInternalUnits(SpacingMm, UnitTypeId.Millimeters);
            double rowOffsetInternal = UnitUtils.ConvertToInternalUnits(RowOffset, UnitTypeId.Millimeters);

            // Group items by Ahela nr.
            var groups = detailItems
                .GroupBy(fi => fi.LookupParameter("Ahela nr.")?.AsString() ?? "")
                .Where(g => !string.IsNullOrWhiteSpace(g.Key))
                .ToList();

            // Find line style (unchanged)
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

                // Get all points
                var points = detailItems
                    .Select(i => (i.Location as LocationPoint)?.Point)
                    .Where(p => p != null)
                    .ToList();

                if (points.Count == 0) return;

                double baseY = points.First().Y;
                double z = 0;
                double minX = points.Min(p => p.X) - offset;
                double maxX = points.Max(p => p.X) + offset;

                if (CurrentPattern == PlacementPattern.TP)
                {
                    double rowTolerance = UnitUtils.ConvertToInternalUnits(2, UnitTypeId.Millimeters); // 2mm tolerance

                    foreach (var ahelaGroup in groups)
                    {
                        string ahelaNr = ahelaGroup.Key;
                        if (string.IsNullOrWhiteSpace(ahelaNr)) continue;

                        var groupPoints = ahelaGroup
                            .Select(i => (i.Location as LocationPoint)?.Point)
                            .Where(p => p != null)
                            .ToList();

                        if (groupPoints.Count == 0) continue;

                        // Group points by Y coordinate (rows)
                        var rowGroups = groupPoints
                            .GroupBy(p => Math.Round(p.Y / rowTolerance))
                            .OrderBy(g => g.Key)
                            .ToList();

                        // Store row points for vertical connection
                        var rows = rowGroups
                            .Select(g => g.OrderBy(p => p.X).ToList())
                            .ToList();

                        // Draw horizontal lines for each row
                        foreach (var rowPoints in rows)
                        {
                            double y = rowPoints.First().Y;
                            double minXRow = rowPoints.Min(p => p.X) - offset;
                            double maxXRow = rowPoints.Max(p => p.X) + offset;

                            XYZ start = new XYZ(minXRow, y, 0);
                            XYZ end = new XYZ(maxXRow, y, 0);

                            Line line = Line.CreateBound(start, end);
                            DetailLine detailLine = doc.Create.NewDetailCurve(draftingView, line) as DetailLine;
                            if (detailLine != null && lineStyleId != null)
                                detailLine.LineStyle = doc.GetElement(lineStyleId);
                        }

                        // Draw vertical lines between rows for points with same X
                        for (int r = 0; r < rows.Count - 1; r++)
                        {
                            var upperRow = rows[r];
                            var lowerRow = rows[r + 1];

                            foreach (var upperPoint in upperRow)
                            {
                                // Find all matching X in lower row (not just first)
                                var matchingLowerPoints = lowerRow
                                    .Where(p => Math.Abs(p.X - upperPoint.X) < rowTolerance)
                                    .ToList();

                                foreach (var lowerPoint in matchingLowerPoints)
                                {
                                    double yDiff = Math.Abs(lowerPoint.Y - upperPoint.Y);
                                    if (Math.Abs(yDiff - rowOffsetInternal) < rowTolerance)
                                    {
                                        XYZ start = new XYZ(upperPoint.X, upperPoint.Y, 0);
                                        XYZ end = new XYZ(lowerPoint.X, lowerPoint.Y, 0);

                                        Line vLine = Line.CreateBound(start, end);
                                        DetailLine vDetailLine = doc.Create.NewDetailCurve(draftingView, vLine) as DetailLine;
                                        if (vDetailLine != null && lineStyleId != null)
                                            vDetailLine.LineStyle = doc.GetElement(lineStyleId);
                                    }
                                }
                            }
                        }
                    }
                }
                else // PP
                {
                    // Draw a single line at baseY
                    XYZ start = new XYZ(minX, baseY, z);
                    XYZ end = new XYZ(maxX, baseY, z);

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
