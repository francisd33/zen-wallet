﻿using System.Threading;
using Consensus;
using System.Linq;
using System;
using System.Collections.Generic;

namespace NBitcoin.Protocol.Behaviors
{
	public class ChainBehavior : NodeBehavior
	{
		Timer _Refresh;
		BlockChain.BlockChain _BlockChain;
		bool IsTipOld;
		List<Node> Nodes = new List<Node>();

		public ChainBehavior(BlockChain.BlockChain blockChain)
		{
			_BlockChain = blockChain;
			IsTipOld = _BlockChain.IsTipOld;
		}

		protected override void AttachCore()
		{
			lock (Nodes)
			{
				Nodes.Add(AttachedNode);
			}
			AttachedNode.StateChanged += AttachedNode_StateChanged;
			AttachedNode.MessageReceived += AttachedNode_MessageReceived;
		}

		protected override void DetachCore()
		{
			AttachedNode.StateChanged -= AttachedNode_StateChanged;
			AttachedNode.MessageReceived -= AttachedNode_MessageReceived;
		}

		void AttachedNode_StateChanged(Node node, NodeState oldState)
		{
			//TODO: to check: don't send to self

			if (node.State == NodeState.HandShaked && IsTipOld)
			{
				AttachedNode.SendMessageAsync(new GetTipPayload());
			}
		}

		void AttachedNode_MessageReceived(Node node, IncomingMessage message)
		{
			message.IfPayloadIs<Types.Block>(bk =>
			{
				switch (_BlockChain.HandleBlock(bk))
				{
					case BlockChain.BlockVerificationHelper.BkResultEnum.Accepted:
						foreach (var other in Nodes)
						{
							if (other != AttachedNode && other.State == NodeState.HandShaked)
								other.SendMessageAsync(bk);
						}
						break;
					case BlockChain.BlockVerificationHelper.BkResultEnum.AcceptedOrphan:
						node.SendMessageAsync(new GetDataPayload(new InventoryVector[] {
							new InventoryVector(InventoryType.MSG_BLOCK, bk.header.parent)
						}));
						break;
					case BlockChain.BlockVerificationHelper.BkResultEnum.Rejected:
						node.SendMessageAsync(new RejectPayload()
						{
							Hash = Consensus.Merkle.blockHeaderHasher.Invoke(bk.header),
							Code = RejectCode.INVALID,
							Message = "bk"
						});
						break;
				}
			});

			message.IfPayloadIs<GetTipPayload>(getTip =>
			{
				var tip = _BlockChain.Tip;

				if (tip != null)
				{
					var bk = _BlockChain.GetBlock(tip.Key);

					if (bk != null)
					{
						NodeServerTrace.Information("Sending tip: " + System.Convert.ToBase64String(tip.Key));
						node.SendMessageAsync(bk);
					}
				}
				else
				{
					NodeServerTrace.Information("No tip to send");
				}
			});

			message.IfPayloadIs<GetDataPayload>(getData =>
			{
				foreach (var inventory in getData.Inventory.Where(i => i.Type == InventoryType.MSG_BLOCK))
				{
					var bk = _BlockChain.GetBlock(inventory.Hash);

					if (bk != null)
					{
						NodeServerTrace.Information("Sending block: " + bk.header.blockNumber);
						node.SendMessageAsync(bk);
					}
				}
			});
		}

		#region ICloneable Members

		public override object Clone()
		{
			var behavior = new ChainBehavior(_BlockChain);
			behavior.Nodes = Nodes;
			return behavior;
		}

		#endregion
	}
}