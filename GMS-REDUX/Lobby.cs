﻿using System;
using System.CodeDom;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;

namespace GMS_CSharp_Server
{
    /// <summary>
    /// Handles sessions of clients.
    /// </summary>
    public class Lobby
    {
        public Thread? ControlThread;
        public Thread? AbortThread;
        public List<SocketHelper>? LobbyClients;
        public int lobbyId;
        public String? lobbyStatus;
        public Server? myServer;
        public int maxClients = 1;

        Random rnd = new Random();
        static readonly object lockname = new();

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
                SortTeams();
            }));
            ControlThread.Start();
            Console.WriteLine("Lobby control thread started for: " + lobbyId);
        }

        /// <summary>
        /// Handles aborting of threads.
        /// </summary>
        public void Abort()
        {
            //Stops Threads
            ControlThread?.Interrupt();
            Console.WriteLine("Control thread aborted on lobby: " + lobbyId);

            myServer?.RemoveLobby(this);
            GC.Collect();
            AbortThread?.Interrupt();
            
        }

        /// <summary>
        /// Lets a client try joining a lobby. If the player is the first to join it wont need confirmation
        /// otherwise, it wont be added to the client list.
        /// </summary>
        public void AddNonConfPlayer(SocketHelper player) 
        {
            Monitor.Enter(lockname);
            try
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
                    sendLobbyData(6);
                    LobbyClients?.Add(player);
                }
                else
                {
                    //Tells the player they're joining an already populated lobby
                    sendLobbyData(7);

                    //Sends Joining Player's Data to Others
                    BufferStream buffer = new BufferStream(NetworkConfig.BufferSize, NetworkConfig.BufferAlignment);

                    if(LobbyClients != null)
                        foreach (SocketHelper client in LobbyClients)
                        {
                            buffer.Seek(0);
                            buffer.Write((UInt16)11);
                            WriteClientIpPortBuffer(player,buffer);
                            client.SendMessage(buffer);
                        }

                    Thread.Sleep(100);

                    //Sends Joining Player All Other User's Data
                    BufferStream buffer2 = new BufferStream(NetworkConfig.BufferSize, NetworkConfig.BufferAlignment);

                    buffer2.Seek(0);
                    buffer2.Write((UInt16)9); //CONT
                    buffer2.Write((UInt16)LobbyClients.Count); //TOTAL CLIENTS

                    foreach (SocketHelper client in LobbyClients) //WRITES EVEY IP AND PORT IN LOBBY
                    {
                        WriteClientIpPortBuffer(client, buffer2);
                    }

                    player.SendMessage(buffer2); //SENDS IT
                }
            }
            finally
            {
                Monitor.Exit(lockname);
            }

            void sendLobbyData(UInt16 constant_out) 
            {
                Console.WriteLine("Sending Data to: " + player.ClientIPAddress);
                BufferStream buffer = new BufferStream(NetworkConfig.BufferSize, NetworkConfig.BufferAlignment);
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
        /// Generates Teams And Starts the Match
        /// </summary>
        public void SortTeams() 
        {
            Thread.Sleep(60);
            while(lobbyStatus != "READY") 
            {
                Monitor.Enter(lockname);

                if (LobbyClients != null && LobbyClients.Count == maxClients)
                {
                    lobbyStatus = "READY";
                    myServer?.UpdateLobbyListReady(this);

                    int colorCombination = rnd.Next(1, 5),
                        musicToPlay = rnd.Next(1, 3),
                        counterTeam1 = 0, counterTeam2 = 0,
                        n = maxClients / 2;

                    foreach (SocketHelper client in LobbyClients)
                    {
                        var buff = new BufferStream(NetworkConfig.BufferSize, NetworkConfig.BufferAlignment);
                        int selector = rnd.Next(1, 3);

                        buff.Seek(0);
                        buff.Write((UInt16)15);
                        buff.Write((UInt16)colorCombination);
                        buff.Write((UInt16)musicToPlay);

                        if (selector == 1 && counterTeam1 < n)
                        {
                            client.team = 1;
                            client.teamPos = counterTeam1;
                            counterTeam1++;
                        }
                        else if (selector == 2 && counterTeam2 < n)
                        {
                            client.team = 2;
                            client.teamPos = counterTeam2;
                            counterTeam2++;
                        }

                        buff.Write((UInt16)client.team);
                        buff.Write((UInt16)client.teamPos);
                        client.SendMessage(buff);
                    }
					Monitor.Exit(lockname);
					
                }                
            }           
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

                BufferStream buffer = new BufferStream(NetworkConfig.BufferSize, NetworkConfig.BufferAlignment);

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
                    //Starts an abort thread.
                    AbortThread = new Thread(new ThreadStart(delegate
                    {
                        Abort();
                    }));
                    Console.WriteLine("Aborting threads on lobby: " + lobbyId);
                    AbortThread.Start();
                }
            }
        }

        /// <summary>
        /// Writes Client Data (Ip,Port) in a Buffer.
        /// </summary>
        public void WriteClientIpPortBuffer(SocketHelper client, BufferStream buffer)
        {
            buffer?.Write(client?.ClientIPAddress);
            buffer?.Write(client?.ClientPort);
        }
    }
}