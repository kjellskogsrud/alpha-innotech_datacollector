// <copyright file="Worker.cs" company="Kjell Skogsrud">
// Copyright (c) Kjell Skogsrud. BSD 3-Clause License
// </copyright>

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using InfluxData.Net.Common.Enums;
using InfluxData.Net.InfluxDb;
using InfluxData.Net.InfluxDb.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;

namespace Alpha_Innotec_DataCollector
{
    /// <inheritdoc/>
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> logger;
        private IConfiguration appsettings;
        private Configuration configuration;

        // Configure JSON conveter settings.
        private JsonSerializerSettings jsonConvertSettings = new JsonSerializerSettings()
        {
            NullValueHandling = NullValueHandling.Ignore,
            MissingMemberHandling = MissingMemberHandling.Ignore,
        };

        /// <summary>
        /// Initializes a new instance of the <see cref="Worker"/> class.
        /// </summary>
        /// <param name="logger">Pass in the logger.</param>
        /// <param name="configuration">The app configuraiton.</param>
        public Worker(ILogger<Worker> logger, IConfiguration configuration)
        {
            this.appsettings = configuration;

            this.logger = logger;
            string configurationFilePathName = this.appsettings["ConfigurationFile"];

            // Look for the configuration file
            if (!File.Exists(configurationFilePathName))
            {
                this.configuration = new Configuration();
                this.configuration.PointsMap = new Dictionary<string, int>();
                this.configuration.TagMap = new Dictionary<string, string>();
                this.configuration.TagMap.Add("pump", "beta");
                this.configuration.PointsMap.Add("flow", 10);
                this.configuration.PointsMap.Add("return", 11);
                this.SerializeConfiguration(this.configuration, configurationFilePathName);
                this.logger.LogWarning("No configuration file found. Bootstrapping sample config and exiting.");
                Environment.Exit(0);
            }
            else
            {
                this.configuration = this.DeserializeConfiguration(configurationFilePathName);
            }
        }

        /// <inheritdoc/>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                
                this.logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);

                // Data buffer for incomming data.
                byte[] bytes = new byte[1024];

                // Try connecting to the remote device.
                try
                {
                    // Establish remote endpoint for the socket
                    // Check if the local Ip has been configured
                    if (string.IsNullOrEmpty(this.configuration.LocalIP))
                    {
                        this.configuration.LocalIP = this.GetLocalIPAddress();
                    }

                    // Make the nessasary endpoint parameters
                    IPAddress localIP = IPAddress.Parse(this.configuration.LocalIP);
                    IPEndPoint remoteEndpoint = new IPEndPoint(IPAddress.Parse(this.configuration.RemoteEndpointIP), this.configuration.RemoteEndpointPort);

                    // Create TCP Socket
                    Socket sender = new Socket(localIP.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

                    // Connect
                    sender.Connect(remoteEndpoint);

                    this.logger.LogInformation("Connected to endpoint {0}", sender.RemoteEndPoint.ToString());

                    // Encode the data string into a byte array.
                    // This is the command 3004 - Read Caluclations.
                    byte[] msg = new byte[8] { 0x00, 0x00, 0x0b, 0xbc, 0x00, 0x00, 0x00, 0x00 };

                    // Send data via the socket.
                    int bytesSent = sender.Send(msg);
                    this.logger.LogInformation("Sendt {0} bytes of data", bytesSent);

                    // Prepeare respons arrays.
                    byte[] returnCommand = new byte[4];
                    byte[] returnStatus = new byte[4];
                    byte[] numCalulations = new byte[4];

                    // Receive the response from the remote device.
                    sender.Receive(returnCommand, 4, SocketFlags.None);
                    sender.Receive(returnStatus, 4, SocketFlags.None);
                    sender.Receive(numCalulations, 4, SocketFlags.None);

                    int commandInt = int.Parse(this.ByteArrayToString(returnCommand), System.Globalization.NumberStyles.HexNumber);
                    int statusInt = int.Parse(this.ByteArrayToString(returnStatus), System.Globalization.NumberStyles.HexNumber);
                    int numCalculationsInt = int.Parse(this.ByteArrayToString(numCalulations), System.Globalization.NumberStyles.HexNumber);

                    int[] rawCalculations = new int[numCalculationsInt];

                    for (int i = 0; i < rawCalculations.Length; i++)
                    {
                        byte[] calculation = new byte[4];
                        sender.Receive(calculation, 4, SocketFlags.None);
                        rawCalculations[i] = int.Parse(this.ByteArrayToString(calculation), System.Globalization.NumberStyles.HexNumber);
                    }

                    // Release the socket.
                    sender.Shutdown(SocketShutdown.Both);
                    sender.Close();

                    // Setup InfluxDBClient
                    // This is a lazy client and does not connect untill you ask it to actually do something.
                    InfluxDbClient influxClient = new InfluxDbClient(
                        this.configuration.GetInfluxEndpoint(),
                        this.configuration.InfluxUser,
                        this.configuration.InfluxPassword,
                        InfluxDbVersion.Latest);

                    // Make a list for InfluxDB points
                    List<Point> points = new List<Point>();

                    foreach (string field in this.configuration.PointsMap.Keys)
                    {
                        Point somePoint = new Point();
                        somePoint.Name = field;
                        somePoint.Tags = new Dictionary<string, object>();
                        foreach (string tag in this.configuration.TagMap.Keys)
                        {
                            somePoint.Tags.Add(tag, this.configuration.TagMap[tag]);
                        }

                        somePoint.Fields = new Dictionary<string, object>()
                        {
                            { "value", (float)rawCalculations[this.configuration.PointsMap[field]] },
                        };
                        points.Add(somePoint);
                    }

                    var result = influxClient.Client.WriteAsync(points, this.configuration.InfluxDatabase).GetAwaiter().GetResult();

                    if (!result.Success)
                    {
                        this.logger.LogError("Failed to write to InfluxDB");
                    }

                }
                catch (Exception ex)
                {
                    this.logger.LogError("Error: {0}", ex.Message);
                }

                await Task.Delay(this.configuration.ReadingInterval * 60000, stoppingToken);
            }
        }

        private string ByteArrayToString(byte[] ba)
        {
            StringBuilder hex = new StringBuilder(ba.Length * 2);
            foreach (byte b in ba)
            {
                hex.AppendFormat("{0:x2}", b);
            }

            return hex.ToString();
        }

        private byte[] StringToByteArray(string hex)
        {
            return Enumerable.Range(0, hex.Length)
                             .Where(x => x % 2 == 0)
                             .Select(x => Convert.ToByte(hex.Substring(x, 2), 16))
                             .ToArray();
        }

        private void SerializeConfiguration(Configuration configuration, string configurationFileNamePath)
        {
            StreamWriter configurationJson = new StreamWriter(configurationFileNamePath);
            configurationJson.Write(JsonConvert.SerializeObject(configuration, Formatting.Indented));
            configurationJson.Close();
        }

        private Configuration DeserializeConfiguration(string configurationFileNamePath)
        {
            StreamReader configurationJson = new StreamReader(configurationFileNamePath);
            Configuration returnConfiguration = JsonConvert.DeserializeObject<Configuration>(configurationJson.ReadToEnd(), this.jsonConvertSettings);
            configurationJson.Close();
            return returnConfiguration;
        }

        private string GetLocalIPAddress()
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                {
                    return ip.ToString();
                }
            }

            throw new Exception("No network adapters with an IPv4 address in the system!");
        }
    }
}
