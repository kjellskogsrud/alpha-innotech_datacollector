// <copyright file="Configuration.cs" company="Kjell Skogsrud">
// Copyright (c) Kjell Skogsrud. BSD 3-Clause License
// </copyright>

using System.Collections.Generic;

namespace Alpha_Innotec_DataCollector
{
    /// <summary>
    /// Configuration properties for the service.
    /// </summary>
    public class Configuration
    {
        /// <summary>
        /// Gets or sets the RemoteEnpointIP. Default: string.empty.
        /// </summary>
        public string RemoteEndpointIP { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the RemoteEndpointPort. Default: 8888.
        /// </summary>
        public int RemoteEndpointPort { get; set; } = 8888;

        /// <summary>
        /// Gets or sets the local IP. This property is not required, and will be auto polulated if left empty. Default: string.empty.
        /// </summary>
        public string LocalIP { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the influx host.
        /// </summary>
        public string InfluxHost { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the influx port.
        /// </summary>
        public int InfluxPort { get; set; } = 8086;

        /// <summary>
        /// Gets or sets Influx user.
        /// </summary>
        public string InfluxUser { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the influx password.
        /// </summary>
        public string InfluxPassword { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the influx password.
        /// </summary>
        public string InfluxDatabase { get; set; } = "heatpump";

        /// <summary>
        /// Gets or sets the reading interval. This is in minutes.
        /// </summary>
        public int ReadingInterval { get; set; } = 1;

        /// <summary>
        /// Gets or sets the tags mapping list.
        /// </summary>
        public Dictionary<string, string> TagMap { get; set; }

        /// <summary>
        /// Gets or sets the Points mapping list.
        /// </summary>
        public Dictionary<string, int> PointsMap { get; set; }

        /// <summary>
        /// Gets the influx enpoint string.
        /// </summary>
        /// <returns>An influx http endpoint.</returns>
        public string GetInfluxEndpoint()
        {
            return "http://" + this.InfluxHost + ":" + this.InfluxPort.ToString() + "/";
        }
    }
}
