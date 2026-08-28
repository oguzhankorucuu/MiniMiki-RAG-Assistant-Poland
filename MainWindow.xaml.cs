using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using MiniMiki.ViewModels;

namespace MiniMiki
{
    public partial class MainWindow : Window
    {
        public MainWindow(MainViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;

            // Yeni mesaj eklendiğinde sohbet alanını otomatik en alta kaydır.
            viewModel.Messages.CollectionChanged += (_, _) =>
                Dispatcher.BeginInvoke(new Action(() => ChatScrollViewer.ScrollToEnd()), DispatcherPriority.Background);
        }

        private void SourceChip_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { Tag: string { Length: > 0 } url })
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
        }
    }
}
