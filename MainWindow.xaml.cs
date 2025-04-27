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
using Microsoft.VisualBasic;
using System.Data;
using NAudio.SoundFont;

namespace AudioRecorder
{
    public class AppSettings
    {
        public double SliderThresholdValue { get; set; }
        public double SliderDaysValue { get; set; }
        public string SelectedMicrophone { get; set; }
        public string RecordsFolder { get; set; }
        public string SelectedDuration { get; set; }
    }
    /// <summary>
    /// Логика взаимодействия для MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {

        private const string SettingsFileName = "settings.json";
        private AppSettings _settings;
        private float _gain = 1.0f;
        private WasapiCapture capture;
        private WaveFileWriter waveWriter;

        private bool isRecording = false;
        private bool isPaused = false;
        private string recordsDir = System.IO.Path.GetFullPath("records");
        private DispatcherTimer uiTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        private DispatcherTimer splitTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        private int totalSeconds = 0;
        private bool splitPending = false;

        private MMDevice _monitorDevice;

        private DispatcherTimer _soundTimer;
        
        private float _lastPeak;
        private MiniWindow miniWindow;
        public MainWindow()
        {
            InitializeComponent();
            miniWindow = new MiniWindow(this);
            this.RefreshMicrophoneList();
            this.RecTimer.Visibility = Visibility.Hidden;
            this.SaveDaysLimit.Value = 7;

            LoadSettings();
            if (!Directory.Exists(recordsDir))
                Directory.CreateDirectory(recordsDir);
            this.FolderPathText.Text = $"{this.recordsDir}";
            
            this.uiTimer.Tick += UpdateTimer;


            _soundTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(100)
            };
            _soundTimer.Tick += SoundTimer_Tick;
            _soundTimer.Start();

            // 2) Подпишемся, когда пользователь выбирает микрофон
            SelectMicro.SelectionChanged += (s, e) => RefreshMonitorDevice();
            RefreshMonitorDevice();
            
