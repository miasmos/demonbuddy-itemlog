//!CompilerOption:AddRef:Newtonsoft.Json.dll
//!CompilerOption:AddRef:Trinity.dll
using System;
using System.Text;
using System.Net.Sockets;
using System.Net;
using Zeta.Common;
using System.Reflection;
using Zeta.Game.Internals.Actors;
using System.Threading;
using System.Drawing;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Drawing.Imaging;
using Zeta.Bot.Settings;
using Zeta.Game;
using Zeta.Bot;
using Zeta.Game.Internals;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using Newtonsoft.Json;
using Trinity.Helpers;
using Trinity.Items;
using Trinity.Technicals;
using Newtonsoft.Json.Serialization;
using System.Threading.Tasks;
using System.Collections.Generic;


namespace ItemLogD3Plugin
{
	public enum LoginResult
	{
		WrongApiKey = 0,
		Success = 1,
		WrongClientVersion = 2,
		ConnectionError = 3,
		Unknown = 4
	}
    public class test
    {
        public int sessionId;
        public String apikey;
        public int version;
    }
    public class LoginResponse
    {
        public int response;
    }
	class Connection
	{
		private TcpClient connection;
		private Thread listenThread;
		private Thread sendThread;
		private object sendLock;
		private GugaPlugin plugin;
		private bool isConnected;
		private bool isLoggedIn;
		private object aliveLock;
		#region Assembly
		private static Assembly dll;
		private static Type typePacket;
		private static Type typePacketLogin;
		private static Type typePacketPlayerData;
		private static Type typePacketScreenShot;
		private static Type typePacketItemDrop;
		private static Type typePacketIsAlive;
		private static Type typePacketProfile;

		private static ConstructorInfo conPacketLogin;
		private static ConstructorInfo conPacketPlayerData;
		private static ConstructorInfo conPacketScreenShot;
		private static ConstructorInfo conPacketItemDrop;
		private static ConstructorInfo conPacketIsAlive;
		private static ConstructorInfo conPacketProfile;

		private static MethodInfo methodPacketToBytes;
		private static MethodInfo methodBytesToPacket;
		//Packet
		private static FieldInfo fieldSessionID;
		//PacketLogin
		private static FieldInfo fieldLoginSuccess;
		#endregion
		private string serverAddress = "127.0.0.1";
		private int serverPort = 15999;
		private int sessionID = 0;

		private List<ACDItem> sendCache = new List<ACDItem>();
        private static readonly log4net.ILog Logging = Logger.GetLoggerInstanceForType();

		public Connection(GugaPlugin plugin)
		{
			this.plugin = plugin;
			isConnected = false;
			isLoggedIn = false;
			connection = null;
			listenThread = null;
			sendThread = null;
			sendLock = new object();
			aliveLock = new object();
		}
		public void Connect()
		{
			try
			{
				connection = new TcpClient();
				connection.Connect(new IPEndPoint(Dns.GetHostAddresses(serverAddress)[0], serverPort));
				isConnected = true;
				connection.ReceiveTimeout = 2000;
				Login();
				connection.ReceiveTimeout = 0;
				listenThread = new Thread(ListenProc);
				listenThread.Start();
				sendThread = new Thread(SendProc);
				sendThread.Start();
			}
			catch (Exception e)
			{	
				Logging.Error("CONNECTION ERROR: " + e.Message + "   " + e.StackTrace);
				isConnected = false;
			}
		}
		public void Disconnect()
		{
			if (listenThread != null)
			listenThread.Abort();
			if (sendThread != null)
			sendThread.Abort();
			connection.Close();
			isConnected = false;
			isLoggedIn = false;
		}
		public bool IsConnected()
		{
			return isConnected && isLoggedIn;
		}
		public bool SendProfiles()
		{
			try
			{
				// string[] profiles = plugin.GetProfileList();
				// for (int i = 0; i < profiles.Length; i++)
				// profiles[i] = Path.GetFileName(profiles[i]);

				// object profilePacket = conPacketProfile.Invoke(new object[] { sessionID, profiles });
				// byte[] buffer = (byte[])methodPacketToBytes.Invoke(null, new object[] { profilePacket });
				// Send(buffer);
				return true;
			}
			catch (Exception e)
			{
				return false;
			}
		}
        static byte[] GetBytes(string str)
        {
        	str += '\0';
            byte[] bytes = new byte[str.Length * sizeof(char)];
            System.Buffer.BlockCopy(str.ToCharArray(), 0, bytes, 0, bytes.Length);
            return bytes;
        }
        static string GetString(byte[] bytes)
        {
            char[] chars = new char[bytes.Length / sizeof(char)];
            System.Buffer.BlockCopy(bytes, 0, chars, 0, bytes.Length);
            return new string(chars);
        }

