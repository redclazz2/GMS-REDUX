using System.Net.Sockets;

namespace GMS_CSharp_Server
{
    /// <summary>
    /// Handles clients. Reads and writes data and stores client information.
    /// </summary>
    public class SocketHelper
    {
        Queue<BufferStream> WriteQueue = new Queue<BufferStream>();
        public Thread? ReadThread;
        public Thread? WriteThread;

        public Server? ParentServer;
        public Lobby? GameLobby;

        public TcpClient? MscClient;

        public string? ClientIPAddress;
        public string? ClientPort;
        public string? ClientUDPPort;
        public string? ClientUsername;

        public bool IsSearching;
        public bool IsIngame;
        //public int team;
        //public int teamPos;
		public int ClientNumber;

		object lockname = new();
        CancellationTokenSource myCancelSource = new CancellationTokenSource();

        /// <summary>
        /// Starts the given client in two threads for reading and writing.
        /// </summary>
        public void StartClient(TcpClient client, Server server)
        {
            //Sets client variable.
            MscClient = client;
            MscClient.SendBufferSize = NetworkConfig.BufferSize;
            MscClient.ReceiveBufferSize = NetworkConfig.BufferSize;

			Socket currentClient = client.Client;
			string[]? format = currentClient.RemoteEndPoint?.ToString()?.Split(':');
            ClientIPAddress = format?[0];
            ClientPort = format?[1];
            ParentServer = server;

            //Starts a read thread.
            ReadThread = new Thread(new ThreadStart(delegate
            {
                Read(client, myCancelSource.Token);
			}));

            ReadThread.Start();
            Console.WriteLine("Client read thread started.");

            //Starts a write thread.
            WriteThread = new Thread(new ThreadStart(delegate
            {
                Write(client, myCancelSource.Token);
			}));
            WriteThread.Start();
            Console.WriteLine("Client write thread started.");

            //Starts Handshake      
            StartHandshake();
        }

        /// <summary>
        /// Provides the Gamemaker Client With it's own information and sets up TCP with the engine
        /// </summary>
        public void StartHandshake()
        {
            Console.WriteLine("\nStarting TCP Handshake with: " + ClientIPAddress);
            BufferStream buffer = new(NetworkConfig.BufferSize, NetworkConfig.BufferAlignment);
            buffer.Seek(0);
            UInt16 constant_out = 254;
            buffer.Write(constant_out);
            SendMessage(buffer);
        }

        /// <summary>
        /// Sends a string message to the client. This message is added to the write queue and send
        /// once it is it's turn. This ensures all messages are send in order they are given.
        /// </summary>
        public void SendMessage(BufferStream buffer)
        {
            lock(lockname)
            WriteQueue.Enqueue(buffer);
        }

        /// <summary>
        /// Disconnects the client from the server and stops all threads for client.
        /// </summary>
        public void DisconnectClient()
        {
            //Console Message.
            Console.WriteLine("\nDisconnecting: " + ClientIPAddress);

            myCancelSource.Cancel();

			//Check if client is ingame.
			if (IsIngame)
            {
                GameLobby?.RemovePlayer(this);
            }

            //Removes client from server.
            ParentServer?.Clients?.Remove(this);

            //Closes Stream.
            MscClient?.Close();

			Console.WriteLine(ClientIPAddress + " disconnected.");
			Console.WriteLine(Convert.ToString(ParentServer?.Clients?.Count) + " clients online.");
		}

        /// <summary>
        /// Writes data to the client in sequence on the server.
        /// </summary>
        public void Write(TcpClient client, CancellationToken myToken)
        {
            while (!myToken.IsCancellationRequested)
            {
                Thread.Sleep(10);
                lock (lockname)
                {
                    if (WriteQueue.Count != 0)
                    {
                        try
                        {
                            BufferStream buffer = WriteQueue.Dequeue();
                            NetworkStream stream = client.GetStream();
                            stream.Write(buffer.Memory, 0, buffer.Iterator);
                            stream.Flush();
                        }
                        catch (System.IO.IOException)
                        {
                            DisconnectClient();
                        }
                        catch (NullReferenceException)
                        {
                            DisconnectClient();
                        }
                        catch (ObjectDisposedException)
                        {

                        }
                        catch (System.InvalidOperationException)
                        {

                        }
					}
                }
            }
			Console.WriteLine("Write Thread Cancelled Petition Processed on Client : " + ClientIPAddress);
		}

        /// <summary>
        /// Reads data from the client and sends back a response.
        /// </summary>
        public void Read(TcpClient client, CancellationToken myToken)
        {
            while (!myToken.IsCancellationRequested)
            {
                try
                {
                    Thread.Sleep(10);
                    BufferStream readBuffer = new(NetworkConfig.BufferSize, 1);
                    NetworkStream stream = client.GetStream();
                    stream.Read(readBuffer.Memory, 0, NetworkConfig.BufferSize);

					//Read the header data.
					readBuffer.Read(out ushort constant);

					//Determine input commmand.
					switch (constant)
                    {
                        //Complete Client's Handshake
                        case 255:
                            {
                                Console.WriteLine("TCP Handshake with: " + ClientIPAddress + " Completed, Client has been connected.");
                                Console.WriteLine(Convert.ToString(ParentServer?.Clients?.Count) + " clients online.");
                                Console.WriteLine("Sending Client Data To GameMaker...");

                                BufferStream buffer = new (NetworkConfig.BufferSize, NetworkConfig.BufferAlignment);

                                buffer.Seek(0);
                                buffer.Write((UInt16)253);
                                buffer.Write(ClientIPAddress);
                                buffer.Write(ClientPort);
                                SendMessage(buffer);

								Console.WriteLine("Client Data has been sent to GameMaker!");
								break;
                            }

                        //Matchmaking requested by client
                        case 3:
                            {
                                lock (lockname)
                                {
                                    ParentServer?.AddPlayerToMatchMaking(this);
                                    IsSearching = true;
                                    IsIngame = false;

                                    //Send matchmaking confirmed by server to gm
                                    BufferStream buffer = new(NetworkConfig.BufferSize, NetworkConfig.BufferAlignment);
                                    buffer.Seek(0);
                                    buffer.Write((UInt16)4);
                                    SendMessage(buffer);

									Console.WriteLine("\nMatchmaking request received from: " + ClientIPAddress + ". Client was added to queue.");
									break;
                                }
                            }

                        case 12:
                            {
                                //Confirm responses by client
                                Console.WriteLine("Updating Lobby Information for: " + ClientIPAddress);
                                GameLobby?.AddConfPlayer(this);

                                BufferStream buffer = new BufferStream(NetworkConfig.BufferSize, NetworkConfig.BufferAlignment);
                                buffer.Seek(0);
                                buffer.Write((UInt16)13);
                                SendMessage(buffer);
                                break;
                            }

                        case 13:
                            {
                                readBuffer.Read(out ClientUsername);
                                Console.WriteLine($"Username has been read from: {ClientIPAddress} as: {ClientUsername}!");
                                break;
                            }

                        case 8:
                            {
                                DisconnectClient();
                                break;
                            }
                    }
                }
                catch (System.IO.IOException)
                {
                    DisconnectClient();
                }
                catch (NullReferenceException)
                {
                    DisconnectClient();
                }
                catch (ObjectDisposedException)
                {
                    //Do nothing.
                }
			}
			Console.WriteLine("Read Thread Cancelled Petition Processed on Client : " + ClientIPAddress);
		}
    }
}
