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
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Windows.UI;

namespace HueLibrary
{
    /// <summary>
    /// A wrapper for Windows.UI.Color that also supports HSB (needed for Hue).
    /// </summary>
    public class HsbColor
    {
        /// <summary>
        /// Creates a new ColorMapping.
        /// </summary>
        public HsbColor(Color color, string name)
        {
            Color = color;
            Name = name; 
        }

        /// <summary>
        /// Gets the original RGB Windows.UI.Color.
        /// </summary>
        public Color Color { get; private set; }

        /// <summary>
        /// Gets the name of the color.
        /// </summary>
        public string Name { get; private set; }

        /// <summary>
        /// Gets the H (hue) of the color.
        /// </summary>
        public ushort H => (ushort)(RgbToHsv(Color.R, Color.G, Color.B).Item1 * 182);

        /// <summary>
        /// Gets the S (saturation) of the color.
        /// </summary>
        public byte S => Math.Min((byte)254, (byte)(RgbToHsv(Color.R, Color.G, Color.B).Item2 * 255));

        /// <summary>
        /// Gets the B (brightness) of the color.
        /// </summary>
        public byte B => Math.Min((byte)254, (byte)(RgbToHsv(Color.R, Color.G, Color.B).Item3 * 255));

        /// <summary>
        /// Converts RGB color values to their HSB equivalents.
        /// Code based on "Introduction to Computer Graphics" by Foley ... et all. 
        /// ISBN: 0201609215
        /// </summary>
        private Tuple<double, double, double> RgbToHsv(double r, double g, double b)
        {
            double[] hsv = new double[3]; 
            r = r / 255.0;
            g = g / 255.0;
            b = b / 255.0;
            double max = new[] { r, g, b }.Max();
            double min = new[] { r, g, b }.Min(); 
            double delta = max - min;
            hsv[1] = max != 0 ? delta / max : 0;
            hsv[2] = max;
            if (hsv[1] == 0) 
            {
                return new Tuple<double, double, double>(hsv[0], hsv[1], hsv[2]);
            }
            if (r == max)
            {
                hsv[0] = ((g - b) / delta);
            }
            else if (g == max)
            {
                hsv[0] = ((b - r) / delta) + 2.0;
            }
            else if (b == max)
            {
                hsv[0] = ((r - g) / delta) + 4.0;
            }
            hsv[0] *= 60.0;
            if (hsv[0] < 0)
            {
                hsv[0] += 360.0;
            }
            return new Tuple<double, double, double>(hsv[0], hsv[1], hsv[2]);
        }

        /// <summary>
        /// Returns a collection of ColorMappings for all system colors known to UWP apps
        /// </summary>
        public static IEnumerable<HsbColor> CreateAll() => 
            typeof(Colors).GetRuntimeProperties().Select(x => new HsbColor((Color)x.GetValue(null), 
                Regex.Replace(x.Name, "([a-z])([A-Z])", "$1 $2", RegexOptions.Compiled))); 
    }
}