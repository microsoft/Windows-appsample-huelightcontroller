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

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Web.Http;

namespace HueLibrary
{
    /// <summary>
    /// Represents a Hue bridge.
    /// </summary>
    public class Bridge
    {
        /// <summary>
        /// Default constructor for the bridge. Requires manually setting the IP and userId.
        /// </summary>
        public Bridge() { }

        /// <summary>
        /// Constructor for the bridge with IP provided. Requires manually setting the userId.
        /// </summary>
        public Bridge(string ip)
        {
            Ip = ip;
        }

        /// <summary>
        /// Constructor for the bridge with IP and username provided. 
        /// </summary>
        public Bridge(string ip, string userId) : this(ip)
        {
            UserId = userId;
        }

        /// <summary>
        /// Attempts to find a bridge on your network and create an object for it. Returns null if no bridge is found. 
        /// </summary>
        public static async Task<Bridge> FindAsync()
        {
            using (var client = new HttpClient())
            {
                try
                {
                    string response = await client.GetStringAsync(new Uri("https://www.meethue.com/api/nupnp"));
                    if (response == "[]")
                    {
                        return null;
                    }
                    string ip = JArray.Parse(response).First["internalipaddress"].ToString();
                    return new Bridge(ip);
                }
                catch (Exception)
                {
                    return null;
                }
            }
        }

        /// <summary>
        /// Gets or sets the UserId for the bridge. If a registered UserId is not set, all calls to the bridge will fail.
        /// </summary>
        public string UserId { get; set; }

        /// <summary>
        /// Gets or sets the IP address of the bridge. If a valid IP is not set, all calls to the bridge will fail.
        /// </summary>
        public string Ip { get; set; }

        /// <summary>
        /// Gets the base URL to send bridge commands to.
        /// </summary>
        public string UrlBase => $"{Ip}/api/{UserId}/";

        /// <summary>
        /// Gets all lights known to the bridge.
        /// </summary>
        public async Task<IEnumerable<Light>> GetLightsAsync()
        {
            var lights = new List<Light>();
            HttpResponseMessage response = await HttpGetAsync("lights");
            string json = await response.Content.ReadAsStringAsync();
            JObject jObject = JObject.Parse(json);
            foreach (JProperty property in jObject.Properties())
            {
                Light light = JsonConvert.DeserializeObject<Light>(property.Value.ToString());
                light.Id = property.Name;
                light._bridge = this;
                light.State._light = light;
                if (light.State.Reachable)
                {
                    lights.Add(light);
                }
            }
            return lights;
        }

        /// <summary>
        /// Gets a light with the given id known to the bridge, or null if no light is found.
        /// </summary>
        public async Task<Light> GetLightAsync(string id)
        {
            HttpResponseMessage response = await HttpGetAsync($"lights/{id}");
            Light light = JsonConvert.DeserializeObject<Light>(await response.Content.ReadAsStringAsync());
            if (null != light)
            {
                light.Id = id;
                light._bridge = this;
                light.State._light = light;
            }
            return light;
        }

        /// <summary>
        /// Instructs the bridge to search for new, unknown lights.
        /// </summary>
        public async Task FindNewLightsAsync() => await HttpPutAsync("lights", String.Empty);

        /// <summary>
        /// Registers the application with the bridge and returns if the authorization succeeded.
        /// </summary>
        public async Task<bool> RegisterAsync()
        {
            try
            {
                using (var client = new HttpClient())
                {
                    string id = new Random().Next().ToString(); 
                    var response = await client.PostAsync(new Uri($"http://{Ip}/api"), 
                        new HttpStringContent($"{{\"devicetype\":\"HueLightController#{id}\"}}"));
                    string content = await response.Content.ReadAsStringAsync();
                    JArray json = JArray.Parse(content);
                    if (null != json.First["success"])
                    {
                        UserId = json.First["success"]["username"].ToString();
                        return true;
                    }
                }
            }
            catch (Exception)
            {
                return false; 
            }
            return false; 
        }

        /// <summary>
        /// Sends a basic command to the bridge and returns whether it receives the expected response.
        /// </summary>
        public async Task<BridgeConnectionStatus> PingAsync()
        {
            try
            {
                HttpResponseMessage response = await HttpGetAsync("config");
                string content = await response.Content.ReadAsStringAsync();
                if (content.Contains("zigbeechannel"))
                {
                    return BridgeConnectionStatus.Success; 
                }
                else if (content.Contains("error"))
                {
                    return BridgeConnectionStatus.Unauthorized; 
                }
                return BridgeConnectionStatus.Fail; 
            }
            catch (Exception)
            {
                return BridgeConnectionStatus.Fail; 
            }
        }

        /// <summary>
        /// Sends a GET command via HTTP and returns the response.
        /// </summary>
        internal async Task<HttpResponseMessage> HttpGetAsync(string commandUrl)
        {
            using (var client = new HttpClient())
            {
                return await client.GetAsync(new Uri($"http://{UrlBase}{commandUrl}"), 
                    HttpCompletionOption.ResponseContentRead);
            }
        }

        /// <summary>
        /// Sends a PUT command via HTTP and returns the response.
        /// </summary>
        internal async Task<string> HttpPutAsync(string commandUrl, string body)
        {
            using (var client = new HttpClient())
            {
                Uri uri = new Uri($"http://{UrlBase}{commandUrl}");
                HttpResponseMessage response = await client.PutAsync(uri, new HttpStringContent(body));
                return await response.Content.ReadAsStringAsync();
            }
        }
    }

    /// <summary>
    /// Represents the status of the connection to the bridge.
    /// </summary>
    public enum BridgeConnectionStatus
    {
        Success, 
        Unauthorized,
        Fail
    }
}