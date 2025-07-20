using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace Schema.Helpers
{
    public static class DrawingArea
    {
        // ISO paper sizes in mm (width x height), from A0 to A5
        private static readonly Dictionary<string, (double Width, double Height)> IsoSizes = new Dictionary<string, (double, double)>
        {
            { "A0", (841, 1189) },
            { "A1", (594, 841) },
            { "A2", (420, 594) },
            { "A3", (297, 420) },
            { "A4", (210, 297) },
            { "A5", (148, 210) }
        };
        public static GraphicsStyle GetDetailLineStyleByName(Document doc, string styleName)
        {
            Category linesCategory = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);
            if (linesCategory == null || !linesCategory.SubCategories.Contains(styleName))
                return null;

            Category subCategory = linesCategory.SubCategories.get_Item(styleName);
            return subCategory?.GetGraphicsStyle(GraphicsStyleType.Projection);
        }

        public static string GetBoundaryInfo(Document doc, ViewDrafting view)
        {
            // Find all detail lines with style "xx_Boundary"
            var boundaryLines = new FilteredElementCollector(doc, view.Id)
                .OfClass(typeof(CurveElement))
                .Cast<CurveElement>()
                .Where(el => el.LineStyle != null && el.LineStyle.Name == "xx_Boundary")
                .ToList();

            if (!boundaryLines.Any())
                return "Detail line with style 'xx_Boundary' not found in this view.";

            // Gather all endpoints
            List<XYZ> points = new List<XYZ>();
            foreach (var line in boundaryLines)
            {
                var curve = line.GeometryCurve;
                if (curve != null)
                {
                    points.Add(curve.GetEndPoint(0));
                    points.Add(curve.GetEndPoint(1));
                }
            }

            if (!points.Any())
                return "Boundary lines found, but no valid geometry.";

            // Calculate bounding box
            double minX = points.Min(p => p.X);
            double maxX = points.Max(p => p.X);
            double minY = points.Min(p => p.Y);
            double maxY = points.Max(p => p.Y);

            double widthMm = UnitUtils.ConvertFromInternalUnits(maxX - minX, UnitTypeId.Millimeters);
            double heightMm = UnitUtils.ConvertFromInternalUnits(maxY - minY, UnitTypeId.Millimeters);

            // Normalize width/height orientation (portrait/landscape agnostic)
            double w = Math.Max(widthMm, heightMm);
            double h = Math.Min(widthMm, heightMm);

            string closestSize = FindClosestIsoSize(w, h);

            return $"Boundary area: {Math.Round(widthMm, 1)} mm x {Math.Round(heightMm, 1)} mm (closest: {closestSize})";
        }

        public static string FindClosestIsoSize(double width, double height)
        {
            string bestMatch = "Unknown";
            double minDiff = double.MaxValue;

            foreach (var kvp in IsoSizes)
            {
                var (w, h) = kvp.Value;
                // Allow both portrait and landscape matching
                double diff1 = Math.Abs(width - w) + Math.Abs(height - h);
                double diff2 = Math.Abs(width - h) + Math.Abs(height - w);
                double diff = Math.Min(diff1, diff2);

                if (diff < minDiff)
                {
                    minDiff = diff;
                    bestMatch = kvp.Key;
                }
            }

            return bestMatch;
        }
        public static double? GetBoundaryHeightMm(Document doc, ViewDrafting view)
        {
            if (doc == null || view == null)
                return null;

            var boundaryLines = new FilteredElementCollector(doc, view.Id)
                .OfClass(typeof(CurveElement))
                .Cast<CurveElement>()
                .Where(el => el.LineStyle != null && el.LineStyle.Name == "xx_Boundary")
                .ToList();

            if (!boundaryLines.Any())
                return null;

            List<XYZ> points = new List<XYZ>();
            foreach (var line in boundaryLines)
            {
                var curve = line.GeometryCurve;
                if (curve != null)
                {
                    points.Add(curve.GetEndPoint(0));
                    points.Add(curve.GetEndPoint(1));
                }
            }

            if (!points.Any())
                return null;

            double minY = points.Min(p => p.Y);
            double maxY = points.Max(p => p.Y);
            double height = maxY - minY;

            return UnitUtils.ConvertFromInternalUnits(height, UnitTypeId.Millimeters);
        }
        public static (double minX, double maxX, double minY, double maxY)? GetBoundaryBox(Document doc, ViewDrafting view, double marginMm = 0)
        {
            var boundaryLines = new FilteredElementCollector(doc, view.Id)
                .OfClass(typeof(CurveElement))
                .Cast<CurveElement>()
                .Where(el => el.LineStyle != null && el.LineStyle.Name == "xx_Boundary")
                .ToList();

            if (!boundaryLines.Any()) return null;

            List<XYZ> points = new List<XYZ>();
            foreach (var line in boundaryLines)
            {
                var curve = line.GeometryCurve;
                if (curve != null)
                {
                    points.Add(curve.GetEndPoint(0));
                    points.Add(curve.GetEndPoint(1));
                }
            }

            if (!points.Any()) return null;

            double minX = points.Min(p => p.X);
            double maxX = points.Max(p => p.X);
            double minY = points.Min(p => p.Y);
            double maxY = points.Max(p => p.Y);

            if (marginMm > 0)
            {
                double marginFt = UnitUtils.ConvertToInternalUnits(marginMm, UnitTypeId.Millimeters);
                minX += marginFt;
                maxX -= marginFt;
                minY += marginFt;
                maxY -= marginFt;
            }

            return (minX, maxX, minY, maxY);
        }


    }
}
