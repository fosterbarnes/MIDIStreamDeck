using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml;
using System.Linq;

namespace MIDIStreamDeck
{
    /// <summary>
    /// Interaction logic for Knob.xaml
    /// </summary>
    public partial class Knob : UserControl
    {
        // Rotation constraints (in degrees)
        private const double MinRotationAngle = -135.0;  // -135 degrees
        private const double MaxRotationAngle = 135.0;   // +135 degrees
        private const double TotalRotationRange = MaxRotationAngle - MinRotationAngle; // 270 degrees
        
        // Step constraints
        private const int MinStep = 1;
        private const int MaxStep = 127;
        private const int TotalSteps = MaxStep - MinStep; // 126 steps
        private const double DegreesPerStep = TotalRotationRange / TotalSteps; // ~2.14 degrees per step
        
        // Mouse drag tracking
        private bool _isDragging = false;
        private Point _lastMousePosition;
        private const double PixelsPerStep = 3.0; // How many pixels of horizontal movement = 1 step
        
        // Current value (1-127)
        private int _value = 64; // Start at middle position
        
        // SVG path caching (like keyboard keys)
        private Dictionary<string, System.Windows.Shapes.Path> svgPaths = new Dictionary<string, System.Windows.Shapes.Path>();
        private Dictionary<string, Brush> originalColors = new Dictionary<string, Brush>();
        
        // Rotation center (center of the circular knob body, not the entire control)
        private Point _rotationCenter = new Point(0, 0);
        
        /// <summary>
        /// Gets or sets an optional name for this knob (used in debug output)
        /// </summary>
        public string KnobName { get; set; } = "Knob";
        
        /// <summary>
        /// Gets or sets the current value of the knob (1-127)
        /// </summary>
        public int Value
        {
            get => _value;
            set
            {
                int clampedValue = Math.Clamp(value, MinStep, MaxStep);
                if (_value != clampedValue)
                {
                    _value = clampedValue;
                    UpdateRotation();
                    ValueChanged?.Invoke(this, _value);
                }
            }
        }
        
        /// <summary>
        /// Event raised when the knob value changes
        /// </summary>
        public event EventHandler<int>? ValueChanged;
        
        /// <summary>
        /// Access to individual SVG paths by ID for color manipulation
        /// </summary>
        public Dictionary<string, System.Windows.Shapes.Path> SvgPaths => svgPaths;
        
        /// <summary>
        /// Get a specific path by ID (e.g., "outer", "inner", "handle")
        /// </summary>
        public System.Windows.Shapes.Path? GetPath(string id)
        {
            return svgPaths.TryGetValue(id, out var path) ? path : null;
        }
        
        /// <summary>
        /// Set the color of a specific path by ID
        /// </summary>
        public void SetPathColor(string id, Brush color)
        {
            if (svgPaths.TryGetValue(id, out var path))
            {
                path.Fill = color;
            }
        }
        
        /// <summary>
        /// Restore the original color of a specific path by ID
        /// </summary>
        public void RestorePathColor(string id)
        {
            if (svgPaths.TryGetValue(id, out var path) && 
                originalColors.TryGetValue(id, out var originalColor))
            {
                path.Fill = originalColor;
            }
        }
        
        public Knob()
        {
            InitializeComponent();
            this.Loaded += Knob_Loaded;
        }
        
        private void Knob_Loaded(object sender, RoutedEventArgs e)
        {
            // Skip loading SVG in design mode (Visual Studio designer)
            if (DesignerProperties.GetIsInDesignMode(this))
            {
                return;
            }
            
            LoadKnobSvg();
            UpdateRotation();
        }
        
