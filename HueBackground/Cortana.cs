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

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.ApplicationModel.AppService;
using Windows.ApplicationModel.Background;
using Windows.ApplicationModel.VoiceCommands;
using HueLibrary;
using System.Linq;
using Windows.Storage;

namespace BackgroundTasks
{
    /// <summary>
    /// Handler for background Cortana interactions with Hue.
    /// </summary>
    public sealed class LightControllerVoiceCommandService : IBackgroundTask
    {
        private VoiceCommandServiceConnection _voiceServiceConnection;
        private VoiceCommand _voiceCommand;
        private BackgroundTaskDeferral _deferral;
        private Bridge _bridge;
        private IEnumerable<Light> _lights;
        private Dictionary<string, HsbColor> _colors;

        /// <summary>
        /// Entry point for the background task.
        /// </summary>
        public async void Run(IBackgroundTaskInstance taskInstance)
        {
            var triggerDetails = taskInstance.TriggerDetails as AppServiceTriggerDetails;
            if (null != triggerDetails && triggerDetails.Name == "LightControllerVoiceCommandService")
            {
                _deferral = taskInstance.GetDeferral();
                taskInstance.Canceled += (s, e) => _deferral.Complete();
                if (true != await InitalizeAsync(triggerDetails))
                {
                    return;
                }
                // These command phrases are coded in the VoiceCommands.xml file.
                switch (_voiceCommand.CommandName)
                {
                    case "changeLightsState": await ChangeLightStateAsync(); break;
                    case "changeLightsColor": await SelectColorAsync(); break;
                    case "changeLightStateByName": await ChangeSpecificLightStateAsync(); break;
                    default: await _voiceServiceConnection.RequestAppLaunchAsync(
                        CreateCortanaResponse("Launching HueLightController")); break;
                }
                // keep alive for 1 second to ensure all HTTP requests sent.
                await Task.Delay(1000);
                _deferral.Complete();
            }
        }

        /// <summary>
        /// Handles the command to change the state of a specific light.
        /// </summary>
        private async Task ChangeSpecificLightStateAsync()
        {
            string name = _voiceCommand.Properties["name"][0];
            string state = _voiceCommand.Properties["state"][0];
            Light light = _lights.FirstOrDefault(x => 
                x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (null != light)
            {
                await ExecutePhrase(light, state);
                var response = CreateCortanaResponse($"Turned {name} {state}.");
                await _voiceServiceConnection.ReportSuccessAsync(response);
            }
        }

        /// <summary>
        /// Handles the command to change the state of all the lights.
        /// </summary>
        private async Task ChangeLightStateAsync()
        {
            string phrase = _voiceCommand.Properties["state"][0];
            foreach (Light light in _lights)
            {
                await ExecutePhrase(light, phrase);
                await Task.Delay(500); // wait 500ms between pings to ensure bridge isn't overloaded.
            }
            var response = CreateCortanaResponse($"Turned your lights {phrase.ToLower()}.");
            await _voiceServiceConnection.ReportSuccessAsync(response);
        }

        /// <summary>
        /// Handles an interaction with Cortana where the user selects 
        /// from randomly chosen colors to change the lights to.
        /// </summary>
        private async Task SelectColorAsync()
        {
            var userPrompt = new VoiceCommandUserMessage();
            userPrompt.DisplayMessage = userPrompt.SpokenMessage = 
                "Here's some colors you can choose from.";

            var userReprompt = new VoiceCommandUserMessage();
            userReprompt.DisplayMessage = userReprompt.SpokenMessage = 
                "Sorry, didn't catch that. What color would you like to use?";

            // Randomly select 6 colors for Cortana to show
            var random = new Random();
            var colorContentTiles = _colors.Select(x => new VoiceCommandContentTile
            {
                ContentTileType = VoiceCommandContentTileType.TitleOnly,
                Title = x.Value.Name
            }).OrderBy(x => random.Next()).Take(6);

            var colorResponse = VoiceCommandResponse.CreateResponseForPrompt(
                userPrompt, userReprompt, colorContentTiles);
            var disambiguationResult = await 
                _voiceServiceConnection.RequestDisambiguationAsync(colorResponse);
            if (null != disambiguationResult)
            {
                var selectedColor = disambiguationResult.SelectedItem.Title;
                foreach (Light light in _lights)
                {
                    await ExecutePhrase(light, selectedColor);
                    await Task.Delay(500);
                }
                var response = CreateCortanaResponse($"Turned your lights {selectedColor}.");
                await _voiceServiceConnection.ReportSuccessAsync(response);
            }
        }

        /// <summary>
        /// Converts a phrase to a light command and executes it.
        /// </summary>
        private async Task ExecutePhrase(Light light, string phrase)
        {
            if (phrase == "On")
            {
                light.State.On = true;
            }
            else if (phrase == "Off")
            {
                light.State.On = false;
            }
            else if (_colors.ContainsKey(phrase))
            {
                light.State.Hue = _colors[phrase].H;
                light.State.Saturation = _colors[phrase].S;
                light.State.Brightness = _colors[phrase].B;
            }
            else
            {
                var response = CreateCortanaResponse("Launching HueLightController");
                await _voiceServiceConnection.RequestAppLaunchAsync(response);
            }
        }

        /// <summary>
        /// Helper method for initalizing the voice service, bridge, and lights. Returns if successful. 
        /// </summary>
        private async Task<bool> InitalizeAsync(AppServiceTriggerDetails triggerDetails)
        {
            _voiceServiceConnection = 
                VoiceCommandServiceConnection.FromAppServiceTriggerDetails(triggerDetails);
            _voiceServiceConnection.VoiceCommandCompleted += (s, e) => _deferral.Complete();

            _voiceCommand = await _voiceServiceConnection.GetVoiceCommandAsync();
            _colors = HsbColor.CreateAll().ToDictionary(x => x.Name);

            var localStorage = ApplicationData.Current.LocalSettings.Values;
            _bridge = new Bridge(
                localStorage["bridgeIp"].ToString(), localStorage["userId"].ToString());
            try
            {
                _lights = await _bridge.GetLightsAsync();
            }
            catch (Exception)
            {
                var response = CreateCortanaResponse("Sorry, I couldn't connect to your bridge.");
                await _voiceServiceConnection.ReportFailureAsync(response);
                return false;
            }
            if (!_lights.Any())
            {
                var response = CreateCortanaResponse("Sorry, I couldn't find any lights.");
                await _voiceServiceConnection.ReportFailureAsync(response);
                return false;
            }
            return true;
        }

        /// <summary>
        /// Helper method for creating a message for Cortana to speak and write to the user.
        /// </summary>
        private VoiceCommandResponse CreateCortanaResponse(string message)
        {
            var userMessage = new VoiceCommandUserMessage()
            {
                DisplayMessage = message,
                SpokenMessage = message
            };
            var response = VoiceCommandResponse.CreateResponse(userMessage);
            return response;
        }
    }
}