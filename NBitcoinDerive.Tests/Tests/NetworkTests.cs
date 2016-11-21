﻿using NUnit.Framework;
using System;
using NBitcoin;
using NBitcoin.Protocol;
using NBitcoin.Protocol.Behaviors;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Consensus;
using Microsoft.FSharp.Collections;

namespace NBitcoinDerive.Tests
{
	[TestFixture()]
	public class NetworkTests : NetworkTestBase
	{
		[Test()]
		public void CanPassTransactionDemo()
		{
			WithServerSet(2, servers =>
			{
				servers.SeedServerIndex = 0;

				AddressManager serverAddressManager = new AddressManager();
				serverAddressManager.Add(
					new NetworkAddress(servers[0].ExternalEndpoint),
					servers[0].ExternalEndpoint.Address
				);
				serverAddressManager.Connected(new NetworkAddress(servers[0].ExternalEndpoint));

				NodeConnectionParameters serverParameters = new NodeConnectionParameters();
				serverParameters.TemplateBehaviors.Add(new AddressManagerBehavior(serverAddressManager));

				//serverParameters.TemplateBehaviors.Add(new TransactionBehavior());
				serverParameters.TemplateBehaviors.Add(new BroadcastHubBehavior());
				serverParameters.TemplateBehaviors.Add(new SPVBehavior(t => { 
					Console.WriteLine("$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$");
					Console.WriteLine("$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$");
					Console.WriteLine("$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$");
				}));

				servers[0].InboundNodeConnectionParameters = serverParameters;
		//		serverParameters.TemplateBehaviors.Find<TransactionBehavior>().ConnectedNodes = servers[0].ConnectedNodes;


				//servers[0].NodeAdded += (sender, node) =>
				//{
					
				//	Console.WriteLine("YYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYY");
				//	Console.WriteLine("YYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYY");
				//	Console.WriteLine("YYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYY");
				//	Console.WriteLine("YYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYY");
				//	Console.WriteLine("YYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYY");
				//	Console.WriteLine("YYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYY");
				//	Console.WriteLine("YYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYY");

				//};

				#region Setup Parameters for NodeGroup

				AddressManager addressManager = new AddressManager();
				addressManager.PeersToFind = 1;

				NodeConnectionParameters parameters = new NodeConnectionParameters();
				parameters.TemplateBehaviors.Add(new AddressManagerBehavior(addressManager));

				parameters.TemplateBehaviors.Add(new BroadcastHubBehavior());
				parameters.TemplateBehaviors.Add(new SPVBehavior(t =>
				{
					Console.WriteLine("****************************************");
					Console.WriteLine("****************************************");
					Console.WriteLine("****************************************");
				}));
				parameters.AddressFrom = servers[1].ExternalEndpoint;

				NodesGroup nodesGroup = new NodesGroup(servers.Network, parameters);
				nodesGroup.AllowSameGroup = true;
				nodesGroup.MaximumNodeConnection = 1;
			//	nodesGroup.NodeConnectionParameters.TemplateBehaviors.Find<TransactionBehavior>().ConnectedNodes = nodesGroup.ConnectedNodes;

				#endregion

				nodesGroup.Connect();

				//Action<Node> sendTransaction = node =>
				//{
				//	node.SendMessage(new TransactionPayload());// { Transaction = GetNewTransaction() });
				//};

				nodesGroup.ConnectedNodes.Added += (object sender, NodeEventArgs e) =>
				{
						Console.WriteLine("YYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYY");
						Console.WriteLine("YYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYY");
						Console.WriteLine("YYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYY");
						Console.WriteLine("YYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYY");
						Console.WriteLine("YYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYYY");
				};

				Thread.Sleep(9000);  //TODO

				//Assert.IsTrue(nodesGroup.ConnectedNodes.Count == 1);

				//foreach (var node in nodesGroup.ConnectedNodes)
				//{
				//	node.SendMessage(new TransactionPayload());// { Transaction = GetNewTransaction() });
				//}


				var hub = BroadcastHub.GetBroadcastHub(nodesGroup.NodeConnectionParameters);

				Console.WriteLine("^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^");
				hub.BroadcastTransactionAsync(Consensus.Tests.tx);

				Thread.Sleep(9000);  //TODO

				Console.WriteLine("^^^^^******---------********^^^^^^^");

			});
		}

		[Test()]
		public void CanHandshake()
		{
			WithServerSet(2, servers =>
			{
				//var node0address = Handshake(servers[0], servers[1]).MyVersion.AddressFrom;
				//Assert.AreEqual(true, AddressManagerContains(servers[1], node0address));

				Node node0to1 = Handshake(servers[0], servers[1]);
				Thread.Sleep(200);

				Assert.AreEqual(true, AddressManagerContains(servers[1], node0to1.MyVersion.AddressFrom));

				Thread.Sleep(200);
			});
		}

		[Test()]
		public void CanDiscoverPeers()
		{
			int SERVER_COUNT = 4;

			int FIRST_SERVER = 0;
			int SECOND_SERVER = 1;
			int LAST_SERVER = 2;
			int NEW_SERVER = 3;

			var ToBeConnected = new List<NodeServer>();
			var ToBeDiscovered = new List<Node>();

			WithServerSet(SERVER_COUNT, servers =>
			{
				servers.SeedServerIndex = LAST_SERVER; //TODO

				ToBeConnected.Add(servers[FIRST_SERVER]);
				ToBeConnected.Add(servers[SECOND_SERVER]);
				ToBeConnected.Add(servers[LAST_SERVER]);

				ToBeDiscovered.Add(Handshake(servers[FIRST_SERVER], servers[SECOND_SERVER]));
				Trace.Information("Handshake First -> Second");

				ToBeDiscovered.Add(Handshake(servers[SECOND_SERVER], servers[LAST_SERVER]));
				Trace.Information("Handshake Second -> Last");

				#region Setup Parameters for NodeGroup

				AddressManager addressManager = new AddressManager();
				addressManager.PeersToFind = ToBeConnected.Count;

				NodeConnectionParameters parameters = new NodeConnectionParameters();
				parameters.TemplateBehaviors.Add(new AddressManagerBehavior(addressManager));

				parameters.AddressFrom = servers[NEW_SERVER].ExternalEndpoint; //TODO

				NodesGroup nodesGroup = new NodesGroup(servers.Network, parameters);
				nodesGroup.AllowSameGroup = true; //TODO
				nodesGroup.MaximumNodeConnection = ToBeConnected.Count; //TODO

				#endregion

				nodesGroup.Connect();

				int connectedNodesCounter = 0;

				nodesGroup.ConnectedNodes.Added += (object sender, NodeEventArgs e) =>
				{
					Console.WriteLine($"\n\n\nPeer found: {e.Node.Peer.Endpoint}\n\n\n");	
					connectedNodesCounter++;
					Node Node = ToBeDiscovered.Find(node => node.MyVersion.AddressFrom.Equals(e.Node.Peer.Endpoint));

					Assert.IsNotNull(Node);
					ToBeDiscovered.Remove(Node);

					//if (ToBeDiscovered.Count == 0 && ToBeConnected.Count == connectedNodesCounter)
					//{
					//	return;
					//}
				};

				Thread.Sleep(19000);  //TODO

				//throw new Exception();

				Assert.IsEmpty(ToBeDiscovered);
				Assert.AreEqual(ToBeConnected.Count, connectedNodesCounter); 
			});
		}
	}
}
