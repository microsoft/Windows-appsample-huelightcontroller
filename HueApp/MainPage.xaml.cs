//  ---------------------------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
// 
//  The MIT License (MIT)
// 
//  Permission is hereby granted, free of charge, to any person obtaining a copy
//  of this software and associated documentation files (the "Software"), to deal
//  in the Software without restriction, including without limitation the rights
//  to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
//  copies of the Software, and to permit persons to whom the Software is
//  furnished to do so, subject to the following conditions:
// 
//  The above copyright notice and this permission notice shall be included in
//  all copies or substantial portions of the Software.
// 
//  THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
//  IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
//  FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
//  AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
//  LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
//  OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
//  THE SOFTWARE.
//  ---------------------------------------------------------------------------------

using HueLibrary;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel.Background;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Radios;
using Windows.Storage.Streams;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace HueApp
{
    /// <summary>
    /// The main page for the Hue app controls.
    /// </summary>
    internal sealed partial class MainPage : Page, INotifyPropertyChanged
    {
        private Bridge _bridge;
        private ObservableCollection<Light> _lights;
        private ObservableCollection<Light> Lights
        {
            get { return _lights; }
            set
            {
                if (_lights != value)
                {
                    _lights = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Lights)));
                }
            }
        }
        private IBackgroundTaskRegistration _taskRegistration;
        private BluetoothLEAdvertisementWatcherTrigger _trigger;
        private const string _taskName = "HueBackgroundTask";

        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Constructor for MainPage.
        /// </summary>
        public MainPage()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Fires when the page is navigated to, which occurs after the Initalizer extended
        /// splash screen has finished loading all Hue resources. 
        /// </summary>
        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            HuePayload args = e.Parameter as HuePayload; 
            if (null != args)
            {
                _bridge = args.Bridge;
                Lights = new ObservableCollection<Light>(args.Lights); 
            }
            _taskRegistration = BackgroundTaskRegistration.AllTasks.Values
                .FirstOrDefault(x => x.Name == _taskName);
            BluetoothWatcherToggle.IsChecked = null != _taskRegistration;
            
            var radios = await Radio.GetRadiosAsync();
            BluetoothWatcherToggle.IsEnabled = radios.Any(radio => 
                radio.Kind == RadioKind.Bluetooth && radio.State == RadioState.On);
            base.OnNavigatedTo(e);
        }

        /// <summary>
        /// Fires when the Bluetooth watcher app bar toggle button is clicked. 
        /// </summary>
        private async void BluetoothWatcher_Click(object sender, RoutedEventArgs e)
        { 
            if ((bool)BluetoothWatcherToggle.IsChecked)
            {
                await EnableWatcherAsync(); 
            }
            else
            {
                DisableWatcher(); 
            }
        }

        /// <summary>
        /// Registers the Bluetooth LE watcher background task, assuming Bluetooth is available.
        /// </summary>
        private async Task EnableWatcherAsync()
        {
            if (_taskRegistration != null)
            {
                return;
            }
            _trigger = new BluetoothLEAdvertisementWatcherTrigger();

            // Add manufacturer data.
            var manufacturerData = new BluetoothLEManufacturerData();
            manufacturerData.CompanyId = 0xFFFE;
            DataWriter writer = new DataWriter();
            writer.WriteUInt16(0x1234);
            manufacturerData.Data = writer.DetachBuffer();
            _trigger.AdvertisementFilter.Advertisement.ManufacturerData.Add(manufacturerData);

            // Add signal strength filters and sampling interval.
            _trigger.SignalStrengthFilter.InRangeThresholdInDBm = -65;
            _trigger.SignalStrengthFilter.OutOfRangeThresholdInDBm = -70;
            _trigger.SignalStrengthFilter.OutOfRangeTimeout = TimeSpan.FromSeconds(2);
            _trigger.SignalStrengthFilter.SamplingInterval = TimeSpan.FromSeconds(1);

            // Create the task.
            BackgroundAccessStatus backgroundAccessStatus = 
                await BackgroundExecutionManager.RequestAccessAsync();
            var builder = new BackgroundTaskBuilder()
            {
                Name = _taskName,
                TaskEntryPoint = "BackgroundTasks.AdvertisementWatcherTask"
            };
            builder.SetTrigger(_trigger);
            builder.AddCondition(new SystemCondition(SystemConditionType.InternetAvailable));
            _taskRegistration = builder.Register();
        }

        /// <summary>
        /// Disables the background watcher.
        /// </summary>
        private void DisableWatcher()
        {
            foreach (var task in BackgroundTaskRegistration.AllTasks.Values.Where(x => x.Name == _taskName))
            {
                task.Unregister(true);
                _taskRegistration = null;
            }
        }

        /// <summary>
        /// Refreshes the UI to match the actual state of the lights.
        /// </summary>
        private async void LightRefresh_Click(object sender, RoutedEventArgs e)
        {
            Lights = new ObservableCollection<Light>(await _bridge.GetLightsAsync());
        }
    }
}