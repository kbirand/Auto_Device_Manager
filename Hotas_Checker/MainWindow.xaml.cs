using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Security.Principal;
using System.Management;
using Microsoft.Win32;
using Newtonsoft.Json;
using System.Collections.Generic;

namespace Hotas_Checker
{
    public partial class MainWindow : Window
    {
        public ObservableCollection<UsbDevice> Devices { get; set; }
        private System.Windows.Threading.DispatcherTimer timer;
        private const string DEVICES_FILE = "devices.xml";
        private const string SETTINGS_FILE = "settings.xml";
        private const int DEBOUNCE_DELAY_MS = 5000; // 5 seconds debounce delay
        private const string REGISTRY_KEY = @"SOFTWARE\HotasChecker";
        private const string AUTO_ENABLE_DISABLE_VALUE = "AutoEnableDisable";
        private const string DEVICES_VALUE = "Devices";
        private ManagementEventWatcher watcher;
        private bool _isEnforcingStates = false;

        [StructLayout(LayoutKind.Sequential)]
        public struct SP_DEVINFO_DATA
        {
            public uint cbSize;
            public Guid ClassGuid;
            public uint DevInst;
            public IntPtr Reserved;
        }

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern IntPtr SetupDiGetClassDevs(ref Guid ClassGuid, IntPtr Enumerator, IntPtr hwndParent, uint Flags);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern bool SetupDiEnumDeviceInfo(IntPtr DeviceInfoSet, uint MemberIndex, ref SP_DEVINFO_DATA DeviceInfoData);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern bool SetupDiGetDeviceInstanceId(IntPtr DeviceInfoSet, ref SP_DEVINFO_DATA DeviceInfoData, StringBuilder DeviceInstanceId, uint DeviceInstanceIdSize, out uint RequiredSize);

        [DllImport("setupapi.dll", SetLastError = true)]
        public static extern bool SetupDiDestroyDeviceInfoList(IntPtr DeviceInfoSet);

        [DllImport("cfgmgr32.dll", SetLastError = true)]
        public static extern int CM_Get_DevNode_Status(ref uint status, ref uint problemCode, uint devInst, uint flags);

