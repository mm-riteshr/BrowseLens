using System;
using System.Windows;
using System.Windows.Threading;
using BrowseLens.Desktop.ViewModels;

namespace BrowseLens.Desktop
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel;
        private readonly DispatcherTimer _timer;

        public MainWindow(MainViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            DataContext = _viewModel;

            // Periodic UI refresh tick (1 second)
            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _timer.Tick += async (s, e) =>
            {
                await _viewModel.PeriodicTickAsync();
            };
            _timer.Start();

            Loaded += async (s, e) =>
            {
                await _viewModel.Dashboard.RefreshDashboardDataAsync();
            };

            Closed += (s, e) =>
            {
                _timer.Stop();
            };
        }
    }
}

