using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using OpenDualHub.Models;

namespace OpenDualHub
{
    public partial class Ps3XmbView : UserControl
    {
        public XmbManager Xmb => _xmb;
        private readonly XmbManager _xmb = new();

        public HardwareLabControl LabControl { get; } = new();
        public NovelFeaturesControl NovelControl { get; } = new();
        public ControllerInspectorControl ControllerInspector { get; } = new();
        public XmbCategoryInspectorControl CategoryInspector { get; } = new();

        public Action<string>? OnNavigateSection;

        public Ps3XmbView()
        {
            InitializeComponent();

            _xmb.CategoryChanged += OnCategoryChanged;
            _xmb.ItemChanged += OnItemChanged;
            _xmb.ItemActivated += OnItemActivated;
            _xmb.LabShortcutTriggered += () => _xmb.SelectCategory(8); // Hardware Lab category

            Loaded += (s, e) =>
            {
                RenderCategories();
                RenderItems();
                UpdateInspector();
            };
        }

        public void SetIpcClient(IpcClient ipc)
        {
            LabControl.IpcClient = ipc;
            CategoryInspector.IpcClient = ipc;
        }

        #region Rendering XMB Elements
        private void RenderCategories()
        {
            PanelCategories.Children.Clear();

            for (int i = 0; i < _xmb.Categories.Count; i++)
            {
                var cat = _xmb.Categories[i];
                bool isSelected = (i == _xmb.ActiveCategoryIndex);

                var catContainer = new Border
                {
                    Width = isSelected ? 88 : 72,
                    Height = isSelected ? 82 : 72,
                    Margin = new Thickness(isSelected ? 8 : 4, 0, isSelected ? 8 : 4, 0),
                    Background = Brushes.Transparent,
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Opacity = isSelected ? 1.0 : 0.48
                };

                int catIdx = i;
                catContainer.MouseDown += (s, e) => _xmb.SelectCategory(catIdx);

                var stack = new StackPanel
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };

                // Circular Icon Frame with Glowing Halo
                var iconFrame = new Border
                {
                    Width = isSelected ? 46 : 38,
                    Height = isSelected ? 46 : 38,
                    CornerRadius = new CornerRadius(isSelected ? 23 : 19),
                    Background = new SolidColorBrush(isSelected ? Color.FromArgb(0x40, 0x00, 0xF5, 0xD4) : Color.FromArgb(0x50, 0x0E, 0x14, 0x22)),
                    BorderBrush = new SolidColorBrush(isSelected ? Color.FromRgb(0x00, 0xF5, 0xD4) : Color.FromArgb(0x40, 0x29, 0x79, 0xFF)),
                    BorderThickness = new Thickness(isSelected ? 2 : 1),
                    HorizontalAlignment = HorizontalAlignment.Center
                };

                var iconTxt = new TextBlock
                {
                    Text = cat.Icon,
                    FontSize = isSelected ? 18 : 14,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                iconFrame.Child = iconTxt;
                stack.Children.Add(iconFrame);

                // Category Title underneath
                var titleTxt = new TextBlock
                {
                    Text = cat.Title,
                    FontSize = isSelected ? 10 : 9,
                    FontWeight = isSelected ? FontWeights.Bold : FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(isSelected ? Color.FromRgb(0x00, 0xF5, 0xD4) : Color.FromRgb(0x9A, 0xAC, 0xC4)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 4, 0, 0)
                };
                stack.Children.Add(titleTxt);

                catContainer.Child = stack;
                PanelCategories.Children.Add(catContainer);
            }
        }

