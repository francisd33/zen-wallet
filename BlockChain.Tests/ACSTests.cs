using System;
using System.Text;
using NUnit.Framework;

namespace BlockChain
{
	public class ACSTests : BlockChainContractTestsBase
	{
		byte[] compiledContract;

#if CSHARP_CONTRACTS
			string contractCode = @"
    using System;
	using System.Collections.Generic;
	using static Consensus.Types;
    using Microsoft.FSharp.Core;

    public class Test
    {
        public static Tuple<IEnumerable<Outpoint>, IEnumerable<Output>, FSharpOption<ExtendedContract>> run(
			byte[] contractHash,
			SortedDictionary<Outpoint, Output> utxos,
			byte[] message
		) {
			var outpoints = new List<Outpoint>();
			foreach (var item in utxos)
			{
				outpoints.Add(item.Key);
			}

			var outputs = new List<Output>();
			foreach (var item in utxos)
			{
				outputs.Add(item.Value);
			}

			return new Tuple<IEnumerable<Outpoint>, IEnumerable<Output>, FSharpOption<ExtendedContract>>(
				outpoints, outputs, FSharpOption<ExtendedContract>.None
			);
        }
    }";
#else
		string contractCode = @"
module Test
open Consensus.Types
let run (hash : byte[], utxos: Map<Outpoint, Output>, message: byte[]) = (utxos |> Map.toSeq |> Seq.map fst, utxos |> Map.toSeq |> Seq.map snd, Option<ExtendedContract>.None)
";
#endif

		[SetUp]
		public void Setup()
		{
			OneTimeSetUp();

			compiledContract = GetCompliedContract(contractCode);
			BlockChainTrace.SetTag(compiledContract, "Contract");
			var contractLockOutput = Utils.GetContractOutput(compiledContract, null, Consensus.Tests.zhash, 100);
			var tx = Utils.GetTx().AddOutput(contractLockOutput).Tag("Tx");

			Assert.That(_BlockChain.HandleBlock(_GenesisBlock.AddTx(tx).Tag("Genesis")), Is.EqualTo(BlockVerificationHelper.BkResultEnum.Accepted));
		}

		[Test]
		public void ShouldExpireAfterOneBlock()
		{
			AddToACS(compiledContract, contractCode, _GenesisBlock.header.blockNumber + 1);

			var bk = _GenesisBlock;

			using (var dbTx = _BlockChain.GetDBTransaction())
			{
				Assert.That(new ActiveContractSet().IsActive(dbTx, compiledContract), Is.True, "Contract should be active");
			}

			bk = bk.Child();
			_BlockChain.HandleBlock(bk);

			using (var dbTx = _BlockChain.GetDBTransaction())
			{
				Assert.That(new ActiveContractSet().IsActive(dbTx, compiledContract), Is.False, "Contract should not be active");
			}
		}

		[Test]
		public void ShouldExpireAfterTwoBlocks()
		{
			AddToACS(compiledContract, contractCode, _GenesisBlock.header.blockNumber + 2);

			var bk = _GenesisBlock;

			using (var dbTx = _BlockChain.GetDBTransaction())
			{
				Assert.That(new ActiveContractSet().IsActive(dbTx, compiledContract), Is.True, "Contract should be active");
			}

			bk = bk.Child();
			_BlockChain.HandleBlock(bk);

			using (var dbTx = _BlockChain.GetDBTransaction())
			{
				Assert.That(new ActiveContractSet().IsActive(dbTx, compiledContract), Is.True, "Contract should remain active");
			}

			bk = bk.Child();
			_BlockChain.HandleBlock(bk);

			using (var dbTx = _BlockChain.GetDBTransaction())
			{
				Assert.That(new ActiveContractSet().IsActive(dbTx, compiledContract), Is.False, "Contract should not be active");
			}
		}

