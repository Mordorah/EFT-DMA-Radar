/*
 * Lone EFT DMA Radar
 * MIT License - Copyright (c) 2025 Lone DMA
 */

using LoneEftDmaRadar.UI.Radar.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace LoneEftDmaRadar.UI.Radar.Views
{
    public partial class ActiveQuestsTab : UserControl
    {
        private readonly ActiveQuestsViewModel _vm;
        private readonly DispatcherTimer _autoRefreshTimer;

        public ActiveQuestsTab()
        {
            InitializeComponent();
            _vm = new ActiveQuestsViewModel();
            DataContext = _vm;

            _autoRefreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(15)
            };
            _autoRefreshTimer.Tick += (_, _) => _vm.RefreshQuests();

            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _vm.RefreshQuests();
            _autoRefreshTimer.Start();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            _autoRefreshTimer.Stop();
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            _vm.RefreshQuests();
        }
    }
}
