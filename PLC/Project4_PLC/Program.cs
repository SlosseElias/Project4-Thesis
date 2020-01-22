using Microsoft.Azure.Devices.Client;
using Microsoft.Azure.Devices.Provisioning.Client;
using Microsoft.Azure.Devices.Provisioning.Client.Transport;
using Microsoft.Azure.Devices.Shared;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;
using Opc.Ua;
using Opc.Ua.Configuration;
using System.Linq;
using System.IO;
using Newtonsoft.Json.Linq;

namespace Project4_PLC
{
    class Program
    {
        // PLC globals.
        static string ValveName;
        static string ValveState;
        static float setpointHeating;         // Setting - can be changed by the operator from IoT Central.       
        static double Temperature;
        const double HighTemperature = 30.0;
        const double LowTemperature = 15.0;

        static bool autoAccept = false;
        static Random rand;
        static StatusCode prevStatusCode = 400;       // Sets the statuscode of the opc ua server and when the current status differs from this then send it to IoT Central.
        static ApplicationConfiguration config;

        const string ApplicationName = "Project4_plc_iot-central";
        const string DeviceName = "OPC UA";
        static string OpcUAServer = "";
        static string OpcUAIP = "";
        const string TemperatureDisplayName = "Temperature Reactor";
        static string TemperatureNode = "";
        static string SetpointHeatingNode = "";

        // Programmatical sensors
        static Dictionary<String, String> DictValves = new Dictionary<String, String> {
            { "Valve 1", ValveEnum.Open.ToString() },
            { "Valve 2", ValveEnum.Open.ToString() },
            { "Valve 3", ValveEnum.Open.ToString() },
        };
        enum ValveEnum
        {
            Open,
            Close,
            Failed
        }

        // Telemetry globals.
        const int intervalInMilliseconds = 5000;        // Time interval required by wait function. 10s

        // IoT Central global variables.
        static DeviceClient s_deviceClient;
        static CancellationTokenSource cts;
        static string GlobalDeviceEndpoint = "global.azure-devices-provisioning.net";
        static TwinCollection reportedProperties = new TwinCollection();

        static Session session;
        const string noEvent = "";
        static string eventInfoText = noEvent;              // Event info text sent to IoT Central.
        static string eventWarnText = noEvent;              // Event warning text sent to IoT Central.
        static string eventErrorText = noEvent;              // Event error text sent to IoT Central.

        // User IDs.
        static string ScopeID = "";
        static string DeviceID = "";
        static string PrimaryKey = "";


        static void colorMessage(string text, ConsoleColor clr)
        {
            Console.ForegroundColor = clr;
            Console.WriteLine(text);
            Console.ResetColor();
        }
        static void greenMessage(string text)
        {
            colorMessage(text, ConsoleColor.Green);
        }
        static void redMessage(string text)
        {
            colorMessage(text, ConsoleColor.Red);
        }

        #region PLC
        
