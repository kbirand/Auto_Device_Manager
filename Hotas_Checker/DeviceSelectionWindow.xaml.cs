using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Management;
using System.Windows.Controls;

namespace Hotas_Checker
{
    public partial class DeviceSelectionWindow : Window
    {
        public ObservableCollection<UsbDevice> UsbDevices { get; set; }
        public UsbDevice SelectedDevice { get; private set; }

        public DeviceSelectionWindow()
        {
            InitializeComponent();
            UsbDevices = new ObservableCollection<UsbDevice>();
            UsbDevicesListBox.ItemsSource = UsbDevices;
            LoadUsbDevices();
        }

        private void LoadUsbDevices()
        {
            try
            {
                ManagementObjectSearcher searcher = new ManagementObjectSearcher(@"Select * From Win32_PnPEntity Where DeviceID Like 'USB%'");
                foreach (ManagementObject device in searcher.Get())
                {
                    string deviceId = device["DeviceID"].ToString();
                    string deviceName = device["Name"].ToString();
                    UsbDevices.Add(new UsbDevice { DeviceId = deviceId, Name = deviceName });
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading USB devices: {ex.Message}");
            }
        }

        private void UsbDevicesListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            SelectedDevice = UsbDevicesListBox.SelectedItem as UsbDevice;
            if (SelectedDevice != null)
            {
                DialogResult = true;
                Close();
            }
        }

        private void Border_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                if (WindowState == WindowState.Maximized)
                    WindowState = WindowState.Normal;
                else
                    WindowState = WindowState.Maximized;
            }
            else
            {
                DragMove();
            }
        }


        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

    }
}