		public bool Login()
		{
			LoginResult result = LoginResult.Unknown;
			try
			{
				//object loginPacket = conPacketLogin.Invoke(new object[] {sessionID, plugin.ApiKey, plugin.VersionRaw });
				//byte[] buffer = (byte[])methodPacketToBytes.Invoke(null, new object[] { loginPacket });
                
                test t = new test{sessionId = sessionID, apikey = "test", version = plugin.VersionRaw};
                string json = JsonConvert.SerializeObject(t);
                plugin.LogMessage("Send: " + json);
                byte[] buffer = GetBytes(json);

				Send(buffer);
                buffer = new byte[connection.Client.ReceiveBufferSize];
				connection.Client.Receive(buffer);

                string rjson = GetString(buffer);
                LoginResponse w = JsonConvert.DeserializeObject<LoginResponse>(rjson);
                plugin.LogMessage("Receive: " + w.response);
                result = (LoginResult)w.response;

				//object recvPacket = methodBytesToPacket.Invoke(null, new object[] { buffer });
				//sessionID = (int)fieldSessionID.GetValue(recvPacket);
				//result = (LoginResult)fieldLoginSuccess.GetValue(recvPacket);
			}
			catch (Exception e)
			{
				isConnected = false;
				isLoggedIn = false;
				sessionID = 0;
				result = LoginResult.ConnectionError;
			}
			HandleLoginResult(result);
			return isLoggedIn;
		}

		public bool SendIsAlive()
		{
			lock (aliveLock)
			{
				try
				{
					object alivePacket = conPacketIsAlive.Invoke(new object[] { sessionID });
					byte[] buffer = (byte[])methodPacketToBytes.Invoke(null, new object[] { alivePacket });
					Send(buffer);
					return true;
				}
				catch (Exception e)
				{
					return false;
				}
			}
		}
		public void SendItemDrop(ACDItem item)
		{
			plugin.LogMessage("Add to send cache");  
			sendCache.Add(item);
		}
		private void SendItemDropEx(ACDItem item)
		{
			try {
	            Trinity.CachedACDItem cItem = Trinity.CachedACDItem.GetCachedItem(item);
				// string json = JsonConvert.SerializeObject(cItem.AcdItem.Stats, Formatting.None,
		  //           	new JsonSerializerSettings()
	   //                      { 
	   //                          ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
	   //                          NullValueHandling = NullValueHandling.Ignore,
	   //                          ContractResolver = new SkipDefaultPropertyValuesContractResolver()
	   //                      });
				// byte[] buffer = GetBytes(json);
				plugin.LogMessage("Serialize");    
				// Send(buffer);   	
			}
			catch(Exception e) {
				plugin.LogMessage("ERROR SENDING ITEM STATS TO SERVER: " + e.Message + "  " + e.StackTrace);
				isConnected = false;
			}
		}

		 public static byte[] Compress(byte[] raw)
	    {
			using (MemoryStream memory = new MemoryStream())
			{
			    using (GZipStream gzip = new GZipStream(memory,
				CompressionMode.Compress, true))
			    {
				gzip.Write(raw, 0, raw.Length);
			    }
			    return memory.ToArray();
			}
	    }

		public void Send(byte[] buffer)
		{
			lock (sendLock)
			{
				connection.GetStream().Write(buffer, 0, buffer.Length);
				plugin.LogMessage("Send");
			}
		}

