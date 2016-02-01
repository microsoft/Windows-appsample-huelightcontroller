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
using System;
using System.Collections;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.Serialization;
using System.Threading.Tasks;

namespace HueLibrary
{
    /// <summary>
    /// Represents a Hue lightbulb.
    /// </summary>
    [DataContract]
    public class Light
    {
        /// <summary>
        /// An internal backlink to the light's bridge.
        /// </summary>
        internal Bridge _bridge;

        /// <summary>
        /// Gets the light's id.
        /// </summary>
        [DataMember(Name = "id")]
        public string Id { get; set; }

        /// <summary>
        /// Gets or sets the light's state.
        /// </summary>
        [DataMember(Name = "state")]
        public LightState State { get; set; }

        /// <summary>
        /// Gets the light type.
        /// </summary>
        [DataMember(Name = "type")]
        public string Type { get; private set; }

        /// <summary>
        /// Gets the light name.
        /// </summary>
        [DataMember(Name = "name")]
        public string Name { get; private set; }
        
        /// <summary>
        /// Gets the model Id.
        /// </summary>
        [DataMember(Name = "modelid")]
        public string ModelId { get; private set; }

        /// <summary>
        /// Gets the software version.
        /// </summary>
        [DataMember(Name = "swversion")]
        public string SoftwareVersion { get; private set; }

        /// <summary>
        /// Attempts to refresh this object to match the current state of its physical light.
        /// </summary>
        public async Task SyncAsync() 
        {
            Light copy = await _bridge.GetLightAsync(Id); 
            if (null != copy)
            {
                State = copy.State;
            }
        }

        /// <summary>
        /// Attempts to rename the light.
        /// </summary>
        public async Task RenameAsync(string name) => 
            await _bridge.HttpPutAsync($"lights/{Id}", $"{{\"name\":\"{name}\"}}");

        /// <summary>
        /// Attempts to change the state of the physical light to match the state of this object.
        /// </summary>
        public async Task ChangeStateAsync() => await 
            _bridge.HttpPutAsync($"lights/{Id}/state", JsonConvert.SerializeObject(this));

        /// <summary>
        /// Attempts to change the state of the physical light to match the state of the selected property.
        /// </summary>
        public async Task ChangeStateAsync<T>(Expression<Func<LightState, T>> selector)
        {
            var expression = selector.Body as MemberExpression;
            if (null == expression)
            {
                throw new ArgumentException(
                    "This method can only modify LightState properties (like bri or hue)");
            }
            string attribute = expression.Member.CustomAttributes.First()
                .NamedArguments.First().TypedValue.Value.ToString();
            object property = selector.Compile().Invoke(State);
            string val = property.GetType().IsArray ? 
                $"[{String.Join(", ", ((IEnumerable)property).Cast<double>().Select(x => x.ToString()))}]" : 
                property.ToString().ToLower();
            string json = $"{{\"{attribute}\": {val}}}";
            await _bridge.HttpPutAsync($"lights/{Id}/state", json);
        }
    }

    /// <summary>
    /// Represents the state of a light.
    /// </summary>
    [DataContract]
    public class LightState
    { 
        /// <summary>
        /// An internal backlink to this state's light.
        /// </summary>
        internal Light _light;

        [DataMember(Name = "on")]
        private bool on;
        /// <summary>
        /// Gets or sets if the light is on.
        /// </summary>
        [LightProperty(Name = "on")]
        public bool On
        {
            get { return on; }
            set
            { 
                on = value;
                Task task = _light.ChangeStateAsync(x => x.On); 
            }
        }

        [DataMember(Name = "bri")]
        private byte bri;
        /// <summary>
        /// Gets or sets the lights brightness.
        /// </summary>
        [LightProperty(Name = "bri")]
        public byte Brightness
        {
            get { return bri; }
            set
            {
                bri = value;
                Task task = _light.ChangeStateAsync(x => x.Brightness); 
            }
        }

        [DataMember(Name = "hue")]
        private ushort hue; 
        /// <summary>
        /// Gets or sets the lights hue.
        /// </summary>
        [LightProperty(Name = "hue")]
        public ushort Hue
        {
            get { return hue; }
            set
            {
                hue = value;
                Task task = _light.ChangeStateAsync(x => x.Hue); 
            }
        }

        [DataMember(Name = "sat")]
        private byte sat;
        /// <summary>
        /// Gets or sets the light's saturation.
        /// </summary>
        [LightProperty(Name = "sat")]
        public byte Saturation
        {
            get { return sat; }
            set
            {
                sat = value;
                Task task = _light.ChangeStateAsync(x => x.Saturation);
            }
        }

        [DataMember(Name = "xy")]
        private double[] xy;
        /// <summary>
        /// Gets or sets the light's xy color coordinates.
        /// </summary>
        [LightProperty(Name = "xy")]
        public double[] ColorCoordinates
        {
            get { return xy; }
            set
            {
                xy = value;
                Task task = _light.ChangeStateAsync(x => x.ColorCoordinates);
            }
        }

        [DataMember(Name = "alert")]
        private string alert;
        /// <summary>
        /// Gets or sets the light's brightness.
        /// </summary>
        [LightProperty(Name = "alert")]
        public string Alert
        {
            get { return alert; }
            set
            {
                alert = value;
                Task task = _light.ChangeStateAsync(x => x.Alert);
            }
        }

        [DataMember(Name = "effect")]
        private string _effect;
        /// <summary>
        /// Gets or sets the light's effect.
        /// </summary>
        [LightProperty(Name = "effect")]
        public string Effect
        {
            get { return _effect; }
            set
            {
                _effect = value;
                Task task = _light.ChangeStateAsync(x => x.Effect);
            }
        }

        [DataMember(Name = "colormode")]
        private string colormode;
        /// <summary>
        /// Gets or sets the light's color mode.
        /// </summary>
        [LightProperty(Name = "colormode")]
        public string ColorMode
        {
            get { return colormode; }
            set
            {
                colormode = value;
                Task task = _light.ChangeStateAsync(x => x.ColorMode);
            }
        }

        /// <summary>
        /// Gets if the light is reachable.
        /// </summary>
        [DataMember(Name = "reachable")]
        public bool Reachable { get; private set; }
    }

    /// <summary>
    /// Maps the properties of LightState to their representation in json.
    /// </summary>
    public class LightProperty : Attribute
    {
        /// <summary>
        /// Gets the name of the property in the Hue REST API.
        /// </summary>
        public string Name { get; set; }
    }
}