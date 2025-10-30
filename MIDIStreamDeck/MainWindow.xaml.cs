using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Xml;
using System.Linq;

namespace MIDIStreamDeck
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        // Color palette - using Color.FromRgb for better performance
        private readonly SolidColorBrush colorWhite = new SolidColorBrush(Color.FromRgb(255, 255, 255));
        private readonly SolidColorBrush colorGreen = new SolidColorBrush(Color.FromRgb(50, 255, 50));
        private readonly SolidColorBrush colorRed = new SolidColorBrush(Color.FromRgb(255, 50, 50));
        private readonly SolidColorBrush colorRedLight1 = new SolidColorBrush(Color.FromRgb(255, 91, 91));
        private readonly SolidColorBrush colorRedLight2 = new SolidColorBrush(Color.FromRgb(255, 132, 132));
        private readonly SolidColorBrush colorRedLight3 = new SolidColorBrush(Color.FromRgb(255, 173, 173));
        private readonly SolidColorBrush colorBlueLight = new SolidColorBrush(Color.FromRgb(100, 200, 255));
        private readonly SolidColorBrush colorBlueMedium = new SolidColorBrush(Color.FromRgb(50, 150, 255));
        
        // SVG path caching for keyboard keys and color restoration
        private Dictionary<string, System.Windows.Shapes.Path> svgPaths = new Dictionary<string, System.Windows.Shapes.Path>();
        private Dictionary<string, Brush> originalColors = new Dictionary<string, Brush>();
        private Dictionary<Button, System.Windows.Shapes.Path> padOuterPaths = new Dictionary<Button, System.Windows.Shapes.Path>();
        
        // Hover colors for different button types
        private readonly SolidColorBrush hoverColorWhite;
        private readonly SolidColorBrush hoverColorBlack;
        private readonly SolidColorBrush hoverColorPadOuter;
        
        // Bank button state (toggles between A and B)
        private System.Windows.Shapes.Path? bankButtonOuterPath = null;
        private System.Windows.Shapes.Path? bankATextPath = null;
        private System.Windows.Shapes.Path? bankBTextPath = null;
        private bool isBankStateB = false;
        private readonly SolidColorBrush bankButtonGreen;
        private readonly SolidColorBrush bankButtonRed;
        
        // Octave tracking (0-8 range, displayed via color gradient)
        private System.Windows.Shapes.Path? octaveDownButtonOuterPath = null;
        private System.Windows.Shapes.Path? octaveUpButtonOuterPath = null;
        private System.Windows.Shapes.Path? octaveDownButtonTextPath = null;
        private System.Windows.Shapes.Path? octaveUpButtonTextPath = null;
        private int currentOctave = 4;
        
        public MainWindow()
        {
            hoverColorWhite = colorBlueLight;
            hoverColorBlack = colorBlueMedium;
            hoverColorPadOuter = colorRed;
            bankButtonGreen = colorGreen;
            bankButtonRed = colorRed;
            
            InitializeComponent();
            MouseDown += Window_MouseDown;
            SourceInitialized += (s, e) => (PresentationSource.FromVisual(this) as System.Windows.Interop.HwndSource)?.AddHook(WndProc);
            Loaded += MainWindow_Loaded;
        }
        
        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            LoadSvgPaths();
            LoadPadSvgs();
            LoadBank();
            LoadOctaveButtons();
            LoadAnalogStickBoundary();
            
            WireKeyButtonEvents();
            WirePadButtonEvents();
            WireBankEvents();
            WireOctaveButtonEvents();
            WireKnobEvents();
            WireAnalogStickEvents();
        }
        
        /// <summary>
        /// Loads the keyboard SVG file and parses individual path elements for each key.
        /// Each path is cached for hover effects and color restoration.
        /// </summary>
        private void LoadSvgPaths()
        {
            try
            {
                var resourceUri = new Uri("pack://application:,,,/svg/KeysCombined.svg");
                var resourceStream = Application.GetResourceStream(resourceUri) ?? throw new Exception("Could not load KeysCombined.svg");
                
                XmlDocument doc = new XmlDocument();
                doc.Load(resourceStream.Stream);
                
                XmlNamespaceManager nsmgr = new XmlNamespaceManager(doc.NameTable);
                nsmgr.AddNamespace("svg", "http://www.w3.org/2000/svg");
                
                // Parse viewBox to calculate scale factors for our canvas size (990x275)
                var svgElement = doc.DocumentElement;
                var viewBoxParts = (svgElement?.GetAttribute("viewBox") ?? "").Split(' ');
                double svgWidth = double.Parse(viewBoxParts[2]);
                double svgHeight = double.Parse(viewBoxParts[3]);
                double scaleX = 990.0 / svgWidth;
                double scaleY = 275.0 / svgHeight;
                
                System.Diagnostics.Debug.WriteLine($"SVG ViewBox: {svgWidth}x{svgHeight}, Scale: {scaleX}x{scaleY}");
                
                var pathNodes = doc.SelectNodes("//svg:path", nsmgr);
                System.Diagnostics.Debug.WriteLine($"Found {pathNodes?.Count ?? 0} path elements in SVG");
                
                if (pathNodes != null)
                {
                    foreach (XmlNode pathNode in pathNodes)
                    {
                        string id = pathNode.Attributes?["id"]?.Value ?? "";
                        string d = pathNode.Attributes?["d"]?.Value ?? "";
                        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(d)) continue;
                        
                        // Extract fill color from style attribute
                        string fill = pathNode.Attributes?["style"]?.Value?.Split(';')
                            .FirstOrDefault(s => s.Trim().StartsWith("fill:"))?.Split(':')[1]?.Trim() ?? "#ffffff";
                        
                        // Parse and apply SVG transforms (translate from parent group or path itself)
                        var transformGroup = new TransformGroup();
                        string? transformStr = pathNode.ParentNode?.Attributes?["transform"]?.Value ?? pathNode.Attributes?["transform"]?.Value;
                        
                        if (!string.IsNullOrEmpty(transformStr) && transformStr.Contains("translate"))
                        {
                            var match = System.Text.RegularExpressions.Regex.Match(transformStr, @"translate\(([^,]+),([^)]+)\)");
                            if (match.Success)
                            {
                                double tx = double.Parse(match.Groups[1].Value.Trim());
                                double ty = double.Parse(match.Groups[2].Value.Trim());
                                transformGroup.Children.Add(new TranslateTransform(tx, ty));
                            }
                        }
                        
                        transformGroup.Children.Add(new ScaleTransform(scaleX, scaleY));
                        
                        var path = new System.Windows.Shapes.Path
                        {
                            Data = Geometry.Parse(d),
                            Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(fill)),
                            Stroke = Brushes.Transparent,
                            StrokeThickness = 0,
                            RenderTransform = transformGroup
                        };
                        
                        KeyboardSvgCanvas.Children.Add(path);
                        svgPaths[id] = path;
                        originalColors[id] = path.Fill.Clone();
                        
                        System.Diagnostics.Debug.WriteLine($"Loaded path: {id}, Fill: {fill}");
                    }
                }
                
                System.Diagnostics.Debug.WriteLine($"Successfully loaded {svgPaths.Count} SVG paths");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading SVG: {ex.Message}");
                MessageBox.Show($"Error loading keyboard SVG: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void LoadPadSvgs()
        {
            try
            {
                foreach (var padButton in new[] { Pad1, Pad2, Pad3, Pad4, Pad5, Pad6, Pad7, Pad8 })
                    LoadSvgForButton(padButton, "PadFull.svg", $"pad_{padButton.Name}_outer");
                
                System.Diagnostics.Debug.WriteLine($"Successfully loaded 8 pad SVGs");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading pad SVGs: {ex.Message}");
                MessageBox.Show($"Error loading pad SVGs: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void LoadBank()
        {
            try
            {
                LoadSvgForButton(Bank, "Button.svg", $"button_{Bank.Name}_outer");
                if (padOuterPaths.TryGetValue(Bank, out var outerPath))
                {
                    bankButtonOuterPath = outerPath;
                    bankButtonOuterPath.Fill = bankButtonGreen;
                }
                if (bankATextPath != null) bankATextPath.Fill = bankButtonGreen;
                
                System.Diagnostics.Debug.WriteLine($"Successfully loaded Bank button SVG");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading Bank button SVG: {ex.Message}");
                MessageBox.Show($"Error loading Bank button SVG: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void LoadOctaveButtons()
        {
            try
            {
                LoadSvgForButton(OctaveDown, "Button.svg", $"button_{OctaveDown.Name}_outer");
                LoadSvgForButton(OctaveUp, "Button.svg", $"button_{OctaveUp.Name}_outer");
                
                if (padOuterPaths.TryGetValue(OctaveDown, out var downPath))
                    octaveDownButtonOuterPath = downPath;
                if (padOuterPaths.TryGetValue(OctaveUp, out var upPath))
                    octaveUpButtonOuterPath = upPath;
                
                UpdateOctaveButtonColors();
                System.Diagnostics.Debug.WriteLine($"Successfully loaded Octave buttons SVG");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading Octave buttons SVG: {ex.Message}");
                MessageBox.Show($"Error loading Octave buttons SVG: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void LoadAnalogStickBoundary()
        {
            try
            {
                var resourceUri = new Uri("pack://application:,,,/svg/Circle.svg");
                var resourceStream = Application.GetResourceStream(resourceUri) ?? throw new Exception("Could not load Circle.svg");
                
                XmlDocument doc = new XmlDocument();
                using (var reader = System.Xml.XmlReader.Create(resourceStream.Stream, new System.Xml.XmlReaderSettings
                {
                    DtdProcessing = System.Xml.DtdProcessing.Ignore,
                    XmlResolver = null
                }))
                {
                    doc.Load(reader);
                }
                
                XmlNamespaceManager nsmgr = new XmlNamespaceManager(doc.NameTable);
                nsmgr.AddNamespace("svg", "http://www.w3.org/2000/svg");
                
                var svgElement = doc.DocumentElement;
                var viewBoxParts = (svgElement?.GetAttribute("viewBox") ?? "").Split(' ');
                double svgWidth = double.Parse(viewBoxParts[2]);
                double svgHeight = double.Parse(viewBoxParts[3]);
                double scale = Math.Min(90.0 / svgWidth, 90.0 / svgHeight);
                
                var pathNodes = doc.SelectNodes("//svg:path", nsmgr);
                if (pathNodes != null)
                {
                    foreach (XmlNode pathNode in pathNodes)
                    {
                        string d = pathNode.Attributes?["d"]?.Value ?? "";
                        if (string.IsNullOrEmpty(d)) continue;
                        
                        string fill = pathNode.Attributes?["style"]?.Value?.Split(';')
                            .FirstOrDefault(s => s.Trim().StartsWith("fill:"))?.Split(':')[1]?.Trim() ?? "#ffffff";
                        
                        var transformGroup = CreateSvgTransformForCanvas(pathNode, scale, svgWidth, svgHeight, 90.0, 90.0);
                        
                        var path = new System.Windows.Shapes.Path
                        {
                            Data = Geometry.Parse(d),
                            Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(fill)),
                            Stroke = Brushes.Transparent,
                            StrokeThickness = 0,
                            RenderTransform = transformGroup
                        };
                        
                        AnalogStickBoundaryCanvas.Children.Add(path);
                    }
                }
                
                System.Diagnostics.Debug.WriteLine($"Successfully loaded Circle.svg for analog stick boundary");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading Circle.svg: {ex.Message}");
                MessageBox.Show($"Error loading Circle.svg: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private TransformGroup CreateSvgTransformForCanvas(XmlNode pathNode, double scale, double svgWidth, double svgHeight, double canvasWidth, double canvasHeight)
        {
            var transformGroup = new TransformGroup();
            
            // Parse translate from parent group
            string? transformStr = pathNode.ParentNode?.Attributes?["transform"]?.Value;
            if (!string.IsNullOrEmpty(transformStr) && transformStr.Contains("translate"))
            {
                var match = System.Text.RegularExpressions.Regex.Match(transformStr, @"translate\(([^,]+),([^)]+)\)");
                if (match.Success)
                {
                    double tx = double.Parse(match.Groups[1].Value.Trim());
                    double ty = double.Parse(match.Groups[2].Value.Trim());
                    transformGroup.Children.Add(new TranslateTransform(tx, ty));
                }
            }
            
            // Apply scale and centering
            transformGroup.Children.Add(new ScaleTransform(scale, scale));
            double offsetX = (canvasWidth - svgWidth * scale) / 2;
            double offsetY = (canvasHeight - svgHeight * scale) / 2;
            transformGroup.Children.Add(new TranslateTransform(offsetX, offsetY));
            
            return transformGroup;
        }
        
        /// <summary>
        /// Generic SVG loader for buttons (pads, bank, octave). Parses SVG, scales it to fit button,
        /// and caches the "outer" path for hover effects.
        /// </summary>
        private void LoadSvgForButton(Button button, string svgFileName, string cacheKey)
        {
            var resourceUri = new Uri($"pack://application:,,,/svg/{svgFileName}");
            var resourceStream = Application.GetResourceStream(resourceUri) ?? throw new Exception($"Could not load {svgFileName}");
            
            XmlDocument doc = new XmlDocument();
            doc.Load(resourceStream.Stream);
            
            XmlNamespaceManager nsmgr = new XmlNamespaceManager(doc.NameTable);
            nsmgr.AddNamespace("svg", "http://www.w3.org/2000/svg");
            
            var svgElement = doc.DocumentElement;
            var viewBoxParts = (svgElement?.GetAttribute("viewBox") ?? "").Split(' ');
            double svgWidth = double.Parse(viewBoxParts[2]);
            double svgHeight = double.Parse(viewBoxParts[3]);
            double scale = Math.Min(button.Width / svgWidth, button.Height / svgHeight);
            
            var pathNodes = doc.SelectNodes("//svg:path", nsmgr);
            if (pathNodes == null) return;
            
            var canvas = new Canvas { Width = button.Width, Height = button.Height, Background = Brushes.Transparent };
            
            // Determine which layers to show based on the button
            bool isOctaveDown = button.Name == "OctaveDown";
            bool isOctaveUp = button.Name == "OctaveUp";
            bool isBank = button.Name == "Bank";
            
            foreach (XmlNode pathNode in pathNodes)
            {
                string id = pathNode.Attributes?["id"]?.Value ?? "";
                string d = pathNode.Attributes?["d"]?.Value ?? "";
                if (string.IsNullOrEmpty(d)) continue;
                
                // Check if this path is in a layer group
                string? parentGroupId = pathNode.ParentNode?.Attributes?["id"]?.Value;
                
                // Skip octave layers based on button type
                if (parentGroupId == "octaveUpLayer" && !isOctaveUp)
                    continue;
                if (parentGroupId == "octaveDownLayer" && !isOctaveDown)
                    continue;
                
                // Skip bank layers based on button type and current state
                if (parentGroupId == "bankALayer")
                {
                    if (!isBank) continue; // Hide for non-bank buttons
                    if (isBankStateB) continue; // Hide Bank A when in state B
                }
                if (parentGroupId == "bankBLayer")
                {
                    if (!isBank) continue; // Hide for non-bank buttons
                    if (!isBankStateB) continue; // Hide Bank B when in state A
                }
                
                string fill = pathNode.Attributes?["style"]?.Value?.Split(';')
                    .FirstOrDefault(s => s.Trim().StartsWith("fill:"))?.Split(':')[1]?.Trim() ?? "#ffffff";
                
                var path = new System.Windows.Shapes.Path
                {
                    Data = Geometry.Parse(d),
                    Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(fill)),
                    Stroke = Brushes.Transparent,
                    StrokeThickness = 0,
                    RenderTransform = CreateSvgTransform(pathNode, scale, svgWidth, svgHeight, button.Width, button.Height)
                };
                
                canvas.Children.Add(path);
                
                // Cache outer path for button state/hover effects
                if (id == "outer")
                {
                    padOuterPaths[button] = path;
                    originalColors[cacheKey] = path.Fill.Clone();
                }
                
                // Cache octave text layer paths
                if (id == "octaveDown" && isOctaveDown)
                {
                    octaveDownButtonTextPath = path;
                }
                else if (id == "octaveUp" && isOctaveUp)
                {
                    octaveUpButtonTextPath = path;
                }
                
                // Cache bank text layer paths
                if (id == "bankA" && isBank)
                {
                    bankATextPath = path;
                }
                else if (id == "bankB" && isBank)
                {
                    bankBTextPath = path;
                }
            }
            
            button.Content = canvas;
        }
        
        /// <summary>
        /// Creates a transform group that applies SVG translate, scale, and centers the content in the button.
        /// </summary>
        private TransformGroup CreateSvgTransform(XmlNode pathNode, double scale, double svgWidth, double svgHeight, double buttonWidth, double buttonHeight)
        {
            var transformGroup = new TransformGroup();
            
            // Parse translate transforms from parent group and path itself (in order)
            foreach (var transformStr in new[] { pathNode.ParentNode?.Attributes?["transform"]?.Value, pathNode.Attributes?["transform"]?.Value })
            {
                if (!string.IsNullOrEmpty(transformStr) && transformStr.Contains("translate"))
                {
                    var match = System.Text.RegularExpressions.Regex.Match(transformStr, @"translate\(([^,]+),([^)]+)\)");
                    if (match.Success)
                    {
                        double tx = double.Parse(match.Groups[1].Value.Trim());
                        double ty = double.Parse(match.Groups[2].Value.Trim());
                        transformGroup.Children.Add(new TranslateTransform(tx, ty));
                    }
                }
            }
            
            // Apply uniform scale and center the scaled SVG within the button
            transformGroup.Children.Add(new ScaleTransform(scale, scale));
            double offsetX = (buttonWidth - svgWidth * scale) / 2;
            double offsetY = (buttonHeight - svgHeight * scale) / 2;
            transformGroup.Children.Add(new TranslateTransform(offsetX, offsetY));
            
            return transformGroup;
        }
        
        private void WireKeyButtonEvents()
        {
            // White keys
            var whiteKeys = new[] { w01, w02, w03, w04, w05, w06, w07, w08, w09, w10, w11, w12, w13, w14, w15 };
            foreach (var key in whiteKeys)
                WireKeyButton(key, key.Name, true);
            
            // Black keys
            var blackKeys = new[] { b01, b02, b03, b04, b05, b06, b07, b08, b09, b10 };
            foreach (var key in blackKeys)
                WireKeyButton(key, key.Name, false);
        }
        
        private void WireKeyButton(Button button, string keyName, bool isWhiteKey)
        {
            button.Click += (s, e) => OnKeyPressed(keyName);
            button.MouseEnter += (s, e) => OnKeyHoverEnter(keyName, isWhiteKey);
            button.MouseLeave += (s, e) => OnKeyHoverLeave(keyName);
        }
        
        private void WirePadButtonEvents()
        {
            foreach (var pad in new[] { Pad1, Pad2, Pad3, Pad4, Pad5, Pad6, Pad7, Pad8 })
            {
                pad.Click += (s, e) => OnPadPressed(pad.Name);
                pad.MouseEnter += (s, e) => OnPadHoverEnter(pad);
                pad.MouseLeave += (s, e) => OnPadHoverLeave(pad);
            }
        }
        
        private void WireBankEvents() => Bank.Click += (s, e) => OnBankPressed();
        
        private void WireOctaveButtonEvents()
        {
            OctaveDown.Click += (s, e) => OnOctaveDownPressed();
            OctaveUp.Click += (s, e) => OnOctaveUpPressed();
        }
        
        private void WireKnobEvents()
        {
            foreach (var knob in new[] { Knob1, Knob2, Knob3, Knob4, Knob5, Knob6, Knob7, Knob8 })
                WireSingleKnob(knob, knob.Name);
        }
        
        private void WireSingleKnob(Knob knob, string knobName)
        {
            knob.ValueChanged += (s, value) =>
            {
                Title = $"{knobName}: {value}";
                System.Diagnostics.Debug.WriteLine($"{knobName} value changed: {value}");
                
                // Change knob handle color based on value (red=low, white=mid, green=high)
                var handlePath = knob.GetPath("handle");
                if (handlePath != null)
                {
                    handlePath.Fill = value <= 42 ? colorRed :
                                     value <= 85 ? colorWhite :
                                                   colorGreen;
                }
            };
        }
        
        private void WireAnalogStickEvents()
        {
            WireSingleAnalogStick(AnalogStick1, AnalogStick1.Name);
        }
        
        private void WireSingleAnalogStick(AnalogStick stick, string stickName)
        {
            stick.AngleChanged += (s, angle) =>
            {
                Title = $"{stickName}: {angle}°";
                System.Diagnostics.Debug.WriteLine($"{stickName} angle changed: {angle}°");
            };
        }
        
        private void OnPadHoverEnter(Button padButton)
        {
            if (padOuterPaths.TryGetValue(padButton, out var outerPath))
                outerPath.Fill = hoverColorPadOuter;
        }
        
        private void OnPadHoverLeave(Button padButton)
        {
            if (padOuterPaths.TryGetValue(padButton, out var outerPath) && 
                originalColors.TryGetValue($"pad_{padButton.Name}_outer", out var originalColor))
                outerPath.Fill = originalColor;
        }
        
        private void OnPadPressed(string padName)
        {
            Title = $"Pad Pressed: {padName}";
            System.Diagnostics.Debug.WriteLine($"Pad pressed: {padName}");
        }
        
        private void OnBankPressed()
        {
            isBankStateB = !isBankStateB;
            LoadSvgForButton(Bank, "Button.svg", $"button_{Bank.Name}_outer");
            
            var bankColor = isBankStateB ? bankButtonRed : bankButtonGreen;
            
            // Apply color to outer path and text layer
            if (padOuterPaths.TryGetValue(Bank, out var outerPath))
            {
                bankButtonOuterPath = outerPath;
                bankButtonOuterPath.Fill = bankColor;
            }
            
            var textPath = isBankStateB ? bankBTextPath : bankATextPath;
            if (textPath != null) textPath.Fill = bankColor;
            
            string state = isBankStateB ? "B" : "A";
            Title = $"Bank: {state}";
            System.Diagnostics.Debug.WriteLine($"Bank button pressed - State: {state}");
        }
        
        private void OnOctaveDownPressed()
        {
            if (currentOctave > 0) { currentOctave--; UpdateOctaveDisplay(); }
        }
        
        private void OnOctaveUpPressed()
        {
            if (currentOctave < 8) { currentOctave++; UpdateOctaveDisplay(); }
        }
        
        private void UpdateOctaveDisplay()
        {
            UpdateOctaveButtonColors();
            Title = $"Octave: {currentOctave}";
            System.Diagnostics.Debug.WriteLine($"Current Octave: {currentOctave}");
        }
        
        /// <summary>
        /// Updates octave button colors. OctaveDown shows color when at low octaves (0-3),
        /// OctaveUp shows color when at high octaves (5-8), both white at octave 4.
        /// Colors get more intense as you approach the limits (0 or 8).
        /// </summary>
        private void UpdateOctaveButtonColors()
        {
            // Octave color map: [0]=red (intense), [4]=white (neutral), [8]=red (intense)
            var downColors = new[] { colorRed, colorRedLight1, colorRedLight2, colorRedLight3, colorWhite, colorWhite, colorWhite, colorWhite, colorWhite };
            var upColors = new[] { colorWhite, colorWhite, colorWhite, colorWhite, colorWhite, colorRedLight3, colorRedLight2, colorRedLight1, colorRed };
            
            var downColor = downColors[currentOctave];
            var upColor = upColors[currentOctave];
            
            // Apply colors to outer backgrounds and text layers
            if (octaveDownButtonOuterPath != null) octaveDownButtonOuterPath.Fill = downColor;
            if (octaveUpButtonOuterPath != null) octaveUpButtonOuterPath.Fill = upColor;
            if (octaveDownButtonTextPath != null) octaveDownButtonTextPath.Fill = downColor;
            if (octaveUpButtonTextPath != null) octaveUpButtonTextPath.Fill = upColor;
            
            System.Diagnostics.Debug.WriteLine($"Octave {currentOctave} - Down: {downColor}, Up: {upColor}");
        }
        
        private void OnKeyHoverEnter(string keyName, bool isWhiteKey)
        {
            if (svgPaths.TryGetValue(keyName, out var path))
                path.Fill = isWhiteKey ? hoverColorWhite : hoverColorBlack;
        }
        
        private void OnKeyHoverLeave(string keyName)
        {
            if (svgPaths.TryGetValue(keyName, out var path) && originalColors.TryGetValue(keyName, out var originalColor))
                path.Fill = originalColor;
        }
        
        private void OnKeyPressed(string keyName)
        {
            Title = $"Key Pressed: {keyName}";
            System.Diagnostics.Debug.WriteLine($"Key pressed: {keyName}");
        }
        
        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            var position = e.GetPosition(this);
            Title = $"X: {position.X:F0}, Y: {position.Y:F0}";
        }

        // Windows message constants for aspect ratio locking
        private const int WM_SIZING = 0x0214;
        private const int WMSZ_LEFT = 1;
        private const int WMSZ_RIGHT = 2;
        private const int WMSZ_TOP = 3;
        private const int WMSZ_BOTTOM = 6;
        private const double AspectRatio = 1066.0 / 640.0;

        /// <summary>
        /// Window message handler that maintains aspect ratio during window resizing.
        /// Adjusts width when dragging vertically, adjusts height when dragging horizontally/corners.
        /// </summary>
        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_SIZING)
            {
                var rc = System.Runtime.InteropServices.Marshal.PtrToStructure<RECT>(lParam);
                double width = rc.Right - rc.Left;
                double height = rc.Bottom - rc.Top;
                int edge = wParam.ToInt32();
                
                if (edge == WMSZ_TOP || edge == WMSZ_BOTTOM)
                {
                    // Vertical drag: adjust width to maintain aspect ratio
                    width = height * AspectRatio;
                    rc.Right = rc.Left + (int)width;
                }
                else
                {
                    // Horizontal or corner drag: adjust height to maintain aspect ratio
                    height = width / AspectRatio;
                    rc.Bottom = rc.Top + (int)height;
                }
                
                System.Runtime.InteropServices.Marshal.StructureToPtr(rc, lParam, true);
                handled = true;
            }
            
            return IntPtr.Zero;
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }
    }
}