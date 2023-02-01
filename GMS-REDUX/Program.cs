using System;

namespace GMS_CSharp_Server
{
	class Program
	{
		//Made by: Redclazz2 Based on GoodPie's work on Python server.
		//Special Thanks to: FatalSheep and CinderFire for GML and C# compatibility.
		static void Main()
		{
			//Starts the server.

			Console.WriteLine("Made By: Redclazz2. Based on GoodPie's work on Python Server.\n" +
				"Special Thanks to: FatalSheep for BufferStream Class and CinderFire for basic server architecture.\n");

			Console.WriteLine("Starting Server...");
			Server server = new Server();
			server.StartServer(8056);
			Console.WriteLine("Server Started!");
		}
	}
}
