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
        private Dictionary<string, System.Windows.Shapes.Path> svgPaths = new Dictionary<string, System.Windows.Shapes.Path>();
        private Dictionary<string, Brush> originalColors = new Dictionary<string, Brush>();
        private Dictionary<Button, System.Windows.Shapes.Path> padOuterPaths = new Dictionary<Button, System.Windows.Shapes.Path>();
        private readonly SolidColorBrush hoverColorWhite = new SolidColorBrush(Color.FromRgb(100, 200, 255)); // Light blue for white keys
        private readonly SolidColorBrush hoverColorBlack = new SolidColorBrush(Color.FromRgb(50, 150, 255)); // Darker blue for black keys
        private readonly SolidColorBrush hoverColorPadOuter = new SolidColorBrush(Color.FromRgb(255, 50, 50)); // Red for pad outer
        
        // Bank button state tracking
        private System.Windows.Shapes.Path? bankButtonOuterPath = null;
        private bool isBankButtonStateB = false; // false = A (green), true = B (red)
        private readonly SolidColorBrush bankButtonGreen = new SolidColorBrush(Color.FromRgb(50, 255, 50)); // Green for state A
        private readonly SolidColorBrush bankButtonRed = new SolidColorBrush(Color.FromRgb(255, 50, 50)); // Red for state B
        
        // Octave button state tracking
        private System.Windows.Shapes.Path? octaveDownButtonOuterPath = null;
        private System.Windows.Shapes.Path? octaveUpButtonOuterPath = null;
        private int currentOctave = 4; // Default to middle octave (0-8 range)
        private readonly SolidColorBrush octaveButtonDefault = new SolidColorBrush(Color.FromRgb(100, 100, 255)); // Blue default
        
        public MainWindow()
        {
            InitializeComponent();
            
            // Helper: Click anywhere to see coordinates in the title bar
            this.MouseDown += Window_MouseDown;
            
            // Set up aspect ratio locking via source initialization
            this.SourceInitialized += (s, e) =>
            {
                var hwndSource = PresentationSource.FromVisual(this) as System.Windows.Interop.HwndSource;
                hwndSource?.AddHook(WndProc);
            };
            
            // Wait for SVG to load, then wire up events
            this.Loaded += MainWindow_Loaded;
        }
        
        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Load and render SVG manually
            LoadSvgPaths();
            LoadPadSvgs();
            LoadBankButton();
            LoadOctaveButtons();
            
            // Wire up keyboard key button events
            WireKeyButtonEvents();
            WirePadButtonEvents();
            WireBankButtonEvents();
            WireOctaveButtonEvents();
            
            // Wire up knob events
            WireKnobEvents();
        }
        
        private void LoadSvgPaths()
        {
            try
            {
                // Load SVG from embedded resource
                var resourceUri = new Uri("pack://application:,,,/svg/KeysCombined.svg");
                var resourceStream = Application.GetResourceStream(resourceUri);
                
                if (resourceStream == null)
                {
                    throw new Exception("Could not load KeysCombined.svg from resources");
                }
                
                XmlDocument doc = new XmlDocument();
                doc.Load(resourceStream.Stream);
                
                XmlNamespaceManager nsmgr = new XmlNamespaceManager(doc.NameTable);
                nsmgr.AddNamespace("svg", "http://www.w3.org/2000/svg");
                
                // Get viewBox to calculate scale
                var svgElement = doc.DocumentElement;
                string viewBox = svgElement?.GetAttribute("viewBox") ?? "";
                var viewBoxParts = viewBox.Split(' ');
                double svgWidth = double.Parse(viewBoxParts[2]);
                double svgHeight = double.Parse(viewBoxParts[3]);
                
                // Calculate scale to fit our canvas (990x275)
                double scaleX = 990.0 / svgWidth;
                double scaleY = 275.0 / svgHeight;
                
                System.Diagnostics.Debug.WriteLine($"SVG ViewBox: {svgWidth}x{svgHeight}, Scale: {scaleX}x{scaleY}");
                
                // Find all path elements
                XmlNodeList? pathNodes = doc.SelectNodes("//svg:path", nsmgr);
                
                System.Diagnostics.Debug.WriteLine($"Found {pathNodes?.Count ?? 0} path elements in SVG");
                
                if (pathNodes != null)
                {
                    foreach (XmlNode pathNode in pathNodes)
                    {
                        string id = pathNode.Attributes?["id"]?.Value ?? "";
                        string d = pathNode.Attributes?["d"]?.Value ?? "";
                        string fill = pathNode.Attributes?["style"]?.Value?.Split(';')
                            .FirstOrDefault(s => s.Trim().StartsWith("fill:"))
                            ?.Split(':')[1]?.Trim() ?? "#ffffff";
                        
                        if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(d))
                        {
                            // Create WPF Path element
                            var path = new System.Windows.Shapes.Path
                            {
                                Data = Geometry.Parse(d),
                                Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(fill)),
                                Stroke = Brushes.Transparent,
                                StrokeThickness = 0
                            };
                            
                            // Apply transform (group transform from SVG)
                            var transformGroup = new TransformGroup();
                            
                            // Check if path is in a group with transform
                            var parentNode = pathNode.ParentNode;
                            string? transformStr = null;
                            
                            // Check parent node for transform
                            if (parentNode?.Attributes?["transform"] != null)
                            {
                                transformStr = parentNode.Attributes["transform"]!.Value;
                            }
                            // Also check path itself for transform
                            else if (pathNode.Attributes?["transform"] != null)
                            {
                                transformStr = pathNode.Attributes["transform"]!.Value;
                            }
                            
                            if (!string.IsNullOrEmpty(transformStr))
                            {
                                // Parse translate(136.08405,-82.109032)
                                if (transformStr.Contains("translate"))
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
                            
                            // Apply scale
                            transformGroup.Children.Add(new ScaleTransform(scaleX, scaleY));
                            path.RenderTransform = transformGroup;
                            
                            // Add to canvas
                            KeyboardSvgCanvas.Children.Add(path);
                            
                            // Cache the path
                            svgPaths[id] = path;
                            originalColors[id] = path.Fill.Clone();
                            
                            System.Diagnostics.Debug.WriteLine($"Loaded path: {id}, Fill: {fill}");
                        }
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
                // Load pad buttons
                Button[] padButtons = { Pad1, Pad2, Pad3, Pad4, Pad5, Pad6, Pad7, Pad8 };
                
                foreach (var padButton in padButtons)
                {
                    LoadPadSvgForButton(padButton);
                }
                
                System.Diagnostics.Debug.WriteLine($"Successfully loaded {padButtons.Length} pad SVGs");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading pad SVGs: {ex.Message}");
                MessageBox.Show($"Error loading pad SVGs: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void LoadBankButton()
        {
            try
            {
                LoadButtonSvgForButton(BankButton, "Button.svg");
                
                // Set initial green color for Bank button
                if (padOuterPaths.TryGetValue(BankButton, out var outerPath))
                {
                    bankButtonOuterPath = outerPath;
                    bankButtonOuterPath.Fill = bankButtonGreen;
                }
                
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
                LoadButtonSvgForButton(OctaveDownButton, "Button.svg");
                LoadButtonSvgForButton(OctaveUpButton, "Button.svg");
                
                // Cache the outer paths and set initial colors
                if (padOuterPaths.TryGetValue(OctaveDownButton, out var downPath))
                {
                    octaveDownButtonOuterPath = downPath;
                }
                
                if (padOuterPaths.TryGetValue(OctaveUpButton, out var upPath))
                {
                    octaveUpButtonOuterPath = upPath;
                }
                
                UpdateOctaveButtonColors();
                
                System.Diagnostics.Debug.WriteLine($"Successfully loaded Octave buttons SVG");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading Octave buttons SVG: {ex.Message}");
                MessageBox.Show($"Error loading Octave buttons SVG: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void LoadPadSvgForButton(Button padButton)
        {
            // Load SVG from embedded resource
            var resourceUri = new Uri("pack://application:,,,/svg/PadFull.svg");
            var resourceStream = Application.GetResourceStream(resourceUri);
            
            if (resourceStream == null)
            {
                throw new Exception("Could not load PadFull.svg from resources");
            }
            
            XmlDocument doc = new XmlDocument();
            doc.Load(resourceStream.Stream);
            
            XmlNamespaceManager nsmgr = new XmlNamespaceManager(doc.NameTable);
            nsmgr.AddNamespace("svg", "http://www.w3.org/2000/svg");
            
            // Get viewBox to calculate scale
            var svgElement = doc.DocumentElement;
            string viewBox = svgElement?.GetAttribute("viewBox") ?? "";
            var viewBoxParts = viewBox.Split(' ');
            double svgWidth = double.Parse(viewBoxParts[2]);
            double svgHeight = double.Parse(viewBoxParts[3]);
            
            // Get button size
            double buttonWidth = padButton.Width;
            double buttonHeight = padButton.Height;
            
            // Calculate scale
            double scaleX = buttonWidth / svgWidth;
            double scaleY = buttonHeight / svgHeight;
            double scale = Math.Min(scaleX, scaleY); // Use uniform scale
            
            // Find all path elements
            XmlNodeList? pathNodes = doc.SelectNodes("//svg:path", nsmgr);
            
            if (pathNodes != null)
            {
                // Clear existing content
                padButton.Content = null;
                
                // Create a canvas to hold the paths
                Canvas padCanvas = new Canvas
                {
                    Width = buttonWidth,
                    Height = buttonHeight,
                    Background = Brushes.Transparent
                };
                
                foreach (XmlNode pathNode in pathNodes)
                {
                    string id = pathNode.Attributes?["id"]?.Value ?? "";
                    string d = pathNode.Attributes?["d"]?.Value ?? "";
                    string fill = pathNode.Attributes?["style"]?.Value?.Split(';')
                        .FirstOrDefault(s => s.Trim().StartsWith("fill:"))
                        ?.Split(':')[1]?.Trim() ?? "#ffffff";
                    
                    if (!string.IsNullOrEmpty(d))
                    {
                        // Create WPF Path element
                        var path = new System.Windows.Shapes.Path
                        {
                            Data = Geometry.Parse(d),
                            Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(fill)),
                            Stroke = Brushes.Transparent,
                            StrokeThickness = 0
                        };
                        
                        // Apply transform
                        var transformGroup = new TransformGroup();
                        
                        // Check if path is in a group with transform
                        var parentNode = pathNode.ParentNode;
                        string? transformStr = null;
                        
                        if (parentNode?.Attributes?["transform"] != null)
                        {
                            transformStr = parentNode.Attributes["transform"]!.Value;
                        }
                        else if (pathNode.Attributes?["transform"] != null)
                        {
                            transformStr = pathNode.Attributes["transform"]!.Value;
                        }
                        
                        if (!string.IsNullOrEmpty(transformStr))
                        {
                            if (transformStr.Contains("translate"))
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
                        
                        // Apply scale and center
                        transformGroup.Children.Add(new ScaleTransform(scale, scale));
                        
                        // Center the scaled content
                        double scaledWidth = svgWidth * scale;
                        double scaledHeight = svgHeight * scale;
                        double offsetX = (buttonWidth - scaledWidth) / 2;
                        double offsetY = (buttonHeight - scaledHeight) / 2;
                        transformGroup.Children.Add(new TranslateTransform(offsetX, offsetY));
                        
                        path.RenderTransform = transformGroup;
                        
                        // Add to canvas
                        padCanvas.Children.Add(path);
                        
                        // Cache the outer path for hover effect
                        if (id == "outer")
                        {
                            padOuterPaths[padButton] = path;
                            originalColors[$"pad_{padButton.Name}_outer"] = path.Fill.Clone();
                        }
                    }
                }
                
                padButton.Content = padCanvas;
            }
        }
        
        private void LoadButtonSvgForButton(Button button, string svgFileName)
        {
            // Load SVG from embedded resource
            var resourceUri = new Uri($"pack://application:,,,/svg/{svgFileName}");
            var resourceStream = Application.GetResourceStream(resourceUri);
            
            if (resourceStream == null)
            {
                throw new Exception($"Could not load {svgFileName} from resources");
            }
            
            XmlDocument doc = new XmlDocument();
            doc.Load(resourceStream.Stream);
            
            XmlNamespaceManager nsmgr = new XmlNamespaceManager(doc.NameTable);
            nsmgr.AddNamespace("svg", "http://www.w3.org/2000/svg");
            
            // Get viewBox to calculate scale
            var svgElement = doc.DocumentElement;
            string viewBox = svgElement?.GetAttribute("viewBox") ?? "";
            var viewBoxParts = viewBox.Split(' ');
            double svgWidth = double.Parse(viewBoxParts[2]);
            double svgHeight = double.Parse(viewBoxParts[3]);
            
            // Get button size
            double buttonWidth = button.Width;
            double buttonHeight = button.Height;
            
            // Calculate scale
            double scaleX = buttonWidth / svgWidth;
            double scaleY = buttonHeight / svgHeight;
            double scale = Math.Min(scaleX, scaleY); // Use uniform scale
            
            // Find all path elements
            XmlNodeList? pathNodes = doc.SelectNodes("//svg:path", nsmgr);
            
            if (pathNodes != null)
            {
                // Clear existing content
                button.Content = null;
                
                // Create a canvas to hold the paths
                Canvas buttonCanvas = new Canvas
                {
                    Width = buttonWidth,
                    Height = buttonHeight,
                    Background = Brushes.Transparent
                };
                
                foreach (XmlNode pathNode in pathNodes)
                {
                    string id = pathNode.Attributes?["id"]?.Value ?? "";
                    string d = pathNode.Attributes?["d"]?.Value ?? "";
                    string fill = pathNode.Attributes?["style"]?.Value?.Split(';')
                        .FirstOrDefault(s => s.Trim().StartsWith("fill:"))
                        ?.Split(':')[1]?.Trim() ?? "#ffffff";
                    
                    if (!string.IsNullOrEmpty(d))
                    {
                        // Create WPF Path element
                        var path = new System.Windows.Shapes.Path
                        {
                            Data = Geometry.Parse(d),
                            Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(fill)),
                            Stroke = Brushes.Transparent,
                            StrokeThickness = 0
                        };
                        
                        // Apply transform
                        var transformGroup = new TransformGroup();
                        
                        // Check if path is in a group with transform
                        var parentNode = pathNode.ParentNode;
                        string? transformStr = null;
                        
                        if (parentNode?.Attributes?["transform"] != null)
                        {
                            transformStr = parentNode.Attributes["transform"]!.Value;
                        }
                        else if (pathNode.Attributes?["transform"] != null)
                        {
                            transformStr = pathNode.Attributes["transform"]!.Value;
                        }
                        
                        if (!string.IsNullOrEmpty(transformStr))
                        {
                            if (transformStr.Contains("translate"))
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
                        
                        // Apply scale and center
                        transformGroup.Children.Add(new ScaleTransform(scale, scale));
                        
                        // Center the scaled content
                        double scaledWidth = svgWidth * scale;
                        double scaledHeight = svgHeight * scale;
                        double offsetX = (buttonWidth - scaledWidth) / 2;
                        double offsetY = (buttonHeight - scaledHeight) / 2;
                        transformGroup.Children.Add(new TranslateTransform(offsetX, offsetY));
                        
                        path.RenderTransform = transformGroup;
                        
                        // Add to canvas
                        buttonCanvas.Children.Add(path);
                        
                        // Cache the outer path for hover effect
                        if (id == "outer")
                        {
                            padOuterPaths[button] = path;
                            originalColors[$"button_{button.Name}_outer"] = path.Fill.Clone();
                        }
                    }
                }
                
                button.Content = buttonCanvas;
            }
        }
        
        
        private void WireKeyButtonEvents()
        {
            // White keys
            WireKeyButton(w01, "w01", true);
            WireKeyButton(w02, "w02", true);
            WireKeyButton(w03, "w03", true);
            WireKeyButton(w04, "w04", true);
            WireKeyButton(w05, "w05", true);
            WireKeyButton(w06, "w06", true);
            WireKeyButton(w07, "w07", true);
            WireKeyButton(w08, "w08", true);
            WireKeyButton(w09, "w09", true);
            WireKeyButton(w10, "w10", true);
            WireKeyButton(w11, "w11", true);
            WireKeyButton(w12, "w12", true);
            WireKeyButton(w13, "w13", true);
            WireKeyButton(w14, "w14", true);
            WireKeyButton(w15, "w15", true);
            
            // Black keys
            WireKeyButton(b01, "b01", false);
            WireKeyButton(b02, "b02", false);
            WireKeyButton(b03, "b03", false);
            WireKeyButton(b04, "b04", false);
            WireKeyButton(b05, "b05", false);
            WireKeyButton(b06, "b06", false);
            WireKeyButton(b07, "b07", false);
            WireKeyButton(b08, "b08", false);
            WireKeyButton(b09, "b09", false);
            WireKeyButton(b10, "b10", false);
        }
        
        private void WireKeyButton(Button button, string keyName, bool isWhiteKey)
        {
            button.Click += (s, e) => OnKeyPressed(keyName);
            button.MouseEnter += (s, e) => OnKeyHoverEnter(keyName, isWhiteKey);
            button.MouseLeave += (s, e) => OnKeyHoverLeave(keyName);
        }
        
        private void WirePadButtonEvents()
        {
            Button[] padButtons = { Pad1, Pad2, Pad3, Pad4, Pad5, Pad6, Pad7, Pad8 };
            
            foreach (var padButton in padButtons)
            {
                padButton.Click += (s, e) => OnPadPressed(padButton.Name);
                padButton.MouseEnter += (s, e) => OnPadHoverEnter(padButton);
                padButton.MouseLeave += (s, e) => OnPadHoverLeave(padButton);
            }
        }
        
        private void WireBankButtonEvents()
        {
            BankButton.Click += (s, e) => OnBankButtonPressed();
            // Don't use hover effects for Bank button - it has toggle behavior instead
        }
        
        private void WireOctaveButtonEvents()
        {
            OctaveDownButton.Click += (s, e) => OnOctaveDownPressed();
            OctaveUpButton.Click += (s, e) => OnOctaveUpPressed();
            // No hover effects for octave buttons
        }
        
        private void WireKnobEvents()
        {
            // Wire up all knobs
            WireSingleKnob(Knob1, "Knob1");
            WireSingleKnob(Knob2, "Knob2");
            WireSingleKnob(Knob3, "Knob3");
            WireSingleKnob(Knob4, "Knob4");
            WireSingleKnob(Knob5, "Knob5");
            WireSingleKnob(Knob6, "Knob6");
            WireSingleKnob(Knob7, "Knob7");
            WireSingleKnob(Knob8, "Knob8");
        }
        
        private void WireSingleKnob(Knob knob, string knobName)
        {
            knob.ValueChanged += (s, value) =>
            {
                Title = $"{knobName}: {value}";
                System.Diagnostics.Debug.WriteLine($"{knobName} value changed: {value}");
                
                // Example: Change the handle color based on value
                // Low values (1-42) = blue, Medium values (43-85) = white, High values (86-127) = orange
                var handlePath = knob.GetPath("handle");
                if (handlePath != null)
                {
                    if (value <= 42)
                    {
                        handlePath.Fill = new SolidColorBrush(Color.FromRgb(100, 150, 255)); // Light blue
                    }
                    else if (value <= 85)
                    {
                        handlePath.Fill = new SolidColorBrush(Color.FromRgb(255, 255, 255)); // White
                    }
                    else
                    {
                        handlePath.Fill = new SolidColorBrush(Color.FromRgb(255, 150, 50)); // Orange
                    }
                }
            };
        }
        
        private void OnPadHoverEnter(Button padButton)
        {
            if (padOuterPaths.TryGetValue(padButton, out var outerPath))
            {
                outerPath.Fill = hoverColorPadOuter;
            }
        }
        
        private void OnPadHoverLeave(Button padButton)
        {
            if (padOuterPaths.TryGetValue(padButton, out var outerPath))
            {
                string key = $"pad_{padButton.Name}_outer";
                if (originalColors.TryGetValue(key, out var originalColor))
                {
                    outerPath.Fill = originalColor;
                }
            }
        }
        
        private void OnPadPressed(string padName)
        {
            Title = $"Pad Pressed: {padName}";
            System.Diagnostics.Debug.WriteLine($"Pad pressed: {padName}");
        }
        
        private void OnBankButtonPressed()
        {
            // Toggle between A and B
            isBankButtonStateB = !isBankButtonStateB;
            
            if (bankButtonOuterPath != null)
            {
                bankButtonOuterPath.Fill = isBankButtonStateB ? bankButtonRed : bankButtonGreen;
            }
            
            string state = isBankButtonStateB ? "B" : "A";
            Title = $"Bank: {state}";
            System.Diagnostics.Debug.WriteLine($"Bank button pressed - State: {state}");
        }
        
        private void OnOctaveDownPressed()
        {
            if (currentOctave > 0)
            {
                currentOctave--;
                UpdateOctaveButtonColors();
                Title = $"Octave: {currentOctave}";
                System.Diagnostics.Debug.WriteLine($"Octave Down pressed - Current Octave: {currentOctave}");
            }
        }
        
        private void OnOctaveUpPressed()
        {
            if (currentOctave < 8)
            {
                currentOctave++;
                UpdateOctaveButtonColors();
                Title = $"Octave: {currentOctave}";
                System.Diagnostics.Debug.WriteLine($"Octave Up pressed - Current Octave: {currentOctave}");
            }
        }
        
        private void UpdateOctaveButtonColors()
        {
            // Color gradient from red (low) through yellow/green to blue (high)
            // Octave 0 = Red, Octave 4 = Yellow/Green (middle), Octave 8 = Blue
            byte red, green, blue;
            
            if (currentOctave <= 4)
            {
                // Octaves 0-4: Red to Yellow to Green
                float ratio = currentOctave / 4.0f;
                red = (byte)(255 - (ratio * 155)); // 255 -> 100
                green = (byte)(50 + (ratio * 205)); // 50 -> 255
                blue = 50;
            }
            else
            {
                // Octaves 5-8: Green to Blue
                float ratio = (currentOctave - 4) / 4.0f;
                red = (byte)(100 - (ratio * 50)); // 100 -> 50
                green = (byte)(255 - (ratio * 155)); // 255 -> 100
                blue = (byte)(50 + (ratio * 205)); // 50 -> 255
            }
            
            var octaveColor = new SolidColorBrush(Color.FromRgb(red, green, blue));
            
            // Update both buttons with the same color to show current octave
            if (octaveDownButtonOuterPath != null)
            {
                octaveDownButtonOuterPath.Fill = octaveColor;
            }
            
            if (octaveUpButtonOuterPath != null)
            {
                octaveUpButtonOuterPath.Fill = octaveColor;
            }
            
            System.Diagnostics.Debug.WriteLine($"Octave {currentOctave} - Color: RGB({red}, {green}, {blue})");
        }
        
        private void OnKeyHoverEnter(string keyName, bool isWhiteKey)
        {
            if (svgPaths.TryGetValue(keyName, out var path))
            {
                path.Fill = isWhiteKey ? hoverColorWhite : hoverColorBlack;
            }
        }
        
        private void OnKeyHoverLeave(string keyName)
        {
            if (svgPaths.TryGetValue(keyName, out var path))
            {
                if (originalColors.TryGetValue(keyName, out var originalColor))
                {
                    path.Fill = originalColor;
                }
            }
        }
        
        private void OnKeyPressed(string keyName)
        {
            // Update title bar to show which key was pressed
            Title = $"Key Pressed: {keyName}";
            
            // TODO: Add MIDI output logic here
            // For now, just display the key name
            System.Diagnostics.Debug.WriteLine($"Key pressed: {keyName}");
        }
        
        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            var position = e.GetPosition(this);
            Title = $"X: {position.X:F0}, Y: {position.Y:F0}";
        }

        private const int WM_SIZING = 0x0214;
        private const int WMSZ_LEFT = 1;
        private const int WMSZ_RIGHT = 2;
        private const int WMSZ_TOP = 3;
        private const int WMSZ_BOTTOM = 6;
        private const double AspectRatio = 1066.0 / 640.0;

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_SIZING)
            {
                var rc = System.Runtime.InteropServices.Marshal.PtrToStructure<RECT>(lParam);
                
                double width = rc.Right - rc.Left;
                double height = rc.Bottom - rc.Top;
                
                int edge = wParam.ToInt32();
                
                // Maintain aspect ratio based on which edge is being dragged
                if (edge == WMSZ_LEFT || edge == WMSZ_RIGHT)
                {
                    // Dragging horizontally, adjust height
                    height = width / AspectRatio;
                    rc.Bottom = rc.Top + (int)height;
                }
                else if (edge == WMSZ_TOP || edge == WMSZ_BOTTOM)
                {
                    // Dragging vertically, adjust width
                    width = height * AspectRatio;
                    rc.Right = rc.Left + (int)width;
                }
                else
                {
                    // Corner drag - adjust based on width
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