		public void HandleLoginResult(LoginResult result)
		{
			isLoggedIn = false;
			if (result == LoginResult.Success)
			{
				isLoggedIn = true;
			}
			else if (result == LoginResult.WrongClientVersion)
			{
				plugin.LogMessage("Wrong Plugin Version! Please update your plugin");
			}
			else if (result == LoginResult.WrongApiKey)
			{
				plugin.LogMessage("Wrong API Key");
			}
			else
			{
				plugin.LogMessage("Server not responding!");
			}
		}
		private void ListenProc()
		{
			while (!plugin.isMainWindowClosed())
			{
				string msg = "";
				Thread.Sleep(50);
				if (IsConnected())
				{
					try
					{
						byte[] buffer = new byte[2048];
						connection.Client.Receive(buffer);
						msg = new ASCIIEncoding().GetString(buffer);
						HandleWebRequest(msg);
					}
					catch (Exception e)
					{
						plugin.LogMessage("Listener ERROR   " + e.Message + "    " + e.StackTrace);
						isConnected = false;
						isLoggedIn = false;
						return;
					}
				}
			}
		}
		private void SendProc()
		{
			while (!plugin.isMainWindowClosed())
			{	
				Thread.Sleep(1000);
				if (IsConnected())
				{
					plugin.LogMessage("Iterate over send cache");
					for (int i = 0; i < sendCache.Count; i++)
					{
						ACDItem item = sendCache[i];
						SendItemDropEx(item);
					}
					sendCache = new List<ACDItem>();
				}
			}
		}
		private void HandleWebRequest(string request)
		{
			/*string[] split = request.Split(';');
			if (split.Length < 2)
			return;

			string cmd = split[0];
			int sessionID = Convert.ToInt32(split[1]);
			if (cmd == "COMMAND_START")
			{
				if (!BotMain.IsRunning)
				{
					plugin.LogMessage("Start command from BuddyStats received! Starting the bot...");
					BotMain.Start();
				}
			}
			else if (cmd == "COMMAND_STOP")
			{
				if (BotMain.IsRunning)
				{
					plugin.LogMessage("Stop command from BuddyStats received! Stopping the bot...");
					BotMain.Stop();
					ZetaDia.Service.Party.LeaveGame();
					SendIsAlive();
				}
			}
			else if (cmd == "COMMAND_SCREENSHOT")
			{
				plugin.LogMessage("Screenshot command from BuddyStats received! Sending screenshots...");
				SendScreenShot();
			}
			else if (cmd == "COMMAND_PROFILE_GET")
			{
				plugin.LogMessage("Profile request from BuddyStats received! Sending profile list...");
				SendProfiles();
			}
			else if (cmd == "COMMAND_PROFILE_LOAD" && split.Length == 3)
			{
				plugin.LogMessage("Profile load request from BuddyStats received! Loading new profile...");
				plugin.LoadProfileByIndex(Convert.ToInt32(split[2]));
			}*/
		}
	}

	public class SkipDefaultPropertyValuesContractResolver : DefaultContractResolver
	{
	  protected override JsonProperty CreateProperty(MemberInfo member,
	      MemberSerialization memberSerialization)
	  {
	    JsonProperty property = base.CreateProperty(member, memberSerialization);
	    var memberProp = member as PropertyInfo;
	    var memberField = member as FieldInfo;


	      property.ShouldSerialize = obj =>
	      {
	      	//need to filter by field name, currently doesn't work
	      	object key = memberField != null
	            ? memberField.GetValue(obj)
	            : null;

	        if (key is string) {
	        	if (key.ToString() == "ItemLink") return false;
	        }

	        object value = memberProp != null
	          ? memberProp.GetValue(obj, null)
	          : null;

	        if (value is bool) {
	        	return (bool) value;
	        } else if (value is sbyte
	            || value is byte
	            || value is short
	            || value is ushort
	            || value is int
	            || value is uint
	            || value is long
	            || value is ulong
	            || value is float
	            || value is double
	            || value is decimal
	            || value is string)
	        {
	          string v = value.ToString();
	          return v != "0" && v != "-1" && !string.IsNullOrWhiteSpace(v);
	        }

	        return true;
	      };
	    

	    return property;
	  }
	}
}
