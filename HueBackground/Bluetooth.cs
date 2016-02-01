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
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel.Background;
using Windows.Devices.Bluetooth.Background;
using Windows.Storage;

namespace BackgroundTasks
{
    /// <summary>
    /// Handler for background BTLE interactions with Hue.
    /// </summary>
    public sealed class AdvertisementWatcherTask : IBackgroundTask
    {
        private IBackgroundTaskInstance backgroundTaskInstance;
        private BackgroundTaskDeferral _deferral;
        private Bridge _bridge; 
        private IEnumerable<Light> _lights; 

        /// <summary>
        /// The entry point of a background task.
        /// </summary>
        public async void Run(IBackgroundTaskInstance taskInstance)
        {
            backgroundTaskInstance = taskInstance;
            var details = taskInstance.TriggerDetails as BluetoothLEAdvertisementWatcherTriggerDetails;
            if (details != null)
            {
                _deferral = backgroundTaskInstance.GetDeferral();
                taskInstance.Canceled += (s, e) => _deferral.Complete();

                var localStorage = ApplicationData.Current.LocalSettings.Values;
                _bridge = new Bridge(localStorage["bridgeIp"].ToString(), localStorage["userId"].ToString()); 
                try
                {
                    _lights = await _bridge.GetLightsAsync();
                }
                catch (Exception)
                {
                    _deferral.Complete();
                    return; 
                }
                foreach(var item in details.Advertisements)
                {
                    Debug.WriteLine(item.RawSignalStrengthInDBm);
                }

                // -127 is a BTLE magic number that indicates out of range. If we hit this, 
                // turn off the lights. Send the command regardless if they are on/off
                // just to be safe, since it will only be sent once.
                if (details.Advertisements.Any(x => x.RawSignalStrengthInDBm == -127))
                {
                    foreach (Light light in _lights)
                    {
                        light.State.On = false;
                        await Task.Delay(250);
                    }
                }
                // If there is no magic number, we are in range. Toggle any lights reporting
                // as off to on. Do not spam the command to lights arleady on. 
                else
                {
                    foreach (Light light in _lights.Where(x => !x.State.On))
                    {
                        light.State.On = true;
                        await Task.Delay(250);
                    }
                }
                // Wait 1 second before exiting to ensure all HTTP requests have sent.
                await Task.Delay(1000);
                _deferral.Complete();
            }
        }
    }
}