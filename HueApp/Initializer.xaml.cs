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
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using Windows.ApplicationModel.Activation;
using Windows.ApplicationModel.VoiceCommands;
using Windows.Foundation;
using Windows.Foundation.Metadata;
using Windows.Graphics.Display;
using Windows.Storage;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace HueApp
{
    /// <summary>
    /// Handles inital loading of Hue resources (including the bridge and lights), and displaying
    /// an extended splash screen to the user while the app prepares.
    /// </summary>
    internal partial class Initializer 
    {
        protected Rect rect;
        protected SplashScreen splash;
        protected Frame rootFrame = new Frame();

        private bool _isPhone = ApiInformation.IsTypePresent("Windows.Phone.UI.Input.HardwareButtons");
        private double _scaleFactor = (double)DisplayInformation.GetForCurrentView().ResolutionScale / 100;
        private Bridge _bridge;
        private IEnumerable<Light> _lights;

        /// <summary>
        /// Constructor for the initializer. This displays an extended splash screen and progress
        /// ring while the app loads the bridge, lights, and Cortana. 
        /// </summary>
        public Initializer(SplashScreen splashscreen)
        {
            InitializeComponent();
            Window.Current.Content = rootFrame;
            Window.Current.SizeChanged += Current_SizeChanged;
            splash = splashscreen;
            rect = splash.ImageLocation;
            SetImage();
            splash.Dismissed += Initialize;
        }

        /// <summary>
        /// Prepares the app for use by finding the bridge and lights, and initalizing Cortana. 
        /// </summary>
        private async void Initialize(SplashScreen sender, object args)
        {
            await FindBridgeAsync();
            await FindLightsAsync();
            await InitializeCortanaAsync();
            SaveBridgeToCache();
            await rootFrame.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                rootFrame.Navigate(typeof(MainPage), 
                    new HuePayload { Bridge = _bridge, Lights = _lights });
                Window.Current.Content = rootFrame; 
            });
        }

        /// <summary>
        /// Tries to find the bridge using multiple methods. Note: this is not an exhaustive list of discovery
        /// options. For complete guidance, see http://www.developers.meethue.com/documentation/hue-bridge-discovery
        /// </summary>
        private async Task FindBridgeAsync()
        {
            try
            {
                // First attempt: local storage cache.
                var localStorage = ApplicationData.Current.LocalSettings.Values;
                if (localStorage.ContainsKey("bridgeIp") && localStorage.ContainsKey("userId"))
                {
                    _bridge = new Bridge(
                        localStorage["bridgeIp"].ToString(), 
                        localStorage["userId"].ToString());
                    if (await PrepareBridgeAsync())
                    {
                        return;
                    }
                }

                // Second attempt: Hue N-UPnP service.
                _bridge = await Bridge.FindAsync();
                if (await PrepareBridgeAsync())
                {
                    return;
                }

                // Third attempt: Re-try Hue N-UPnP service.
                await DispatchAwaitableUITask(async () =>
                    await RetryBridgeSearchPopup.ShowAsync());
                _bridge = await Bridge.FindAsync();
                if (await PrepareBridgeAsync())
                {
                    return;
                }

                // Final attempt: manual entry.
                await DispatchAwaitableUITask(async () =>
                {
                    await BridgeEntryPopup.ShowAsync();
                    _bridge = new Bridge(BridgEntryIp.Text);
                });
                if (await PrepareBridgeAsync())
                {
                    return;
                }
            }
            catch (Exception e)
            {
                await ReportErrorAsync(
                    "We encountered an unexpected problem trying to find your bridge: " + e);
            }
            await ReportErrorAsync("We couldn't find your bridge. Make sure it's powered on, " +
                "has 3 blue lights illuminated, on the same network as this device, " +
                "and that you're connected to the Internet.");
        }

        /// <summary>
        /// Checks whether the bridge is reachable and the app is authorized to send commands. If the bridge
        /// is reachable but the app isn't authorized, it prompts the user to register it. 
        /// </summary>
        private async Task<bool> PrepareBridgeAsync(int attempts = 0)
        {
            if (null == _bridge || attempts > 2)
            {
                return false;
            }
            switch (await _bridge.PingAsync())
            {
                case BridgeConnectionStatus.Success:
                    return true;
                case BridgeConnectionStatus.Fail:
                    return false;
                case BridgeConnectionStatus.Unauthorized:
                    await DispatchAwaitableUITask(async () => await PressButtonPopup.ShowAsync());
                    await _bridge.RegisterAsync();
                    return await PrepareBridgeAsync(++attempts);
                default:
                    throw new InvalidOperationException();
            }
        }

        /// <summary>
        /// Tries to find lights on the network.
        /// </summary>
        private async Task FindLightsAsync()
        {
            try
            {
                _lights = new ObservableCollection<Light>(await _bridge.GetLightsAsync());
                if (!_lights.Any())
                {
                    await ReportErrorAsync("We couldn't find any lights. Make sure they're in " +
                        "range and connected to a power source.");
                }
            }
            catch (Exception e)
            {
                await ReportErrorAsync(
                    "We encountered an unexpected problem trying to find your lights: " + e);
            }
        }

        /// <summary>
        /// Prepares Cortana for background use. 
        /// </summary>
        private async Task InitializeCortanaAsync()
        {
            // You can't write to application files by default, so we need to create a 
            // secondary VCD file to dynamically write Cortana commands to.
            StorageFile dynamicFile = await ApplicationData.Current.RoamingFolder.CreateFileAsync(
                "VoiceCommands.xml", CreationCollisionOption.ReplaceExisting);

            // Load the base file and parse the PhraseList we want from it.
            StorageFile baseFile = await StorageFile.GetFileFromApplicationUriAsync(
                new Uri("ms-appx:///VoiceCommands.xml"));
            XDocument xml = XDocument.Load(baseFile.Path);
            XElement state = xml.Descendants().First(x => x.Name.LocalName == "PhraseList" &&
                null != x.Attribute("Label") && x.Attribute("Label").Value == "state");

            // A ColorMapping is a RGB and HSV compliant representation a system color.
            // ColorMapping.CreateAll() returns a ColorMapping for all system colors available to UWP apps.
            // For each ColorMapping, add it to the list of phrases Cortana knows.
            foreach (HsbColor color in HsbColor.CreateAll())
            {
                state.Add(new XElement("Item", color.Name));
            }

            // Add the light names.
            XElement names = xml.Descendants().First(x => x.Name.LocalName == "PhraseList" &&
                null != x.Attribute("Label") && x.Attribute("Label").Value == "name");
            foreach (Light light in _lights)
            {
                names.Add(new XElement("Item", light.Name));
            }

            // Save the file, and then load so Cortana recognizes it.
            using (Stream stream = await dynamicFile.OpenStreamForWriteAsync())
            {
                xml.Save(stream);
            }
            try
            {
                await VoiceCommandDefinitionManager.InstallCommandDefinitionsFromStorageFileAsync(dynamicFile);
            }
            catch (FileNotFoundException)
            {
                // Do nothing. This is a workaround for a spurious FileNotFoundException that 
                // is thrown even though dynamicFile exists on disk. 
            }
        }

        /// <summary>
        /// Saves bridge information to the app's local storage so the app can load faster next time. 
        /// </summary>
        private void SaveBridgeToCache()
        {
            var localStorage = ApplicationData.Current.LocalSettings.Values;
            localStorage["bridgeIp"] = _bridge.Ip;
            localStorage["userId"] = _bridge.UserId;
        }

        /// <summary>
        /// Displays a popup to the user indicating an error occured. 
        /// </summary>
        private async Task ReportErrorAsync(string message)
        {
            await DispatchAwaitableUITask(async () =>
            {
                ErrorPopupText.Text = "Something went wrong.\r\n" + 
                    message + "\r\nThe app will now exit.";
                var results = await ErrorPopup.ShowAsync();
                Application.Current.Exit();
            });
        }

        /// <summary>
        /// Sets the extended splash screen image. Called on first load 
        /// and if the app window is resized by the user. 
        /// </summary>
        private void SetImage()
        {
            DisplayOrientations orientation = 
                DisplayInformation.GetForCurrentView().CurrentOrientation;
            extendedSplashImage.SetValue(Canvas.LeftProperty, rect.X);
            extendedSplashImage.SetValue(Canvas.TopProperty, rect.Y);

            if (orientation == DisplayOrientations.Portrait || 
                orientation == DisplayOrientations.PortraitFlipped)
            {
                extendedSplashImage.Height = _isPhone ? 
                    rect.Height / _scaleFactor : rect.Height;
                extendedSplashImage.Width = _isPhone ? 
                    rect.Width / _scaleFactor : rect.Width;
            }
            else
            {
                extendedSplashImage.Height = _isPhone ? 
                    rect.Width / _scaleFactor : rect.Height;
                extendedSplashImage.Width = _isPhone ? 
                    rect.Height / _scaleFactor : rect.Width;
            }
        }

        /// <summary>
        /// Fires when the user changes the size of the app window to adjust the image and progress ring positioning.
        /// </summary>
        private void Current_SizeChanged(object sender, WindowSizeChangedEventArgs e)
        {
            if (splash != null)
            {
                SetImage();
            }
        }

        /// <summary>
        /// Utility method for awaiting calls sent to the UI thread via the dispatcher. Used to display popups
        /// on the UI thread from a background thread and wait for user input before proceeding. 
        /// </summary>
        private Task DispatchAwaitableUITask(Func<Task> task)
        {
            var completion = new TaskCompletionSource<bool>();
            var action = rootFrame.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, async () =>
            {
                try
                {
                    await task.Invoke();
                    completion.TrySetResult(true);
                }
                catch (Exception e)
                {
                    completion.TrySetException(e);
                }
            });
            return completion.Task;
        }
    }

    /// <summary>
    /// Container class for passing Hue bridge and lights from the initializer page to MainPage.
    /// </summary>
    internal class HuePayload
    {
        public Bridge Bridge { get; set; }
        public IEnumerable<Light> Lights { get; set; }
    }
}