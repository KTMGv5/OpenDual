using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OpenDualHub.Models;

namespace OpenDualHub
{
    public partial class CrossMediaBarView : UserControl
    {
        public XmbManager Xmb => _xmb;
        private readonly XmbManager _xmb = new();

        public HardwareLabControl LabControl { get; } = new();
        public NovelFeaturesControl NovelControl { get; } = new();
        public ControllerInspectorControl ControllerInspector { get; } = new();
        public XmbCategoryInspectorControl CategoryInspector { get; } = new();

        public Action<string>? OnNavigateSection;

        private int _builtCategoryCount = -1;
        private int _renderedItemCatIndex = -1;
        private int _renderedItemCount = -1;

        private static readonly SolidColorBrush s_catFrameSelBg = CreateFrozenBrush(Color.FromArgb(0x40, 0x00, 0xF5, 0xD4));
        private static readonly SolidColorBrush s_catFrameUnselBg = CreateFrozenBrush(Color.FromArgb(0x50, 0x0E, 0x14, 0x22));
        private static readonly SolidColorBrush s_catFrameSelBorder = CreateFrozenBrush(Color.FromRgb(0x00, 0xF5, 0xD4));
        private static readonly SolidColorBrush s_catFrameUnselBorder = CreateFrozenBrush(Color.FromArgb(0x40, 0x29, 0x79, 0xFF));
        private static readonly SolidColorBrush s_catTitleSelFg = CreateFrozenBrush(Color.FromRgb(0x00, 0xF5, 0xD4));
        private static readonly SolidColorBrush s_catTitleUnselFg = CreateFrozenBrush(Color.FromRgb(0x9A, 0xAC, 0xC4));

        private static readonly SolidColorBrush s_itemSelBg = CreateFrozenBrush(Color.FromArgb(0xD9, 0x16, 0x24, 0x3C));
        private static readonly SolidColorBrush s_itemUnselBg = CreateFrozenBrush(Color.FromArgb(0x70, 0x0E, 0x14, 0x22));
        private static readonly SolidColorBrush s_itemSelBorder = CreateFrozenBrush(Color.FromRgb(0x00, 0xF5, 0xD4));
        private static readonly SolidColorBrush s_itemUnselBorder = CreateFrozenBrush(Color.FromArgb(0x25, 0x29, 0x79, 0xFF));
        private static readonly SolidColorBrush s_itemIconSelBg = CreateFrozenBrush(Color.FromArgb(0x30, 0x00, 0xF5, 0xD4));
        private static readonly SolidColorBrush s_itemIconUnselBg = CreateFrozenBrush(Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF));
        private static readonly SolidColorBrush s_itemTitleSelFg = CreateFrozenBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
        private static readonly SolidColorBrush s_itemTitleUnselFg = CreateFrozenBrush(Color.FromRgb(0xDA, 0xE2, 0xEE));
        private static readonly SolidColorBrush s_itemSubSelFg = CreateFrozenBrush(Color.FromRgb(0x00, 0xF5, 0xD4));
        private static readonly SolidColorBrush s_itemSubUnselFg = CreateFrozenBrush(Color.FromRgb(0x8A, 0x9B, 0xB0));
        private static readonly SolidColorBrush s_badgeSelBg = CreateFrozenBrush(Color.FromArgb(0x30, 0x00, 0xF5, 0xD4));
        private static readonly SolidColorBrush s_badgeUnselBg = CreateFrozenBrush(Color.FromArgb(0x20, 0x29, 0x79, 0xFF));
        private static readonly SolidColorBrush s_badgeSelBorder = CreateFrozenBrush(Color.FromRgb(0x00, 0xF5, 0xD4));
        private static readonly SolidColorBrush s_badgeUnselBorder = CreateFrozenBrush(Color.FromArgb(0x40, 0x29, 0x79, 0xFF));

        private static SolidColorBrush CreateFrozenBrush(Color color)
        {
            var b = new SolidColorBrush(color);
            b.Freeze();
            return b;
        }

        public CrossMediaBarView()
        {
            InitializeComponent();

            _xmb.CategoryChanged += OnCategoryChanged;
            _xmb.ItemChanged += OnItemChanged;
            _xmb.ItemActivated += OnItemActivated;

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
            NovelControl.IpcClient = ipc;
        }

        public MainWindow? MainWindow { get; private set; }

        public void SetMainWindow(MainWindow main)
        {
            MainWindow = main;
            CategoryInspector.MainWindow = main;
            ControllerInspector.MainWindow = main;
            NovelControl.MainWindow = main;
        }