		[Test]
		public void ShouldExtendContract()
		{
			ACSItem acsItem = null;
			AddToACS(compiledContract, contractCode, _GenesisBlock.header.blockNumber + 1);

			ulong blocksToExtend = 2;

			using (var dbTx = _BlockChain.GetDBTransaction())
			{
				acsItem = new ActiveContractSet().Get(dbTx, compiledContract).Value;
			}

			var output = Utils.GetContractSacrificeLock(compiledContract, acsItem.KalapasPerBlock * blocksToExtend);
			var tx = Utils.GetTx().AddOutput(output);
			var bk = _GenesisBlock.Child().AddTx(tx);

			Assert.That(_BlockChain.HandleBlock(bk), Is.EqualTo(BlockVerificationHelper.BkResultEnum.Accepted), "Should add block");

			using (var dbTx = _BlockChain.GetDBTransaction())
			{
				Assert.That(new ActiveContractSet().IsActive(dbTx, compiledContract), Is.True, "Contract should be active");
			}

			using (var dbTx = _BlockChain.GetDBTransaction())
			{
				var acsItemChanged = new ActiveContractSet().Get(dbTx, compiledContract).Value;

				Assert.That(acsItemChanged.LastBlock - acsItem.LastBlock, Is.EqualTo(blocksToExtend), "Contract should be extended");
			}
		}

		[Test]
		public void ShouldNotExtendInactiveContract()
		{
			ACSItem acsItem = null;
			AddToACS(compiledContract, contractCode, _GenesisBlock.header.blockNumber + 1);

			ulong blocksToExtend = 2;

			using (var dbTx = _BlockChain.GetDBTransaction())
			{
				acsItem = new ActiveContractSet().Get(dbTx, compiledContract).Value;
			}

			var output = Utils.GetContractSacrificeLock(compiledContract, acsItem.KalapasPerBlock * blocksToExtend);
			var tx = Utils.GetTx().AddOutput(output);

			using (var dbTx = _BlockChain.GetDBTransaction())
			{
				Assert.That(new ActiveContractSet().IsActive(dbTx, compiledContract), Is.True, "Should be active");
			}

			var bk = _GenesisBlock.Child();
			_BlockChain.HandleBlock(bk);

			using (var dbTx = _BlockChain.GetDBTransaction())
			{
				Assert.That(new ActiveContractSet().IsActive(dbTx, compiledContract), Is.False, "Should be inactive");
			}

			_BlockChain.HandleBlock(bk.Child().AddTx(tx));

			using (var dbTx = _BlockChain.GetDBTransaction())
			{
				Assert.That(new ActiveContractSet().IsActive(dbTx, compiledContract), Is.False, "Should be inactive");
			}

			using (var dbTx = _BlockChain.GetDBTransaction())
			{

				Assert.That(new ActiveContractSet().Get(dbTx, compiledContract), Is.Null);
			}
		}

		[Test]
		public void ShouldAcceptTxGenereatedByActiveContract()
		{
			AddToACS(compiledContract, contractCode, _GenesisBlock.header.blockNumber + 1);

			var tx = ExecuteContract(compiledContract);

			Assert.That(_BlockChain.HandleBlock(_GenesisBlock.Child().AddTx(tx)), Is.EqualTo(BlockVerificationHelper.BkResultEnum.Accepted));
		}

		[Test]
		public void ShouldAcceptTxGenereatedByActiveContract2()
		{
			AddToACS(compiledContract, contractCode, _GenesisBlock.header.blockNumber + 2);

			var tx = ExecuteContract(compiledContract);

			var bk = _GenesisBlock.Child();
			Assert.That(_BlockChain.HandleBlock(bk), Is.EqualTo(BlockVerificationHelper.BkResultEnum.Accepted));
			Assert.That(_BlockChain.HandleBlock(bk.Child().AddTx(tx)), Is.EqualTo(BlockVerificationHelper.BkResultEnum.Accepted));
		}

		[Test]
		public void ShouldBeOrphanOfInactiveContract()
		{
			AddToACS(compiledContract, contractCode, _GenesisBlock.header.blockNumber);
			BlockChainTrace.SetTag(compiledContract, "contract");
			var tx = ExecuteContract(compiledContract).Tag("tx");

			_BlockChain.HandleBlock(_GenesisBlock.Child());
			Assert.That(_BlockChain.HandleTransaction(tx), Is.EqualTo(BlockChain.TxResultEnum.OrphanIC));
		}