        static async void OPCStart()
        {
            #region Startup
            Console.WriteLine("Step 1 - Create application configuration and certificate.");
            try
            {
                config = new ApplicationConfiguration()
                {
                    ApplicationName = ApplicationName,
                    ApplicationUri = Utils.Format(@"urn:{0}:{1}", System.Net.Dns.GetHostName(), ApplicationName),
                    ApplicationType = ApplicationType.Client,
                    SecurityConfiguration = new SecurityConfiguration
                    {
                        #region Comment
                        /// Summary
                        #region
                        // These directories are locally on your computer.
                        // Generate self-signed (untrusted) client certificate(s). 
                        // AutoAcceptUntrustedCertificates = true: Use this for testing only or if application does not require server certificate check.

                        // Explanation of how this works (this is for another library, but it's the same idea): http://opclabs.doc-that.com/files/onlinedocs/QuickOpc/Latest/User's%20Guide%20and%20Reference-QuickOPC/Trusting%20Server%20Instance%20Certificate.html
                        #endregion

                        /// Full information of security model and certificates of OPC UA. It's quite a big document, but worth reading if you care about security (you should).
                        // More information (Security and certificates): https://opcfoundation.org/wp-content/uploads/2014/05/OPC-UA_Security_Model_for_Administrators_V1.00.pdf

                        // If you want to use cloud for certificates, check this out: https://docs.microsoft.com/bs-latn-ba/azure/iot-accelerators/overview-opc-vault-architecture
                        #endregion
                        ApplicationCertificate = new CertificateIdentifier { StoreType = @"Directory", StorePath = @"%CommonApplicationData%\OPC Foundation\CertificateStores\MachineDefault", SubjectName = Utils.Format(@"CN={0}, DC={1}", ApplicationName, System.Net.Dns.GetHostName()) },
                        TrustedIssuerCertificates = new CertificateTrustList { StoreType = @"Directory", StorePath = @"%CommonApplicationData%\OPC Foundation\CertificateStores\UA Certificate Authorities" },
                        TrustedPeerCertificates = new CertificateTrustList { StoreType = @"Directory", StorePath = @"%CommonApplicationData%\OPC Foundation\CertificateStores\UA Applications" },
                        RejectedCertificateStore = new CertificateTrustList { StoreType = @"Directory", StorePath = @"%CommonApplicationData%\OPC Foundation\CertificateStores\RejectedCertificates" },
                        AutoAcceptUntrustedCertificates = true, 
                        AddAppCertToTrustedStore = true,
                    },
                    TransportConfigurations = new TransportConfigurationCollection(),
                    TransportQuotas = new TransportQuotas { OperationTimeout = 15000 },
                    ClientConfiguration = new ClientConfiguration { DefaultSessionTimeout = 60000 },
                    TraceConfiguration = new TraceConfiguration()
                };
                config.Validate(ApplicationType.Client).GetAwaiter().GetResult();
                
                var application = new ApplicationInstance
                {
                    ApplicationName = ApplicationName,
                    ApplicationType = ApplicationType.Client,
                    ApplicationConfiguration = config
                };

                bool haveAppCertificate = await application.CheckApplicationInstanceCertificate(false, 0);
                if (!haveAppCertificate)
                    throw new Exception("Application instance certificate invalid!");
                
                if (haveAppCertificate)
                {
                    if (config.SecurityConfiguration.AutoAcceptUntrustedCertificates)
                        autoAccept = true;

                    config.CertificateValidator.CertificateValidation += new CertificateValidationEventHandler(CertificateValidator_CertificateValidation);
                }
                else
                {
                    Console.WriteLine("    WARN: missing application certificate, using unsecure connection.");
                }

            }
            catch (Exception ex)
            {
                Console.WriteLine("Exception: " + ex.Message);
            }

            var selectedEndpoint = CoreClientUtils.SelectEndpoint(OpcUAServer, useSecurity: false, operationTimeout: 150000);

            Console.WriteLine($"Step 2 - Create a session with your server: {selectedEndpoint.EndpointUrl} ");
            session = Session.Create(config, new ConfiguredEndpoint(null, selectedEndpoint, EndpointConfiguration.Create(config)), false, "", 60000, new UserIdentity(new AnonymousIdentityToken()), null).GetAwaiter().GetResult();
            greenMessage("Successfully connected to PLC");

            Console.WriteLine("Step 3 - Browse the server namespace.");
            ReferenceDescriptionCollection refs;
            Byte[] cp;
            session.Browse(null, null, ObjectIds.ObjectsFolder, 0u, BrowseDirection.Forward, ReferenceTypeIds.HierarchicalReferences, true, (uint)NodeClass.Variable | (uint)NodeClass.Object | (uint)NodeClass.Method, out cp, out refs);
            Console.WriteLine("DisplayName: BrowseName, NodeClass");
            // Show only the nodes
            #region
            var rd = refs[4];
            Console.WriteLine("{0}: {1}, {2}", rd.DisplayName, rd.BrowseName, rd.NodeClass);
            ReferenceDescriptionCollection nextRefs;
            byte[] nextCp;
            session.Browse(null, null, ExpandedNodeId.ToNodeId(rd.NodeId, session.NamespaceUris), 0u, BrowseDirection.Forward, ReferenceTypeIds.HierarchicalReferences, true, (uint)NodeClass.Variable | (uint)NodeClass.Object | (uint)NodeClass.Method, out nextCp, out nextRefs);
            foreach (var nextRd in nextRefs)
            {
                Console.WriteLine("+ {0}: {1}, {2}", nextRd.DisplayName, nextRd.BrowseName, nextRd.NodeClass);
            }
            #endregion

            // Show all information of the OPC UA Server, including nodes
            #region
            //foreach (var rd in refs)
            //{
            //    Console.WriteLine("{0}: {1}, {2}", rd.DisplayName, rd.BrowseName, rd.NodeClass);
            //    ReferenceDescriptionCollection nextRefs;
            //    byte[] nextCp;
            //    session.Browse(null, null, ExpandedNodeId.ToNodeId(rd.NodeId, session.NamespaceUris), 0u, BrowseDirection.Forward, ReferenceTypeIds.HierarchicalReferences, true, (uint)NodeClass.Variable | (uint)NodeClass.Object | (uint)NodeClass.Method, out nextCp, out nextRefs);
            //    foreach (var nextRd in nextRefs)
            //    {
            //        Console.WriteLine("+ {0}: {1}, {2}", nextRd.DisplayName, nextRd.BrowseName, nextRd.NodeClass);
            //    }
            //}
            #endregion

            Console.WriteLine("Step 4 - Create a subscription. Set a faster publishing interval if you wish.");
            var subscription = new Subscription(session.DefaultSubscription) { PublishingInterval = 3500 };

            Console.WriteLine("Step 5 - Add a list of items you wish to monitor to the subscription.");
            var list = new List<MonitoredItem> {
                new MonitoredItem(subscription.DefaultItem)
                {
                    DisplayName = "ServerStatusCurrentTime",
                    StartNodeId = "i=2258"
                },
                new MonitoredItem(subscription.DefaultItem)
                {
                    DisplayName = TemperatureDisplayName,
                    StartNodeId = TemperatureNode,
                    AttributeId = 13, // Monitor the AttributeId 13 (= value) of the TemperatureNode
                    DiscardOldest = true,
                    QueueSize = 1
                }
            };
            list.ForEach(i => i.Notification += OnNotification);
            subscription.AddItems(list);

            Console.WriteLine("Step 6 - Add the subscription to the session.");
            session.AddSubscription(subscription);
            subscription.Create();

            #endregion

            //Read nodes
            ReadNode(TemperatureNode);

        }

