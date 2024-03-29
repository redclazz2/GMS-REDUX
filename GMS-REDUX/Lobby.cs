﻿using System.Text.Json;

namespace GMS_CSharp_Server
{
    /// <summary>
    /// Handles sessions of clients.
    /// </summary>
    public class Lobby
    {
        public Thread? ControlThread;
        public List<SocketHelper>? LobbyClients;
        public int lobbyId;
        public String? lobbyStatus;
        public Server? myServer;
        public int maxClients = 2;
        public int confirmationMessages = 0;
        public int lastColorCombination = 0;

        Random rnd = new();
        object lockname = new();

		CancellationTokenSource myCancelSource = new();

		private static Random rng = new Random();

		public static void Shuffle<T>(List<T> list)
		{
			int n = list.Count;
			while (n > 1)
			{
				n--;
				int k = rng.Next(n + 1);
				(list[n], list[k]) = (list[k], list[n]);
			}
		}

		/// <summary>
		/// Lobby's Variables and Lists Init
		/// </summary>
		public void SetupLobby(Server myServer) 
        {
            lobbyStatus = "WAITING";
            this.myServer = myServer;
            lobbyId = rnd.Next(0, 1000);
            LobbyClients = new List<SocketHelper>();
            Console.WriteLine("New lobby created with LobbyId: " + lobbyId);

            //Starts a Control Thread for the lobby
            ControlThread = new Thread(new ThreadStart(delegate
            {
                SortTeams(myCancelSource.Token);
            }));
            ControlThread.Start();
            Console.WriteLine("Lobby control thread started for: " + lobbyId);
        }

        /// <summary>
        /// Lets a client try joining a lobby. If the player is the first to join it wont need confirmation
        /// otherwise, it wont be added to the client list.
        /// </summary>
        public void AddNonConfPlayer(SocketHelper player)
        {
            lock (lockname)
            {
                player.GameLobby = this;
                if (LobbyClients != null)
                    player.ClientNumber = LobbyClients.Count + 1;
                player.IsIngame = true;
                player.IsSearching = false;

                //First to join the lobby
                if (player.ClientNumber == 1)
                {
                    //Tells the player they're joning a new lobby
                    player.p2pConnected = true;
                    sendLobbyData(6);
                    LobbyClients?.Add(player);
                    AddConfirmationMessageValue();
                }
                else
                {
                    //Tells the player they're joining an already populated lobby
                    sendLobbyData(7);

                    //Sends Joining Player's Data to Others
                    BufferStream buffer = new(NetworkConfig.BufferSize, NetworkConfig.BufferAlignment);

                    if (LobbyClients != null)
                        foreach (SocketHelper client in LobbyClients)
                        {
                            Thread.Sleep(100);
                            buffer.Seek(0);
                            buffer.Write((UInt16)11);
                            WriteClientIpPortBuffer(player, buffer);
                            client.SendMessage(buffer);
                        }

                    Thread.Sleep(700);

                    //Sends Joining Player All Other User's Data
                    BufferStream buffer2 = new (NetworkConfig.BufferSize, NetworkConfig.BufferAlignment);

                    buffer2.Seek(0);
                    buffer2.Write((UInt16)9); //CONT
                    buffer2.Write((UInt16)LobbyClients.Count); //TOTAL CLIENTS

                    foreach (SocketHelper client in LobbyClients) //WRITES EVEY IP AND PORT IN LOBBY
                    {
                        WriteClientIpPortBuffer(client, buffer2);
                    }

                    player.SendMessage(buffer2); //SENDS IT

					LobbyClients?.Add(player);
				}
            }

			void sendLobbyData(UInt16 constant_out) 
            {
                Console.WriteLine("Sending Data to: " + player.ClientIPAddress);
                BufferStream buffer = new(NetworkConfig.BufferSize, NetworkConfig.BufferAlignment);
                buffer.Seek(0);
                buffer.Write(constant_out);
                buffer.Write((UInt16)lobbyId);
                player.SendMessage(buffer);
            }
        }

        /// <summary>
        /// Lets a client fully join a lobby since it completed P2P communication.
        /// </summary>
        public void AddConfPlayer(SocketHelper player)
        {
            Monitor.Enter(lockname);
            try
            {
                Console.WriteLine("Added player: " + player.ClientIPAddress + " To lobby: " + lobbyId);
                LobbyClients?.Add(player);
            }
            finally
            {
                Monitor.Exit(lockname);
            }
        }

		/// <summary>
		/// This function checks if the ammount of players in the lobby is lower than
		/// max. If that's the case, the lobby will be re-added to waiting list in server class.
		/// </summary>

