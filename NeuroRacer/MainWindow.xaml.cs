
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Threading;
using System.Collections.Generic;
using SharpDX.DirectInput;
using System.Security.AccessControl;
using Microsoft.Win32;



namespace NeuroRacer
{


    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private const string SettingsFile = "settings.json";
        private UdpClient udpClient;
        private List<ScheduleAction> schedule;
        private DateTime startTime;
        private bool isRunning;
        private bool isPaused;
        private ScheduleAction currentCue;
        private DirectInput directInput;
        private Joystick joystick;
        private Random random;
        private int selectedButtonIndex = 0;
        private bool listeningForInput = false;
        private List<DeviceInstance> availableDevices;
        private DateTime cueStartTime;
        private bool buttonPressedDuringCue;
        private bool isLoadingSettings = true;
        private string csvLogFile;
        private string scheduleFilePath;
        private string outputDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);


        private class AppSettings
        {
            public string SelectedDevice { get; set; }
            public int SelectedButtonIndex { get; set; }
        }

        // Wrapper class for JSON root
        public class ScheduleWrapper
        {
            public List<ScheduleAction> test_schedule { get; set; }
        }


        public MainWindow()
        {
            InitializeComponent();
            this.Title = "NeuroRacer - Test Conductor";
            udpClient = new UdpClient();
            random = new Random();
            LoadSchedule();
            InitializeController();
            //OpenConsole();
            PopulateDeviceDropdown();
            PopulateControllerDropdown();
            LoadSettings(); // Load saved settings on startup
            ButtonPollLoop();
            CountdownProgressBar.Value = 0; // Ensure progress bar starts at 0
            StartDeviceMonitoring();
            LogToConsole($"Default output directory: {outputDirectory}");

        }

        private void SelectOutputDirectory_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                CheckFileExists = false, // Allows selecting a folder
                ValidateNames = false,
                FileName = "Select Folder" // Trick to enable folder selection
            };

            if (dialog.ShowDialog() == true)
            {
                outputDirectory = System.IO.Path.GetDirectoryName(dialog.FileName);
                LogToConsole($"Output directory set to: {outputDirectory}");
            }
        }

        private async Task Countdown(int durationMs, string phase, int currentStep, int totalSteps)
        {

            int steps = 100;
            int interval = durationMs / steps;

            Dispatcher.Invoke(() => CountdownProgressBar.Value = 0);

            for (int i = 0; i <= steps; i++)
            {
                while (isPaused) await Task.Delay(100); // Wait while paused, then continue

                if (!isRunning) break; // Stop progress if stopped

                Dispatcher.Invoke(() =>
                {
                    CountdownProgressBar.Value = i;
                    CountdownText.Text = $"{i * (durationMs / steps) / 1000.0:F1}s (Step {currentStep} of {totalSteps})";
                });
                await Task.Delay(interval);
            }
        }
        private void SelectScheduleFile_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = "JSON Files (*.json)|*.json",
                Title = "Select Test Schedule File"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                scheduleFilePath = openFileDialog.FileName;
                LogToConsole($"Selected schedule file: {scheduleFilePath}");
                LoadScheduleFromFile(scheduleFilePath);
            }
        }
        private void LoadScheduleFromFile(string filePath)
        {
            try
            {
                string json = File.ReadAllText(filePath);

                // Define a wrapper class to match the JSON structure
                var scheduleWrapper = JsonSerializer.Deserialize<ScheduleWrapper>(json);

                if (scheduleWrapper?.test_schedule != null)
                {
                    schedule = scheduleWrapper.test_schedule;
                    LogToConsole("Schedule loaded successfully from file.");
                }
                else
                {
                    LogToConsole("Failed to load schedule: No valid test_schedule found.");
                }
            }
            catch (Exception ex)
            {
                LogToConsole($"Failed to load schedule: {ex.Message}");
            }
        }

        private void SetCsvFileName()
        {
            Dispatcher.Invoke(() =>
            {
                if (string.IsNullOrWhiteSpace(outputDirectory) || !Directory.Exists(outputDirectory))
                {
                    outputDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                    LogToConsole($"Invalid output directory, defaulting to: {outputDirectory}");
                }

                string testName = string.IsNullOrWhiteSpace(TestNameTextBox.Text) ? "Test" : TestNameTextBox.Text;
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                csvLogFile = Path.Combine(outputDirectory, $"{testName}_{timestamp}.csv");
                InitializeCsvLog();
            });
        }

        private void InitializeCsvLog()
        {
            if (!File.Exists(csvLogFile))
            {
                File.WriteAllText(csvLogFile, "Timestamp,Cue Type,Button Pressed,Reaction Time (ms)\n");
            }
        }

        private void LogCueToCsv(string cueType, bool buttonPressed, double reactionTime)
        {
            string logEntry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss},{cueType},{buttonPressed},{reactionTime}\n";
            File.AppendAllText(csvLogFile, logEntry);
        }

        private void SaveSettings()
        {

            if (isLoadingSettings) return; // Prevent saving while settings are still loading

            Dispatcher.Invoke(() =>
            {
                var settings = new AppSettings
                {
                    SelectedDevice = DeviceDropdown.SelectedItem?.ToString(),
                    SelectedButtonIndex = selectedButtonIndex + 1
                };
                File.WriteAllText(SettingsFile, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
                LogToConsole("Settings saved.");
            });
        }

        private void LoadSettings()
        {
            if (!File.Exists(SettingsFile))
            {
                isLoadingSettings = false;
                return;
            }

            try
            {
                string json = File.ReadAllText(SettingsFile);
                var settings = JsonSerializer.Deserialize<AppSettings>(json);
                if (settings != null)
                {
                    selectedButtonIndex = settings.SelectedButtonIndex;
                    Dispatcher.Invoke(() =>
                    {
                        int deviceIndex = availableDevices.FindIndex(d => d.InstanceName == settings.SelectedDevice);
                        if (deviceIndex >= 0)
                        {
                            DeviceDropdown.SelectedIndex = deviceIndex;
                        }

                        // Ensure the correct button is selected in the dropdown
                        if (selectedButtonIndex >= 0 && selectedButtonIndex < ButtonDropdown.Items.Count)
                        {
                            ButtonDropdown.SelectedIndex = selectedButtonIndex;
                        }
                    });
                    LogToConsole($"Settings loaded. Selected Button: {selectedButtonIndex + 1}");
                }
            }
            catch (Exception ex)
            {
                LogToConsole($"Failed to load settings: {ex.Message}");
            }

            isLoadingSettings = false; // Mark settings as loaded
        }

        //private void OpenConsole()
        //{
        //    AllocConsole(); // Use this to open a real console window
        //}

        //[System.Runtime.InteropServices.DllImport("kernel32.dll")]
        //private static extern bool AllocConsole();

        private void LogToConsole(string message)
        {
            //AllocConsole();
            Console.WriteLine(message);
            Console.Out.Flush();

            Dispatcher.Invoke(() =>
            {
                LogTextBox.AppendText(message + Environment.NewLine);
                LogTextBox.ScrollToEnd(); // Auto-scroll to the latest log
            });

        }

        private void LoadSchedule()
        {
            try
            {
                string json = File.ReadAllText("schedule.json");
                var scheduleData = JsonSerializer.Deserialize<ScheduleData>(json);
                schedule = scheduleData?.test_schedule ?? new List<ScheduleAction>();
                LogToConsole("Schedule loaded successfully.");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading schedule: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                LogToConsole($"Error loading schedule: {ex.Message}");
            }
        }

        private void InitializeController()
        {
            directInput = new DirectInput();
            availableDevices = new List<DeviceInstance>();
            availableDevices.AddRange(directInput.GetDevices(DeviceType.Gamepad, DeviceEnumerationFlags.AttachedOnly));
            availableDevices.AddRange(directInput.GetDevices(DeviceType.Joystick, DeviceEnumerationFlags.AttachedOnly));
            availableDevices.AddRange(directInput.GetDevices(DeviceType.Flight, DeviceEnumerationFlags.AttachedOnly));

            if (availableDevices.Count > 0)
            {
                joystick = new Joystick(directInput, availableDevices[0].InstanceGuid);
                joystick.Acquire();
                LogToConsole("Game controller initialized.");
            }
        }

        private void StartDeviceMonitoring()
        {
            Task.Run(async () =>
            {
                List<DeviceInstance> previousDevices = new List<DeviceInstance>(directInput.GetDevices(DeviceType.Joystick, DeviceEnumerationFlags.AttachedOnly));
                previousDevices.AddRange(directInput.GetDevices(DeviceType.Gamepad, DeviceEnumerationFlags.AttachedOnly));
                previousDevices.AddRange(directInput.GetDevices(DeviceType.Flight, DeviceEnumerationFlags.AttachedOnly));

                while (true)
                {
                    await Task.Delay(2000); // Check every 2 seconds

                    var currentDevices = new List<DeviceInstance>(directInput.GetDevices(DeviceType.Joystick, DeviceEnumerationFlags.AttachedOnly));
                    currentDevices.AddRange(directInput.GetDevices(DeviceType.Gamepad, DeviceEnumerationFlags.AttachedOnly));
                    currentDevices.AddRange(directInput.GetDevices(DeviceType.Flight, DeviceEnumerationFlags.AttachedOnly));

                    if (currentDevices.Count != previousDevices.Count) // Device added/removed
                    {
                        previousDevices = currentDevices;
                        availableDevices = currentDevices;
                        Dispatcher.Invoke(() =>
                        {
                            PopulateDeviceDropdown();
                            LogToConsole("Device list updated.");
                        });
                    }
                }
            });
        }

        private void PopulateDeviceDropdown()
        {

            Dispatcher.Invoke(() =>
            {
                DeviceDropdown.Items.Clear();
                foreach (var device in availableDevices)
                {
                    DeviceDropdown.Items.Add(device.InstanceName);
                }
                if (DeviceDropdown.Items.Count > 0)
                {
                    DeviceDropdown.SelectedIndex = 0;
                    UpdateButtonDropdown();
                }
            });
        }



        private void DeviceDropdown_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (DeviceDropdown.SelectedIndex >= 0)
            {
                joystick = new Joystick(directInput, availableDevices[DeviceDropdown.SelectedIndex].InstanceGuid);
                joystick.Acquire();
                LogToConsole($"Selected input device: {availableDevices[DeviceDropdown.SelectedIndex].InstanceName}");
                UpdateButtonDropdown();
                SaveSettings();
            }
        }

        private void UpdateButtonDropdown()
        {
            if (joystick == null) return;

            ButtonDropdown.Items.Clear(); // Ensure the list is cleared before updating

            try
            {
                joystick.Poll();
                var state = joystick.GetCurrentState();
                int buttonCount = joystick.Capabilities.ButtonCount;

                for (int i = 0; i < buttonCount; i++)
                {
                    ButtonDropdown.Items.Add($"Button {i}");
                }

                if (ButtonDropdown.Items.Count > 0)
                {
                    ButtonDropdown.SelectedIndex = 0;
                }
            }
            catch (Exception ex)
            {
                LogToConsole($"Failed to update button dropdown: {ex.Message}");
            }
        }

        private async void StartScheduleButton_Click(object sender, RoutedEventArgs e)
        {
            if (isRunning) return;
            isRunning = true;
            isPaused = false;
            startTime = DateTime.Now;
            LogToConsole("Schedule execution started.");
            await MonitorSchedule();
        }

        private async Task MonitorSchedule()
        {
            SetCsvFileName();
            int actionIndex = 0;
            foreach (var action in schedule)
            {
                while (isPaused)
                {
                    await Task.Delay(500);
                }

                if (!isRunning) break;

                if (action.wait != null)
                {
                    int min = action.wait.between[0].min;
                    int max = action.wait.between[1].max;
                    if (min > max)
                    {
                        LogToConsole($"Invalid wait time range: min ({min}) cannot be greater than max ({max}). Skipping...");
                        continue;
                    }

                    int waitTime = random.Next(min, max) * 1000;
                    LogToConsole($"Waiting for {waitTime / 1000} seconds.");
                    await Countdown(waitTime, "Waiting", actionIndex + 1, schedule.Count);
                }
                else if (action.audio_cue || action.visual_cue)
                {
                    currentCue = action;
                    buttonPressedDuringCue = false;
                    cueStartTime = DateTime.Now;
                    SendCommand(action);
                    LogToConsole($"Executing cue: {(action.audio_cue ? "Audio" : "Visual")} at {cueStartTime:HH:mm:ss.fff}");
                    await Countdown(1000, "Waiting", actionIndex + 1, schedule.Count);

                    if (!buttonPressedDuringCue)
                    {
                        LogToConsole("Cue missed! No button was pressed during the cue.");
                    }

                    string cueType = action.audio_cue ? "Audio" : "Visual";
                    double reactionTime = buttonPressedDuringCue ? (DateTime.Now - cueStartTime).TotalMilliseconds : -1;
                    LogCueToCsv(cueType, buttonPressedDuringCue, reactionTime);


                    currentCue = null;
                }
                actionIndex++;
            }
            isRunning = false;
            LogToConsole("Schedule execution finished.");
            CountdownProgressBar.Value = 0; // Ensure progress bar starts at 0

        }

        private void SendCommand(ScheduleAction action)
        {
            try
            {
                string ipAddress = "localhost";
                int port = 50000;
                string jsonString = JsonSerializer.Serialize(action);
                byte[] jsonBytes = Encoding.UTF8.GetBytes(jsonString);
                udpClient.Send(jsonBytes, jsonBytes.Length, ipAddress, port);
                LogToConsole($"Sent command: {jsonString}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error sending command: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                LogToConsole($"Error sending command: {ex.Message}");
            }
        }

        private void PopulateControllerDropdown()
        {
            Dispatcher.Invoke(() =>
            {
                ButtonDropdown.Items.Clear(); // Clear existing entries

                if (joystick != null)
                {
                    var buttonNames = joystick.Capabilities.ButtonCount;

                    for (int i = 0; i < buttonNames; i++)
                    {
                        ButtonDropdown.Items.Add($"Button {i + 1}"); // More user-friendly numbering
                    }

                    LogToConsole($"Controller has {buttonNames} buttons.");
                }
                else
                {
                    LogToConsole("No joystick detected.");
                }

                ButtonDropdown.SelectedIndex = 0;
            });
        }


        private void CheckButtonPress()
        {
            if (joystick == null) return;
            joystick.Poll();
            var state = joystick.GetCurrentState();

            if (state.Buttons[selectedButtonIndex])
            {
                DateTime buttonPressTime = DateTime.Now;
                bool wasCueActive = currentCue != null;
                LogToConsole($"Button {selectedButtonIndex + 1} pressed at {buttonPressTime:HH:mm:ss.fff} - Cue Active: {wasCueActive}");

                if (wasCueActive)
                {
                    buttonPressedDuringCue = true;
                    TimeSpan reactionTime = buttonPressTime - cueStartTime;
                    LogToConsole($"Reaction time: {reactionTime.TotalMilliseconds} ms.");
                }
            }
        }


        private void ButtonPollLoop()
        {
            Task.Run(async () =>
            {
                while (true)
                {
                    CheckButtonPress();
                    await Task.Delay(100);
                }
            });
        }

        private void StopScheduleButton_Click(object sender, RoutedEventArgs e)
        {
            isRunning = false;
            Dispatcher.Invoke(() =>
            {
                CountdownProgressBar.Value = 0;
                CountdownText.Text = "Stopped";
            });
            LogToConsole("Schedule execution stopped.");
        }

        private void PauseScheduleButton_Click(object sender, RoutedEventArgs e)
        {
            isPaused = !isPaused;
            LogToConsole(isPaused ? "Schedule execution paused." : "Schedule execution resumed.");
        }

        private void ButtonDropdown_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            selectedButtonIndex = ButtonDropdown.SelectedIndex; 
            LogToConsole($"Selected button changed to: Button {selectedButtonIndex + 1}");
            SaveSettings();
        }

        private void ListenForButtonPress_Click(object sender, RoutedEventArgs e)
        {
            listeningForInput = true;
            LogToConsole("Listening for button press...");
            DetectButtonPressButton.Content = "Waiting for button press...";

            Task.Run(() => DetectButtonPress());
        }

        private void DetectButtonPress()
        {
            while (listeningForInput)
            {
                joystick.Poll();
                var state = joystick.GetCurrentState();
                for (int i = 0; i < state.Buttons.Length; i++)
                {
                    if (state.Buttons[i])
                    {
                        selectedButtonIndex = i;
                        listeningForInput = false;
                        Dispatcher.Invoke(async () =>
                        {
                            LogToConsole($"Detected button press: Button {i + 1}");
                            DetectButtonPressButton.Content = $"Button {i + 1} detected";
                            ButtonDropdown.SelectedIndex = i;
                            SaveSettings(); // Save button selection after detection
                            await Task.Delay(2000); // Display detected button for 1 second
                            DetectButtonPressButton.Content = "Detect Button Press";

                        });

                        return;
                    }
                }
                Thread.Sleep(100);
            }
        }

        private void CountdownProgressBar_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {

        }
    }

    public class ScheduleData
    {
        public List<ScheduleAction> test_schedule { get; set; }
    }

    public class ScheduleAction
    {
        public WaitAction wait { get; set; }
        public bool audio_cue { get; set; }
        public bool visual_cue { get; set; }
    }

    public class WaitAction
    {
        public List<TimeRange> between { get; set; }
    }

    public class TimeRange
    {
        public int min { get; set; }
        public int max { get; set; }
    }

}