using System.Net.Sockets;
using System.Net;
using System.Text;

namespace GMS_CSharp_Server
{
    public class Server
    {
        public List<SocketHelper>? Clients;
        //public List<Lobby>? Lobbies;
        //public List<Lobby>? WaitingLobbies;
        //public List<Lobby>? ReadyLobbies;

        public Dictionary<int, Lobby>? Lobbies;
        public Dictionary<int, Lobby>? WaitingLobbies;

        public Queue<SocketHelper>? SearchingClients;
        Thread? TCPThread;
        Thread? UDPThread;
        Thread? MatchmakingThread;
        Thread? PingThread;
        TcpListener? TCPListener = null;
        UdpClient? UDPClient = null;

        object lockname = new();
		CancellationTokenSource myCancelSource = new CancellationTokenSource();

		/// <summary>
		/// Starts the server.
		/// </summary>
		public void StartServer(int tcpPort)
        {
            //Creates a client list.
            Clients = new List<SocketHelper>();
            Lobbies = new Dictionary<int,Lobby>();
            //ReadyLobbies= new List<Lobby>();
            SearchingClients = new Queue<SocketHelper>();
            WaitingLobbies = new Dictionary<int,Lobby>();

            //Starts a listen thread to listen for connections.
            TCPThread = new Thread(new ThreadStart(delegate
            {
                Listen(tcpPort,myCancelSource.Token);
            }));
            TCPThread.Start();
            Console.WriteLine("TCP Listen thread started.");

            UDPThread = new Thread(new ThreadStart(delegate
            {
                ListenUDP(tcpPort, myCancelSource.Token);    
            }));
            UDPThread.Start();
            Console.WriteLine("UDP Listen thread started.");

            //Starts a matchmaking thread to create lobbies.
            MatchmakingThread = new Thread(new ThreadStart(delegate
            {
                Matchmaking(myCancelSource.Token);
            }));
            MatchmakingThread.Start();
            Console.WriteLine("Matchmaking thread started.");

            //Starts a thread to handle ping to all connected clients
            PingThread = new Thread(new ThreadStart(delegate
            {
                SendPingToAllClients(myCancelSource.Token);
			}));
            PingThread.Start();
            Console.WriteLine("Ping Thread Started.");
        }

        /// <summary>
        /// Stops the server from running.
        /// </summary>
        public void StopServer()
        {
            myCancelSource.Cancel();

            if(Clients != null)
                foreach (SocketHelper client in Clients)
                {
                    client.MscClient?.GetStream().Close();
                    client.MscClient?.Close();
                    client?.DisconnectClient();
                }

            Clients?.Clear();
            Lobbies?.Clear();
            SearchingClients?.Clear();
            WaitingLobbies?.Clear();
            //ReadyLobbies?.Clear();
        }

        /// <summary>
        /// Sends a message out to all connected clients.
        /// </summary>
        public void SendToAllClients(BufferStream buffer)
        {
            if(Clients != null)
                foreach (SocketHelper client in Clients)
                {
                    client.SendMessage(buffer);
                }
        }

        /// <summary>
        /// Listens for clients and starts threads to handle them.
        /// </summary>
        private void Listen(int port, CancellationToken myToken)
        {
            TCPListener = new TcpListener(IPAddress.Any, port);
            TCPListener.Start();

            while (!myToken.IsCancellationRequested)
            {
                Thread.Sleep(10);
                TcpClient tcpClient = TCPListener.AcceptTcpClient();
                Console.WriteLine("\nNew client detected. Connecting client...");
                SocketHelper helper = new SocketHelper();
                helper.StartClient(tcpClient, this);
                Clients?.Add(helper);
            }
            Console.WriteLine("Listen Thread has been cancelled on main server!");
        }