        /// <summary>
        /// Reads a node from the PLC
        /// </summary>
        /// <param name="node"></param>
        static void ReadNode(string node)
        {
            if(string.Equals(node, TemperatureNode))
            {
                Temperature = Convert.ToDouble(session.ReadValue(node).WrappedValue.Value.ToString());
                if (Temperature >= HighTemperature)
                    eventWarnText = TemperatureDisplayName + " is high";
                else if (Temperature <= LowTemperature)
                    eventWarnText = TemperatureDisplayName + " is low";
            }
            // If you have other nodes on your PLC, add an else if here.
            else
            {
                Console.WriteLine("No nodes with name {0} found on PLC", node);
            }
            
        }

        // HANDLERS PLC
        /// <summary>
        /// Handler for Telemetry data changes.
        /// </summary>
        /// <param name="item"></param>
        /// <param name="e"></param>
        private static void OnNotification(MonitoredItem item, MonitoredItemNotificationEventArgs e)
        {
            string symbol = "";
            if(item.DisplayName == TemperatureDisplayName)
                symbol = "°C";

            foreach (var value in item.DequeueValues())
            {
                Console.WriteLine("{0} ({1}): {2} - {3}{4}", value.SourceTimestamp, value.StatusCode, item.DisplayName, value.Value, symbol);

                if (item.DisplayName == "ServerStatusCurrentTime")
                {
                    // Only send the status to IoT Central when it changes.
                    if (value.StatusCode != prevStatusCode)
                    {
                        eventInfoText = "OPC UA Server status" + " - " + value.StatusCode;
                        SendEventTelemetryAsync();
                    }
                    prevStatusCode = value.StatusCode;
                    
                }
            }
            
        }
        