		[Test]
		public void ShouldUndoExtendOnReorder()
		{
			ACSItem acsItem = null;
			AddToACS(compiledContract, contractCode, _GenesisBlock.header.blockNumber + 1);

			ulong blocksToExtend = 20;

			using (var dbTx = _BlockChain.GetDBTransaction())
			{
				acsItem = new ActiveContractSet().Get(dbTx, compiledContract).Value;
			}

			var output = Utils.GetContractSacrificeLock(compiledContract, acsItem.KalapasPerBlock * blocksToExtend);
			var tx = Utils.GetTx().AddOutput(output);
			var bk = _GenesisBlock.Child().AddTx(tx);

			Assert.That(_BlockChain.HandleBlock(bk), Is.EqualTo(BlockVerificationHelper.BkResultEnum.Accepted), "Should add block");

			using (var dbTx = _BlockChain.GetDBTransaction())
			{
				Assert.That(new ActiveContractSet().IsActive(dbTx, compiledContract), Is.True, "Contract should be active");
			}

			using (var dbTx = _BlockChain.GetDBTransaction())
			{
				var acsItemChanged = new ActiveContractSet().Get(dbTx, compiledContract).Value;

				Assert.That(acsItemChanged.LastBlock - acsItem.LastBlock, Is.EqualTo(blocksToExtend), "Contract should be extended");
			}

			var child = _GenesisBlock.Child();
			_BlockChain.HandleBlock(child);
			child = child.Child();
			_BlockChain.HandleBlock(child); // cause reorder

			using (var dbTx = _BlockChain.GetDBTransaction())
			{
				Assert.That(new ActiveContractSet().Get(dbTx, compiledContract), Is.Null);
			}
		}

		//var tx = ExecuteContract(compiledContract);

		//var child = _GenesisBlock.Child();
		//var orphan = child.Child().AddTx(tx);

		//Assert.That(_BlockChain.HandleBlock(orphan), Is.EqualTo(BlockVerificationHelper.BkResultEnum.Accepted));
		//Assert.That(_BlockChain.HandleBlock(child), Is.EqualTo(BlockVerificationHelper.BkResultEnum.Accepted)); // cause an undo

		[Test]
		public void ShouldNotActivateUnderSacrificedContract()
		{
			var kalapasPerBlock = (ulong)contractCode.Length * 1000;
			var tx = Utils.GetTx().AddOutput(Utils.GetContractSacrificeLock(new byte[] { }, kalapasPerBlock)).SetContract(
				new Consensus.Types.Contract(Encoding.ASCII.GetBytes(contractCode), new byte[] { }, new byte[] { }));

			Assert.That(_BlockChain.HandleBlock(_GenesisBlock.Child().AddTx(tx)), Is.EqualTo(BlockVerificationHelper.BkResultEnum.Accepted));

			using (var dbTx = _BlockChain.GetDBTransaction())
			{
				Assert.That(new ActiveContractSet().IsActive(dbTx, compiledContract), Is.False);
			}
		}

		[Test]
		public void ShouldNotActivateHighVersionContract()
		{
			var highVContract = Consensus.Types.ExtendedContract.NewHighVContract(10, new byte[] { });
			var tx = Utils.GetTx().AddOutput(Utils.GetContractSacrificeLock(new byte[] { }, 10)).SetContract(highVContract);

			Assert.That(_BlockChain.HandleBlock(_GenesisBlock.Child().AddTx(tx)), Is.EqualTo(BlockVerificationHelper.BkResultEnum.Accepted));

			using (var dbTx = _BlockChain.GetDBTransaction())
			{
				Assert.That(new ActiveContractSet().IsActive(dbTx, compiledContract), Is.False);
			}
		}

		//cannot specify non-zen amount in SacrificeLock
		//[Test]
		//public void ShouldNotActivateNonZenSacrificedContract()
		//{
		//}

		[Test]
		public void ShouldActivateContractOfSameTx()
		{
			var kalapasPerBlock = (ulong)contractCode.Length * 1000 * 2;
			var tx = Utils.GetTx().AddOutput(Utils.GetContractSacrificeLock(new byte[] { }, kalapasPerBlock)).SetContract(
				new Consensus.Types.Contract(Encoding.ASCII.GetBytes(contractCode), new byte[] { }, new byte[] { }));

			Assert.That(_BlockChain.HandleBlock(_GenesisBlock.Child().AddTx(tx)), Is.EqualTo(BlockVerificationHelper.BkResultEnum.Accepted));

			using (var dbTx = _BlockChain.GetDBTransaction())
			{
				Assert.That(new ActiveContractSet().IsActive(dbTx, compiledContract), Is.True);
			}
		}

		//[Test]
		//public void ShouldActivateReferencesContract()
		//{
		//}
	}
}