        [DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern bool SetupDiGetDeviceRegistryProperty(
            IntPtr deviceInfoSet,
            ref SP_DEVINFO_DATA deviceInfoData,
            uint property,
            out uint propertyRegDataType,
            StringBuilder propertyBuffer,
            uint propertyBufferSize,
            out uint requiredSize
        );

        public const uint DIGCF_PRESENT = 0x2;
        public const uint DIGCF_ALLCLASSES = 0x4;
        public const uint DIGCF_DEVICEINTERFACE = 0x10;
        public const uint DN_STARTED = 0x00000008;
        public const uint SPDRP_DEVICEDESC = 0x00000000;
        public const uint SPDRP_FRIENDLYNAME = 0x0000000C;
        private System.Timers.Timer _saveSettingsTimer;


        public MainWindow()
        {
            if (!IsAdministrator())
            {
                MessageBox.Show("This application requires administrative privileges to function properly. Please run as administrator.", "Admin Rights Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                Environment.Exit(1);
            }

            InitializeComponent();
            Debug.WriteLine("MainWindow constructor started");

            // Initialize Devices as an empty collection
            Devices = new ObservableCollection<UsbDevice>();
            DeviceListBox.ItemsSource = Devices;

            SetupWakeEventHandler();

            LoadSettings().ContinueWith(_ =>
            {
                Dispatcher.Invoke(() =>
                {
                    MonitorDevices();
                });
            });

            Debug.WriteLine("MainWindow constructor completed");
        }


        private void SetupWakeEventHandler()
        {
            try
            {
                WqlEventQuery query = new WqlEventQuery("SELECT * FROM Win32_PowerManagementEvent WHERE EventType = 7");
                watcher = new ManagementEventWatcher(query);
                watcher.EventArrived += new EventArrivedEventHandler(HandleWakeEvent);
                watcher.Start();
                Debug.WriteLine("Wake event handler set up successfully");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to set up wake event handler: {ex.Message}");
                MessageBox.Show($"Failed to set up wake event handler: {ex.Message}");
            }
        }


        private void HandleWakeEvent(object sender, EventArrivedEventArgs e)
        {
            Debug.WriteLine("Wake event detected");
            Dispatcher.Invoke(async () =>
            {
                await Task.Delay(1000);
                Debug.WriteLine($"Auto-disable is {(AutoEnableDisableCheckBox.IsChecked == true ? "enabled" : "disabled")}");
                if (AutoEnableDisableCheckBox.IsChecked == true)
                {
                    Debug.WriteLine("Enforcing device states after wake event");
                    await DisableAllDevices();
                }
                else
                {
                    Debug.WriteLine("Auto-disable is not enabled, not enforcing device states");
                }
            });
        }

        private void EnforceDeviceStates()
        {
            if (_isEnforcingStates)
                return;

            _isEnforcingStates = true;
            try
            {
                foreach (var device in Devices)
                {
                    SetDeviceEnabled(device.DeviceId, device.IsActive);
                }
            }
            finally
            {
                _isEnforcingStates = false;
            }
        }


        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        private void Border_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            this.DragMove();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private bool IsAdministrator()
        {
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
            {
                WindowsPrincipal principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
        }

        private async Task LoadSettings()
        {
            Debug.WriteLine("LoadSettings started");
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(REGISTRY_KEY, true))  // Open with write access
                {
                    if (key != null)
                    {
                        // Load Auto-Enable/Disable setting
                        object autoEnableDisableValue = key.GetValue(AUTO_ENABLE_DISABLE_VALUE);
                        Debug.WriteLine($"Loaded AUTO_ENABLE_DISABLE_VALUE: {autoEnableDisableValue}");
                        if (autoEnableDisableValue is int intValue)
                        {
                            AutoEnableDisableCheckBox.IsChecked = intValue != 0;
                            Debug.WriteLine($"Set AutoEnableDisableCheckBox.IsChecked to: {AutoEnableDisableCheckBox.IsChecked}");
                        }
                        else
                        {
                            AutoEnableDisableCheckBox.IsChecked = false;
                            Debug.WriteLine("Set AutoEnableDisableCheckBox.IsChecked to false (default)");
                        }

                        // Load Devices
                        object devicesValue = key.GetValue(DEVICES_VALUE);
                        Debug.WriteLine($"Loaded DEVICES_VALUE: {devicesValue}");
                        if (devicesValue is string devicesJson && !string.IsNullOrEmpty(devicesJson))
                        {
                            try
                            {
                                var loadedDevices = JsonConvert.DeserializeObject<List<UsbDevice>>(devicesJson);
                                Debug.WriteLine($"Deserialized devices count: {loadedDevices?.Count ?? 0}");
                                if (loadedDevices != null && loadedDevices.Any())
                                {
                                    Devices.Clear();
                                    foreach (var device in loadedDevices)
                                    {
                                        Devices.Add(device);
                                        Debug.WriteLine($"Added device: {device.Name}, ID: {device.DeviceId}");
                                    }
                                }
                                else
                                {
                                    Debug.WriteLine("No devices loaded from registry");
                                }
                            }
                            catch (JsonException ex)
                            {
                                Debug.WriteLine($"Error deserializing devices: {ex.Message}");
                            }
                        }
                        else
                        {
                            Debug.WriteLine("No devices value found in registry");
                        }
                    }
                    else
                    {
                        AutoEnableDisableCheckBox.IsChecked = false;
                        Debug.WriteLine("Registry key not found, set AutoEnableDisableCheckBox.IsChecked to false");
                    }
                }

                // Update UI
                DeviceListBox.ItemsSource = null;
                DeviceListBox.ItemsSource = Devices;
                Debug.WriteLine($"Updated DeviceListBox.ItemsSource, Devices count: {Devices.Count}");

                // Apply device states
                if (AutoEnableDisableCheckBox.IsChecked == true)
                {
                    Debug.WriteLine("Calling EnableAllDevices");
                    await EnableAllDevices();
                }
                else
                {
                    Debug.WriteLine("Calling DisableAllDevices");
                    await DisableAllDevices();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to load settings: {ex.Message}");
                MessageBox.Show($"Failed to load settings: {ex.Message}");
                AutoEnableDisableCheckBox.IsChecked = false;
            }

            // Ensure the UI is updated
            await UpdateDeviceStatusAsync();
            Debug.WriteLine("Finished LoadSettings");
        }

        private void SaveSettings()
        {
            Debug.WriteLine("SaveSettings started");
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(REGISTRY_KEY))
                {
                    bool autoEnableDisableValue = AutoEnableDisableCheckBox.IsChecked ?? false;
                    key.SetValue(AUTO_ENABLE_DISABLE_VALUE, autoEnableDisableValue ? 1 : 0, RegistryValueKind.DWord);
                    Debug.WriteLine($"Saved AUTO_ENABLE_DISABLE_VALUE: {autoEnableDisableValue}");

                    if (Devices != null && Devices.Any())
                    {
                        string devicesJson = JsonConvert.SerializeObject(Devices);
                        key.SetValue(DEVICES_VALUE, devicesJson, RegistryValueKind.String);
                        Debug.WriteLine($"Saved DEVICES_VALUE: {devicesJson}");
                        Debug.WriteLine($"Saved Devices count: {Devices.Count}");
                    }
                    else
                    {
                        Debug.WriteLine("No devices to save, keeping existing registry value");
                        if (key.GetValue(DEVICES_VALUE) == null)
                        {
                            key.SetValue(DEVICES_VALUE, "[]", RegistryValueKind.String);
                            Debug.WriteLine("Set empty array for DEVICES_VALUE");
                        }
                    }
                }
                Debug.WriteLine("Settings saved successfully");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to save settings: {ex.Message}");
                MessageBox.Show($"Failed to save settings: {ex.Message}");
            }
        }