        /// <summary>
        /// Handler for the OPC UA server certificate.
        /// </summary>
        /// <param name="validator"></param>
        /// <param name="e"></param>
        private static void CertificateValidator_CertificateValidation(CertificateValidator validator, CertificateValidationEventArgs e)
        {
            if (e.Error.StatusCode == StatusCodes.BadCertificateUntrusted)
            {
                e.Accept = autoAccept;
                if (autoAccept)
                    Console.WriteLine("Accepted Certificate: {0}", e.Certificate.Subject);
                else
                    Console.WriteLine("Rejected Certificate: {0}", e.Certificate.Subject);
            }
        }

        #endregion

        #region IoT Central

        /// <summary>
        /// Register Device with IoT Central
        /// </summary>
        /// <param name="security"></param>
        /// <returns></returns>
        public static async Task<DeviceRegistrationResult> RegisterDeviceAsync(SecurityProviderSymmetricKey security)
        {
            Console.WriteLine("Register device...");

            using (var transport = new ProvisioningTransportHandlerMqtt(TransportFallbackType.TcpOnly))
            {
                ProvisioningDeviceClient provClient =
                          ProvisioningDeviceClient.Create(GlobalDeviceEndpoint, ScopeID, security, transport);

                Console.WriteLine($"RegistrationID = {security.GetRegistrationID()}");

                Console.Write("Provisioning Client RegisterAsync...");
                DeviceRegistrationResult result = await provClient.RegisterAsync();

                Console.WriteLine($"{result.Status}");

                return result;
            }
        }

        static Task<MethodResponse> CmdCloseAllValves(MethodRequest methodRequest, object userContext)
        {
            try
            {
                for (int i = 0; i < DictValves.Count; i++)
                {
                    DictValves[DictValves.ElementAt(i).Key] = ValveEnum.Close.ToString();
                }

                redMessage("Emergency Stop! All valves closed!");
                eventErrorText = "Emergency Stop Activated! All valves closed.";
                GetAllValveStates();

                // Acknowledge the direct method call with a 200 success message.
                string result = "{\"result\":\"Executed direct method: " + methodRequest.Name + "\"}";
                return Task.FromResult(new MethodResponse(Encoding.UTF8.GetBytes(result), 200));
            }
            catch
            {
                // Acknowledge the direct method call with a 400 error message.
                string result = "{\"result\":\"Invalid call\"}";
                return Task.FromResult(new MethodResponse(Encoding.UTF8.GetBytes(result), 400));
            }
        }

        static Task<MethodResponse> CmdChangeValveState(MethodRequest methodRequest, object userContext)
        {
            try
            {
                // Pick up variables from the request payload, with the name specified in IoT Central.
                var payloadString = Encoding.UTF8.GetString(methodRequest.Data);
                var payloadJson = Newtonsoft.Json.JsonConvert.DeserializeObject<dynamic>(payloadString);
                ValveName = payloadJson["ValveName"];
                ValveState = payloadJson["ValveState"];
                
                if(DictValves[ValveName] != ValveState)
                {
                    DictValves[ValveName] = ValveState;
                    ValveStateChanged(ValveName);
                }
                else
                {
                    eventWarnText = ValveName + " is already in state: " + ValveState;
                }

                // Acknowledge the direct method call with a 200 success message.
                string result = "{\"result\":\"Executed direct method: " + methodRequest.Name + "\"}";
                return Task.FromResult(new MethodResponse(Encoding.UTF8.GetBytes(result), 200));
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                
                // Acknowledge the direct method call with a 400 error message.
                string result = "{\"result\":\"Invalid call\"}";
                return Task.FromResult(new MethodResponse(Encoding.UTF8.GetBytes(result), 400));
            }
        }

        /// <summary>
        /// Static function that is called when a valve state changes
        /// </summary>
        /// <param name="key"></param>
        static void ValveStateChanged(string key)
        {
            if (string.IsNullOrEmpty(key) == false && DictValves.ContainsKey(key))
            {
                ValveState = DictValves[key];
                eventInfoText = key + " changed state: " + ValveState;
                colorMessage(key + " changed state: " + ValveState, ConsoleColor.Yellow);

                // If the state is Failed, we send an error event to IoT Central
                if (string.Equals(ValveState, ValveEnum.Failed.ToString()))
                {
                    eventErrorText = key + ValveState;
                }

                SendEventTelemetryAsync();
            }
        }

