using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using NAudio.CoreAudioApi;
using NAudio.Gui;
using NAudio.Wave;
using Microsoft.WindowsAPICodePack.Dialogs;
using System.Globalization;

namespace WpfApp1
{
    /// <summary>
    /// Логика взаимодействия для MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private bool is_recording = false;
        private string recordsDir = System.IO.Path.GetFullPath("records");
        public MainWindow()
        {
            InitializeComponent();
            this.RefreshMicrophoneList();
            this.RecTimer.Visibility = Visibility.Hidden;
            if (!Directory.Exists(recordsDir))
                Directory.CreateDirectory(recordsDir);
            this.FolderPathText.Text = $"{this.recordsDir}";
            this.SaveDaysLimit.Value = 7;
        }
        private void HeaderBorder_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                this.DragMove();
            }
        }
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        private void SaveDaysLimit_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            this.SaveLimitInt.Content = ((int)e.NewValue).ToString();
        }

        private void SampleRateSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            this.LabelValue.Content = $"{((int)e.NewValue)} %";
        }

        private void StartButton_Click(object sender, RoutedEventArgs e) => StartRecording();
        private void PauseButton_Click(object sender, RoutedEventArgs e) => PauseRecording();
        private void ResumeButton_Click(object sender, RoutedEventArgs e) => ResumeRecording();
        private void StopButton_Click(object sender, RoutedEventArgs e) => StopRecording();

        private void RefreshMicrophoneList()
        {
            SelectMicro.Items.Clear();
            var enumerator = new MMDeviceEnumerator();
            var devices = enumerator
                .EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
            foreach (var dev in devices)
                SelectMicro.Items.Add(dev.FriendlyName);

            if (SelectMicro.Items.Count > 0)
                SelectMicro.SelectedIndex = 0;
            else
                SelectMicro.Items.Add("Пристрої не знайдено!");
        }

        private void StartRecording()
        {

            this.RecStartButton.IsEnabled = false;
            this.RecPauseButton.IsChecked = false;
        }

        private void StopRecording()
        {

            this.RecPauseButton.IsEnabled = true;
            this.RecStartButton.IsEnabled = true;
            this.RecStartButton.IsChecked = false;
            this.RecPauseButton.IsChecked = false;
        }
        private void PauseRecording()
        {
            if (this.is_recording == false) {
                this.RecPauseButton.IsChecked = false;
                return;
            }


            this.RecPauseButton.IsEnabled = false;
        }

        private void ResumeRecording()
        {

            this.RecPauseButton.IsChecked = false;
            this.RecPauseButton.IsEnabled = true;
        }


        private DateTime GetNextSplitTime(DateTime dt)
        {
            dt = dt.AddSeconds(-dt.Second).AddMilliseconds(-dt.Millisecond);
            int minutes = 10;
            if (this.Duration10min.IsChecked == true) minutes = 10;
            if (this.Duration30min.IsChecked == true) minutes = 30;
            if (this.Duration1h.IsChecked == true) minutes = 60;
            if (this.Duration2h.IsChecked == true) minutes = 120;

            int slot = (dt.Minute / minutes) + 1;
            int nextM = slot * minutes;
            if (nextM >= 60)
                return dt.Date.AddHours(dt.Hour + 1);
            else
                return dt.AddMinutes(nextM - dt.Minute);
        }

        private void DurationButton_Checked(object sender, RoutedEventArgs e)
        {
            var pressed = sender as ToggleButton;
            if (pressed == null) return;
            var all = new[] { this.Duration10min, this.Duration30min, this.Duration1h, this.Duration2h };
            foreach (var btn in all)
            {
                if (!ReferenceEquals(btn, pressed))
                    btn.IsChecked = false;
            }
        }
        private void ChooseFolder(object sender, RoutedEventArgs e)
        {
            var dlg = new CommonOpenFileDialog
            {
                Title = "Виберіть папку для збереження записів.",
                InitialDirectory = recordsDir,
                IsFolderPicker = true
            };

            if (dlg.ShowDialog() == CommonFileDialogResult.Ok)
            {
                recordsDir = dlg.FileName;
                this.FolderPathText.Text = $"{this.recordsDir}";
                //SaveSettings();
            }
        }
        private void CleanOldRecords()
        {
            if (!Directory.Exists(recordsDir))
            {
                Directory.CreateDirectory(recordsDir);
            }

            int daysToKeep = Convert.ToInt32(SaveDaysLimit.Value);
            if (daysToKeep == 0)
            {
                return;
            }

            DateTime today = DateTime.Now;

            foreach (string dirPath in Directory.GetDirectories(recordsDir))
            {
                string folderName = System.IO.Path.GetFileName(dirPath);

                if (DateTime.TryParseExact(
                    folderName,
                    "dd-MM-yyyy",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out DateTime folderDate))
                {
                    int daysDiff = (today - folderDate).Days;
                    if (daysDiff > daysToKeep)
                    {
                        try
                        {
                            Directory.Delete(dirPath, recursive: true);
                            Console.WriteLine($"Удалена папка: {dirPath}");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Ошибка при удалении {dirPath}: {ex.Message}");
                        }
                    }
                }
            }
        }
    }
}

