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
    /// Interaction logic for AnalogStick.xaml
    /// </summary>
    public partial class AnalogStick : UserControl
    {
        // Rotation constraints - full 360 degree rotation
        private const int MinAngle = 1;       // 1 degree
        private const int MaxAngle = 360;     // 360 degrees
        
        // Mouse drag tracking
        private bool _isDragging = false;
        
        // Current angle value (0 = centered/neutral, 360=up, 90=right, 180=down, 270=left)
        private int _angle = 0; // Start at center
        
        // SVG path caching (like keyboard keys and knobs)
        private Dictionary<string, System.Windows.Shapes.Path> svgPaths = new Dictionary<string, System.Windows.Shapes.Path>();
        private Dictionary<string, Brush> originalColors = new Dictionary<string, Brush>();
        
        // Center and movement
        private Point _center = new Point(0, 0);
        private const double MaxMovementRadius = 15.0; // Maximum distance the stick can move from center
        
        /// <summary>
        /// Gets or sets an optional name for this analog stick (used in debug output)
        /// </summary>
        public string AnalogStickName { get; set; } = "AnalogStick";
        
        /// <summary>
        /// Gets or sets the current angle of the analog stick
        /// 0 = centered/neutral
        /// 360/1 = up, 90 = right, 180 = down, 270 = left
        /// </summary>
        public int Angle
        {
            get => _angle;
            set
            {
                int newValue;
                if (value == 0)
                {
                    // 0 means centered/neutral
                    newValue = 0;
                }
                else
                {
                    // Wrap value to 1-360 range
                    newValue = ((value - 1) % 360) + 1;
                    if (newValue < 1) newValue += 360;
                }
                
                if (_angle != newValue)
                {
                    _angle = newValue;
                    UpdateRotation();
                    AngleChanged?.Invoke(this, _angle);
                }
            }
        }
        
        /// <summary>
        /// Event raised when the analog stick angle changes
        /// </summary>
        public event EventHandler<int>? AngleChanged;
        
        /// <summary>
        /// Access to individual SVG paths by ID for color manipulation
        /// </summary>
        public Dictionary<string, System.Windows.Shapes.Path> SvgPaths => svgPaths;
        
        /// <summary>
        /// Get a specific path by ID (e.g., "outer", "inner", "ring")
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
        
        public AnalogStick()
        {
            InitializeComponent();
            this.Loaded += AnalogStick_Loaded;
        }
        
        private void AnalogStick_Loaded(object sender, RoutedEventArgs e)
        {
            // Skip loading SVG in design mode (Visual Studio designer)
            if (DesignerProperties.GetIsInDesignMode(this))
            {
                return;
            }
            
            LoadAnalogStickSvg();
            UpdateRotation();
        }
        
        private void LoadAnalogStickSvg()
        {
            try
            {
                // Load SVG from embedded resource
                var resourceUri = new Uri("pack://application:,,,/svg/AnalogStick.svg");
                var resourceStream = Application.GetResourceStream(resourceUri);
                
                if (resourceStream == null)
                {
                    throw new Exception("Could not load AnalogStick.svg from resources");
                }
                
                XmlDocument doc = new XmlDocument();
                
                // Use XmlReaderSettings to handle SVG parsing more robustly
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
                nsmgr.AddNamespace("xlink", "http://www.w3.org/1999/xlink");
                
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
                    AnalogStickCanvas.Children.Clear();
                    svgPaths.Clear();
                    originalColors.Clear();
                    
                    foreach (XmlNode pathNode in pathNodes)
                    {
                        string id = pathNode.Attributes?["id"]?.Value ?? "";
                        string d = pathNode.Attributes?["d"]?.Value ?? "";
                        
                        // Parse fill from style attribute or fill attribute
                        string fill = "#ffffff"; // Default
                        string? styleAttr = pathNode.Attributes?["style"]?.Value;
                        if (!string.IsNullOrEmpty(styleAttr))
                        {
                            var fillStyle = styleAttr.Split(';')
                                .FirstOrDefault(s => s.Trim().StartsWith("fill:"));
                            if (fillStyle != null && fillStyle.Contains(':'))
                            {
                                string fillValue = fillStyle.Split(':')[1].Trim();
                                // Handle gradient references
                                if (fillValue.StartsWith("url("))
                                {
                                    // Use appropriate color based on path id
                                    fill = id == "inner" ? "#b95255" : "#9d2830";
                                }
                                else
                                {
                                    fill = fillValue;
                                }
                            }
                        }
                        
                        // Check for direct fill attribute
                        string? fillAttr = pathNode.Attributes?["fill"]?.Value;
                        if (!string.IsNullOrEmpty(fillAttr))
                        {
                            if (fillAttr.StartsWith("url("))
                            {
                                // Handle gradient references - use appropriate color based on path id
                                fill = id == "inner" ? "#b95255" : "#9d2830";
                            }
                            else
                            {
                                fill = fillAttr;
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
                            AnalogStickCanvas.Children.Add(path);
                            
                            // Cache the path and original color
                            if (!string.IsNullOrEmpty(id))
                            {
                                svgPaths[id] = path;
                                originalColors[id] = path.Fill.Clone();
                                System.Diagnostics.Debug.WriteLine($"Loaded analog stick path: {id}, Fill: {fill}");
                            }
                        }
                    }
                }
                
                System.Diagnostics.Debug.WriteLine($"Successfully loaded AnalogStick.svg with {AnalogStickCanvas.Children.Count} paths");
                
                // Calculate rotation center based on the inner circle's bounds
                CalculateRotationCenter();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading AnalogStick.svg: {ex.Message}");
                MessageBox.Show($"Error loading AnalogStick.svg: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void CalculateRotationCenter()
        {
            // Calculate the center of the control
            _center = new Point(this.ActualWidth / 2, this.ActualHeight / 2);
            System.Diagnostics.Debug.WriteLine($"AnalogStick center calculated: ({_center.X:F2}, {_center.Y:F2})");
        }
        
        private void UpdateRotation()
        {
            if (_angle == 0)
            {
                // Centered/neutral position
                AnalogStickTranslate.X = 0;
                AnalogStickTranslate.Y = 0;
                System.Diagnostics.Debug.WriteLine($"{AnalogStickName} centered (neutral position)");
            }
            else
            {
                // Convert angle to screen coordinates
                // Angle is: up=360, right=90, down=180, left=270
                // Convert to radians, adjusting so up=360 points upward (negative Y on screen)
                double adjustedAngle = _angle - 90; // Rotate coordinate system
                double angleRadians = adjustedAngle * (Math.PI / 180.0);
                
                // Calculate how far to move the entire analog stick
                double offsetX = Math.Cos(angleRadians) * MaxMovementRadius;
                double offsetY = Math.Sin(angleRadians) * MaxMovementRadius;
                
                // Apply translation to the entire canvas (moves all SVG layers together)
                AnalogStickTranslate.X = offsetX;
                AnalogStickTranslate.Y = offsetY;
                
                System.Diagnostics.Debug.WriteLine($"{AnalogStickName} angle: {_angle}°, Offset: ({offsetX:F2}, {offsetY:F2})");
            }
        }
        
        private void AnalogStickCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _isDragging = true;
            AnalogStickCanvas.CaptureMouse();
            
            // Immediately update position to point toward mouse
            UpdateAngleFromMousePosition(e.GetPosition(this));
            
            e.Handled = true;
        }
        
        private void AnalogStickCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDragging)
            {
                _isDragging = false;
                AnalogStickCanvas.ReleaseMouseCapture();
                
                // Set angle to 0 (centered/neutral)
                Angle = 0;
                
                e.Handled = true;
            }
        }
        
        private void AnalogStickCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDragging)
            {
                UpdateAngleFromMousePosition(e.GetPosition(this));
                e.Handled = true;
            }
        }
        
        private void UpdateAngleFromMousePosition(Point mousePosition)
        {
            // Calculate the angle from the center to the mouse cursor
            double deltaX = mousePosition.X - _center.X;
            double deltaY = mousePosition.Y - _center.Y;
            
            // Calculate angle in degrees (atan2 returns radians)
            // atan2 gives: right=0°, down=90°, left=180°, up=270°
            double angleRadians = Math.Atan2(deltaY, deltaX);
            double angleDegrees = angleRadians * (180.0 / Math.PI);
            
            // Convert to 0-360 range (atan2 gives -180 to 180)
            if (angleDegrees < 0)
                angleDegrees += 360;
            
            // Rotate by +90 degrees so that: up=360°, right=90°, down=180°, left=270°
            angleDegrees = (angleDegrees + 90) % 360;
            
            // Round to nearest degree
            int newAngle = (int)Math.Round(angleDegrees);
            
            // Convert 0 to 360 (so we have up=360)
            if (newAngle == 0) newAngle = 360;
            
            Angle = newAngle;
        }
        
        private void AnalogStickCanvas_MouseLeave(object sender, MouseEventArgs e)
        {
            // Don't stop dragging if we leave the control while dragging
            // This allows for smoother interaction
        }
    }
}

