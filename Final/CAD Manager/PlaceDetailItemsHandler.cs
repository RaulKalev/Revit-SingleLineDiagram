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
            if (UiDoc == null || TargetView == null || FireAlarmEntries == null || LevelEntries == null)
                return;

            Document doc = UiDoc.Document;
            double mmToFt = 1.0 / 304.8;
            double spacingFt = SpacingMm * mmToFt;
            double offset = StartOffsetMm * mmToFt;
            double rightOffset = RightOffsetMm * mmToFt;

            // 1. Get boundary box from vertical xx_Boundary lines
            var boundaryLines = new FilteredElementCollector(doc, TargetView.Id)
                .OfClass(typeof(CurveElement))
                .Cast<CurveElement>()
                .Where(el => el.LineStyle?.Name == "xx_Boundary")
                .Select(el => el.GeometryCurve)
                .ToList();

            var points = boundaryLines
                .SelectMany(c => new[] { c.GetEndPoint(0), c.GetEndPoint(1) })
                .ToList();

            if (!points.Any()) return;

            double minX = points.Min(p => p.X); // no margin applied here
            double maxX = points.Max(p => p.X);
            double minY = points.Min(p => p.Y);
            double maxY = points.Max(p => p.Y);


            var selectedLevels = LevelEntries.Where(l => l.IsSelected).Reverse().ToList();

            if (!selectedLevels.Any()) return;

            // Detect xx_Level lines in the view
            var levelLines = new FilteredElementCollector(doc, TargetView.Id)
                .OfClass(typeof(CurveElement))
                .Cast<CurveElement>()
                .Where(el => el.LineStyle?.Name == "xx_Level")
                .Select(el => el.GeometryCurve)
                .Where(c => c.IsBound)
                .ToList();

            // Get Y-coordinates of those lines (average of endpoints)
            var levelLineYs = levelLines
                .Select(c => (c.GetEndPoint(0).Y + c.GetEndPoint(1).Y) / 2.0)
                .Distinct()
                .OrderByDescending(y => y)
                .ToList();

            // Warn if there are not enough level lines
            if (levelLineYs.Count < selectedLevels.Count + 1)
            {
                TaskDialog.Show("Error", $"Expected at least {selectedLevels.Count + 1} level lines (xx_Level), but found {levelLineYs.Count}. Make sure one extra line is drawn above the top level.");
                return;
            }

            // Compute Y-coordinates for placing symbols between each level line
            List<double> placementHeights = new List<double>();
            for (int i = 0; i < levelLineYs.Count - 1; i++)
            {
                double centerY = (levelLineYs[i] + levelLineYs[i + 1]) / 2;
                placementHeights.Add(centerY);
            }

            // Map selected level names to placement Y-coordinates
            Dictionary<string, double> levelYMap = new Dictionary<string, double>();
            for (int i = 0; i < selectedLevels.Count; i++)
            {
                levelYMap[selectedLevels[i].Name] = placementHeights[i];
            }

            using (Transaction tx = new Transaction(doc, "Place Detail Items"))
            {
                tx.Start();

                // Delete existing detail items in the view
                var existingDetailItems = new FilteredElementCollector(doc, TargetView.Id)
                    .OfCategory(BuiltInCategory.OST_DetailComponents)
                    .WhereElementIsNotElementType()
                    .ToElementIds();

                if (existingDetailItems.Any())
                {
                    doc.Delete(existingDetailItems);
                }

                // Track how many items have been placed on each level
                Dictionary<Tuple<string, int, string>, int> levelPlacementIndex = new Dictionary<Tuple<string, int, string>, int>();

                // Group entries by (level, section) → distinct AhelaNr values
                var groupedByLevelAndSection = FireAlarmEntries
                    .Where(e => e.IsSelected)
                    .GroupBy(e => new { e.LevelName, e.SectionNumber })
                    .ToDictionary(
                        g => g.Key,
                        g => g
                            .Select(e => e.AhelaNr)
                            .Distinct()
                            .OrderBy(x => x) // Optional: consistent order
                            .ToList()
                    );


                foreach (var entry in FireAlarmEntries.Where(e => e.IsSelected))
                {
                    if (entry.SelectedDetailType == null || entry.Count <= 0)
                        continue;

                    if (!levelYMap.TryGetValue(entry.LevelName, out double centerY))
                        continue;

                    string[] split = entry.SelectedDetailType.DisplayName.Split(new[] { " : " }, StringSplitOptions.None);
                    if (split.Length != 2) continue;

                    string famName = split[0];
                    string typeName = split[1];

                    FamilySymbol symbol = new FilteredElementCollector(doc)
                        .OfClass(typeof(FamilySymbol))
                        .OfCategory(BuiltInCategory.OST_DetailComponents)
                        .Cast<FamilySymbol>()
                        .FirstOrDefault(f => f.Family.Name == famName && f.Name == typeName);

                    if (symbol == null)
                        continue;

                    if (!symbol.IsActive)
                        symbol.Activate();

                    int sectionNumber = Math.Max(1, entry.SectionNumber);
                    Tuple<string, int, string> placementKey = new Tuple<string, int, string>(entry.LevelName, sectionNumber, entry.AhelaNr ?? "");

                    if (!levelPlacementIndex.ContainsKey(placementKey))
                        levelPlacementIndex[placementKey] = 0;

                    int indexOnLevel = levelPlacementIndex[placementKey];
                    double x;

                    // Section 1 → Left to Right
                    if (sectionNumber == 1)
                    {
                        x = minX + offset + (indexOnLevel * spacingFt);
                    }
                    // Section 2 → Right to Left
                    else if (sectionNumber == 2)
                    {
                        x = maxX - rightOffset - (indexOnLevel * spacingFt);
                    }

                    // Future extension: other sections could be centered or stacked
                    else
                    {
                        x = minX + offset + (indexOnLevel * spacingFt); // fallback
                    }


                    // Lookup all Ahelad in this (level, section)
                    var groupKey = new { entry.LevelName, entry.SectionNumber };
                    if (!groupedByLevelAndSection.TryGetValue(groupKey, out var ahelaList))
                        continue;


                    int index = ahelaList.IndexOf(entry.AhelaNr);
                    int total = ahelaList.Count;

                    // Compute spacing within the height band (from topY to bottomY)
                    int levelIndex = selectedLevels.FindIndex(l => l.Name == entry.LevelName);
                    if (levelIndex == -1 || levelIndex + 1 >= levelLineYs.Count)
                        continue;

                    double topY = levelLineYs[levelIndex];
                    double bottomY = levelLineYs[levelIndex + 1];

                    // Convert margin to feet
                    double verticalMarginFt = VerticalMarginMm / 304.8;

                    // Shrink the band vertically by the top/bottom margin
                    double adjustedTopY = topY - verticalMarginFt;
                    double adjustedBottomY = bottomY + verticalMarginFt;
                    double adjustedBandHeight = adjustedTopY - adjustedBottomY;

                    double y;
                    if (total == 1)
                    {
                        y = (adjustedTopY + adjustedBottomY) / 2.0;
                    }
                    else
                    {
                        y = adjustedTopY - ((index + 0.5) * (adjustedBandHeight / total));
                    }

                    XYZ point = new XYZ(x, y, 0);

                    levelPlacementIndex[placementKey]++;


                    var detail = doc.Create.NewFamilyInstance(point, symbol, TargetView) as FamilyInstance;

                    if (detail != null)
                    {
                        // Set the parameters for the detail item
                        SetParameters(detail, entry);

                        // Call the new method for placing tags
                        PlaceTag(detail, point, doc);
                    }

                }

                tx.Commit();
            }
        }
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
        private void SetParameters(FamilyInstance detail, FireAlarmEntry entry)
        {
            // Set the user-defined parameter (e.g., "Comments")
            Parameter userParam = detail.LookupParameter(TagParameterName);
            if (userParam != null && userParam.StorageType == StorageType.String && !userParam.IsReadOnly)
            {
                userParam.Set(entry.Count.ToString());
            }

            // Set the "Ahela nr." parameter
            Parameter ahelaParam = detail.LookupParameter("Ahela nr.");
            if (ahelaParam != null && ahelaParam.StorageType == StorageType.String && !ahelaParam.IsReadOnly)
            {
                ahelaParam.Set(entry.AhelaNr ?? "");
            }
        }


    }
}