        /// <summary>
        /// Gets all the valves (valve name and current valve state)
        /// And send it to IoT Central as event telemetry.
        /// </summary>
        static void GetAllValveStates()
        {
            foreach (KeyValuePair<String, String> valve in DictValves)
            {
                eventInfoText = valve.Key + " - " + valve.Value;
                SendEventTelemetryAsync();
            }   
        }

        /// <summary>
        /// Send telemetry data of the events to IoT Central
        /// </summary>
        static async void SendEventTelemetryAsync()
        {
            // Create the telemetry JSON message.
            var telemetryDataPoint = new
            {
                EventInfo = eventInfoText,
                EventWarn = eventWarnText,
                EventError = eventErrorText,
            };

            var telemetryMessageString = JsonSerializer.Serialize(telemetryDataPoint);
            var telemetryMessage = new Message(Encoding.ASCII.GetBytes(telemetryMessageString));

            // Clear the events, as the message has been sent.
            eventInfoText = noEvent;
            eventWarnText = noEvent;
            eventErrorText = noEvent;

            Console.WriteLine($"\nEvent Telemetry data: {telemetryMessageString}");

            // Send the telemetry message.
            await s_deviceClient.SendEventAsync(telemetryMessage);
            greenMessage($"Event Telemetry sent {DateTime.Now.ToShortTimeString()}");
        }

        /// <summary>
        /// Handles the sending of telemetry, states, and events to IoT Central.
        /// </summary>
        /// <param name="token"></param>
        static async void SendPLCTelemetryAsync(CancellationToken token)
        {
            rand = new Random();
            
            while (true)
            {
                // Read the temperature from the OPC UA
                ReadNode(TemperatureNode);

                // Test the valve state: failed
                //DictValves["Valve 1"] = ValveEnum.Failed.ToString();
                //ValveStateChanged("Valve 1");

                // Generate Test Temperature data
                Temperature = rand.Next(0, 30);

                // Create the telemetry JSON message.
                var telemetryDataPoint = new
                {
                    Temperature = Temperature,
                    EventInfo = eventInfoText,
                    EventWarn = eventWarnText,
                    EventError = eventErrorText,
                };
                
                var telemetryMessageString = JsonSerializer.Serialize(telemetryDataPoint);
                var telemetryMessage = new Message(Encoding.ASCII.GetBytes(telemetryMessageString));

                // Clear the events, as the message has been sent.
                eventInfoText = noEvent;
                eventWarnText = noEvent;
                eventErrorText = noEvent;

                Console.WriteLine($"\nTelemetry data: {telemetryMessageString}");

                // Bail if requested.
                token.ThrowIfCancellationRequested();

                // Send the telemetry message.
                await s_deviceClient.SendEventAsync(telemetryMessage);
                greenMessage($"Telemetry sent {DateTime.Now.ToShortTimeString()}");

                await Task.Delay(intervalInMilliseconds);
            }
        }

        static async Task SendDevicePropertiesAsync()
        {
            reportedProperties["DeviceID"] = DeviceName;
            await s_deviceClient.UpdateReportedPropertiesAsync(reportedProperties);
            greenMessage($"Sent device properties: {JsonSerializer.Serialize(reportedProperties)}");
        }

        static async Task HandleSettingChanged(TwinCollection desiredProperties, object userContext)
        {
            string setting = "SetpointHeating";
            if (desiredProperties.Contains(setting))
            {
                BuildAcknowledgement(desiredProperties, setting);
                setpointHeating = (int)desiredProperties[setting]["value"];
                greenMessage($"Heating Setpoint updated: {setpointHeating}");
                
                // Write to OPC UA, Setpoint Temperature node
                try
                {
                    WriteValue nodeToWrite = new WriteValue()
                    {
                        NodeId = SetpointHeatingNode,
                        AttributeId = Attributes.Value,
                        Value = new DataValue(setpointHeating)
                    };
                    WriteValueCollection nodesToWrite = new WriteValueCollection();
                    nodesToWrite.Add(nodeToWrite);

                    // read the attributes.
                    StatusCodeCollection results = null;
                    DiagnosticInfoCollection diagnosticInfos = null;

                    // Write to setpoint temperature node
                    ResponseHeader responseHeader = session.Write(null, nodesToWrite, out results, out diagnosticInfos);

                    ClientBase.ValidateResponse(results, nodesToWrite);
                    ClientBase.ValidateDiagnosticInfos(diagnosticInfos, nodesToWrite);

                    // check for error.
                    if (StatusCode.IsBad(results[0]))
                    {
                        throw ServiceResultException.Create(results[0], 0, diagnosticInfos, responseHeader.StringTable);
                    }
                    // If no error, send the updated setpoint temperature to IoT Central.
                    else if (StatusCode.IsGood(results[0]))
                    {
                        Console.WriteLine(session.ReadValue(SetpointHeatingNode).Value);
                        await s_deviceClient.UpdateReportedPropertiesAsync(reportedProperties);
                        
                        // Send an informational event to IoT Central
                        eventInfoText = "Setpoint Heating has been updated: " + setpointHeating.ToString() + " °C";
                    }

                }
                catch (Exception ex)
                {
                    Console.WriteLine("Exception message: " + ex.Message);
                }

            }
               
        }

