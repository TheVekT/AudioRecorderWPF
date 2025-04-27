using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace AudioRecorder
{
    /// <summary>
    /// Логика взаимодействия для MiniWindow.xaml
    /// </summary>
    public partial class MiniWindow : Window
    {
        private MainWindow mainWindow;

        public MiniWindow(MainWindow main)
        {
            InitializeComponent();
            this.Visibility = Visibility.Hidden;
            this.mainWindow = main;
        }
        private void ShowMainWindowButton_Click(object sender, RoutedEventArgs e)
        {
            this.Visibility = Visibility.Hidden;
            this.mainWindow.Visibility = Visibility.Visible;
        }

        private void CloseMiniwin(object sender, EventArgs e)
        {
            this.mainWindow.SaveSettings();
            this.mainWindow.StopRecording();
            Application.Current.Shutdown();
        }

        private void Dragbtn_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                this.DragMove();
            }
        }
        public void setRecbtnChecked(bool rule) { 
            this.RecStartButton.IsChecked = rule;
        }
        public void setPausebtnChecked(bool rule)
        {
            this.RecPauseButton.IsChecked = rule;
        }
        public void setRecbtnEnabled(bool rule)
        {
            this.RecStartButton.IsEnabled = rule;
        }
        public void setPausebtnEnabled(bool rule)
        {
            this.RecPauseButton.IsEnabled = rule;
        }

        public void UpdateTimer(string time) { 
            this.RecTimer.Text = time;
        }
        public void ToggleTimer(Visibility status)
        {
            this.RecTimer.Visibility = status;
        }

        private void RecStartButton_Click(object sender, RoutedEventArgs e)
        {
            this.mainWindow.StartRecording();
        }

        private void RecPauseButton_Click(object sender, RoutedEventArgs e)
        {
            this.mainWindow.PauseRecording();
        }
    }
}