        private async Task EnableAllDevices()
        {
            Debug.WriteLine("EnableAllDevices started");
            if (Devices == null)
            {
                Debug.WriteLine("Devices is null, initializing as empty collection");
                Devices = new ObservableCollection<UsbDevice>();
            }

            foreach (var device in Devices)
            {
                Debug.WriteLine($"Enabling device: {device.Name}, ID: {device.DeviceId}");
                await SetDeviceEnabledAsync(device.DeviceId, true);
                device.IsActive = true;
            }
            await UpdateDeviceStatusAsync();
            Debug.WriteLine("EnableAllDevices completed");
        }

        private async Task DisableAllDevices()
        {
            Debug.WriteLine("DisableAllDevices started");
            if (Devices == null)
            {
                Debug.WriteLine("Devices is null, initializing as empty collection");
                Devices = new ObservableCollection<UsbDevice>();
            }

            foreach (var device in Devices)
            {
                Debug.WriteLine($"Disabling device: {device.Name}, ID: {device.DeviceId}");
                await SetDeviceEnabledAsync(device.DeviceId, false);
                device.IsActive = false;
            }
            await UpdateDeviceStatusAsync();
            Debug.WriteLine("DisableAllDevices completed");
        }

        private async void AutoEnableDisableCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            await EnableAllDevices();
            SaveSettings();
        }