		/// <summary>
		/// Listens for UDP Datagrams and sets proper UDP port to client.
		/// </summary>
		private void ListenUDP(int port, CancellationToken myToken)
		{
			UDPClient = new UdpClient(port);
            IPEndPoint groupEp = new IPEndPoint(IPAddress.Any, port);
			
			while (!myToken.IsCancellationRequested)
			{
                Thread.Sleep(10);
				Console.WriteLine("Waiting for broadcast");
               
				string myData = Encoding.ASCII.GetString(UDPClient.Receive(ref groupEp));
                string[] dataFormat = myData.Split('1',2);
                dataFormat[1] = "1" + dataFormat[1];

				string[] format = groupEp.ToString()?.Split(':',2);
				string ClientIPAddress = format?[0],
                       ClientPort = format?[1];
                
 
				if (Clients != null)
                foreach (SocketHelper client in Clients){
                    if(client.ClientIPAddress == ClientIPAddress){
                        client.ClientUDPPort = ClientPort;

						Console.WriteLine($"Recieved UDP Data from client: {ClientIPAddress}. " +
                            $"\nThe TCP Port of Client is: {client.ClientPort}. " +
                            $"\nThe UDP Port of Client is: {client.ClientUDPPort} ");

						BufferStream buffer = new(NetworkConfig.BufferSize, NetworkConfig.BufferAlignment);
						buffer.Seek(0);
						buffer.Write((UInt16)252);
						buffer.Write(dataFormat[1]);
						client.SendMessage(buffer);
					}
				}
			}
			Console.WriteLine("UDP Listen Thread has been cancelled on main server!");
		}

		/// <summary>
		/// Handles matchmaking between clients searching for games.
		/// </summary>
		public void Matchmaking(CancellationToken myToken)
        {
            while (!myToken.IsCancellationRequested)
            {
                Thread.Sleep(10);
                if (SearchingClients?.Count > 0)
                {
					if (WaitingLobbies?.Count == 0)
                    {
                        CreateNewLobby(SearchingClients.Dequeue());
                    }
                    else
                    {                       
                        foreach(KeyValuePair<int,Lobby> entry in WaitingLobbies)
                        {
							Lobby current = entry.Value;

							if (current.lobbyStatus == "WAITING" && current.LobbyClients?.Count < current.maxClients)
							{
							    current.AddNonConfPlayer(SearchingClients.Dequeue());
                                break;
							}
						}
					}
				}
            }
			Console.WriteLine("Matchmaking Thread has been cancelled on main server!");

			void CreateNewLobby(SocketHelper client)
            {
                Console.WriteLine("\nCreating a new lobby...");

                Lobby newLobby = new();
                newLobby.SetupLobby(this);
                newLobby.AddNonConfPlayer(client);

                lock (lockname) 
                {
                    Lobbies?.Add(newLobby.lobbyId,newLobby);
                    WaitingLobbies.Add(newLobby.lobbyId,newLobby);
                }
            }
        }

        /// <summary>
        /// Adds a player to the matchmaking waiting list
        /// </summary>
        public void AddPlayerToMatchMaking(SocketHelper player) 
        {
            lock (lockname) 
            {
                SearchingClients?.Enqueue(player);
            }
        }

        /// <summary>
        /// Removes a Lobby from the lists in the server.
        /// </summary>
        public void RemoveLobby(Lobby lobby) 
        {
            lock (lockname) 
            {
                Lobbies?.Remove(lobby.lobbyId);
                if (WaitingLobbies != null && WaitingLobbies.ContainsKey(lobby.lobbyId)) WaitingLobbies?.Remove(lobby.lobbyId);
                //else ReadyLobbies?.Remove(lobby);

                Console.WriteLine("Lobby: " + lobby.lobbyId + " Has been removed from server's lists.");
            }
        }

        /// <summary>
        /// Properly moves a lobby from the waiting list
        /// </summary>
        public void UpdateLobbyListReady(Lobby lobby) 
        {
            lock (lockname) 
            {
                WaitingLobbies?.Remove(lobby.lobbyId);
                //ReadyLobbies?.Add(lobby);
            }
        }


		/// <summary>
		/// Properly registers lobby in waiting list
		/// </summary>
		public void UpdateLobbyListRedo(Lobby lobby)
		{
			lock (lockname)
			{
                if (!WaitingLobbies.ContainsKey(lobby.lobbyId))
                {
					WaitingLobbies?.Add(lobby.lobbyId,lobby);
					//ReadyLobbies?.Remove(lobby);
				}			
			}
		}

		/// <summary>
		/// This function sends a ping signal to all clients every 5 seconds.
		/// </summary>
		public void SendPingToAllClients(CancellationToken myToken)
		{
            while (!myToken.IsCancellationRequested){
				Thread.Sleep(6000);
				BufferStream buffer = new BufferStream(NetworkConfig.BufferSize, NetworkConfig.BufferAlignment);
                buffer.Seek(0);
                buffer.Write((UInt16)0);
                SendToAllClients(buffer);
                Console.WriteLine("Ping sent to all clients!");
                Console.WriteLine($"Total Lobbys: {Lobbies.Count}.\nWaiting Lobbies: {WaitingLobbies.Count}.");
            }
			Console.WriteLine("Ping Thread has been cancelled on main server!");
		}
    }
}