        static void BuildAcknowledgement(TwinCollection desiredProperties, string setting)
        {
            reportedProperties[setting] = new
            {
                value = desiredProperties[setting]["value"],
                status = "completed",
                desiredVersion = desiredProperties["$version"],
                message = "Processed"
            };
        }    
        

        #endregion

        static void Main(string[] args)
        {            
            colorMessage($"Starting {DeviceID}", ConsoleColor.Yellow);
            try
            {
                // Read the json file with the connection keys.
                // Assign the values from the json file to the variables.
                using (StreamReader r = new StreamReader("../../ConnectionKeys.json"))
                {
                    var jobj = JObject.Parse(r.ReadToEnd());
                    ScopeID = jobj["ScopeID"].ToString();
                    DeviceID = jobj["DeviceID"].ToString();
                    PrimaryKey = jobj["PrimaryKey"].ToString();
                    OpcUAServer = jobj["OpcUAServerEndpoint"].ToString();
                    OpcUAIP = jobj["OpcUAIP"].ToString();
                    TemperatureNode = jobj["TemperatureNode"].ToString();
                    SetpointHeatingNode = jobj["SetpointHeatingNode"].ToString();
                }
            }
            catch(Exception ex)
            {
                Console.WriteLine("Exception: " + ex.Message);
                Console.WriteLine("Check the name of the file ConnectionKeys.json. Did you type it correctly?"); 
            }

            try
            {
                using (var security = new SecurityProviderSymmetricKey(DeviceID, PrimaryKey, null))
                {
                    // Register device in IoT Central
                    DeviceRegistrationResult result = RegisterDeviceAsync(security).GetAwaiter().GetResult();
                    if (result.Status != ProvisioningRegistrationStatusType.Assigned)
                    {
                        Console.WriteLine("Failed to register device");
                        return;
                    }
                    IAuthenticationMethod auth = new DeviceAuthenticationWithRegistrySymmetricKey(result.DeviceId, (security as SecurityProviderSymmetricKey).GetPrimaryKey());
                    s_deviceClient = DeviceClient.Create(result.AssignedHub, auth, TransportType.Mqtt);
                }
                greenMessage("Device successfully connected to Azure IoT Central");

                // Connect to the OPC
                OPCStart();
   
                SendDevicePropertiesAsync().GetAwaiter().GetResult();

                Console.Write("Register settings changed handler...");
                s_deviceClient.SetDesiredPropertyUpdateCallbackAsync(HandleSettingChanged, null).GetAwaiter().GetResult();
                Console.WriteLine("Done");

                // Shows for the first time all the valves information in the event in IoT Central
                GetAllValveStates();

                cts = new CancellationTokenSource();

                // Create a handler for the direct method calls.
                s_deviceClient.SetMethodHandlerAsync("EmergencyStop", CmdCloseAllValves, null).Wait();
                s_deviceClient.SetMethodHandlerAsync("ValveState", CmdChangeValveState, null).Wait();

                SendPLCTelemetryAsync(cts.Token);

                Console.WriteLine("Press any key to exit...");
                Console.ReadKey();
                cts.Cancel();

            }
            catch (Exception ex)
            {
                Console.WriteLine();
                Console.WriteLine(ex.Message);
            }

        }

    }
}