        private async void AutoEnableDisableCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            await DisableAllDevices();
            SaveSettings();
        }

        private void ScheduleSettingsSave()
        {
            if (_saveSettingsTimer == null)
            {
                _saveSettingsTimer = new System.Timers.Timer(500);
                _saveSettingsTimer.Elapsed += (s, e) =>
                {
                    _saveSettingsTimer.Stop();
                    Dispatcher.Invoke(SaveSettings);
                };
                _saveSettingsTimer.AutoReset = false;
            }
            _saveSettingsTimer.Stop();
            _saveSettingsTimer.Start();
        }


        private async Task SetDeviceEnabledAsync(string deviceId, bool enable)
        {
            string action = enable ? "Enable" : "Disable";
            Debug.WriteLine($"SetDeviceEnabledAsync started: {action} device {deviceId}");
            string command = $"Get-PnpDevice -InstanceId '{deviceId}' | {action}-PnpDevice -Confirm:$false";

            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("powershell.exe", command)
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using (Process process = new Process { StartInfo = psi })
                {
                    process.Start();
                    string output = await process.StandardOutput.ReadToEndAsync();
                    string error = await process.StandardError.ReadToEndAsync();
                    await Task.Run(() => process.WaitForExit());

                    if (process.ExitCode != 0)
                    {
                        Debug.WriteLine($"Failed to {action.ToLower()} device. Error: {error}");
                        MessageBox.Show($"Failed to {action.ToLower()} device. Error: {error}");
                    }
                    else
                    {
                        Debug.WriteLine($"Device {action.ToLower()}d successfully. Output: {output}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"An error occurred while trying to {action.ToLower()} the device: {ex.Message}");
                MessageBox.Show($"An error occurred while trying to {action.ToLower()} the device: {ex.Message}");
            }
            Debug.WriteLine($"SetDeviceEnabledAsync completed: {action} device {deviceId}");
        }


        protected override async void OnClosing(CancelEventArgs e)
        {
            Debug.WriteLine("OnClosing started");
            if (AutoEnableDisableCheckBox.IsChecked == true)
            {
                Debug.WriteLine("Auto-disable is enabled, disabling all devices");
                e.Cancel = true; // Temporarily cancel the closing event

                foreach (var device in Devices)
                {
                    Debug.WriteLine($"Disabling device: {device.Name}, ID: {device.DeviceId}");
                    await SetDeviceEnabledAsync(device.DeviceId, false);
                    device.IsActive = false;
                }

                await UpdateDeviceStatusAsync();
                Debug.WriteLine("All devices disabled");

                SaveSettings();
                Debug.WriteLine("Settings saved");
            }
            else
            {
                Debug.WriteLine("Auto-disable is not enabled, saving settings");
                SaveSettings();
            }

            watcher?.Stop();
            Debug.WriteLine("Wake event watcher stopped");

            if (e.Cancel)
            {
                Debug.WriteLine("Closing was temporarily cancelled, now shutting down the application");
                Application.Current.Shutdown(); // Force the application to close after disabling devices
            }
            else
            {
                base.OnClosing(e);
            }
            Debug.WriteLine("OnClosing completed");
        }




        private void AddNewDevice_Click(object sender, RoutedEventArgs e)
        {
            var deviceSelectionWindow = new DeviceSelectionWindow();
            if (deviceSelectionWindow.ShowDialog() == true)
            {
                var selectedDevice = deviceSelectionWindow.SelectedDevice;
                if (selectedDevice != null && !Devices.Any(d => d.DeviceId == selectedDevice.DeviceId))
                {
                    Devices.Add(selectedDevice);
                    Debug.WriteLine($"Added new device: {selectedDevice.Name}, ID: {selectedDevice.DeviceId}");
                    SaveSettings();
                    Debug.WriteLine("SaveSettings called after adding new device");
                }
            }
        }

        private void RemoveDevice_Click(object sender, RoutedEventArgs e)
        {
            var button = (Button)sender;
            var device = (UsbDevice)button.DataContext;

            if (MessageBox.Show($"Are you sure you want to remove {device.Name}?", "Confirm Removal", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            {
                Devices.Remove(device);
                SaveSettings();
            }
        }

        private void MonitorDevices()
        {
            var timer = new System.Windows.Threading.DispatcherTimer();
            timer.Tick += Timer_Tick;
            timer.Interval = TimeSpan.FromSeconds(1);
            timer.Start();
        }

        private async void Timer_Tick(object sender, EventArgs e)
        {
            await UpdateDeviceStatusAsync();
        }

        private async Task UpdateDeviceStatusAsync()
        {
            foreach (UsbDevice device in Devices)
            {
                bool isConnected = await IsDeviceConnectedAsync(device.DeviceId);
                if (isConnected != device.IsConnected)
                {
                    device.IsConnected = isConnected;
                    if (isConnected)
                    {
                        bool isEnabled = await IsDeviceEnabledAsync(device.DeviceId);
                        device.IsActive = isEnabled;
                    }
                    else
                    {
                        device.IsActive = false;
                    }
                }
            }
        }

        private Task<bool> IsDeviceConnectedAsync(string targetDeviceId)
        {
            return Task.Run(() =>
            {
                Guid empty = Guid.Empty;
                IntPtr deviceInfoSet = SetupDiGetClassDevs(ref empty, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_ALLCLASSES);

                if (deviceInfoSet.ToInt64() == -1)
                {
                    return false;
                }

                try
                {
                    SP_DEVINFO_DATA deviceInfoData = new SP_DEVINFO_DATA();
                    deviceInfoData.cbSize = (uint)Marshal.SizeOf(deviceInfoData);

                    for (uint i = 0; SetupDiEnumDeviceInfo(deviceInfoSet, i, ref deviceInfoData); i++)
                    {
                        StringBuilder deviceInstanceId = new StringBuilder(256);
                        uint requiredSize = 0;
                        if (SetupDiGetDeviceInstanceId(deviceInfoSet, ref deviceInfoData, deviceInstanceId, 256, out requiredSize))
                        {
                            if (deviceInstanceId.ToString().Contains(targetDeviceId))
                            {
                                return true;
                            }
                        }
                    }
                }
                finally
                {
                    if (deviceInfoSet != IntPtr.Zero)
                    {
                        SetupDiDestroyDeviceInfoList(deviceInfoSet);
                    }
                }

                return false;
            });
        }

        private Task<bool> IsDeviceEnabledAsync(string deviceId)
        {
            return Task.Run(() =>
            {
                Guid empty = Guid.Empty;
                IntPtr deviceInfoSet = SetupDiGetClassDevs(ref empty, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_ALLCLASSES);

                try
                {
                    SP_DEVINFO_DATA deviceInfoData = new SP_DEVINFO_DATA();
                    deviceInfoData.cbSize = (uint)Marshal.SizeOf(deviceInfoData);

                    for (uint i = 0; SetupDiEnumDeviceInfo(deviceInfoSet, i, ref deviceInfoData); i++)
                    {
                        StringBuilder deviceInstanceId = new StringBuilder(256);
                        uint requiredSize = 0;
                        if (SetupDiGetDeviceInstanceId(deviceInfoSet, ref deviceInfoData, deviceInstanceId, 256, out requiredSize))
                        {
                            if (deviceInstanceId.ToString().Contains(deviceId))
                            {
                                uint status = 0, problemCode = 0;
                                if (CM_Get_DevNode_Status(ref status, ref problemCode, deviceInfoData.DevInst, 0) == 0)
                                {
                                    return (status & DN_STARTED) != 0;
                                }
                                break;
                            }
                        }
                    }
                }
                finally
                {
                    if (deviceInfoSet != IntPtr.Zero)
                    {
                        SetupDiDestroyDeviceInfoList(deviceInfoSet);
                    }
                }

                return false;
            });
        }

        private void CheckBox_Checked(object sender, RoutedEventArgs e)
        {
            var checkBox = (CheckBox)sender;
            UsbDevice device = checkBox.DataContext as UsbDevice;
            if (device != null)
            {
                SetDeviceEnabled(device.DeviceId, true);
            }
        }

        private void CheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            var checkBox = (CheckBox)sender;
            UsbDevice device = checkBox.DataContext as UsbDevice;
            if (device != null)
            {
                SetDeviceEnabled(device.DeviceId, false);
            }
        }

        private void SetDeviceEnabled(string deviceId, bool enable)
        {
            string action = enable ? "Enable" : "Disable";
            string command = $"Get-PnpDevice -InstanceId '{deviceId}' | {action}-PnpDevice -Confirm:$false";

            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("powershell.exe", command)
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using (Process process = Process.Start(psi))
                {
                    string output = process.StandardOutput.ReadToEnd();
                    string error = process.StandardError.ReadToEnd();
                    process.WaitForExit();

                    if (process.ExitCode != 0)
                    {
                        MessageBox.Show($"Failed to {action.ToLower()} device. Error: {error}");
                    }
                    else
                    {
                        Debug.WriteLine($"Device {action.ToLower()}d successfully. Output: {output}");
                        // Force an immediate update of the device status
                        Task.Run(async () => await UpdateDeviceStatusAsync());
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An error occurred while trying to {action.ToLower()} the device: {ex.Message}");
            }
        }

        public string GetDeviceName(string deviceId)
        {
            string deviceName = "Unknown Device";
            Guid empty = Guid.Empty;
            IntPtr deviceInfoSet = SetupDiGetClassDevs(ref empty, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_ALLCLASSES);

            try
            {
                SP_DEVINFO_DATA deviceInfoData = new SP_DEVINFO_DATA();
                deviceInfoData.cbSize = (uint)Marshal.SizeOf(deviceInfoData);

                for (uint i = 0; SetupDiEnumDeviceInfo(deviceInfoSet, i, ref deviceInfoData); i++)
                {
                    StringBuilder deviceInstanceId = new StringBuilder(256);
                    uint requiredSize = 0;
                    if (SetupDiGetDeviceInstanceId(deviceInfoSet, ref deviceInfoData, deviceInstanceId, 256, out requiredSize))
                    {
                        if (deviceInstanceId.ToString().Contains(deviceId))
                        {
                            deviceName = GetDevicePropertyString(deviceInfoSet, deviceInfoData, SPDRP_DEVICEDESC);
                            if (string.IsNullOrEmpty(deviceName))
                            {
                                deviceName = GetDevicePropertyString(deviceInfoSet, deviceInfoData, SPDRP_FRIENDLYNAME);
                            }
                            break;
                        }
                    }
                }
            }
            finally
            {
                if (deviceInfoSet != IntPtr.Zero)
                {
                    SetupDiDestroyDeviceInfoList(deviceInfoSet);
                }
            }

            return deviceName;
        }

        private string GetDevicePropertyString(IntPtr deviceInfoSet, SP_DEVINFO_DATA deviceInfoData, uint property)
        {
            StringBuilder propertyBuffer = new StringBuilder(1024);
            uint propertyType = 0;
            uint requiredSize = 0;

            if (SetupDiGetDeviceRegistryProperty(deviceInfoSet, ref deviceInfoData, property, out propertyType, propertyBuffer, (uint)propertyBuffer.Capacity, out requiredSize))
            {
                return propertyBuffer.ToString();
            }

            return string.Empty;
        }


    }

    public class UsbDevice : INotifyPropertyChanged
    {
        public string DeviceId { get; set; }
        public string Name { get; set; }
        private bool _isActive;
        public bool IsActive
        {
            get { return _isActive; }
            set
            {
                if (_isActive != value)
                {
                    _isActive = value;
                    OnPropertyChanged(nameof(IsActive));
                }
            }
        }

        private bool _isConnected;
        public bool IsConnected
        {
            get { return _isConnected; }
            set
            {
                if (_isConnected != value)
                {
                    _isConnected = value;
                    OnPropertyChanged(nameof(IsConnected));
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}