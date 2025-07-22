using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Schema.Collectors;
using Schema.Setup;
using Schema.ExternalEvents;
using Newtonsoft.Json;
using Schema.Helpers;
using System;
using Schema;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Schema
{
    public partial class MainWindow : Window
    {
        private readonly Document _doc;
        private readonly UIDocument _uiDoc;
        private readonly WindowResizer _windowResizer;
        private bool _isDarkMode = true;
        public ObservableCollection<LevelEntry> Levels { get; set; }
        private DateTime _lastEscapePress = DateTime.MinValue;
        private const int EscapeDoublePressThresholdMs = 400;

        private double _detailLineHeight = 0.0; // Used for row spacing calculation
        private double _marginFromEdge = 10.0;

        private ExternalEvent drawLinesEvent;
        private DrawDetailLinesHandler drawLinesHandler;
        public ObservableCollection<FireAlarmEntry> FireAlarmElements { get; set; }

        private readonly ExternalEvent placeDetailItemsEvent;
        private readonly PlaceDetailItemsHandler placeDetailItemsHandler;
        private List<FamilySymbol> _availableTagTypes;
        private readonly ExternalEvent _placeControllerEvent;
        private readonly PlaceControllerHandler _placeControllerHandler;
        private readonly ExternalEvent _drawLinesEvent;
        private readonly DrawLinesHandler _drawLinesHandler;
        private ConfigModel _config;
        private string ViewKey => (ViewComboBox.SelectedItem as ViewDrafting)?.Name ?? "UnknownView";
        private bool _isLoaded = false;
        public int SectionCount { get; set; } = 1;

        private string ProjectKey
        {
            get
            {
                string path = _doc.PathName;
                return !string.IsNullOrWhiteSpace(path)
                    ? Path.GetFileNameWithoutExtension(path)
                    : _doc.Title;
            }
        }
        public class TagDisplay
        {
            public FamilySymbol Symbol { get; set; }
            public string DisplayName { get; set; }
        }

        public MainWindow(UIDocument uiDoc)
        {
            try
            {
                InitializeComponent();

                _doc = uiDoc.Document;
                _uiDoc = uiDoc;
                Topmost = true;

                this.Closed += Window1_Closed;

                _windowResizer = new WindowResizer(this);

                this.MouseMove += Window_MouseMove;
                this.MouseLeftButtonUp += Window_MouseLeftButtonUp;

                if (Application.ResourceAssembly == null)
                {
                    Application.ResourceAssembly = Assembly.GetExecutingAssembly();
                }

                _config = ConfigManager.Load();

                if (_config.WindowLeft.HasValue && _config.WindowTop.HasValue)
                {
                    this.WindowStartupLocation = WindowStartupLocation.Manual;
                    this.Left = _config.WindowLeft.Value;
                    this.Top = _config.WindowTop.Value;
                }
                else
                {
                    this.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                }

                LoadConfig();
                LoadTheme();
                LoadViewsIntoComboBox();

                this.Focusable = true;
                this.Focus();

                InitializeDrawHandler();
                placeDetailItemsHandler = new PlaceDetailItemsHandler();
                placeDetailItemsEvent = ExternalEvent.Create(placeDetailItemsHandler);
                _placeControllerHandler = new PlaceControllerHandler { UiDoc = _uiDoc };
                _placeControllerEvent = ExternalEvent.Create(_placeControllerHandler);

                _drawLinesHandler = new DrawLinesHandler { UiDoc = _uiDoc };
                _drawLinesEvent = ExternalEvent.Create(_drawLinesHandler);

                LoadControllerDetailTypes();
                ConfigManager.Save(_config);
                _config = ConfigManager.Load();
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Startup Error", ex.ToString());
            }
        }
        private string GetCurrentStage()
        {
            return PPCheckBox.IsChecked == true ? "PP" : "TP";
        }
        private string GetCurrentSubsystemKey()
        {
            if (ATSCheckBox.IsChecked == true) return "ATS";
            if (VVSCheckBox.IsChecked == true) return "VVS";
            if (LPSCheckBox.IsChecked == true) return "LPS";
            if (SHSCheckBox.IsChecked == true) return "SHS";
            if (SideCheckBox.IsChecked == true) return "Side";
            return "ATS"; // fallback
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            Levels = new ObservableCollection<LevelEntry>(LevelInfo.GetLevelEntries(_doc));
            DataContext = this;

            string viewName = (ViewComboBox.SelectedItem as ViewDrafting)?.Name ?? "UnnamedView";
            string stageKey = PPCheckBox.IsChecked == true ? "PP" : "TP";

            ProjectConfig projectConfig = null;

            // Try retrieve stage config
            if (_config.Projects.TryGetValue(ProjectKey, out var viewsDict) &&
                viewsDict.TryGetValue(viewName, out var stagesDict) &&
                stagesDict.TryGetValue(stageKey, out var loadedConfig))
            {
                projectConfig = loadedConfig;

                foreach (var level in Levels)
                {
                    level.IsSelected = loadedConfig.SelectedLevels.Contains(level.Name);
                }
            }

            Levels.CollectionChanged += (sender1, args1) => UpdateSelectedCountLabel();

            foreach (var level in Levels)
            {
                level.PropertyChanged += (sender2, args2) =>
                {
                    if (args2.PropertyName == nameof(LevelEntry.IsSelected))
                    {
                        UpdateSelectedCountLabel();

                        // Only save config when we have a valid selected view
                        if (ViewComboBox.SelectedItem is ViewDrafting)
                            SaveConfig();
                    }
                };
            }


            UpdateSelectedCountLabel();
            LoadFireAlarmElements(_doc);

            // ✅ Restore FireAlarmEntry selections
            string subsystemKey = GetCurrentSubsystemKey();
            if (projectConfig?.Subsystems != null &&
                projectConfig.Subsystems.TryGetValue(subsystemKey, out var subsystem))
            {
                if (subsystem.SystemSelection != null)
                {
                    foreach (var entry in FireAlarmElements)
                    {
                        var key = $"{entry.FamilyName}|{entry.TypeName}|{entry.LevelName}|{entry.AhelaNr}";
                        if (subsystem.SystemSelection.TryGetValue(key, out bool isSelected))
                            entry.IsSelected = isSelected;
                    }
                }

                if (subsystem.SystemSections != null)
                {
                    foreach (var entry in FireAlarmElements)
                    {
                        var key = $"{entry.FamilyName}|{entry.TypeName}|{entry.LevelName}|{entry.AhelaNr}";
                        if (subsystem.SystemSections.TryGetValue(key, out int section))
                            entry.SectionNumber = section;
                    }
                }
            }


            // Load available detail item tag types
            _availableTagTypes = new FilteredElementCollector(_doc)
                .OfClass(typeof(FamilySymbol))
                .WhereElementIsElementType()
                .Cast<FamilySymbol>()
                .Where(tag =>
                    tag.Category?.Id.Value == (long)BuiltInCategory.OST_DetailComponentTags)
                .OrderBy(tag => $"{tag.FamilyName} : {tag.Name}")
                .ToList();

            var tagDisplayList = _availableTagTypes
                .Select(t => new TagDisplay
                {
                    Symbol = t,
                    DisplayName = $"{t.FamilyName} : {t.Name}"
                })
                .ToList();

            TagTypeComboBox.ItemsSource = tagDisplayList;
            TagTypeComboBox.DisplayMemberPath = "DisplayName";
            TagTypeComboBox.SelectedValuePath = "Symbol";
            TagTypeComboBox.SelectedIndex = tagDisplayList.Any() ? 0 : -1;

            // ✅ Restore tag settings
            if (!string.IsNullOrEmpty(projectConfig?.SelectedTagName))
            {
                var match = tagDisplayList.FirstOrDefault(t => t.DisplayName == projectConfig.SelectedTagName);
                if (match != null)
                    TagTypeComboBox.SelectedItem = match;
            }

            if (projectConfig != null)
            {
                TagOffsetTextBox.Text = projectConfig.TagOffsetMm.ToString(CultureInfo.InvariantCulture);
                StartOffsetTextBox.Text = projectConfig.StartOffsetMm.ToString(CultureInfo.InvariantCulture);
                RightMarginTextBox.Text = projectConfig.RightOffsetMm.ToString(CultureInfo.InvariantCulture);
                SpacingTextBox.Text = projectConfig.SpacingMm.ToString(CultureInfo.InvariantCulture);
                MarginTextBox.Text = projectConfig.MarginMm.ToString(CultureInfo.InvariantCulture);
                VerticalMarginTextBox.Text = projectConfig.VerticalMarginMm.ToString(CultureInfo.InvariantCulture);
                TagParamater.Text = projectConfig.TagParameterName;
                ItemsInRow.Text = projectConfig.ItemsInRow.ToString();
                RowOffset.Text = projectConfig.RowOffset.ToString(CultureInfo.InvariantCulture);
            }

            // Restore Controller offset
            ControllerOffsetTextBox.Text = projectConfig.ControllerOffsetMm.ToString(CultureInfo.InvariantCulture);

            // Restore Controller type
            if (!string.IsNullOrEmpty(projectConfig.ControllerDetailTypeId))
            {
                var items = ControllerTypeComboBox.ItemsSource as IEnumerable<DetailTypeOption>;
                if (items != null)
                {
                    var match = items.FirstOrDefault(i => i.Id == projectConfig.ControllerDetailTypeId);
                    if (match != null)
                        ControllerTypeComboBox.SelectedItem = match;
                }
            }

            this.Focus();
            Keyboard.Focus(this);
            _isLoaded = true;
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                var now = DateTime.Now;
                if ((now - _lastEscapePress).TotalMilliseconds < EscapeDoublePressThresholdMs)
                {
                    this.Close();
                }
                _lastEscapePress = now;
            }
        }

        private void LoadTheme()
        {
            var assemblyName = Assembly.GetExecutingAssembly().GetName().Name;
            var themeUri = _isDarkMode
                ? $"pack://application:,,,/{assemblyName};component/Themes/DarkTheme.xaml"
                : $"pack://application:,,,/{assemblyName};component/Themes/LightTheme.xaml";

            try
            {
                var resourceDict = new ResourceDictionary
                {
                    Source = new Uri(themeUri, UriKind.Absolute)
                };

                this.Resources.MergedDictionaries.Clear();
                this.Resources.MergedDictionaries.Add(resourceDict);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Failed to load theme: {ex.Message}\nTheme URI: {themeUri}", "Theme Load Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ToggleTheme_Click(object sender, RoutedEventArgs e)
        {
            // Update the state immediately based on ToggleButton's IsChecked property
            _isDarkMode = ThemeToggleButton.IsChecked == true;

            // Reload the theme based on the updated state
            LoadTheme();

            // Explicitly update the icon in case the trigger doesn't update
            if (ThemeToggleButton.Template.FindName("ThemeToggleIcon", ThemeToggleButton)
                is MaterialDesignThemes.Wpf.PackIcon themeIcon)
            {
                themeIcon.Kind = _isDarkMode
                    ? MaterialDesignThemes.Wpf.PackIconKind.ToggleSwitchOffOutline
                    : MaterialDesignThemes.Wpf.PackIconKind.ToggleSwitchOutline;
            }
        }

        private void Window1_Closed(object sender, System.EventArgs e)
        {
            // Save theme and window position
            SaveConfig();

            // Save window position
            _config.WindowLeft = this.Left;
            _config.WindowTop = this.Top;

            ConfigManager.Save(_config);

            // Cleanup
            this.MouseMove -= Window_MouseMove;
            this.MouseLeftButtonUp -= Window_MouseLeftButtonUp;
        }

        
        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                this.DragMove();
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => this.Close();
        private void MinimizeButton_Click(object sender, RoutedEventArgs e) => this.WindowState = System.Windows.WindowState.Minimized;

        private void LeftEdge_MouseEnter(object sender, MouseEventArgs e) => this.Cursor = Cursors.SizeWE; // Horizontal resize cursor for Left Edge
        private void RightEdge_MouseEnter(object sender, MouseEventArgs e) => this.Cursor = Cursors.SizeWE; // Horizontal resize cursor for Right Edge
        private void BottomEdge_MouseEnter(object sender, MouseEventArgs e) => this.Cursor = Cursors.SizeNS; // Vertical resize cursor for Bottom Edge
        private void Edge_MouseLeave(object sender, MouseEventArgs e) => this.Cursor = Cursors.Arrow; // Reset to default cursor
        private void BottomLeftCorner_MouseEnter(object sender, MouseEventArgs e) => this.Cursor = Cursors.SizeNESW; // Diagonal resize cursor (↙) for Bottom-Left Corner
        private void BottomRightCorner_MouseEnter(object sender, MouseEventArgs e) => this.Cursor = Cursors.SizeNWSE; // Diagonal resize cursor (↘) for Bottom-Right Corner
        private void Window_MouseMove(object sender, MouseEventArgs e) => _windowResizer.ResizeWindow(e);
        private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => _windowResizer.StopResizing();
        private void LeftEdge_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => _windowResizer.StartResizing(e, ResizeDirection.Left);
        private void RightEdge_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => _windowResizer.StartResizing(e, ResizeDirection.Right);
        private void BottomEdge_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => _windowResizer.StartResizing(e, ResizeDirection.Bottom);
        private void BottomLeftCorner_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => _windowResizer.StartResizing(e, ResizeDirection.BottomLeft);
        private void BottomRightCorner_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) =>_windowResizer.StartResizing(e, ResizeDirection.BottomRight);
        private void SaveConfig(string subsystemKeyOverride = null)
        {
            string viewName = (ViewComboBox.SelectedItem as ViewDrafting)?.Name ?? "UnnamedView";
            string stageKey = PPCheckBox.IsChecked == true ? "PP" : "TP";
            string subsystemKey = subsystemKeyOverride ?? GetCurrentSubsystemKey(); // 👈 Use override if provided

            // Ensure project dictionary exists
            if (!_config.Projects.ContainsKey(ProjectKey))
                _config.Projects[ProjectKey] = new Dictionary<string, Dictionary<string, ProjectConfig>>();

            // Ensure view dictionary exists
            if (!_config.Projects[ProjectKey].ContainsKey(viewName))
                _config.Projects[ProjectKey][viewName] = new Dictionary<string, ProjectConfig>();

            // Ensure stage config exists
            if (!_config.Projects[ProjectKey][viewName].ContainsKey(stageKey))
                _config.Projects[ProjectKey][viewName][stageKey] = new ProjectConfig();

            var projectConfig = _config.Projects[ProjectKey][viewName][stageKey];

            // Save selected levels
            projectConfig.SelectedLevels = Levels
                .Where(l => l.IsSelected)
                .Select(l => l.Name)
                .ToList();

            // Save selected tag
            if (TagTypeComboBox.SelectedItem is TagDisplay selectedTag)
                projectConfig.SelectedTagName = selectedTag.DisplayName;

            // Save tag offset
            if (double.TryParse(TagOffsetTextBox.Text, out double offsetMm))
                projectConfig.TagOffsetMm = offsetMm;

            // Save FireAlarm checkbox selections
            if (FireAlarmElements != null)
            {
                if (!projectConfig.Subsystems.ContainsKey(subsystemKey))
                    projectConfig.Subsystems[subsystemKey] = new SubsystemConfig();

                var subsystem = projectConfig.Subsystems[subsystemKey];

                subsystem.SystemSelection = FireAlarmElements
                    .GroupBy(entry => $"{entry.FamilyName}|{entry.TypeName}|{entry.LevelName}|{entry.AhelaNr}")
                    .ToDictionary(g => g.Key, g => g.First().IsSelected);

                subsystem.SystemSections = FireAlarmElements
                    .GroupBy(entry => $"{entry.FamilyName}|{entry.TypeName}|{entry.LevelName}|{entry.AhelaNr}")
                    .ToDictionary(g => g.Key, g => g.First().SectionNumber);
            }

            if (double.TryParse(StartOffsetTextBox.Text, out double startOffsetMm))
                projectConfig.StartOffsetMm = startOffsetMm;

            if (double.TryParse(RightMarginTextBox.Text, out double rightOffsetMm))
                projectConfig.RightOffsetMm = rightOffsetMm;

            if (double.TryParse(SpacingTextBox.Text, out double spacingMm))
                projectConfig.SpacingMm = spacingMm;

            if (double.TryParse(MarginTextBox.Text, out double marginMm))
                projectConfig.MarginMm = marginMm;

            if (double.TryParse(VerticalMarginTextBox.Text, out double verticalMarginMm))
                projectConfig.VerticalMarginMm = verticalMarginMm;

            if (double.TryParse(ControllerOffsetTextBox.Text, out double controllerOffsetMm))
                projectConfig.ControllerOffsetMm = controllerOffsetMm;

            if (ControllerTypeComboBox.SelectedItem is DetailTypeOption selectedControllerType)
                projectConfig.ControllerDetailTypeId = selectedControllerType.Id;
            if (int.TryParse(ItemsInRow.Text, out int itemsInRow))
                projectConfig.ItemsInRow = itemsInRow;
            if (double.TryParse(RowOffset.Text, out double rowOffset))
                projectConfig.RowOffset = rowOffset;
            projectConfig.TagParameterName = TagParamater.Text.Trim();
            _config.IsDarkMode = _isDarkMode;

            ConfigManager.Save(_config);
        }

        private void LoadConfig()
        {
            try
            {
                _config = ConfigManager.Load(); // ✅ No arguments

            }
            catch
            {
                _config = new ConfigModel();
            }

            // Apply theme
            _isDarkMode = _config.IsDarkMode;
            ThemeToggleButton.IsChecked = _isDarkMode;

            // Apply theme icon
            if (ThemeToggleButton.Template?.FindName("ThemeToggleIcon", ThemeToggleButton)
                is MaterialDesignThemes.Wpf.PackIcon themeIcon)
            {
                themeIcon.Kind = _isDarkMode
                    ? MaterialDesignThemes.Wpf.PackIconKind.ToggleSwitchOffOutline
                    : MaterialDesignThemes.Wpf.PackIconKind.ToggleSwitchOutline;
            }
        }

        private void LoadViewsIntoComboBox()
        {
            var views = ViewCollector.GetDraftingViews(_doc); // ← Only drafting views

            ViewComboBox.ItemsSource = views;
            ViewComboBox.DisplayMemberPath = "Name";
            ViewComboBox.SelectedIndex = views.Any() ? 0 : -1;
        }
        private void ViewComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ViewComboBox.SelectedItem is ViewDrafting selectedView)
            {
                int scale = selectedView.Scale;

                // Get raw width/height from DrawingArea
                var bounds = DrawingArea.GetBoundaryBox(_doc, selectedView);
                string paperSizeInfo = "";

                if (bounds.HasValue)
                {
                    var (minX, maxX, minY, maxY) = bounds.Value;

                    double widthMm = UnitUtils.ConvertFromInternalUnits(maxX - minX, UnitTypeId.Millimeters);
                    double heightMm = UnitUtils.ConvertFromInternalUnits(maxY - minY, UnitTypeId.Millimeters);

                    // Divide by scale (1:50 => actual paper is boundary/50)
                    double paperWidth = widthMm / scale;
                    double paperHeight = heightMm / scale;

                    // Normalize to match orientation
                    double w = Math.Max(paperWidth, paperHeight);
                    double h = Math.Min(paperWidth, paperHeight);

                    // Find closest ISO paper size
                    string closestPaper = DrawingArea.FindClosestIsoSize(w, h);

                    paperSizeInfo = $"Paper size: {Math.Round(paperWidth, 1)} mm x {Math.Round(paperHeight, 1)} mm (closest: {closestPaper})";

                }

                // Update spacing height for row spacing preview
                var height = DrawingArea.GetBoundaryHeightMm(_doc, selectedView);
                _detailLineHeight = height ?? 0.0;

                BoundaryInfoTextBlock.Text = $"Scale: 1:{scale}   •   {paperSizeInfo}";
                UpdateSelectedCountLabel();
            }
            else
            {
                BoundaryInfoTextBlock.Text = "Please select a drafting view.";
                _detailLineHeight = 0.0;
            }
        }

        private void UpdateSelectedCountLabel()
        {
            int selectedCount = Levels?.Count(l => l.IsSelected) ?? 0;

            // Prevent usage before detail line height is initialized
            if (selectedCount > 0 && _detailLineHeight > 0)
            {
                double spacing = (_detailLineHeight - 2 * _marginFromEdge) / selectedCount;

            }
            else
            {
            }
        }
        private void MarginTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !double.TryParse(((System.Windows.Controls.TextBox)sender).Text + e.Text, out _);
        }
        private void MarginTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (double.TryParse(((System.Windows.Controls.TextBox)sender).Text, out double result))
            {
                _marginFromEdge = result;
                UpdateSelectedCountLabel();
            }
        }
        private void DrawLinesButton_Click(object sender, RoutedEventArgs e)
        {
            if (_uiDoc == null)
            {
                TaskDialog.Show("Error", "Revit document is not available.");
                return;
            }

            var selectedView = ViewComboBox.SelectedItem as ViewDrafting;
            if (selectedView == null)
            {
                TaskDialog.Show("Error", "Please select a drafting view.");
                return;
            }

            if (!double.TryParse(MarginTextBox.Text, out double marginMm))
            {
                TaskDialog.Show("Error", "Invalid margin value.");
                return;
            }

            if (!double.TryParse(SpacingTextBox.Text, out double spacingMm))
            {
                TaskDialog.Show("Error", "Invalid spacing value.");
                return;
            }

            var selectedLevels = Levels?.Where(l => l.IsSelected).ToList() ?? new List<LevelEntry>();
            if (selectedLevels.Count == 0)
            {
                TaskDialog.Show("Info", "No levels selected.");
                return;
            }

            _drawLinesHandler.UiDoc = _uiDoc;
            _drawLinesHandler.TargetView = selectedView;
            _drawLinesHandler.MarginMm = marginMm;
            _drawLinesHandler.SpacingMm = spacingMm;

            if (TPCheckBox.IsChecked == true)
            {
                // For TP, draw lines for each row (section) where the same AhelaNr appears
                // Group FireAlarmElements by SectionNumber and LevelName
                var grouped = FireAlarmElements
                    .Where(entry => entry.IsSelected)
                    .GroupBy(entry => new { entry.LevelName, entry.SectionNumber })
                    .OrderBy(g => GetLevelSortKey(g.Key.LevelName))
                    .ThenBy(g => g.Key.SectionNumber)
                    .ToList();

                var levelsForRows = new List<Level>();
                foreach (var group in grouped)
                {
                    var level = FindLevelByName(group.Key.LevelName);
                    if (level != null)
                    {
                        // Add the level for each section (row)
                        levelsForRows.Add(level);
                    }
                }
                _drawLinesHandler.SelectedLevels = levelsForRows;
            }
            else
            {
                _drawLinesHandler.SelectedLevels = selectedLevels
                    .Select(le => FindLevelByName(le.Name))
                    .Where(l => l != null)
                    .ToList();
            }

            _drawLinesHandler.CurrentPattern = PPCheckBox.IsChecked == true
                ? PlacementPattern.PP
                : PlacementPattern.TP;

            _drawLinesEvent.Raise();
        }

        private void InitializeDrawHandler()
        {
            drawLinesHandler = new DrawDetailLinesHandler();
            drawLinesEvent = ExternalEvent.Create(drawLinesHandler);
        }

        private List<FireAlarmEntry> GetFlatFireAlarmEntries(List<FireAlarmEntry> groupedEntries)
        {
            var flatList = new List<FireAlarmEntry>();
            foreach (var entry in groupedEntries)
            {
                for (int i = 0; i < entry.Count; i++)
                {
                    flatList.Add(new FireAlarmEntry
                    {
                        Element = entry.Element,
                        FamilyName = entry.FamilyName,
                        TypeName = entry.TypeName,
                        LevelName = entry.LevelName,
                        AhelaNr = entry.AhelaNr,
                        Count = 1,
                        AvailableDetailTypes = entry.AvailableDetailTypes,
                        SelectedDetailType = entry.SelectedDetailType,
                        IsSelected = entry.IsSelected,
                        SectionNumber = entry.SectionNumber,
                        Aadress = entry.Aadress
                    });
                }
            }
            return flatList;
        }

        private List<FireAlarmEntry> GetGroupedByLevelAhelaFamily(List<FireAlarmEntry> entries)
        {
            // Group by LevelName, then AhelaNr, then FamilyName, aggregate Count
            return entries
                .GroupBy(e => new { e.LevelName, e.AhelaNr, e.FamilyName })
                .OrderBy(g => GetLevelSortKey(g.Key.LevelName)) // Sort by level, negative values first
                .ThenBy(g => g.Key.AhelaNr)
                .ThenBy(g => g.Key.FamilyName)
                .Select(g =>
                {
                    var first = g.First();
                    return new FireAlarmEntry
                    {
                        Element = first.Element,
                        FamilyName = first.FamilyName,
                        TypeName = first.TypeName,
                        LevelName = first.LevelName,
                        AhelaNr = first.AhelaNr,
                        Count = g.Sum(e => e.Count),
                        AvailableDetailTypes = first.AvailableDetailTypes,
                        SelectedDetailType = first.SelectedDetailType,
                        IsSelected = g.Any(e => e.IsSelected),
                        SectionNumber = first.SectionNumber,
                        Aadress = first.Aadress
                    };
                })
                .ToList();
        }

        // Helper to sort levels, negative values first
        private double GetLevelSortKey(string levelName)
        {
            // Try to parse elevation from level name, fallback to 0
            var level = Levels?.FirstOrDefault(l => l.Name == levelName);
            return level != null ? level.Elevation : 0;
        }
        public void LoadFireAlarmElements(Document doc)
        {
            var entries = FireAlarmCollector.GetFireAlarmElements(doc);

            // Always apply section logic for both PP and TP
            if (TPCheckBox.IsChecked == true)
            {
                FireAlarmElements = new ObservableCollection<FireAlarmEntry>(GetSortedFlatFireAlarmEntries(GetFlatFireAlarmEntries(entries)));
            }
            else // PP
            {
                FireAlarmElements = new ObservableCollection<FireAlarmEntry>(GetGroupedByLevelAhelaFamily(entries));
            }
            SystemDataGrid.ItemsSource = FireAlarmElements;

            // Restore selection and section state from config after loading
            string subsystemKey = GetCurrentSubsystemKey();
            string viewName = (ViewComboBox.SelectedItem as ViewDrafting)?.Name ?? "UnnamedView";
            string stageKey = PPCheckBox.IsChecked == true ? "PP" : "TP";
            if (_config.Projects.TryGetValue(ProjectKey, out var viewsDict) &&
                viewsDict.TryGetValue(viewName, out var stagesDict) &&
                stagesDict.TryGetValue(stageKey, out var projectConfig) &&
                projectConfig.Subsystems.TryGetValue(subsystemKey, out var subsystem))
            {
                if (subsystem.SystemSelection != null)
                {
                    foreach (var entry in FireAlarmElements)
                    {
                        var key = $"{entry.FamilyName}|{entry.TypeName}|{entry.LevelName}|{entry.AhelaNr}";
                        if (subsystem.SystemSelection.TryGetValue(key, out bool isSelected))
                            entry.IsSelected = isSelected;
                    }
                }
                if (subsystem.SystemSections != null)
                {
                    foreach (var entry in FireAlarmElements)
                    {
                        var key = $"{entry.FamilyName}|{entry.TypeName}|{entry.LevelName}|{entry.AhelaNr}";
                        if (subsystem.SystemSections.TryGetValue(key, out int section))
                            entry.SectionNumber = section;
                    }
                }
            }
        }
        private List<FireAlarmEntry> GetSortedFlatFireAlarmEntries(List<FireAlarmEntry> entries)
        {
            return entries
                .OrderBy(e => GetLevelSortKey(e.LevelName))
                .ThenBy(e => e.AhelaNr)
                .ThenBy(e => e.Aadress) // Seadme Nr
                .ThenBy(e => e.FamilyName)
                .ToList();
        }
            
        private void HandleSubsystemCheckboxChange(string subsystemKey, CheckBox senderCheckbox)
        {
            if (_doc == null) return;

            // Save previous state before switching
            if (senderCheckbox.IsChecked == false)
            {
                SaveConfig(subsystemKey); // Use the subsystem that is being turned off
            }

            // Turn off other checkboxes if this one is turned on
            if (senderCheckbox.IsChecked == true)
            {
                if (subsystemKey != "ATS") ATSCheckBox.IsChecked = false;
                if (subsystemKey != "VVS") VVSCheckBox.IsChecked = false;
                if (subsystemKey != "LPS") LPSCheckBox.IsChecked = false;
                if (subsystemKey != "SHS") SHSCheckBox.IsChecked = false;
                if (subsystemKey != "Side") SideCheckBox.IsChecked = false;
            }

            LoadConfig();                // Load latest config
            FireAlarmElements.Clear();

            switch (subsystemKey)
            {
                case "ATS":
                    foreach (var item in FireAlarmCollector.GetFireAlarmElements(_doc))
                        FireAlarmElements.Add(item);
                    break;

                case "VVS":
                    foreach (var item in VVSCollector.GetSecurityDeviceElements(_doc))
                    {
                        FireAlarmElements.Add(new FireAlarmEntry
                        {
                            FamilyName = item.FamilyName,
                            TypeName = item.TypeName,
                            LevelName = item.LevelName,
                            AhelaNr = item.AhelaNr,
                            Count = item.Count,
                            Element = item.Element,
                            AvailableDetailTypes = item.AvailableDetailTypes,
                            SelectedDetailType = item.SelectedDetailType
                        });
                    }
                    break;

                case "LPS":
                    foreach (var item in LPSCollector.GetSecurityDeviceElements(_doc))
                    {
                        FireAlarmElements.Add(new FireAlarmEntry
                        {
                            FamilyName = item.FamilyName,
                            TypeName = item.TypeName,
                            LevelName = item.LevelName,
                            AhelaNr = item.AhelaNr,
                            Count = item.Count,
                            Element = item.Element,
                            AvailableDetailTypes = item.AvailableDetailTypes,
                            SelectedDetailType = item.SelectedDetailType
                        });
                    }
                    break;

                case "SHS":
                    foreach (var item in SHSCollector.GetSecurityDeviceElements(_doc))
                    {
                        FireAlarmElements.Add(new FireAlarmEntry
                        {
                            FamilyName = item.FamilyName,
                            TypeName = item.TypeName,
                            LevelName = item.LevelName,
                            AhelaNr = item.AhelaNr,
                            Count = item.Count,
                            Element = item.Element,
                            AvailableDetailTypes = item.AvailableDetailTypes,
                            SelectedDetailType = item.SelectedDetailType
                        });
                    }
                    break;

                case "Side":
                    foreach (var item in SideCollector.GetSecurityDeviceElements(_doc))
                    {
                        FireAlarmElements.Add(new FireAlarmEntry
                        {
                            FamilyName = item.FamilyName,
                            TypeName = item.TypeName,
                            LevelName = item.LevelName,
                            AhelaNr = item.AhelaNr,
                            Count = item.Count,
                            Element = item.Element,
                            AvailableDetailTypes = item.AvailableDetailTypes,
                            SelectedDetailType = item.SelectedDetailType
                        });
                    }
                    break;

            }
            // Populate UI with elements from the selected subsystem
            ApplyConfigToUI();           // Apply config to current view/UI
        }
        private void ATSCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            HandleSubsystemCheckboxChange("ATS", ATSCheckBox);
        }

        private void VVSCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            HandleSubsystemCheckboxChange("VVS", VVSCheckBox);
        }

        private void LPSCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            HandleSubsystemCheckboxChange("LPS", LPSCheckBox);
        }

        private void SHSCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            HandleSubsystemCheckboxChange("SHS", SHSCheckBox);
        }

        private void SideCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            HandleSubsystemCheckboxChange("Side", SideCheckBox);
        }

        private void ApplyConfigToUI()
        {
            string viewName = (ViewComboBox.SelectedItem as ViewDrafting)?.Name ?? "UnnamedView";
            string stageKey = GetCurrentStage();

            if (_config.Projects.TryGetValue(ProjectKey, out var viewsDict) &&
                viewsDict.TryGetValue(viewName, out var stagesDict) &&
                stagesDict.TryGetValue(stageKey, out var projectConfig))
            {
                string subsystemKey = GetCurrentSubsystemKey();
                if (projectConfig.Subsystems.TryGetValue(subsystemKey, out var subsystem))
                {
                    if (subsystem.SystemSelection != null)
                    {
                        foreach (var entry in FireAlarmElements)
                        {
                            var key = $"{entry.FamilyName}|{entry.TypeName}|{entry.LevelName}|{entry.AhelaNr}";
                            if (subsystem.SystemSelection.TryGetValue(key, out bool savedSelection))
                                entry.IsSelected = savedSelection;
                        }
                    }

                    if (subsystem.SystemSections != null)
                    {
                        foreach (var entry in FireAlarmElements)
                        {
                            var key = $"{entry.FamilyName}|{entry.TypeName}|{entry.LevelName}|{entry.AhelaNr}";
                            if (subsystem.SystemSections.TryGetValue(key, out int savedSection))
                                entry.SectionNumber = savedSection;
                        }
                    }
                }
                ItemsInRow.Text = projectConfig.ItemsInRow.ToString();
                RowOffset.Text = projectConfig.RowOffset.ToString(CultureInfo.InvariantCulture);
                // Restore other fields
                TagOffsetTextBox.Text = projectConfig.TagOffsetMm.ToString(CultureInfo.InvariantCulture);
                StartOffsetTextBox.Text = projectConfig.StartOffsetMm.ToString(CultureInfo.InvariantCulture);
                RightMarginTextBox.Text = projectConfig.RightOffsetMm.ToString(CultureInfo.InvariantCulture);
                SpacingTextBox.Text = projectConfig.SpacingMm.ToString(CultureInfo.InvariantCulture);
                MarginTextBox.Text = projectConfig.MarginMm.ToString(CultureInfo.InvariantCulture);
                VerticalMarginTextBox.Text = projectConfig.VerticalMarginMm.ToString(CultureInfo.InvariantCulture);
                TagParamater.Text = projectConfig.TagParameterName;

                // Restore selected tag
                var tagDisplayList = TagTypeComboBox.ItemsSource as List<TagDisplay>;
                if (!string.IsNullOrEmpty(projectConfig.SelectedTagName) && tagDisplayList != null)
                {
                    var match = tagDisplayList.FirstOrDefault(t => t.DisplayName == projectConfig.SelectedTagName);
                    if (match != null)
                        TagTypeComboBox.SelectedItem = match;
                }

                // Restore controller values
                ControllerOffsetTextBox.Text = projectConfig.ControllerOffsetMm.ToString(CultureInfo.InvariantCulture);

                if (!string.IsNullOrEmpty(projectConfig.ControllerDetailTypeId))
                {
                    var items = ControllerTypeComboBox.ItemsSource as IEnumerable<DetailTypeOption>;
                    var match = items?.FirstOrDefault(i => i.Id == projectConfig.ControllerDetailTypeId);
                    if (match != null)
                        ControllerTypeComboBox.SelectedItem = match;
                }
            }
        }

        private void PlaceDetailsButton_Click(object sender, RoutedEventArgs e)
        {

            if (!(ViewComboBox.SelectedItem is ViewDrafting selectedView))
                return;

            // Parse tag offset (in mm), default to 150mm if invalid
            double mmOffset;
            if (!double.TryParse(TagOffsetTextBox.Text, out mmOffset))
                mmOffset = 150;

            // Convert to feet
            double offsetFt = mmOffset / 304.8;

            var selectedTagDisplay = TagTypeComboBox.SelectedItem as TagDisplay;

            placeDetailItemsHandler.UiDoc = _uiDoc;
            placeDetailItemsHandler.TargetView = selectedView;
            placeDetailItemsHandler.FireAlarmEntries = FireAlarmElements.ToList();
            placeDetailItemsHandler.LevelEntries = Levels.ToList();
            placeDetailItemsHandler.SelectedTagType = selectedTagDisplay?.Symbol;
            placeDetailItemsHandler.TagOffset = offsetFt; // 👈 new
            if (double.TryParse(StartOffsetTextBox.Text, out double startOffsetMm))
                placeDetailItemsHandler.StartOffsetMm = startOffsetMm;

            if (double.TryParse(RightMarginTextBox.Text, out double rightOffsetMm))
                placeDetailItemsHandler.RightOffsetMm = rightOffsetMm;

            if (double.TryParse(SpacingTextBox.Text, out double spacingMm))
                placeDetailItemsHandler.SpacingMm = spacingMm;

            if (double.TryParse(VerticalMarginTextBox.Text, out double verticalMarginMm))
                placeDetailItemsHandler.VerticalMarginMm = verticalMarginMm;

            if (int.TryParse(ItemsInRow.Text, out int itemsInRow))
                placeDetailItemsHandler.ItemsInRow = itemsInRow;
            if (double.TryParse(RowOffset.Text, out double rowOffset))
                placeDetailItemsHandler.RowOffset = rowOffset;

            placeDetailItemsHandler.SectionCount = SectionCount;
            placeDetailItemsHandler.TagParameterName = TagParamater.Text.Trim();

            // Set the pattern based on the checkboxes
            placeDetailItemsHandler.CurrentPattern = PPCheckBox.IsChecked == true
                ? PlaceDetailItemsHandler.PlacementPattern.PP
                : PlaceDetailItemsHandler.PlacementPattern.TP;

            // Now raise the event to trigger placement
            placeDetailItemsEvent.Raise();
        }
        private void PlaceKSButton_Click(object sender, RoutedEventArgs e)
        {
            _placeControllerHandler.UiDoc = _uiDoc;
            _placeControllerHandler.OffsetText = ControllerOffsetTextBox.Text;

            if (ControllerTypeComboBox.SelectedItem is DetailTypeOption selectedOption)
            {
                // Use Int64 for ElementId constructor to avoid obsolete warning
                _placeControllerHandler.SelectedSymbolId = new ElementId(long.Parse(selectedOption.Id));
                _placeControllerEvent.Raise();
            }
            else
            {
                TaskDialog.Show("Error", "Please select a controller detail item.");
            }
        }
        public class DetailTypeOption
        {
            public string DisplayName { get; set; }
            public string Id { get; set; } // ← store ElementId.ToString()

            public override string ToString() => DisplayName;
        }

        private void LoadControllerDetailTypes()
        {
            var allDetailTypes = new FilteredElementCollector(_doc)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(BuiltInCategory.OST_DetailComponents)
                .Cast<FamilySymbol>()
                .Where(f =>
                    f.FamilyName.Contains("KS") || f.Name.Contains("KS")) // Case-sensitive
                .Select(f => new DetailTypeOption
                {
                    DisplayName = $"{f.FamilyName} : {f.Name}",
                    Id = f.Id.Value.ToString()
                })
                .OrderBy(o => o.DisplayName)
                .ToList();

            ControllerTypeComboBox.ItemsSource = allDetailTypes;
            ControllerTypeComboBox.SelectedIndex = allDetailTypes.Any() ? 0 : -1;
        }
        private void DrawLines_Click(object sender, RoutedEventArgs e)
        {
            if (_uiDoc == null)
            {
                TaskDialog.Show("Error", "Revit document is not available.");
                return;
            }

            var selectedView = ViewComboBox.SelectedItem as ViewDrafting;
            if (selectedView == null)
            {
                TaskDialog.Show("Error", "Please select a drafting view.");
                return;
            }

            if (!double.TryParse(MarginTextBox.Text, out double marginMm))
            {
                TaskDialog.Show("Error", "Invalid margin value.");
                return;
            }

            if (!double.TryParse(SpacingTextBox.Text, out double spacingMm))
            {
                TaskDialog.Show("Error", "Invalid spacing value.");
                return;
            }

            var selectedLevels = Levels?.Where(l => l.IsSelected).ToList();
            if (selectedLevels == null || selectedLevels.Count == 0)
            {
                TaskDialog.Show("Info", "No levels selected.");
                return;
            }

            drawLinesHandler.UiDoc = _uiDoc;
            drawLinesHandler.TargetView = selectedView;
            drawLinesHandler.MarginMm = marginMm;
            drawLinesHandler.SpacingMm = spacingMm;
            drawLinesHandler.SelectedLevels = selectedLevels;

            drawLinesEvent.Raise();
        }

        private void PPCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SaveConfig(); // Save current config before switching
            if (TPCheckBox != null && TPCheckBox.IsChecked == true)
                TPCheckBox.IsChecked = false;
            placeDetailItemsHandler.CurrentPattern = PlaceDetailItemsHandler.PlacementPattern.PP;
            LoadFireAlarmElements(_doc);
            ApplyConfigToUI();
        }

        private void TPCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SaveConfig(); // Save current config before switching
            if (PPCheckBox != null && PPCheckBox.IsChecked == true)
                PPCheckBox.IsChecked = false;
            placeDetailItemsHandler.CurrentPattern = PlaceDetailItemsHandler.PlacementPattern.TP;
            LoadFireAlarmElements(_doc);
        }

        private void PPCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            if (TPCheckBox != null && TPCheckBox.IsChecked == false)
                TPCheckBox.IsChecked = true;
        }

        private void TPCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            if (PPCheckBox != null && PPCheckBox.IsChecked == false)
                PPCheckBox.IsChecked = true;
        }
        private void SystemDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {

        }

        private void SetSectionNumber_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(BulkValueTextBox.Text, out int sectionValue))
            {
                foreach (FireAlarmEntry entry in SystemDataGrid.SelectedItems)
                {
                    entry.SectionNumber = sectionValue;
                }
            }
        }

        private Level FindLevelByName(string name)
        {
            // Use the Revit API to find the Level element by name
            return new FilteredElementCollector(_doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .FirstOrDefault(l => l.Name == name);
        }
    }
}
