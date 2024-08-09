using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Diagnostics;
using System.IO;
using System.Xml.Serialization;
using System.Linq;
using System.Threading.Tasks;
using System.Security.Principal;
using System.Timers;

namespace Hotas_Checker
{
    public partial class MainWindow : Window
    {
        public ObservableCollection<UsbDevice> Devices { get; set; }
        private System.Windows.Threading.DispatcherTimer timer;
        private const string DEVICES_FILE = "devices.xml";
        private const string SETTINGS_FILE = "settings.xml";
        private const int DEBOUNCE_DELAY_MS = 5000; // 5 seconds debounce delay

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
            Devices = new ObservableCollection<UsbDevice>();
            DeviceListBox.ItemsSource = Devices;

            LoadDevices();
            LoadSettings();
            MonitorDevices();

            if (AutoEnableDisableCheckBox.IsChecked == true)
            {
                EnableAllDevices();
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

        private void LoadSettings()
        {
            if (File.Exists(SETTINGS_FILE))
            {
                try
                {
                    string content = File.ReadAllText(SETTINGS_FILE);
                    AutoEnableDisableCheckBox.IsChecked = bool.Parse(content);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error loading settings: {ex.Message}");
                    // Silently fail and use default value
                    AutoEnableDisableCheckBox.IsChecked = false;
                }
            }
        }

        private void SaveSettings()
        {
            try
            {
                File.WriteAllText(SETTINGS_FILE, AutoEnableDisableCheckBox.IsChecked.ToString());
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error saving settings: {ex.Message}");
                // Silently fail as the functionality still works
            }
        }

        private async void EnableAllDevices()
        {
            foreach (var device in Devices)
            {
                await SetDeviceEnabledAsync(device.DeviceId, true);
            }
            await UpdateDeviceStatusAsync();
        }

        private async Task DisableAllDevices()
        {
            foreach (var device in Devices)
            {
                await SetDeviceEnabledAsync(device.DeviceId, false);
            }
            await UpdateDeviceStatusAsync();
        }

        private void AutoEnableDisableCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            ScheduleSettingsSave();
        }

        private void AutoEnableDisableCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            ScheduleSettingsSave();
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
                MessageBox.Show($"An error occurred while trying to {action.ToLower()} the device: {ex.Message}");
            }
        }


        protected override async void OnClosing(CancelEventArgs e)
        {
            _saveSettingsTimer?.Dispose();

            if (AutoEnableDisableCheckBox.IsChecked == true)
            {
                e.Cancel = true; // Temporarily cancel the closing event
                await DisableAllDevices();
            }

            SaveDevices();
            SaveSettings();
            base.OnClosing(e);

            if (e.Cancel)
            {
                Application.Current.Shutdown(); // Force the application to close after disabling devices
            }
        }


        private void LoadDevices()
        {
            if (File.Exists(DEVICES_FILE))
            {
                XmlSerializer serializer = new XmlSerializer(typeof(ObservableCollection<UsbDevice>));
                using (FileStream fs = new FileStream(DEVICES_FILE, FileMode.Open))
                {
                    Devices = (ObservableCollection<UsbDevice>)serializer.Deserialize(fs);
                }
                DeviceListBox.ItemsSource = Devices;
            }
        }

        private void SaveDevices()
        {
            XmlSerializer serializer = new XmlSerializer(typeof(ObservableCollection<UsbDevice>));
            using (FileStream fs = new FileStream(DEVICES_FILE, FileMode.Create))
            {
                serializer.Serialize(fs, Devices);
            }
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
                    SaveDevices();
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
                SaveDevices();
            }
        }

        private void MonitorDevices()
        {
            timer = new System.Windows.Threading.DispatcherTimer();
            timer.Tick += Timer_Tick;
            timer.Interval = TimeSpan.FromSeconds(1); // Increased interval to reduce unnecessary checks
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
                    // Wait for the debounce period before updating the status
                    await Task.Delay(DEBOUNCE_DELAY_MS);
                    isConnected = await IsDeviceConnectedAsync(device.DeviceId);

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
                // The actual state will be updated in UpdateDeviceStatus
            }
        }

        private void CheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            var checkBox = (CheckBox)sender;
            UsbDevice device = checkBox.DataContext as UsbDevice;
            if (device != null)
            {
                SetDeviceEnabled(device.DeviceId, false);
                // The actual state will be updated in UpdateDeviceStatus
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