        private void RenderItems()
        {
            PanelItems.Children.Clear();
            var activeCat = _xmb.ActiveCategory;

            TxtCategoryIcon.Text = activeCat.Icon;
            TxtCategoryHeader.Text = $"{activeCat.Title} OPTIONS";

            for (int i = 0; i < activeCat.Items.Count; i++)
            {
                var item = activeCat.Items[i];
                bool isSelected = (i == _xmb.ActiveItemIndex);

                var itemBorder = new Border
                {
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(12, 9, 12, 9),
                    Margin = new Thickness(0, 0, 0, 8),
                    Background = new SolidColorBrush(isSelected ? Color.FromArgb(0xD9, 0x16, 0x24, 0x3C) : Color.FromArgb(0x70, 0x0E, 0x14, 0x22)),
                    BorderBrush = new SolidColorBrush(isSelected ? Color.FromRgb(0x00, 0xF5, 0xD4) : Color.FromArgb(0x25, 0x29, 0x79, 0xFF)),
                    BorderThickness = new Thickness(isSelected ? 1.5 : 1.0),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Opacity = isSelected ? 1.0 : 0.72
                };

                int itemIdx = i;
                itemBorder.MouseDown += (s, e) => _xmb.SelectItem(itemIdx);

                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                // Left Icon
                var iconBorder = new Border
                {
                    Width = 26,
                    Height = 26,
                    CornerRadius = new CornerRadius(6),
                    Background = new SolidColorBrush(isSelected ? Color.FromArgb(0x30, 0x00, 0xF5, 0xD4) : Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF)),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Center
                };
                iconBorder.Child = new TextBlock
                {
                    Text = item.Icon,
                    FontSize = 13,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(iconBorder, 0);
                grid.Children.Add(iconBorder);

                // Center Title & Subtitle
                var textStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 8, 0) };
                var title = new TextBlock
                {
                    Text = item.Title,
                    FontSize = 12,
                    FontWeight = isSelected ? FontWeights.Bold : FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(isSelected ? Color.FromRgb(0xFF, 0xFF, 0xFF) : Color.FromRgb(0xDA, 0xE2, 0xEE))
                };
                var subtitle = new TextBlock
                {
                    Text = item.Subtitle,
                    FontSize = 10,
                    Foreground = new SolidColorBrush(isSelected ? Color.FromRgb(0x00, 0xF5, 0xD4) : Color.FromRgb(0x8A, 0x9B, 0xB0)),
                    Margin = new Thickness(0, 1, 0, 0),
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                textStack.Children.Add(title);
                textStack.Children.Add(subtitle);
                Grid.SetColumn(textStack, 1);
                grid.Children.Add(textStack);

                // Right Badge & Selection Indicator
                var rightStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                if (!string.IsNullOrEmpty(item.ValueBadge))
                {
                    var badge = new Border
                    {
                        CornerRadius = new CornerRadius(5),
                        Padding = new Thickness(6, 2, 6, 2),
                        Background = new SolidColorBrush(isSelected ? Color.FromArgb(0x30, 0x00, 0xF5, 0xD4) : Color.FromArgb(0x20, 0x29, 0x79, 0xFF)),
                        BorderBrush = new SolidColorBrush(isSelected ? Color.FromRgb(0x00, 0xF5, 0xD4) : Color.FromArgb(0x40, 0x29, 0x79, 0xFF)),
                        BorderThickness = new Thickness(1),
                        Margin = new Thickness(0, 0, isSelected ? 6 : 0, 0)
                    };
                    badge.Child = new TextBlock
                    {
                        Text = item.ValueBadge,
                        FontSize = 9,
                        FontWeight = FontWeights.Bold,
                        Foreground = new SolidColorBrush(isSelected ? Color.FromRgb(0x00, 0xF5, 0xD4) : Color.FromRgb(0x9A, 0xAC, 0xC4))
                    };
                    rightStack.Children.Add(badge);
                }

                if (isSelected)
                {
                    var chevron = new TextBlock
                    {
                        Text = "►",
                        FontSize = 11,
                        FontWeight = FontWeights.Bold,
                        Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xF5, 0xD4)),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    rightStack.Children.Add(chevron);
                }

