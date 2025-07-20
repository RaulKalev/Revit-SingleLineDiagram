using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Schema.Collectors;
using Schema.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Schema.ExternalEvents
{
    public class DetailTypeOption
    {
        public string DisplayName { get; set; }
        public string Id { get; set; }
    }
    public class PlaceDetailItemsHandler : IExternalEventHandler
    {
        public UIDocument UiDoc { get; set; }
        public ViewDrafting TargetView { get; set; }
        public List<FireAlarmEntry> FireAlarmEntries { get; set; }
        public List<LevelEntry> LevelEntries { get; set; }
        public FamilySymbol SelectedTagType { get; set; }
        public double TagOffset { get; set; } = 150.0 / 304.8; // default 150 mm
        public double StartOffsetMm { get; set; } = 10;
        public double SpacingMm { get; set; } = 500;
        public double RightOffsetMm { get; set; } = 10;
        public string TagParameterName { get; set; } = "Comments"; // default fallback
        public double VerticalMarginMm { get; set; } = 10.0;

        public string GetName() => "Place Detail Items";
        public int SectionCount { get; set; } = 1;
        public enum HorizontalAlignment
        {
            Left,
            Right
        }
        public HorizontalAlignment SectionAlignment { get; set; } = HorizontalAlignment.Left;

        public void Execute(UIApplication app)
        {
            try
            {
                if (UiDoc == null || TargetView == null || FireAlarmEntries == null || LevelEntries == null)
                    return;

                Document doc = UiDoc.Document;
                double mmToFt = 1.0 / 304.8;
                double spacingFt = SpacingMm * mmToFt;
                double offset = StartOffsetMm * mmToFt;
                double rightOffset = RightOffsetMm * mmToFt;
                double verticalMarginFt = VerticalMarginMm * mmToFt;

                var boundaryLines = GetBoundaryLines(doc, TargetView);
                if (boundaryLines.Count == 0) return;

                var (minX, maxX, minY, maxY) = GetBoundaryExtents(boundaryLines);

                var selectedLevels = LevelEntries.Where(l => l.IsSelected).Reverse().ToList();
                if (selectedLevels.Count == 0) return;

                var levelLineYs = GetLevelLineYs(doc, TargetView);
                if (levelLineYs.Count < selectedLevels.Count + 1)
                {
                    TaskDialog.Show("Error", $"Expected at least {selectedLevels.Count + 1} level lines (xx_Level), but found {levelLineYs.Count}. Make sure one extra line is drawn above the top level.");
                    return;
                }

                var placementHeights = GetPlacementHeights(levelLineYs);
                var levelYMap = GetLevelYMap(selectedLevels, placementHeights);
                var levelIndexMap = GetLevelIndexMap(selectedLevels);
                var symbolLookup = GetSymbolLookup(doc);

                using (Transaction tx = new Transaction(doc, "Place Detail Items"))
                {
                    tx.Start();

                    DeleteExistingDetailItems(doc, TargetView);

                    var levelPlacementIndex = new Dictionary<Tuple<string, int, string>, int>();
                    var groupedByLevelAndSection = GroupEntriesByLevelAndSection(FireAlarmEntries);

                    var selectedEntries = FireAlarmEntries.Where(e => e.IsSelected).ToList();

                    foreach (var entry in selectedEntries)
                    {
                        if (!IsEntryValid(entry, levelYMap, symbolLookup, out FamilySymbol symbol, out double centerY))
                            continue;

                        if (!symbol.IsActive)
                            symbol.Activate();

                        int sectionNumber = Math.Max(1, entry.SectionNumber);
                        var placementKey = Tuple.Create(entry.LevelName, sectionNumber, entry.AhelaNr ?? "");

                        if (!levelPlacementIndex.ContainsKey(placementKey))
                            levelPlacementIndex[placementKey] = 0;

                        int indexOnLevel = levelPlacementIndex[placementKey];
                        double x = GetXCoordinate(sectionNumber, minX, maxX, offset, rightOffset, spacingFt, indexOnLevel);

                        var groupKey = (entry.LevelName, entry.SectionNumber);
                        if (!groupedByLevelAndSection.TryGetValue(groupKey, out var ahelaList))
                            continue;

                        int index = ahelaList.IndexOf(entry.AhelaNr);
                        int total = ahelaList.Count;

                        if (!levelIndexMap.TryGetValue(entry.LevelName, out int levelIndex))
                            continue;
                        if (levelIndex + 1 >= levelLineYs.Count)
                            continue;

                        double topY = levelLineYs[levelIndex];
                        double bottomY = levelLineYs[levelIndex + 1];

                        double adjustedTopY = topY - verticalMarginFt;
                        double adjustedBottomY = bottomY + verticalMarginFt;
                        double adjustedBandHeight = adjustedTopY - adjustedBottomY;

                        double y = total == 1
                            ? (adjustedTopY + adjustedBottomY) / 2.0
                            : adjustedTopY - ((index + 0.5) * (adjustedBandHeight / total));

                        XYZ point = new XYZ(x, y, 0);

                        levelPlacementIndex[placementKey]++;

                        var detail = doc.Create.NewFamilyInstance(point, symbol, TargetView) as FamilyInstance;

                        if (detail != null)
                        {
                            SetParameters(detail, entry);
                            PlaceTag(detail, point, doc);
                        }
                    }

                    tx.Commit();
                }
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Error", $"An unexpected error occurred: {ex.Message}");
            }
        }

        // Gets boundary lines with "xx_Boundary" style
        private List<Curve> GetBoundaryLines(Document doc, ViewDrafting view)
        {
            return new FilteredElementCollector(doc, view.Id)
                .OfClass(typeof(CurveElement))
                .Cast<CurveElement>()
                .Where(el => el.LineStyle?.Name == "xx_Boundary")
                .Select(el => el.GeometryCurve)
                .ToList();
        }

        // Returns min/max X and Y from boundary lines
        private (double minX, double maxX, double minY, double maxY) GetBoundaryExtents(List<Curve> boundaryLines)
        {
            var points = boundaryLines.SelectMany(c => new[] { c.GetEndPoint(0), c.GetEndPoint(1) }).ToList();
            return (
                points.Min(p => p.X),
                points.Max(p => p.X),
                points.Min(p => p.Y),
                points.Max(p => p.Y)
            );
        }

        // Gets Y coordinates of level lines
        private List<double> GetLevelLineYs(Document doc, ViewDrafting view)
        {
            return new FilteredElementCollector(doc, view.Id)
                .OfClass(typeof(CurveElement))
                .Cast<CurveElement>()
                .Where(el => el.LineStyle?.Name == "xx_Level")
                .Select(el => el.GeometryCurve)
                .Where(c => c.IsBound)
                .Select(c => (c.GetEndPoint(0).Y + c.GetEndPoint(1).Y) / 2.0)
                .Distinct()
                .OrderByDescending(y => y)
                .ToList();
        }

        // Calculates placement heights between level lines
        private double[] GetPlacementHeights(List<double> levelLineYs)
        {
            var placementHeights = new double[levelLineYs.Count - 1];
            for (int i = 0; i < levelLineYs.Count - 1; i++)
                placementHeights[i] = (levelLineYs[i] + levelLineYs[i + 1]) / 2;
            return placementHeights;
        }

        // Maps level names to placement heights
        private Dictionary<string, double> GetLevelYMap(List<LevelEntry> selectedLevels, double[] placementHeights)
        {
            var levelYMap = new Dictionary<string, double>(selectedLevels.Count);
            for (int i = 0; i < selectedLevels.Count; i++)
                levelYMap[selectedLevels[i].Name] = placementHeights[i];
            return levelYMap;
        }

        // Maps level names to their index
        private Dictionary<string, int> GetLevelIndexMap(List<LevelEntry> selectedLevels)
        {
            return selectedLevels
                .Select((lvl, idx) => new { lvl.Name, idx })
                .ToDictionary(x => x.Name, x => x.idx);
        }

        // Lookup for available detail symbols
        private Dictionary<string, FamilySymbol> GetSymbolLookup(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(BuiltInCategory.OST_DetailComponents)
                .Cast<FamilySymbol>()
                .ToDictionary(s => $"{s.Family.Name} : {s.Name}", s => s);
        }

        // Deletes all detail items in the view
        private void DeleteExistingDetailItems(Document doc, ViewDrafting view)
        {
            var existingDetailItems = new FilteredElementCollector(doc, view.Id)
                .OfCategory(BuiltInCategory.OST_DetailComponents)
                .WhereElementIsNotElementType()
                .ToElementIds();

            if (existingDetailItems.Count > 0)
                doc.Delete(existingDetailItems);
        }

        // Groups entries by level and section
        private Dictionary<(string LevelName, int SectionNumber), List<string>> GroupEntriesByLevelAndSection(List<FireAlarmEntry> entries)
        {
            return entries
                .Where(e => e.IsSelected)
                .GroupBy(e => (e.LevelName, e.SectionNumber))
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(e => e.AhelaNr).Distinct().OrderBy(x => x).ToList()
                );
        }

        // Checks if entry is valid and gets symbol and Y
        private bool IsEntryValid(
            FireAlarmEntry entry,
            Dictionary<string, double> levelYMap,
            Dictionary<string, FamilySymbol> symbolLookup,
            out FamilySymbol symbol,
            out double centerY)
        {
            centerY = 0;
            symbol = null;
            if (entry.SelectedDetailType == null || entry.Count <= 0)
                return false;
            if (!levelYMap.TryGetValue(entry.LevelName, out centerY))
                return false;
            if (!symbolLookup.TryGetValue(entry.SelectedDetailType.DisplayName, out symbol))
                return false;
            if (symbol == null)
                return false;
            return true;
        }

        // Calculates X coordinate for placement
        private double GetXCoordinate(int sectionNumber, double minX, double maxX, double offset, double rightOffset, double spacingFt, int indexOnLevel)
        {
            return sectionNumber == 1
                ? minX + offset + (indexOnLevel * spacingFt)
                : sectionNumber == 2
                    ? maxX - rightOffset - (indexOnLevel * spacingFt)
                    : minX + offset + (indexOnLevel * spacingFt);
        }

        // Places a tag for the detail item
        private void PlaceTag(FamilyInstance detail, XYZ point, Document doc)
        {
            var tagPoint = point + new XYZ(0, TagOffset, 0);
            var tag = IndependentTag.Create(
                doc,
                TargetView.Id,
                new Reference(detail),
                false,
                TagMode.TM_ADDBY_CATEGORY,
                TagOrientation.Horizontal,
                tagPoint
            );

            if (tag != null && SelectedTagType != null)
            {
                if (!SelectedTagType.IsActive)
                    SelectedTagType.Activate();

                if (tag.GetTypeId() != SelectedTagType.Id)
                    tag.ChangeTypeId(SelectedTagType.Id);
            }
        }

        // Sets parameters on the detail item
        private void SetParameters(FamilyInstance detail, FireAlarmEntry entry)
        {
            Parameter userParam = detail.LookupParameter(TagParameterName);
            if (userParam != null && userParam.StorageType == StorageType.String && !userParam.IsReadOnly)
            {
                userParam.Set(entry.Count.ToString());
            }

            Parameter ahelaParam = detail.LookupParameter("Ahela nr.");
            if (ahelaParam != null && ahelaParam.StorageType == StorageType.String && !ahelaParam.IsReadOnly)
            {
                ahelaParam.Set(entry.AhelaNr ?? "");
            }
        }
    }
}