        public void SetNerdStatsVisible(bool visible)
        {
            if (PillNerdStats != null)
            {
                PillNerdStats.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        public void UpdateProfileBadge(string activeProfileName)
        {
            if (TxtFooterProfile != null)
            {
                TxtFooterProfile.Text = activeProfileName;
            }

            var userCat = _xmb.Categories.FirstOrDefault(c => c.Id == "users");
            if (userCat != null)
            {
                foreach (var item in userCat.Items)
                {
                    if (item.Title.Equals(activeProfileName, StringComparison.OrdinalIgnoreCase) ||
                        (activeProfileName.Contains("Esports", StringComparison.OrdinalIgnoreCase) && item.Id == "prof_esports") ||
                        (activeProfileName.Contains("FPS", StringComparison.OrdinalIgnoreCase) && item.Id == "prof_fps") ||
                        (activeProfileName.Contains("Racing", StringComparison.OrdinalIgnoreCase) && item.Id == "prof_racing") ||
                        (activeProfileName.Contains("Standard", StringComparison.OrdinalIgnoreCase) && item.Id == "prof_default"))
                    {
                        item.ValueBadge = "Active";
                    }
                    else if (item.Id == "prof_esports") item.ValueBadge = "Esports";
                    else if (item.Id == "prof_fps") item.ValueBadge = "Gyro Aim";
                    else if (item.Id == "prof_racing") item.ValueBadge = "Dynamic";
                    else if (item.Id == "prof_default") item.ValueBadge = "Default";
                }
                RenderItems();
            }
        }

        #region Rendering XMB Elements (Cached Zero-Allocation UI Tree)
        private void RenderCategories()
        {
            if (_builtCategoryCount != _xmb.Categories.Count || PanelCategories.Children.Count != _xmb.Categories.Count)
            {
                PanelCategories.Children.Clear();
                for (int i = 0; i < _xmb.Categories.Count; i++)
                {
                    var cat = _xmb.Categories[i];
                    var catContainer = new Border
                    {
                        Background = Brushes.Transparent,
                        Cursor = System.Windows.Input.Cursors.Hand
                    };

                    int catIdx = i;
                    catContainer.MouseDown += (s, e) => _xmb.SelectCategory(catIdx);

                    var stack = new StackPanel
                    {
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };

                    var iconFrame = new Border
                    {
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Child = new TextBlock
                        {
                            Text = cat.Icon,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center
                        }
                    };
                    stack.Children.Add(iconFrame);

                    var titleTxt = new TextBlock
                    {
                        Text = cat.Title,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Margin = new Thickness(0, 4, 0, 0)
                    };
                    stack.Children.Add(titleTxt);

                    catContainer.Child = stack;
                    PanelCategories.Children.Add(catContainer);
                }
                _builtCategoryCount = _xmb.Categories.Count;
            }

            for (int i = 0; i < _xmb.Categories.Count; i++)
            {
                if (PanelCategories.Children[i] is not Border catContainer ||
                    catContainer.Child is not StackPanel stack ||
                    stack.Children[0] is not Border iconFrame ||
                    iconFrame.Child is not TextBlock iconTxt ||
                    stack.Children[1] is not TextBlock titleTxt) continue;

                bool isSelected = (i == _xmb.ActiveCategoryIndex);
                catContainer.Width = isSelected ? 88 : 72;
                catContainer.Height = isSelected ? 82 : 72;
                catContainer.Margin = new Thickness(isSelected ? 8 : 4, 0, isSelected ? 8 : 4, 0);
                catContainer.Opacity = isSelected ? 1.0 : 0.55;

                iconFrame.Width = isSelected ? 46 : 38;
                iconFrame.Height = isSelected ? 46 : 38;
                iconFrame.CornerRadius = new CornerRadius(isSelected ? 23 : 19);
                iconFrame.Background = isSelected ? s_catFrameSelBg : s_catFrameUnselBg;
                iconFrame.BorderBrush = isSelected ? s_catFrameSelBorder : s_catFrameUnselBorder;
                iconFrame.BorderThickness = new Thickness(isSelected ? 2 : 1);

                iconTxt.FontSize = isSelected ? 18 : 14;

                titleTxt.FontSize = isSelected ? 10 : 9;
                titleTxt.FontWeight = isSelected ? FontWeights.Bold : FontWeights.SemiBold;
                titleTxt.Foreground = isSelected ? s_catTitleSelFg : s_catTitleUnselFg;
            }
        }

        private void RenderItems()
        {
            var activeCat = _xmb.ActiveCategory;
            TxtCategoryIcon.Text = activeCat.Icon;
            TxtCategoryHeader.Text = $"{activeCat.Title} OPTIONS";

            if (_renderedItemCatIndex != _xmb.ActiveCategoryIndex || _renderedItemCount != activeCat.Items.Count || PanelItems.Children.Count != activeCat.Items.Count)
            {
                PanelItems.Children.Clear();
                for (int i = 0; i < activeCat.Items.Count; i++)
                {
                    var item = activeCat.Items[i];
                    var itemBorder = new Border
                    {
                        CornerRadius = new CornerRadius(10),
                        Padding = new Thickness(12, 9, 12, 9),
                        Margin = new Thickness(0, 0, 0, 8),
                        Cursor = System.Windows.Input.Cursors.Hand
                    };

                    int itemIdx = i;
                    itemBorder.MouseDown += (s, e) =>
                    {
                        _xmb.SelectItem(itemIdx);
                        _xmb.ActivateCurrentItem();
                    };

                    var grid = new Grid();
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                    var iconBorder = new Border
                    {
                        Width = 26,
                        Height = 26,
                        CornerRadius = new CornerRadius(6),
                        HorizontalAlignment = HorizontalAlignment.Left,
                        VerticalAlignment = VerticalAlignment.Center,
                        Child = new TextBlock
                        {
                            Text = item.Icon,
                            FontSize = 13,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center
                        }
                    };
                    Grid.SetColumn(iconBorder, 0);
                    grid.Children.Add(iconBorder);

                    var textStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 8, 0) };
                    var title = new TextBlock { Text = item.Title, FontSize = 12 };
                    var subtitle = new TextBlock
                    {
                        Text = item.Subtitle,
                        FontSize = 10,
                        Margin = new Thickness(0, 1, 0, 0),
                        TextTrimming = TextTrimming.CharacterEllipsis
                    };
                    textStack.Children.Add(title);
                    textStack.Children.Add(subtitle);
                    Grid.SetColumn(textStack, 1);
                    grid.Children.Add(textStack);

                    var rightStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                    var badge = new Border
                    {
                        CornerRadius = new CornerRadius(5),
                        Padding = new Thickness(6, 2, 6, 2),
                        BorderThickness = new Thickness(1),
                        Child = new TextBlock { FontSize = 9, FontWeight = FontWeights.Bold }
                    };
                    var chevron = new TextBlock
                    {
                        Text = "►",
                        FontSize = 11,
                        FontWeight = FontWeights.Bold,
                        Foreground = s_catTitleSelFg,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    rightStack.Children.Add(badge);
                    rightStack.Children.Add(chevron);
                    Grid.SetColumn(rightStack, 2);
                    grid.Children.Add(rightStack);

                    itemBorder.Child = grid;
                    PanelItems.Children.Add(itemBorder);
                }
                _renderedItemCatIndex = _xmb.ActiveCategoryIndex;
                _renderedItemCount = activeCat.Items.Count;
            }

            for (int i = 0; i < activeCat.Items.Count; i++)
            {
                var item = activeCat.Items[i];
                bool isSelected = (i == _xmb.ActiveItemIndex);

                if (PanelItems.Children[i] is not Border itemBorder ||
                    itemBorder.Child is not Grid grid ||
                    grid.Children[0] is not Border iconBorder ||
                    grid.Children[1] is not StackPanel textStack ||
                    textStack.Children[0] is not TextBlock title ||
                    textStack.Children[1] is not TextBlock subtitle ||
                    grid.Children[2] is not StackPanel rightStack ||
                    rightStack.Children[0] is not Border badge ||
                    badge.Child is not TextBlock badgeTxt ||
                    rightStack.Children[1] is not TextBlock chevron) continue;

                itemBorder.Background = isSelected ? s_itemSelBg : s_itemUnselBg;
                itemBorder.BorderBrush = isSelected ? s_itemSelBorder : s_itemUnselBorder;
                itemBorder.BorderThickness = new Thickness(isSelected ? 1.5 : 1.0);
                itemBorder.Opacity = isSelected ? 1.0 : 0.78;

                iconBorder.Background = isSelected ? s_itemIconSelBg : s_itemIconUnselBg;
                title.FontWeight = isSelected ? FontWeights.Bold : FontWeights.SemiBold;
                title.Foreground = isSelected ? s_itemTitleSelFg : s_itemTitleUnselFg;
                subtitle.Foreground = isSelected ? s_itemSubSelFg : s_itemSubUnselFg;

                if (!string.IsNullOrEmpty(item.ValueBadge))
                {
                    badge.Visibility = Visibility.Visible;
                    badge.Background = isSelected ? s_badgeSelBg : s_badgeUnselBg;
                    badge.BorderBrush = isSelected ? s_badgeSelBorder : s_badgeUnselBorder;
                    badge.Margin = new Thickness(0, 0, isSelected ? 6 : 0, 0);
                    badgeTxt.Text = item.ValueBadge;
                    badgeTxt.Foreground = isSelected ? s_catTitleSelFg : s_catTitleUnselFg;
                }
                else
                {
                    badge.Visibility = Visibility.Collapsed;
                }

                chevron.Visibility = isSelected ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void UpdateInspector()
        {
            var cat = _xmb.ActiveCategory;
            var item = _xmb.ActiveItem;
            string itemId = item?.Id ?? "";

            // 1. Pro Response & Sensor Tuning / Novel Features
            if (itemId == "ctrl_battery_sentinel" || itemId == "ctrl_hall_stabilizer" || itemId == "ctrl_deep_suite" || itemId == "ctrl_pro_studio" || itemId == "aud_spatial_radar")
            {
                NovelControl.FocusItem(itemId);
                InspectorContentHost.Content = NovelControl;
                return;
            }

            // 2. Controller Category (Overview, Live Input, 1000Hz Overclock)
            if (cat.Id == "controller")
            {
                ControllerInspector.FocusItem(itemId);
                InspectorContentHost.Content = ControllerInspector;
                return;
            }

            // 3. Hardware Lab Category (Gimbal, Circularity, ZeroShift, Jitter)
            if (cat.Id == "lab" || itemId.StartsWith("lab_"))
            {
                LabControl.FocusItem(itemId);
                InspectorContentHost.Content = LabControl;
                return;
            }

            // 4. All Other Categories (Triggers, Gyro, Lightbar, Touchpad, Audio, Profiles, Settings)
            CategoryInspector.SetSectionAndItem(
                cat.Id,
                itemId,
                $"{cat.Icon} {cat.Title}",
                item?.Title,
                item?.Subtitle,
                item?.ValueBadge);
            InspectorContentHost.Content = CategoryInspector;
        }
        #endregion

        #region Navigation Handlers
        private void OnCategoryChanged(int newIndex)
        {
            RenderCategories();
            RenderItems();
            UpdateInspector();

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

            if (item.Id.StartsWith("prof_") && item.Id != "prof_new")
            {
                MainWindow?.ApplyProfileByName(item.Title);
            }

            if (item.Id.StartsWith("lab_"))
            {
                InspectorContentHost.Content = LabControl;
            }
            else if (item.Id == "ctrl_battery_sentinel" || item.Id == "ctrl_hall_stabilizer" || item.Id == "ctrl_deep_suite" || item.Id == "ctrl_pro_studio" || item.Id == "aud_spatial_radar")
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

            // Only update the currently active inspector control to eliminate 4x redundant UI layout/canvas work
            var activeInspector = InspectorContentHost.Content;
            if (ReferenceEquals(activeInspector, LabControl))
            {
                LabControl.UpdateTelemetry(t);
            }
            else if (ReferenceEquals(activeInspector, NovelControl))
            {
                NovelControl.UpdateTelemetry(t);
            }
            else if (ReferenceEquals(activeInspector, ControllerInspector))
            {
                ControllerInspector.UpdateTelemetry(t);
            }
            else
            {
                CategoryInspector.UpdateTelemetry(t);
            }

            TxtXmbClock.Text = MainWindow != null ? MainWindow.FormatCurrentClock(includeDate: true) : DateTime.Now.ToString("ddd dd/MM  h:mm tt");
            TxtXmbBattery.Text = $"{t.Battery}%";
            TxtXmbBatteryIcon.Text = t.Charging ? "⚡" : "🔋";
            if (PillNerdStats.Visibility == Visibility.Visible)
            {
                TxtTopBusInfo.Text = $"{t.PollingRateHz}Hz DIRECT HID KERNEL BUS • {t.LatencyMs:F1}ms LATENCY";
            }

            if (t.Connected)
            {
                TxtFooterStatus.Text = $"{t.ModelName} ({t.ConnectionName})";
                TxtFooterConnection.Text = $"{t.PollingRateHz}Hz Direct HID";
            }
            else
            {
                TxtFooterStatus.Text = "Controller Disconnected";
                TxtFooterConnection.Text = "Waiting for HID Report...";
            }
        }

        private void BtnSlot_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && int.TryParse(btn.Tag?.ToString(), out int slot))
            {
                var btns = new[] { BtnSlot1, BtnSlot2, BtnSlot3, BtnSlot4 };
                foreach (var b in btns)
                {
                    b.Background = Brushes.Transparent;
                    b.Foreground = s_itemSubUnselFg;
                }
                btn.Background = s_catFrameSelBorder;
                btn.Foreground = new SolidColorBrush(Color.FromRgb(0x08, 0x0B, 0x13));

                if (MainWindow != null)
                {
                    MainWindow.CurrentConfig.ActiveControllerSlot = slot;
                    MainWindow.CurrentConfig.PlayerLeds = slot switch { 0 => 4, 1 => 10, 2 => 21, _ => 27 };
                    CategoryInspector.IpcClient?.SendConfig(MainWindow.CurrentConfig);
                    MainWindow.PersistLiveHardwareConfig();
                }
            }
        }

        private void BtnMiniWidget_Click(object sender, RoutedEventArgs e)
        {
            MainWindow?.EnterMiniWidgetMode();
        }
        #endregion
    }
}