                Grid.SetColumn(rightStack, 2);
                grid.Children.Add(rightStack);

                itemBorder.Child = grid;
                PanelItems.Children.Add(itemBorder);
            }
        }

        private void UpdateInspector()
        {
            var cat = _xmb.ActiveCategory;
            var item = _xmb.ActiveItem;

            if (item != null)
            {
                // Feature 1, 2, 3: Novel features inspection
                if (item.Id == "ctrl_battery_sentinel" || item.Id == "ctrl_hall_stabilizer" || item.Id == "aud_spatial_radar")
                {
                    InspectorContentHost.Content = NovelControl;
                    return;
                }

                // Controller Overview & Live Input Test
                if (item.Id == "ctrl_status" || item.Id == "ctrl_input" || item.Id == "ctrl_overclock")
                {
                    InspectorContentHost.Content = ControllerInspector;
                    return;
                }

                // Hardware Lab items
                if (item.Id.StartsWith("lab_"))
                {
                    InspectorContentHost.Content = LabControl;
                    return;
                }
            }

            if (cat.Id == "controller")
            {
                InspectorContentHost.Content = ControllerInspector;
                return;
            }

            if (cat.Id == "lab")
            {
                InspectorContentHost.Content = LabControl;
                return;
            }

            CategoryInspector.SetSection(cat.Id);
            InspectorContentHost.Content = CategoryInspector;
        }
        #endregion

        #region Navigation Handlers
        private void OnCategoryChanged(int newIndex)
        {
            RenderCategories();
            RenderItems();
            UpdateInspector();

            // Smoothly center the active category in the ScrollViewer
            double targetOffset = Math.Max(0, newIndex * 78 - 180);
            CategoryScrollViewer.ScrollToHorizontalOffset(targetOffset);
        }

        private void OnItemChanged(int catIndex, int itemIndex)
        {
            RenderItems();
            UpdateInspector();
        }

        private void OnItemActivated(XmbItem item)
        {
            item.OnActivate?.Invoke();

            if (item.Id.StartsWith("lab_"))
            {
                InspectorContentHost.Content = LabControl;
            }
            else if (item.Id == "ctrl_battery_sentinel" || item.Id == "ctrl_hall_stabilizer" || item.Id == "aud_spatial_radar")
            {
                InspectorContentHost.Content = NovelControl;
            }
            else
            {
                OnNavigateSection?.Invoke(item.Id);
            }
        }
        #endregion

        #region Telemetry & Updates
        public void UpdateTelemetry(ControllerTelemetry t)
        {
            if (t == null) return;

            // 1. Process Gamepad inputs (D-Pad, Left Stick, L1/R1, ✕, ◯, △, □)
            _xmb.ProcessControllerTelemetry(t);

            // 2. Forward to child hardware views
            LabControl.UpdateTelemetry(t);
            NovelControl.UpdateTelemetry(t);
            ControllerInspector.UpdateTelemetry(t);
            CategoryInspector.UpdateTelemetry(t);

            // 3. Update top clock & header badges
            TxtXmbClock.Text = DateTime.Now.ToString("ddd dd/MM  h:mm tt");
            TxtXmbBattery.Text = $"{t.Battery}%";
            TxtXmbBatteryIcon.Text = t.Charging ? "⚡" : "🔋";
            TxtTopBusInfo.Text = $"{t.PollingRateHz}Hz DIRECT HID KERNEL BUS • {t.LatencyMs:F1}ms LATENCY";

            if (t.Connected)
            {
                TxtFooterStatus.Text = $"{t.ModelName} Connected ({t.ConnectionName})";
                TxtFooterConnection.Text = "Ultra Low-Latency 1000Hz HID";
            }
            else
            {
                TxtFooterStatus.Text = "Controller Disconnected";
                TxtFooterConnection.Text = "Waiting for USB / BT HID Report...";
            }
        }
        #endregion
    }
}
