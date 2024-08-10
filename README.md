# AUTO Device Manager
<img height="300" alt="AUTO_Device_Manager_Tgtu5XufP6" src="https://github.com/user-attachments/assets/b5c6d438-821d-42d8-a039-09372d32d860">
<img height="300" alt="AUTO_Device_Manager_0kAijqcJDM" src="https://github.com/user-attachments/assets/c7baf388-8ef0-4d55-84b7-17609b572a67">

AUTO Device Manager is a Windows WPF application designed to manage USB devices, specifically those related to HOTAS (Hands On Throttle And Stick) setups. Since most HOTAS controllers prevent display/computer to sleep, it provides functionalities to enable, disable, auto enable.disable with launc or close and monitor the status of connected devices, ensuring that all devices are properly configured and operational.

## Features

- **Device Monitoring**: Automatically detects and monitors connected USB devices.
- **Enable/Disable Devices**: Easily enable or disable USB devices from within the application.
- **Auto-Enable on Startup**: Option to automatically enable all configured devices when the application starts.
- **Persistent Device List**: Save and load the list of managed devices across sessions.
- **Admin Privileges Required**: The application checks for administrative privileges to ensure it can perform necessary operations.
- **Flight Simulator**: Users can auto launch this app to enable HOTAS and disable them by closing the sim.

## Prerequisites

- **Windows OS**: The application is designed to run on Windows with .NET Framework.
- **Administrator Rights**: The application requires administrative privileges to manage USB devices.

## Installation

1. Clone the repository:
   ```bash
   git clone https://github.com/yourusername/hotas-device-manager.git
   ```
2. Open the project in Visual Studio.
3. Build and run the application.

## Usage

1. Launch the application as an administrator.
2. Add devices you want to manage using the "Add New Device" button.
3. Use the "Auto enable on start / disable on exit" checkbox to automatically manage devices on startup and shutdown.
4. Monitor the status of each device in the list and manually enable or disable them as needed.

## Important Notes

- The application relies on PowerShell commands to enable and disable devices.
- Device status updates may be delayed due to debounce logic to prevent false positives.
- The application stores device and settings information in registry (\HKEY_CURRENT_USER\Software\HotasChecker).

## Contributing

Contributions are welcome! Please feel free to submit a pull request or open an issue if you encounter any bugs or have suggestions for new features.

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.