		public void CheckForWaitingRegistry()
		{
			lock (lockname)
			{
                if(LobbyClients?.Count < maxClients)
                {
                    myServer?.UpdateLobbyListRedo(this);
				}
			}
		}

		/// <summary>
		/// Adds one to the confirmation message variable
		/// </summary>
		public void AddConfirmationMessageValue()
		{
            lock (lockname) {
                Console.WriteLine("Conf Recieved!");
                confirmationMessages++;
            }
		}

		/// <summary>
		/// Generates Teams And Starts the Match
		/// </summary>
		public void SortTeams(CancellationToken myToken) 
        {
            Thread.Sleep(60);       
            while(lobbyStatus != "READY" && !myToken.IsCancellationRequested) 
            {				
				Monitor.Enter(lockname);
                try {
                    if (LobbyClients != null && LobbyClients.All(SocketHelper => SocketHelper.p2pConnected == true) && LobbyClients.Count == maxClients)
                    {

                        Thread.Sleep(4500);

						Console.WriteLine("SORTING TEAMS!");
						lobbyStatus = "READY";
                        myServer?.UpdateLobbyListReady(this);

                        int colorCombination = rnd.Next(1, 5),
                            musicToPlay = rnd.Next(1, 4),
                            counterTeam1 = 0, counterTeam2 = 0, selector = 0,
                            selectedTeam = 0, selectedPosition = 0, currentTeamDiff = 0;

                        while(colorCombination == lastColorCombination)
                        {
                            colorCombination = rnd.Next(1,5);
                        }

                        lastColorCombination = colorCombination;

                        Shuffle(LobbyClients);

						BufferStream buff = new BufferStream(NetworkConfig.BufferSize, NetworkConfig.BufferAlignment);
						
                        buff.Seek(0);
						buff.Write((UInt16)15);
						buff.Write((UInt16)colorCombination);
						buff.Write((UInt16)musicToPlay);

						string[][] myData = new string[maxClients][];

						for (int i = 0; i < maxClients; i++)
						{
                            currentTeamDiff = counterTeam1 - counterTeam2;

							if (currentTeamDiff == 0) selector = rnd.Next(1, 3);
							else if (currentTeamDiff == -1) selector = 1;
							else if (currentTeamDiff == 1) selector = 2;

							switch (selector)
							{
								case 1:
                                    selectedTeam = 1;
									selectedPosition = counterTeam1;
									counterTeam1++;
                                    
									break;

								case 2:
									selectedTeam = 2;
									selectedPosition = counterTeam2;
									counterTeam2++;
									break;
							}
							myData[i] = new string[] 
                                { LobbyClients[i].ClientIPAddress, LobbyClients[i].ClientUDPPort, selectedTeam.ToString(), selectedPosition.ToString() };
						}

                        string json = JsonSerializer.Serialize(myData);
                        buff.Write(json);

                        foreach(SocketHelper client in LobbyClients)
                        {
                            client.p2pConnected = false;
							client.SendMessage(buff);
						}

                        lobbyStatus = "WAITING";
                    }		
                }
                finally { Monitor.Exit(lockname); }           
            }
            Console.WriteLine("Lobby Control Thread has been properly cancelled.");
        }

        /// <summary>
        /// Removes a player from the lobby and checks to see if GB is needed.
        /// </summary>
        public void RemovePlayer(SocketHelper player) 
        {
            lock (lockname) 
            {
                player.GameLobby = null;
                player.IsIngame = false;
                player.ClientNumber = -1;
                LobbyClients?.Remove(player);

                BufferStream buffer = new(NetworkConfig.BufferSize, NetworkConfig.BufferAlignment);

                if(LobbyClients != null)
                foreach (SocketHelper client in LobbyClients)
                {
                    buffer.Seek(0);
                    buffer.Write((UInt16)14);
					WriteClientIpPortBuffer(player,buffer);
                    client.SendMessage(buffer);
                }

                if (LobbyClients?.Count == 0)
                {
					myCancelSource.Cancel();
					Console.WriteLine("Cancellation Token Active on Lobby: " + lobbyId);
					myServer?.RemoveLobby(this);
				}
            }
        }

        /// <summary>
        /// Writes Client Data (Ip,Port) in a Buffer.
        /// </summary>
        public static void WriteClientIpPortBuffer(SocketHelper client, BufferStream buffer)
        {
            var ip = client.ClientIPAddress;
            var port = client.ClientUDPPort;

            if (ip != null) { buffer?.Write(ip); }
            if (port != null) { buffer?.Write(port); }
            else { buffer?.Write(8056); }
        }
    }
}