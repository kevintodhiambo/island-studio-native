// UseWindowsForms + UseWPF both bring their namespaces into scope via ImplicitUsings,
// so these type names clash. Prefer the WPF types everywhere; the few WinForms/Drawing
// types (NotifyIcon, Icon, SystemIcons) are referenced with explicit namespaces in Tray.cs.
global using Application = System.Windows.Application;
global using Point = System.Windows.Point;
global using Size = System.Windows.Size;
global using Rectangle = System.Windows.Shapes.Rectangle;
global using Color = System.Windows.Media.Color;
global using Image = System.Windows.Controls.Image;
global using Brushes = System.Windows.Media.Brushes;
global using Orientation = System.Windows.Controls.Orientation;
global using Cursors = System.Windows.Input.Cursors;
global using HorizontalAlignment = System.Windows.HorizontalAlignment;
global using VerticalAlignment = System.Windows.VerticalAlignment;
