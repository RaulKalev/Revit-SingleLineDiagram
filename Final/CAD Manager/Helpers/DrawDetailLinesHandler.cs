using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Schema.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Schema.ExternalEvents
{
    public class DrawDetailLinesHandler : IExternalEventHandler
    {
        public UIDocument UiDoc { get; set; }
        public Document Doc => UiDoc.Document;
        public ViewDrafting TargetView { get; set; }
        public double MarginMm { get; set; } // User input
        public List<LevelEntry> SelectedLevels { get; set; } = new List<LevelEntry>();
        public double SpacingMm { get; set; }

        public void Execute(UIApplication app)
        {
            if (UiDoc == null || TargetView == null || SelectedLevels == null)
                return;

            var selectedLevels = SelectedLevels.Where(l => l.IsSelected).ToList();
            if (!selectedLevels.Any()) return;

            double offset = UnitUtils.ConvertToInternalUnits(MarginMm, UnitTypeId.Millimeters);

            // Collect boundary lines
            var boundaryLines = new FilteredElementCollector(Doc, TargetView.Id)
                .OfClass(typeof(CurveElement))
                .Cast<CurveElement>()
                .Where(el => el.LineStyle?.Name == "xx_Boundary")
                .ToList();

            if (!boundaryLines.Any()) return;

            var points = boundaryLines
                .SelectMany(l => new[] { l.GeometryCurve.GetEndPoint(0), l.GeometryCurve.GetEndPoint(1) })
                .ToList();

            double minX = points.Min(p => p.X) + offset;
            double maxX = points.Max(p => p.X) - offset;
            double minY = points.Min(p => p.Y) + offset;
            double maxY = points.Max(p => p.Y) - offset;

            double spacing = (maxY - minY) / selectedLevels.Count;
            double verticalOffset = UnitUtils.ConvertToInternalUnits(200.0, UnitTypeId.Millimeters);


            // Find the detail line style
            var style = DrawingArea.GetDetailLineStyleByName(Doc, "xx_Level");
            if (style == null)
            {
                TaskDialog.Show("Error", "Detail line style 'xx_Level' not found.");
                return;
            }

            // Find the text note type named "Calibri - 2.0 mm"
            var textNoteType = new FilteredElementCollector(Doc)
                .OfClass(typeof(TextNoteType))
                .Cast<TextNoteType>()
                .FirstOrDefault(t => t.Name == "Calibri - 2.0 mm");

            if (textNoteType == null)
            {
                TaskDialog.Show("Error", "Text note type 'Calibri - 2.0 mm' not found.");
                return;
            }

            using (Transaction tx = new Transaction(Doc, "Draw Level Lines with Labels"))
            {
                tx.Start();

                // Delete existing lines with style 'xx_Level'
                var existingLevelLines = new FilteredElementCollector(Doc, TargetView.Id)
                    .OfClass(typeof(CurveElement))
                    .Cast<CurveElement>()
                    .Where(el => el.LineStyle != null && el.LineStyle.Name == "xx_Level")
                    .Select(el => el.Id)
                    .ToList();

                if (existingLevelLines.Any())
                {
                    Doc.Delete(existingLevelLines);
                }

                // Delete existing text notes in the target view
                var existingTextNotes = new FilteredElementCollector(Doc, TargetView.Id)
                    .OfClass(typeof(TextNote))
                    .Cast<TextNote>()
                    .Select(tn => tn.Id)
                    .ToList();

                if (existingTextNotes.Any())
                {
                    Doc.Delete(existingTextNotes);
                }
                // 1. Draw extra top line
                double extraY = maxY;
                var extraStart = new XYZ(minX, extraY, 0);
                var extraEnd = new XYZ(maxX, extraY, 0);

                var extraLine = Line.CreateBound(extraStart, extraEnd);
                var extraCurve = Doc.Create.NewDetailCurve(TargetView, extraLine);
                extraCurve.LineStyle = style;

                string extraLabel = selectedLevels.Last().Name;

                // 📉 Place text **below** the top line
                double textOffsetBelowLine = UnitUtils.ConvertToInternalUnits(-200.0, UnitTypeId.Millimeters);
                var extraLeft = new XYZ(minX, extraY + textOffsetBelowLine, 0);
                var extraRight = new XYZ(maxX, extraY + textOffsetBelowLine, 0);

                TextNoteOptions extraOpts = new TextNoteOptions
                {
                    VerticalAlignment = VerticalTextAlignment.Bottom, // text sits below the point
                    HorizontalAlignment = HorizontalTextAlignment.Left,
                    TypeId = textNoteType.Id
                };
                _ = TextNote.Create(Doc, TargetView.Id, extraLeft, extraLabel, extraOpts);

                extraOpts.HorizontalAlignment = HorizontalTextAlignment.Right;
                _ = TextNote.Create(Doc, TargetView.Id, extraRight, extraLabel, extraOpts);


                // Draw new lines and add labels
                for (int i = 0; i < selectedLevels.Count; i++)
                {
                    var level = selectedLevels[i];
                    double y = minY + (i * spacing);
                    var start = new XYZ(minX, y, 0);
                    var end = new XYZ(maxX, y, 0);

                    var line = Line.CreateBound(start, end);
                    var curve = Doc.Create.NewDetailCurve(TargetView, line);
                    curve.LineStyle = style;

                    // Text vertical offset for centering on line
                    var leftTextPos = new XYZ(minX, y + verticalOffset, 0);
                    var rightTextPos = new XYZ(maxX, y + verticalOffset, 0);

                    TextNoteOptions opts = new TextNoteOptions
                    {
                        VerticalAlignment = VerticalTextAlignment.Top,
                        HorizontalAlignment = HorizontalTextAlignment.Left,
                        TypeId = textNoteType.Id
                    };
                    _ = TextNote.Create(Doc, TargetView.Id, leftTextPos, level.Name, opts);

                    opts.HorizontalAlignment = HorizontalTextAlignment.Right;
                    _ = TextNote.Create(Doc, TargetView.Id, rightTextPos, level.Name, opts);

                    // ✅ Draw "level below" label at Y = 0 (on line), if not the first level
                    if (i > 0)
                    {
                        string lowerLevelName = selectedLevels[i - 1].Name;

                        var labelY = y; // same Y as the line
                        var lowerLeft = new XYZ(minX, labelY, 0);
                        var lowerRight = new XYZ(maxX, labelY, 0);

                        TextNoteOptions belowOpts = new TextNoteOptions
                        {
                            VerticalAlignment = VerticalTextAlignment.Top,
                            HorizontalAlignment = HorizontalTextAlignment.Left,
                            TypeId = textNoteType.Id
                        };
                        _ = TextNote.Create(Doc, TargetView.Id, lowerLeft, lowerLevelName, belowOpts);

                        belowOpts.HorizontalAlignment = HorizontalTextAlignment.Right;
                        _ = TextNote.Create(Doc, TargetView.Id, lowerRight, lowerLevelName, belowOpts);
                    }
                }


                tx.Commit();
            }
        }

        public string GetName() => "Draw Detail Lines for Levels";
    }
}
