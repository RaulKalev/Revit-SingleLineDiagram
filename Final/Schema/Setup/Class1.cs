using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace Schema
{
    [Transaction(TransactionMode.Manual)]
    public class Class1 : IExternalCommand
    {
        private static MainWindow _windowInstance; // Store a static reference to the window

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uiDoc = commandData.Application.ActiveUIDocument;
            Document doc = uiDoc.Document;
            View currentView = doc.ActiveView;

            try
            {
                if (_windowInstance == null || !_windowInstance.IsVisible)
                {

                    // Create a new window instance
                    _windowInstance = new MainWindow(uiDoc);
                    _windowInstance.Closed += (s, e) => _windowInstance = null; // Clear the reference when closed
                    _windowInstance.Show();
                }
                else
                {
                    // If the window is minimized, restore it
                    if (_windowInstance.WindowState == WindowState.Minimized)
                    {
                        _windowInstance.WindowState = WindowState.Normal;
                    }

                    // Bring the existing window to the front
                    _windowInstance.Activate();
                }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Error", ex.Message);
                return Result.Failed;
            }
        }
    }
}
