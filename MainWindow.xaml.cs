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
using System.Threading;

namespace WpfApp1
{
    /// <summary>
    /// Логика взаимодействия для MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private WasapiCapture capture;
        private WaveFileWriter waveWriter;

        // Состояние
        private bool isRecording = false;
        private bool isPaused = false;
        private string recordsDir = System.IO.Path.GetFullPath("records");
        private DispatcherTimer uiTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        private DispatcherTimer splitTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        private int totalSeconds = 0;
        private bool splitPending = false;

        public MainWindow()
        {
            InitializeComponent();
            this.RefreshMicrophoneList();
            this.RecTimer.Visibility = Visibility.Hidden;
            if (!Directory.Exists(recordsDir))
                Directory.CreateDirectory(recordsDir);
            this.FolderPathText.Text = $"{this.recordsDir}";
            this.SaveDaysLimit.Value = 7;
            this.uiTimer.Tick += UpdateTimer;
            this.Duration10min.IsChecked = true;
        }

        private void UpdateTimer(object sender, EventArgs e)
        {
            if (this.totalSeconds == 60) {
                this.ScheduleNextSplit();
            }
            totalSeconds++;
            TimeSpan time = TimeSpan.FromSeconds(totalSeconds);
            this.RecTimer.Text = $"{time.Hours:D2}:{time.Minutes:D2}:{time.Seconds:D2}";
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
            CleanOldRecords();

            if (isPaused == true)
            {
                this.ResumeRecording();
                return;
            }
            this.SetUiBlocked(true);
            this.RecTimer.Text = "00:00:00";
            RecStartButton.IsEnabled = false;
            RecPauseButton.IsChecked = false;

            string selectedName = this.SelectMicro.SelectedItem as string;
            var dev = new MMDeviceEnumerator()
                .EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
                .FirstOrDefault(d => d.FriendlyName == selectedName);
            if (dev == null)
            {
                MessageBox.Show("Устройство не найдено");
                return;
            }


            var startTime = DateTime.Now;
            string dateFolder = startTime.ToString("dd-MM-yyyy");
            string fullDir = System.IO.Path.Combine(recordsDir, dateFolder);
            Directory.CreateDirectory(fullDir);

            string fileName = startTime.ToString("HH-mm-ss") + ".wav";
            string fullPath = System.IO.Path.Combine(fullDir, fileName);

            capture?.Dispose();
            waveWriter?.Dispose();

            capture = new WasapiCapture(dev);
            capture.WaveFormat = new WaveFormat(22050, 1);
            capture.DataAvailable += (s, e) =>
            {
                waveWriter.Write(e.Buffer, 0, e.BytesRecorded);
            };
            capture.RecordingStopped += Capture_RecordingStopped;

            waveWriter = new WaveFileWriter(fullPath, capture.WaveFormat);

            RecTimer.Visibility = Visibility.Visible;
            uiTimer.Start();
            this.totalSeconds = 0;
            
            capture.StartRecording();
            this.isRecording = true;
            this.isPaused = false;
        }

        private void PauseRecording()
        {
            if (isRecording == false) { this.RecPauseButton.IsChecked = false; return; }
            isPaused = true;
            capture.StopRecording();
            uiTimer?.Stop();
            this.RecPauseButton.IsEnabled = false;
        }

        private void ResumeRecording()
        {
            if (isPaused == false) return;
            isPaused = false;
            capture.StartRecording();
            uiTimer.Start();
            this.RecPauseButton.IsChecked = false;
            this.RecPauseButton.IsEnabled = true;
        }

        private void StopRecording()
        {
            capture?.StopRecording();
            this.RecPauseButton.IsEnabled = true;
            this.RecStartButton.IsEnabled = true;
            this.RecStartButton.IsChecked = false;
            this.RecPauseButton.IsChecked = false;
            this.SetUiBlocked(false);
        }

        // Этот метод сработает, когда capture.StopRecording() завершится
        private void Capture_RecordingStopped(object sender, StoppedEventArgs e)
        {
            // Если мы просто ставили на паузу — не закрываем waveWriter/capture
            if (isPaused)
                return;

            // Иначе — всегда сначала освобождаем "старые" ресурсы
            waveWriter?.Dispose();
            waveWriter = null;
            capture?.Dispose();
            capture = null;

            uiTimer.Stop();
            splitTimer.Stop();
            RecTimer.Visibility = Visibility.Hidden;
            totalSeconds = 0;
            isRecording = false;

            // Если это был сплит — сразу стартуем новую запись
            if (splitPending)
            {
                splitPending = false;
                StartRecording();
                this.RecStartButton.IsChecked = true;
            }
        }

        private void ScheduleNextSplit()
        {
            // Остановим и отпишемся от предыдущего, если он есть
            if (splitTimer != null)
            {
                splitTimer.Stop();
                splitTimer.Tick -= SplitTimer_Tick;
            }

            DateTime now = DateTime.Now;
            DateTime next = GetNextSplitTime(now);
            var interval = next - now;
            if (interval.TotalMilliseconds < 0)
                interval = TimeSpan.Zero;

            splitTimer = new DispatcherTimer { Interval = interval };
            splitTimer.Tick += SplitTimer_Tick;
            splitTimer.Start();
        }
        private void SplitTimer_Tick(object sender, EventArgs e)
        {
            var timer = (DispatcherTimer)sender;
            timer.Stop();
            timer.Tick -= SplitTimer_Tick;

            if (isRecording && !isPaused)
            {
                // Отмечаем, что после остановки надо запустить новый сплит
                splitPending = true;
                StopRecording();
                // Никакого Thread.Sleep и прямого StartRecording() здесь больше нет!
            }
            else if (isPaused)
            {
                ScheduleNextSplit();
            }
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
        private void SetUiBlocked(bool block)
        {
            bool enabled = !block;

            OutputGroup.IsEnabled = enabled;
            SettingsGroup.IsEnabled = enabled;
            SaveDaysLimit.IsEnabled = enabled;
            SaveLimitText.IsEnabled = enabled;
            SaveLimitInt.IsEnabled = enabled;
        }

        private void AudioRecorder_Closed(object sender, EventArgs e)
        {
            this.StopRecording();
        }

    }
}