            SampleRateSlider.ValueChanged += (_, __) => SaveSettings();
            SaveDaysLimit.ValueChanged += (_, __) => SaveSettings();
            SelectMicro.SelectionChanged += (_, __) => SaveSettings();
            this.miniWindow.ToggleTimer(Visibility.Hidden);
        }

        private void UpdateTimer(object sender, EventArgs e)
        {
            if (this.totalSeconds == 60)
            {
                this.ScheduleNextSplit();
            }
            totalSeconds++;
            TimeSpan time = TimeSpan.FromSeconds(totalSeconds);
            this.RecTimer.Text = $"{time.Hours:D2}:{time.Minutes:D2}:{time.Seconds:D2}";
            this.miniWindow.UpdateTimer($"{time.Hours:D2}:{time.Minutes:D2}:{time.Seconds:D2}");
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
            SaveSettings();
            Application.Current.Shutdown();
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            this.Visibility = Visibility.Hidden;
            this.miniWindow.Visibility = Visibility.Visible;
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

        public void StartRecording()
        {
            CleanOldRecords();

            if (isPaused == true)
            {
                this.ResumeRecording();
                return;
            }
            this.SetUiBlocked(true);
            this.RecTimer.Text = "00:00:00";
            this.miniWindow.UpdateTimer("00:00:00");
            RecStartButton.IsEnabled = false;
            this.miniWindow.setRecbtnEnabled(false);
            RecStartButton.IsChecked = true;
            RecPauseButton.IsChecked = false;
            this.miniWindow.setPausebtnChecked(false);
            this.miniWindow.setRecbtnChecked(true);


            string selectedName = this.SelectMicro.SelectedItem as string;
            var dev = new MMDeviceEnumerator()
                .EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
                .FirstOrDefault(d => d.FriendlyName == selectedName);
            if (dev == null)
            {
                MessageBox.Show("Устройство не найдено");
                return;
            }
            var sliderVal = (float)SampleRateSlider.Value;
            _gain = 1 + sliderVal * 0.25f;                  

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
            capture.DataAvailable -= OnDataAvailable;
            capture.DataAvailable += OnDataAvailable;
            capture.RecordingStopped -= Capture_RecordingStopped;
            capture.RecordingStopped += Capture_RecordingStopped;

            waveWriter = new WaveFileWriter(fullPath, capture.WaveFormat);

            RecTimer.Visibility = Visibility.Visible;
            this.miniWindow.ToggleTimer(Visibility.Visible);
            uiTimer.Start();
            this.totalSeconds = 0;

            capture.StartRecording();
            this.isRecording = true;
            this.isPaused = false;
        }

        public void PauseRecording()
        {
            if (isRecording == false) { this.RecPauseButton.IsChecked = false; return; }
            isPaused = true;
            capture.StopRecording();
            uiTimer?.Stop();
            this.RecPauseButton.IsEnabled = false;
            this.RecPauseButton.IsChecked = true;
            this.miniWindow.setPausebtnChecked(true);
            this.miniWindow.setPausebtnEnabled(false);
        }

        public void ResumeRecording()
        {
            if (isPaused == false) return;
            isPaused = false;
            capture.StartRecording();
            uiTimer.Start();
            this.RecPauseButton.IsChecked = false;
            this.RecPauseButton.IsEnabled = true;
            this.miniWindow.setPausebtnChecked(false);
            this.miniWindow.setPausebtnEnabled(true);
        }

        public void StopRecording()
        {
            capture?.StopRecording();
            this.RecPauseButton.IsEnabled = true;
            this.RecStartButton.IsEnabled = true;
            this.RecStartButton.IsChecked = false;
            this.RecPauseButton.IsChecked = false;
            this.miniWindow.setRecbtnChecked(false);
            this.miniWindow.setPausebtnChecked(false);
            this.miniWindow.setRecbtnEnabled(true);
            this.miniWindow.setPausebtnEnabled(true);
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
            this.miniWindow.ToggleTimer(Visibility.Hidden);
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
            this.TitleText.Text = $"Заплановано розбиття на: {next}";
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
            pressed.IsChecked = true;
            pressed.IsEnabled = false;
            if (pressed == null) return;
            var all = new[] { this.Duration10min, this.Duration30min, this.Duration1h, this.Duration2h };
            foreach (var btn in all)
            {
                if (!ReferenceEquals(btn, pressed)) { 
                    btn.IsChecked = false;
                    btn.IsEnabled = true;
                }
            }
            SaveSettings();
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
                SaveSettings();
                this.FolderPathText.Text = $"{this.recordsDir}";
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
            SaveSettings();
        }

        private void SoundTimer_Tick(object sender, EventArgs e)
        {
            if (_monitorDevice == null)
            {
                SetMicroIcon(false);
                return;
            }
            _lastPeak = _monitorDevice.AudioMeterInformation.MasterPeakValue * 10000;
            float threshold = 150 - (float)SampleRateSlider.Value;
            //this.TitleText.Text = $"Threshold: {threshold} | Last Peek: {_lastPeak}";
            SetMicroIcon(_lastPeak > threshold);
        }
        private void SetMicroIcon(bool active)
        {
            string rel = active ? "source/micro_green.png" : "source/micro.png";
            this.microImg.Source = new BitmapImage(new Uri(rel, UriKind.Relative));
        }
        private void RefreshMonitorDevice()
        {
            var name = SelectMicro.SelectedItem as string;
            if (string.IsNullOrEmpty(name)) return;
            _monitorDevice = new MMDeviceEnumerator()
                .EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
                .FirstOrDefault(d => d.FriendlyName == name);
        }
        private void LoadSettings()
        {
            if (File.Exists(SettingsFileName))
            {
                try
                {
                    var json = File.ReadAllText(SettingsFileName);
                    _settings = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json);
                }
                catch
                {
                    _settings = null;
                }
            }

            if (_settings == null)
                _settings = new AppSettings();

            // 1) Позиции слайдеров
            SampleRateSlider.Value = _settings.SliderThresholdValue;
            SaveDaysLimit.Value = _settings.SliderDaysValue;

            // 2) Папка
            if (Directory.Exists(_settings.RecordsFolder))
            {
                recordsDir = _settings.RecordsFolder;
                this.FolderPathText.Text = recordsDir;
            }

            // 3) Выбранный микрофон
            if (!string.IsNullOrEmpty(_settings.SelectedMicrophone))
            {
                int idx = SelectMicro.Items
                                 .Cast<string>()
                                 .ToList()
                                 .IndexOf(_settings.SelectedMicrophone);
                if (idx >= 0) SelectMicro.SelectedIndex = idx;
            }
            // 4) Выбранная длительность
            if (!string.IsNullOrEmpty(_settings.SelectedDuration))
            {
                if (_settings.SelectedDuration == "10") Duration10min.IsChecked = true;
                if (_settings.SelectedDuration == "30") Duration30min.IsChecked = true;
                if (_settings.SelectedDuration == "60") Duration1h.IsChecked = true;
                if (_settings.SelectedDuration == "120") Duration2h.IsChecked = true;
            }
            else
            {
                Duration10min.IsChecked = true;
            }
        }

        public void SaveSettings()
        {
            _settings.SliderThresholdValue = SampleRateSlider.Value;
            _settings.SliderDaysValue = SaveDaysLimit.Value;
            _settings.RecordsFolder = recordsDir;
            _settings.SelectedMicrophone = SelectMicro.SelectedItem as string;
            _settings.SelectedDuration =
                Duration10min.IsChecked == true ? "10" :
                Duration30min.IsChecked == true ? "30" :
                Duration1h.IsChecked == true ? "60" : "120";

            var json = System.Text.Json.JsonSerializer
                       .Serialize(_settings, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsFileName, json);
        }
        private void OnDataAvailable(object sender, WaveInEventArgs e)
        {
            var writer = waveWriter;
            if (writer == null) return;

            int bytes = e.BytesRecorded;
            byte[] buffer = new byte[bytes];

            for (int i = 0; i < bytes; i += 2)
            {
                short sample = BitConverter.ToInt16(e.Buffer, i);
                int amplified = (int)(sample * _gain);
                amplified = Math.Clamp(amplified, short.MinValue, short.MaxValue);
                var bs = BitConverter.GetBytes((short)amplified);
                buffer[i] = bs[0];
                buffer[i + 1] = bs[1];
            }

            writer.Write(buffer, 0, bytes);
            writer.Flush();
        }
    }
}