        private void LoadKnobSvg()
        {
            try
            {
                // Load SVG from embedded resource
                var resourceUri = new Uri("pack://application:,,,/svg/Knob.svg");
                var resourceStream = Application.GetResourceStream(resourceUri);
                
                if (resourceStream == null)
                {
                    throw new Exception("Could not load Knob.svg from resources");
                }
                
                XmlDocument doc = new XmlDocument();
                doc.Load(resourceStream.Stream);
                
                XmlNamespaceManager nsmgr = new XmlNamespaceManager(doc.NameTable);
                nsmgr.AddNamespace("svg", "http://www.w3.org/2000/svg");
                
                // Get viewBox to calculate scale
                var svgElement = doc.DocumentElement;
                string viewBox = svgElement?.GetAttribute("viewBox") ?? "";
                var viewBoxParts = viewBox.Split(' ');
                
                if (viewBoxParts.Length < 4)
                {
                    throw new Exception("Invalid viewBox in SVG");
                }
                
                double svgWidth = double.Parse(viewBoxParts[2]);
                double svgHeight = double.Parse(viewBoxParts[3]);
                
                // Calculate scale to fit our control
                double scaleX = this.ActualWidth / svgWidth;
                double scaleY = this.ActualHeight / svgHeight;
                double scale = Math.Min(scaleX, scaleY); // Use uniform scale
                
                // Find all path elements
                XmlNodeList? pathNodes = doc.SelectNodes("//svg:path", nsmgr);
                
                if (pathNodes != null)
                {
                    KnobCanvas.Children.Clear();
                    svgPaths.Clear();
                    originalColors.Clear();
                    
                    foreach (XmlNode pathNode in pathNodes)
                    {
                        string id = pathNode.Attributes?["id"]?.Value ?? "";
                        string d = pathNode.Attributes?["d"]?.Value ?? "";
                        
                        // Parse fill from style attribute
                        string fill = "#ffffff"; // Default
                        string? styleAttr = pathNode.Attributes?["style"]?.Value;
                        if (!string.IsNullOrEmpty(styleAttr))
                        {
                            var fillStyle = styleAttr.Split(';')
                                .FirstOrDefault(s => s.Trim().StartsWith("fill:"));
                            if (fillStyle != null && fillStyle.Contains(':'))
                            {
                                fill = fillStyle.Split(':')[1].Trim();
                            }
                        }
                        
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
                            double offsetX = (this.ActualWidth - scaledWidth) / 2;
                            double offsetY = (this.ActualHeight - scaledHeight) / 2;
                            transformGroup.Children.Add(new TranslateTransform(offsetX, offsetY));
                            
                            path.RenderTransform = transformGroup;
                            
                            // Add to canvas
                            KnobCanvas.Children.Add(path);
                            
                            // Cache the path and original color (like keyboard keys)
                            if (!string.IsNullOrEmpty(id))
                            {
                                svgPaths[id] = path;
                                originalColors[id] = path.Fill.Clone();
                                System.Diagnostics.Debug.WriteLine($"Loaded knob path: {id}, Fill: {fill}");
                            }
                        }
                    }
                }
                
                System.Diagnostics.Debug.WriteLine($"Successfully loaded Knob.svg with {KnobCanvas.Children.Count} paths");
                
                // Calculate rotation center based on the inner circle's bounds
                CalculateRotationCenter();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading Knob.svg: {ex.Message}");
                MessageBox.Show($"Error loading Knob.svg: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void CalculateRotationCenter()
        {
            // Find the inner circle path to calculate the center of the circular knob body
            if (svgPaths.TryGetValue("inner", out var innerPath))
            {
                // Get the bounds of the inner circle
                var bounds = innerPath.Data.Bounds;
                
                // Calculate the center of the bounds (this is the center of the circle)
                double centerX = bounds.Left + (bounds.Width / 2);
                double centerY = bounds.Top + (bounds.Height / 2);
                
                // Apply the path's transform to get the actual position in the canvas
                if (innerPath.RenderTransform != null)
                {
                    _rotationCenter = innerPath.RenderTransform.Transform(new Point(centerX, centerY));
                }
                else
                {
                    _rotationCenter = new Point(centerX, centerY);
                }
                
                System.Diagnostics.Debug.WriteLine($"Rotation center calculated: ({_rotationCenter.X:F2}, {_rotationCenter.Y:F2})");
            }
            else
            {
                // Fallback to control center if we can't find the inner path
                _rotationCenter = new Point(this.ActualWidth / 2, this.ActualHeight / 2);
                System.Diagnostics.Debug.WriteLine("Warning: Could not find inner path, using control center for rotation");
            }
        }
        
        private void UpdateRotation()
        {
            // Map value (1-127) to rotation angle (-135 to +135 degrees)
            double normalizedValue = (double)(_value - MinStep) / TotalSteps;
            double angle = MinRotationAngle + (normalizedValue * TotalRotationRange);
            
            KnobRotation.Angle = angle;
            KnobRotation.CenterX = _rotationCenter.X;
            KnobRotation.CenterY = _rotationCenter.Y;
            
            System.Diagnostics.Debug.WriteLine($"{KnobName} value: {_value}, Angle: {angle:F2}°, Center: ({_rotationCenter.X:F2}, {_rotationCenter.Y:F2})");
        }
        
        private void KnobCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _isDragging = true;
            _lastMousePosition = e.GetPosition(this);
            KnobCanvas.CaptureMouse();
            e.Handled = true;
        }
        
        private void KnobCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDragging)
            {
                _isDragging = false;
                KnobCanvas.ReleaseMouseCapture();
                e.Handled = true;
            }
        }
        
        private void KnobCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDragging)
            {
                Point currentPosition = e.GetPosition(this);
                double deltaX = currentPosition.X - _lastMousePosition.X;
                
                // Calculate step change based on horizontal movement
                int stepChange = (int)Math.Round(deltaX / PixelsPerStep);
                
                if (stepChange != 0)
                {
                    Value = _value + stepChange;
                    _lastMousePosition = currentPosition;
                }
                
                e.Handled = true;
            }
        }
        
        private void KnobCanvas_MouseLeave(object sender, MouseEventArgs e)
        {
            // Don't stop dragging if we leave the control while dragging
            // This allows for smoother interaction
        }
    }
}

