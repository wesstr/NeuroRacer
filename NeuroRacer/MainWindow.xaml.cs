
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
    public partial class MainWindow : Window
    {
        // Networking and polling constants
        private const string IP_ADDRESS = "localhost";
        private const int PORT = 50000;
        private const int POLL_INTERVAL_MS = 100;
        private const int DEVICE_MONITOR_INTERVAL_MS = 2000;

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
        private int selectedButtonIndex = 0; // Zero-indexed
        private bool listeningForInput = false;
        private List<DeviceInstance> availableDevices;
        private DateTime cueStartTime;
        private bool buttonPressedDuringCue;
        private bool isLoadingSettings = true;
        private string csvLogFile;
        private string scheduleFilePath;
        private string outputDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        private ScheduleAction currentAction = null;
        private bool loggedReactionTimeToCsv = false;

        // Field for debouncing button presses
        private bool buttonPreviouslyPressed = false;

        // Cancellation tokens for background tasks
        private CancellationTokenSource buttonPollTokenSource;
        private CancellationTokenSource deviceMonitorTokenSource;

        private class AppSettings
        {
            public string SelectedDevice { get; set; }
            public int SelectedButtonIndex { get; set; }
        }

        // Unified JSON schedule wrapper
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
            PopulateDeviceDropdown();
            PopulateControllerDropdown();
            LoadSettings(); // Load saved settings on startup
            CountdownProgressBar.Value = 0; // Ensure progress bar starts at 0

            // Initialize cancellation tokens BEFORE starting device monitoring.
            buttonPollTokenSource = new CancellationTokenSource();
            deviceMonitorTokenSource = new CancellationTokenSource();

            StartDeviceMonitoring();
            LogToConsole($"Default output directory: {outputDirectory}");

            // Start polling for button press with cancellation token
            ButtonPollLoop(buttonPollTokenSource.Token);
        }

        protected override void OnClosed(EventArgs e)
        {
            // Cancel background tasks
            buttonPollTokenSource?.Cancel();
            deviceMonitorTokenSource?.Cancel();

            // Dispose resources
            udpClient?.Dispose();
            joystick?.Unacquire();
            joystick?.Dispose();
            directInput?.Dispose();
            base.OnClosed(e);
        }

        /// <summary>
        /// Uses the built-in WPF OpenFileDialog (with a hack) to let the user select a folder.
        /// </summary>
        private void SelectOutputDirectory_Click(object sender, RoutedEventArgs e)
        {
            // Using Microsoft.Win32.OpenFileDialog to allow folder selection.
            var dialog = new OpenFileDialog
            {
                CheckFileExists = false, // Allows selection of non-existent file
                ValidateNames = false,   // Allows selection of folders
                FileName = "Select Folder" // This text is ignored but required to enable folder selection mode
            };

            if (dialog.ShowDialog() == true)
            {
                // The hack: the FileName property returns a path with a fake file name.
                // Use GetDirectoryName to extract the folder path.
                outputDirectory = Path.GetDirectoryName(dialog.FileName);
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
                while (isPaused) await Task.Delay(100); // Wait while paused

                if (!isRunning) break; // Stop progress if stopped

                Dispatcher.Invoke(() =>
                {
                    CountdownProgressBar.Value = i;
                    CountdownText.Text = $"{(i * interval) / 1000.0:F1}s (Step {currentStep} of {totalSteps})";
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
            if (isLoadingSettings) return; // Prevent saving while loading settings

            Dispatcher.Invoke(() =>
            {
                var settings = new AppSettings
                {
                    SelectedDevice = DeviceDropdown.SelectedItem?.ToString(),
                    SelectedButtonIndex = selectedButtonIndex // Save as zero-indexed
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
            isLoadingSettings = false;
        }

        private void LogToConsole(string message)
        {
            Console.WriteLine(message);
            Console.Out.Flush();
            Dispatcher.Invoke(() =>
            {
                LogTextBox.AppendText(message + Environment.NewLine);
                LogTextBox.ScrollToEnd();
            });
        }

        private void LoadSchedule()
        {
            try
            {
                string json = File.ReadAllText("schedule.json");
                var scheduleWrapper = JsonSerializer.Deserialize<ScheduleWrapper>(json);
                schedule = scheduleWrapper?.test_schedule ?? new List<ScheduleAction>();
                LogToConsole("Schedule loaded successfully.");
            }
            catch (Exception ex)
            {
                //MessageBox.Show($"Error loading schedule: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                LogToConsole($"Error loading schedule: {ex.Message}");
            }
        }

        private void InitializeController()
        {
            // Single instantiation of DirectInput.
            directInput = new DirectInput();
            availableDevices = new List<DeviceInstance>();
            availableDevices.AddRange(directInput.GetDevices(DeviceType.Gamepad, DeviceEnumerationFlags.AttachedOnly));
            availableDevices.AddRange(directInput.GetDevices(DeviceType.Joystick, DeviceEnumerationFlags.AttachedOnly));
            availableDevices.AddRange(directInput.GetDevices(DeviceType.Flight, DeviceEnumerationFlags.AttachedOnly));
            availableDevices.AddRange(directInput.GetDevices(DeviceType.Driving, DeviceEnumerationFlags.AttachedOnly));

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
                previousDevices.AddRange(directInput.GetDevices(DeviceType.Driving, DeviceEnumerationFlags.AttachedOnly));

                while (!deviceMonitorTokenSource.Token.IsCancellationRequested)
                {
                    await Task.Delay(DEVICE_MONITOR_INTERVAL_MS);
                    var currentDevices = new List<DeviceInstance>(directInput.GetDevices(DeviceType.Joystick, DeviceEnumerationFlags.AttachedOnly));
                    currentDevices.AddRange(directInput.GetDevices(DeviceType.Gamepad, DeviceEnumerationFlags.AttachedOnly));
                    currentDevices.AddRange(directInput.GetDevices(DeviceType.Flight, DeviceEnumerationFlags.AttachedOnly));
                    currentDevices.AddRange(directInput.GetDevices(DeviceType.Driving, DeviceEnumerationFlags.AttachedOnly));

                    if (currentDevices.Count != previousDevices.Count)
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
            }, deviceMonitorTokenSource.Token);
        }

        private void PopulateDeviceDropdown()
        {
            Dispatcher.Invoke(() =>
            {
                // Save the current device selection if one exists.
                string currentSelection = DeviceDropdown.SelectedItem?.ToString();
                DeviceDropdown.Items.Clear();
                foreach (var device in availableDevices)
                {
                    DeviceDropdown.Items.Add(device.InstanceName);
                }

                if (DeviceDropdown.Items.Count > 0)
                {
                    // Try to find the previously selected device in the updated list.
                    int index = !string.IsNullOrEmpty(currentSelection) ?
                                DeviceDropdown.Items.IndexOf(currentSelection) : -1;
                    DeviceDropdown.SelectedIndex = (index >= 0) ? index : 0;
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
            ButtonDropdown.Items.Clear();
            try
            {
                joystick.Poll();
                var state = joystick.GetCurrentState();
                int buttonCount = joystick.Capabilities.ButtonCount;
                for (int i = 0; i < buttonCount; i++)
                {
                    ButtonDropdown.Items.Add($"Button {i + 1}");
                }
                if (ButtonDropdown.Items.Count > 0)
                {
                    ButtonDropdown.SelectedIndex = selectedButtonIndex;
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

                currentAction = action;

                if (action.wait != null)
                {
                    if (action.wait.between == null || action.wait.between.Count < 2)
                    {
                        LogToConsole("Invalid wait time range in schedule action. Skipping...");
                        continue;
                    }
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
                    await Countdown(1000, "Cue Duration", actionIndex + 1, schedule.Count);

                    if (!buttonPressedDuringCue)
                    {
                        LogToConsole("Cue missed! No button was pressed during the cue.");
                    }

                    currentCue = null;
                }
                loggedReactionTimeToCsv = false;
                actionIndex++;
            }
            isRunning = false;
            LogToConsole("Schedule execution finished.");
            Dispatcher.Invoke(() =>
            {
                CountdownProgressBar.Value = 0;
                CountdownText.Text = "Finished";
            });
        }

        private void SendCommand(ScheduleAction action)
        {
            try
            {
                string jsonString = JsonSerializer.Serialize(action);
                byte[] jsonBytes = Encoding.UTF8.GetBytes(jsonString);
                udpClient.Send(jsonBytes, jsonBytes.Length, IP_ADDRESS, PORT);
                LogToConsole($"Sent command: {jsonString}");
            }
            catch (Exception ex)
            {
                //MessageBox.Show($"Error sending command: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                LogToConsole($"Error sending command: {ex.Message}");
            }
        }

        private void PopulateControllerDropdown()
        {
            Dispatcher.Invoke(() =>
            {
                ButtonDropdown.Items.Clear();
                if (joystick != null)
                {
                    int buttonCount = joystick.Capabilities.ButtonCount;
                    for (int i = 0; i < buttonCount; i++)
                    {
                        ButtonDropdown.Items.Add($"Button {i + 1}");
                    }
                    LogToConsole($"Controller has {buttonCount} buttons.");
                }
                else
                {
                    LogToConsole("No joystick detected.");
                }
                if (ButtonDropdown.Items.Count > 0)
                {
                    ButtonDropdown.SelectedIndex = selectedButtonIndex;
                }
            });
        }

        private void CheckButtonPress()
        {
            if (joystick == null) return;
            joystick.Poll();
            var state = joystick.GetCurrentState();
            if (state == null) return;
            if (selectedButtonIndex < 0) return;
            bool currentButtonState = state.Buttons[selectedButtonIndex];

            // Debounce: register press only when state changes from unpressed to pressed.
            if (currentButtonState && !buttonPreviouslyPressed)
            {
                buttonPreviouslyPressed = true;
                DateTime buttonPressTime = DateTime.Now;
                bool wasCueActive = currentCue != null;
                LogToConsole($"Button {selectedButtonIndex + 1} pressed at {buttonPressTime:HH:mm:ss.fff} - Cue Active: {wasCueActive}");
                if (wasCueActive && !loggedReactionTimeToCsv)
                {
                    buttonPressedDuringCue = true;
                    loggedReactionTimeToCsv = true;
                    TimeSpan reactionTime = buttonPressTime - cueStartTime;
                    LogToConsole($"Reaction time: {reactionTime.TotalMilliseconds} ms.");
                    string cueType = currentAction.audio_cue ? "Audio" : "Visual";
                    LogCueToCsv(cueType, buttonPressedDuringCue, reactionTime.TotalMilliseconds);
                }
            }
            else if (!currentButtonState)
            {
                buttonPreviouslyPressed = false;
            }
        }

        private async void ButtonPollLoop(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    CheckButtonPress();
                    await Task.Delay(POLL_INTERVAL_MS, token);
                }
            }
            catch (TaskCanceledException) { }
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

        private async void ListenForButtonPress_Click(object sender, RoutedEventArgs e)
        {
            listeningForInput = true;
            LogToConsole("Listening for button press...");
            DetectButtonPressButton.Content = "Waiting for button press...";
            await DetectButtonPress();
        }

        private async Task DetectButtonPress()
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
                        await Dispatcher.InvokeAsync(async () =>
                        {
                            LogToConsole($"Detected button press: Button {i + 1}");
                            DetectButtonPressButton.Content = $"Button {i + 1} detected";
                            ButtonDropdown.SelectedIndex = i;
                            SaveSettings();
                            await Task.Delay(2000);
                            DetectButtonPressButton.Content = "Detect Button Press";
                        });
                        return;
                    }
                }
                await Task.Delay(100);
            }
        }

        private void CountdownProgressBar_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            // Optionally handle progress bar changes
        }
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