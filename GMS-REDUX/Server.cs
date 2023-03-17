using System.Net.Sockets;
using System.Net;

namespace GMS_CSharp_Server
{
    public class Server
    {
        public List<SocketHelper>? Clients;
        public List<Lobby>? Lobbies;
        public List<Lobby>? WaitingLobbies;
        public List<Lobby>? ReadyLobbies;
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
            Lobbies = new List<Lobby>();
            ReadyLobbies= new List<Lobby>();
            SearchingClients = new Queue<SocketHelper>();
            WaitingLobbies = new List<Lobby>();

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

            //TCPListener?.Stop();
            //TCPThread?.Interrupt();
            //MatchmakingThread?.Interrupt();

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
            ReadyLobbies?.Clear();
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
				UDPClient.Receive(ref groupEp);

                string[] format = groupEp.ToString()?.Split(':');
				string ClientIPAddress = format?[0];
				string ClientPort = ((IPEndPoint)UDPClient.Client.LocalEndPoint).Port.ToString();

				if (Clients != null)
                foreach (SocketHelper client in Clients){
                    if(client.ClientIPAddress == ClientIPAddress){
                            client.ClientUDPPort = ClientPort;

							Console.WriteLine($"Recieved UDP Data from client: {ClientIPAddress}. " +
                                $"\nThe TCP Port of Client is: {client.ClientPort}. " +
                                $"\nThe UDP Port of Client is: {client.ClientUDPPort} ");
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
                        for(int count = 0; count < WaitingLobbies.Count; count++) 
                        {
                            Lobby current = WaitingLobbies[count];

							if (current.lobbyStatus == "WAITING")
                            {
                                current.AddNonConfPlayer(SearchingClients.Dequeue());
                                count = WaitingLobbies.Count + 1;
                            }
                            
                        }
                    }
				}
            }
			Console.WriteLine("Matchmaking Thread has been cancelled on main server!");

			void CreateNewLobby(SocketHelper client)
            {
                Console.WriteLine("\nCreating a new lobby...");

                Lobby newLobby = new Lobby();
                newLobby.SetupLobby(this);
                newLobby.AddNonConfPlayer(client);

                lock (lockname) 
                {
                    Lobbies?.Add(newLobby);
                    WaitingLobbies.Add(newLobby);
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
                Lobbies?.Remove(lobby);
                if (WaitingLobbies != null && WaitingLobbies.Contains(lobby)) WaitingLobbies?.Remove(lobby);
                else ReadyLobbies?.Remove(lobby);

                Console.WriteLine("Lobby: " + lobby.lobbyId + " Has been removed from server's lists.");
            }
        }

        /// <summary>
        /// Properly moves a lobby from the waiting list to the ready list
        /// </summary>
        public void UpdateLobbyListReady(Lobby lobby) 
        {
            lock (lockname) 
            {
                WaitingLobbies?.Remove(lobby);
                ReadyLobbies?.Add(lobby);
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
            }
			Console.WriteLine("Ping Thread has been cancelled on main server!");
		}
    }
}