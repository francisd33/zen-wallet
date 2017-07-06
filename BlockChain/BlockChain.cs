using System;
using Consensus;
using BlockChain.Store;
using Store;
using Infrastructure;
using System.Collections.Generic;
using Microsoft.FSharp.Collections;
using System.Linq;
using BlockChain.Data;
using System.Threading.Tasks.Dataflow;
using System.Threading.Tasks;
using System.Collections;
using Microsoft.FSharp.Core;
using System.Text;
using static BlockChain.BlockVerificationHelper;

namespace BlockChain
{
    using TransactionSkeleton = Tuple<FSharpList<Types.Outpoint>, FSharpList<Types.Output>, byte[]>;
    using ContractFunction = FSharpFunc<Tuple<byte[], byte[], FSharpFunc<Types.Outpoint, FSharpOption<Types.Output>>>, Tuple<FSharpList<Types.Outpoint>, FSharpList<Types.Output>, byte[]>>;
    //CHANGE
    using ContractUtxoGetter = FSharpFunc<Types.Outpoint, FSharpOption<Types.Output>>;
    using ContractFunctionInput = Tuple<byte[], byte[], FSharpFunc<Types.Outpoint, FSharpOption<Types.Output>>>;

    public class BlockChain : ResourceOwner
    {
#if TEST
        const int COINBASE_MATURITY = 0;
#else
		const int COINBASE_MATURITY = 100;
#endif

        readonly TimeSpan OLD_TIP_TIME_SPAN = TimeSpan.FromMinutes(5);

        DBContext _DBContext = null;

        public MemPool memPool { get; set; }
        public UTXOStore UTXOStore { get; set; }
        public ActiveContractSet ActiveContractSet { get; set; }
        public BlockStore BlockStore { get; set; }
        public BlockNumberDifficulties BlockNumberDifficulties { get; set; }
        public ChainTip ChainTip { get; set; }
        public BlockTimestamps Timestamps { get; set; }
        public byte[] GenesisBlockHash { get; set; }

        public enum TxResultEnum
        {
            Accepted,
            OrphanMissingInputs, // don't ban peer
            OrphanIC, // don't ban peer
            Invalid, // ban peer or inform wallet
            DoubleSpend, //don't ban peer
            Known
        }

        public enum IsTxOrphanResult
        {
            NotOrphan,
            Orphan,
            Invalid,
        }

        public enum IsContractGeneratedTxResult
        {
            NotContractGenerated,
            ContractGenerated,
            Invalid,
        }

        public BlockChain(string dbName, byte[] genesisBlockHash)
        {
            memPool = new MemPool();
            UTXOStore = new UTXOStore();
            ActiveContractSet = new ActiveContractSet();
            BlockStore = new BlockStore();
            BlockNumberDifficulties = new BlockNumberDifficulties();
            ChainTip = new ChainTip();
            Timestamps = new BlockTimestamps();
            GenesisBlockHash = genesisBlockHash;

            _DBContext = new DBContext(dbName);
            OwnResource(_DBContext);

            using (var context = _DBContext.GetTransactionContext())
            {
                var chainTip = ChainTip.Context(context).Value;

                //TODO: check if makred as main?
                Tip = chainTip == null ? null : BlockStore.GetBlock(context, chainTip);

                if (Tip != null)
                    BlockChainTrace.Information("Tip's block number is " + Tip.Value.header.blockNumber);
                else
                    BlockChainTrace.Information("No tip.");

                InitBlockTimestamps(context);
            }

            var listener = new EventLoopMessageListener<QueueAction>(HandleQueueAction, "BlockChain listener");

            MessageProducer<QueueAction>.Instance.AddMessageListener(listener);

            OwnResource(listener);
        }

        void HandleQueueAction(QueueAction action)
        {
            try
            {
                //((dynamic)this).Handle((dynamic)action);

                if (action is HandleBlockAction)
                    ((HandleBlockAction)action).SetResult(HandleBlock(action as HandleBlockAction));
                else if (action is GetActiveContactsAction)
                    ((GetActiveContactsAction)action).SetResult(GetActiveContacts());
                else if (action is GetContractPointedOutputsAction)
                    ((GetContractPointedOutputsAction)action).SetResult(GetContractPointedOutputs(
                        ((GetContractPointedOutputsAction)action).ContractHash));
                else if (action is HandleOrphansOfTxAction)
                    HandleOrphansOfTransaction(action as HandleOrphansOfTxAction);
                else if (action is GetIsContractActiveAction)
                    ((GetIsContractActiveAction)action).SetResult(IsContractActive(
                        ((GetIsContractActiveAction)action).ContractHash));
                //else if (action is GetUTXOAction)
                //((GetUTXOAction)action).SetResult(GetUTXO(((GetUTXOAction)action).Outpoint, ((GetUTXOAction)action).IsInBlock));
                else if (action is GetIsConfirmedUTXOExistAction)
                {
                    var outpoint = ((GetIsConfirmedUTXOExistAction)action).Outpoint;
                    ((GetIsConfirmedUTXOExistAction)action).SetResult(IsConfirmedUTXOExist(outpoint));
                }
                else if (action is GetContractCodeAction)
                    ((GetContractCodeAction)action).SetResult(GetContractCode(
                        ((GetContractCodeAction)action).ContractHash));
                else if (action is HandleTransactionAction)
                    ((HandleTransactionAction)action).SetResult(HandleTransaction(((HandleTransactionAction)action).Tx));
                else if (action is GetBlockAction)
                    ((GetBlockAction)action).SetResult(GetBlock(((GetBlockAction)action).BkHash));
                else if (action is GetTxAction)
                    ((GetTxAction)action).SetResult(GetTransaction(((GetTxAction)action).TxHash));
                else if (action is ExecuteContractAction)
                {
                    var executeContractAction = ((ExecuteContractAction)action);
                    Types.Transaction tx;
                    var result = AssembleAutoTx(executeContractAction.ContractHash, executeContractAction.Message, out tx, executeContractAction.Message != null);
                    ((ExecuteContractAction)action).SetResult(new Tuple<bool, Types.Transaction>(result, tx));
                }
                else if (action is GetUTXOSetAction)
                {
                    var getUTXOSetAction = (GetUTXOSetAction)action;
                    HashDictionary<List<Types.Output>> txOutputs;
                    HashDictionary<Types.Transaction> txs;
                    GetUTXOSet(getUTXOSetAction.Predicate, out txOutputs, out txs);
                    getUTXOSetAction.SetResult(new Tuple<HashDictionary<List<Types.Output>>, HashDictionary<Types.Transaction>>(txOutputs, txs));
                }
#if TEST
                else if (action is GetBlockLocationAction)
                {
                    using (var dbTx = _DBContext.GetTransactionContext())
                    {
                        ((GetBlockLocationAction)action).SetResult(BlockStore.GetLocation(dbTx, ((GetBlockLocationAction)action).Block));
                    }
                }
#endif
            }
			catch (Exception e)
			{
				BlockChainTrace.Error("BlockChain handler", e);
			}
		}

		void HandleOrphansOfTransaction(HandleOrphansOfTxAction a)
		{
			using (var dbTx = _DBContext.GetTransactionContext())
			{
				lock (memPool)
				{
                    memPool.OrphanTxPool.GetDependencies(a.TxHash).ToList().ForEach(t =>
					{
                        var tx = memPool.OrphanTxPool[t.Item2];

                        memPool.OrphanTxPool.Remove(t.Item2);
                        new HandleTransactionAction {Tx = tx }.Publish();
					});
				}
			}
		}

		/// <summary>
		/// Handles a new transaction from network or wallet. 
		/// </summary>
		TxResultEnum HandleTransaction(Types.Transaction tx)
		{
			using (var dbTx = _DBContext.GetTransactionContext())
			{
				TransactionValidation.PointedTransaction ptx;
				var txHash = Merkle.transactionHasher.Invoke(tx);

				lock (memPool)
				{
					if (memPool.TxPool.Contains(txHash) || memPool.ICTxPool.Contains(txHash) || memPool.OrphanTxPool.Contains(txHash))
					{
						BlockChainTrace.Information("Tx already in mempool", txHash);
						return TxResultEnum.Known;
					}

					if (BlockStore.TxStore.ContainsKey(dbTx, txHash))
					{
						BlockChainTrace.Information("Tx already in store", txHash);
						return TxResultEnum.Known;
					}

					if (memPool.TxPool.ContainsInputs(tx))
					{
						BlockChainTrace.Information("Mempool contains spending input", tx);
						return TxResultEnum.DoubleSpend;
					}

					switch (IsOrphanTx(dbTx, tx, out ptx))
					{
						case IsTxOrphanResult.Orphan:
							BlockChainTrace.Information("tx added as orphan", tx);
							memPool.OrphanTxPool.Add(txHash, tx);
							return TxResultEnum.OrphanMissingInputs;
						case IsTxOrphanResult.Invalid:
							BlockChainTrace.Information("tx contains invalid reference(s)", tx);
							return TxResultEnum.Invalid;
					}

                    if (IsCoinbaseTx(ptx))
                    {
						BlockChainTrace.Information("tx is coinbase", tx);
						return TxResultEnum.Invalid;
                    }

					if (!IsReferencedCoinbaseTxsValid(dbTx, ptx))
					{
						BlockChainTrace.Information("referenced coinbase immature", tx);
						return TxResultEnum.Invalid;
					}

					//if (!IsStructurallyValidTx(dbTx, ptx))
					//{
					//	BlockChainTrace.Information("tx invalid - structural", ptx);
					//	return TxResultEnum.Invalid;
					//}

					byte[] contractHash;
					switch (IsContractGeneratedTx(ptx, out contractHash))
					{
						case IsContractGeneratedTxResult.ContractGenerated:
							if (!ActiveContractSet.IsActive(dbTx, contractHash))
							{
								BlockChainTrace.Information("tx added to ICTx mempool", tx);
								BlockChainTrace.Information(" of contract", contractHash);
								memPool.TxPool.ICTxPool.Add(txHash, ptx);
								return TxResultEnum.OrphanIC;
							}

            				var contractUtxoGetter = FactorContractUtxoGetter(dbTx, false, ptx);
                            var acsItem = ActiveContractSet.Get(dbTx, contractHash);
							var contractFunction = ContractExamples.Execution.deserialize(acsItem.Value.CompiledContract);

                            if (!IsValidAutoTx(ptx, contractUtxoGetter, contractHash, contractFunction))
							{
								BlockChainTrace.Information("auto-tx invalid", ptx);
								return TxResultEnum.Invalid;
							}
							break;
						case IsContractGeneratedTxResult.Invalid:
							BlockChainTrace.Information("tx invalid - input locks", tx);
							return TxResultEnum.Invalid;
                        case IsContractGeneratedTxResult.NotContractGenerated: // assume user generated
                            if (!IsValidUserGeneratedTx(dbTx, ptx))
                            {
								BlockChainTrace.Information("tx invalid - input locks", tx);
								return TxResultEnum.Invalid;
							}
                            break;
					}

					BlockChainTrace.Information("tx added to mempool", ptx);
					memPool.TxPool.Add(txHash, ptx);
				}

				return TxResultEnum.Accepted;
			}
		}

        bool IsCoinbaseTx(TransactionValidation.PointedTransaction ptx)
        {
            return ptx.pInputs.Any(t => t.Item2.@lock is Types.OutputLock.CoinbaseLock);
        }

        bool IsReferencedCoinbaseTxsValid(TransactionContext dbTx, TransactionValidation.PointedTransaction ptx)
		{
			var currentHeight = Tip == null ? 0 : Tip.Value.header.blockNumber;

			foreach (var refTx in ptx.pInputs.Select(t => t.Item1.txHash))
			{
				Types.BlockHeader refTxBk;
				if (BlockStore.IsCoinbaseTx(dbTx, refTx, out refTxBk))
				{
					if (refTxBk.blockNumber - currentHeight < COINBASE_MATURITY)
					{
						return false;
					}
				}
			}

			return true;
		}

		public Task<BkResult> HandleBlock(Types.Block bk)
		{
			return new HandleBlockAction(bk).Publish();
		}

		BlockVerificationHelper.BkResult HandleBlock(HandleBlockAction a)
		{
			BlockVerificationHelper action = null;

			using (var dbTx = _DBContext.GetTransactionContext())
			{
				action = new BlockVerificationHelper(
					this,
					dbTx,
					a.BkHash,
					a.Bk,
					a.IsOrphan
				);

				switch (action.Result.BkResultEnum)
				{
					case BlockVerificationHelper.BkResultEnum.AcceptedOrphan:
						dbTx.Commit();
						break;
					case BlockVerificationHelper.BkResultEnum.Accepted:
                        if (action.ConfirmedTxs.Any() || action.UnconfirmedTxs.Any()) 
                        {
    						UpdateMempool(dbTx, action.ConfirmedTxs, action.UnconfirmedTxs);
                        }
                        else
                        {
                            dbTx.Commit();
                        }
                        BlockStore.Orphans(dbTx, a.BkHash).ToList().ForEach(t => new HandleBlockAction(t.Key, t.Value, true).Publish());
						break;
					case BlockVerificationHelper.BkResultEnum.Rejected:
						return action.Result;
				}
			}

			action.QueueActions.ForEach(t =>
			{
				if (t is MessageAction)
					(t as MessageAction).Message.Publish();
				else
					t.Publish();
			});

			return action.Result;
		}

		void UpdateMempool(TransactionContext dbTx, HashDictionary<TransactionValidation.PointedTransaction> confirmedTxs, HashDictionary<Types.Transaction> unconfirmedTxs)
		{
            lock (memPool)
            {
                dbTx.Commit();

                var activeContracts = new HashDictionary<byte[]>();

                foreach (var item in ActiveContractSet.All(dbTx))
                {
                    activeContracts[item.Item1] = item.Item2.CompiledContract;
                }

                foreach (var item in memPool.ContractPool)
                {
                    activeContracts[item.Key] = item.Value.CompiledContract;
                }

                RemoveConfirmedTxsFromMempool(confirmedTxs);

                MakeOrphan(dbTx);

                memPool.TxPool.MoveToICTxPool(activeContracts);

                RemoveInvalidAutoTxs(dbTx);

                foreach (var t in unconfirmedTxs)
                {
                    new HandleTransactionAction { Tx = t.Value }.Publish();
                }

                memPool.ICTxPool.Where(t =>
                {
                    byte[] contractHash;
                    IsContractGeneratedTx(t.Value, out contractHash);

                    return activeContracts.ContainsKey(contractHash);
                })
               .ToList().ForEach(t =>
               {
                   memPool.ICTxPool.Remove(t.Key);
                   new HandleTransactionAction { Tx = TransactionValidation.unpoint(t.Value) }.Publish();
               });
            }
		}

        void MakeOrphan(TransactionContext dbTx)
        {
            Func<KeyValuePair<byte[], TransactionValidation.PointedTransaction>, bool> p = t =>
			{
				TransactionValidation.PointedTransaction ptx;
				var tx = TransactionValidation.unpoint(t.Value);
				return IsOrphanTx(dbTx, tx, out ptx) == IsTxOrphanResult.Orphan;
			};

            memPool.TxPool.Where(p).Select(t => t.Key).ToList().ForEach(memPool.TxPool.MoveToOrphansWithDependencies);
			memPool.ICTxPool.Where(p).Select(t => t.Key).ToList().ForEach(memPool.ICTxPool.MoveToOrphansWithDependencies);
		}

		void RemoveInvalidAutoTxs(TransactionContext dbTx)
		{
            foreach (var item in memPool.TxPool.ToList())
			{
                var ptx = item.Value;
				byte[] contractHash;

                if (IsContractGeneratedTx(ptx, out contractHash) == IsContractGeneratedTxResult.ContractGenerated)
                {
					var contractUtxoGetter = FactorContractUtxoGetter(dbTx, false, ptx);
					var acsItem = ActiveContractSet.Get(dbTx, contractHash);
					var contractFunction = ContractExamples.Execution.deserialize(acsItem.Value.CompiledContract);

                    if (!IsValidAutoTx(ptx, contractUtxoGetter, contractHash, contractFunction))
					{
						BlockChainTrace.Information("invalid auto-tx removed from mempool", item.Value);
						memPool.TxPool.RemoveWithDependencies(item.Key);
					}
				}
			}
		}

		void RemoveConfirmedTxsFromMempool(HashDictionary<TransactionValidation.PointedTransaction> confirmedTxs)
		{
			//foreach (var ptx in confirmedTxs.Values)
			//{
			//	// Make list of **keys** in txpool and ictxpool
			//	// for each key in list, check if Double Spent. Remove recursively.
			//	// RemoveIfDoubleSpent is recursive over all pools, then sends a RemoveRef to ContractPool
			//	memPool.TxPool.RemoveDoubleSpends(spentOutputs);
			//	memPool.ICTxPool.RemoveDoubleSpends(spentOutputs);

			//	spentOutputs.AddRange(ptx.pInputs.Select(t => t.Item1));
			//}


			foreach (var item in confirmedTxs)
			{
				if (memPool.TxPool.Contains(item.Key))
				{
					memPool.TxPool.Remove(item.Key);
					memPool.ContractPool.Remove(item.Key);
				}
                else if (memPool.ICTxPool.Contains(item.Key))
                {
                    memPool.ICTxPool.Remove(item.Key);
					new HandleOrphansOfTxAction(item.Key).Publish(); // assume tx is unseen. try to unorphan
				}
                else if (memPool.OrphanTxPool.ContainsKey(item.Key))
                {
                    memPool.OrphanTxPool.Remove(item.Key);
					new HandleOrphansOfTxAction(item.Key).Publish(); // assume tx is unseen. try to unorphan
				}
                else 
                {
					memPool.RemoveDoubleSpends(item.Value.pInputs.Select(t => t.Item1));
					new HandleOrphansOfTxAction(item.Key).Publish(); // assume tx is unseen. try to unorphan
				}

				//new TxMessage(item.Key, item.Value, TxStateEnum.Confirmed).Publish();
			}
		}

		public IsTxOrphanResult IsOrphanTx(TransactionContext dbTx, Types.Transaction tx, out TransactionValidation.PointedTransaction ptx)
		{
			var outputs = new List<Types.Output>();

			ptx = null;

			foreach (Types.Outpoint input in tx.inputs)
			{
				if (UTXOStore.ContainsKey(dbTx, input))
				{
					outputs.Add(UTXOStore.Get(dbTx, input).Value);
				}
				else if (memPool.TxPool.Contains(input.txHash))
				{
					if (input.index < memPool.TxPool[input.txHash].outputs.Length)
					{
						outputs.Add(memPool.TxPool[input.txHash].outputs[(int)input.index]);
					}
					else
					{
						BlockChainTrace.Information("can't construct ptx", tx);
						return IsTxOrphanResult.Invalid;
					}
				}
			}

			if (outputs.Count < tx.inputs.Count())
			{
				return IsTxOrphanResult.Orphan;
			}

			ptx = TransactionValidation.toPointedTransaction(
				tx,
				ListModule.OfSeq<Types.Output>(outputs)
			);

			return IsTxOrphanResult.NotOrphan;
		}

		public static bool IsValidUserGeneratedTx(TransactionContext dbTx, TransactionValidation.PointedTransaction ptx)
		{
			//Verify crypto signatures for each input; reject if any are bad


			//Using the referenced output transactions to get input values, check that each input value, as well as the sum, are in legal money range
			//Reject if the sum of input values < sum of output values


			//for (var i = 0; i < ptx.pInputs.Length; i++)
			//{
			//	if (!TransactionValidation.validateAtIndex(ptx, i))
			//		return false;
			//}

			return true;
		}

        //Types.Output GetUTXO(Types.Outpoint outpoint, bool IsInBlock)
        //{
        //	using (TransactionContext dbTx = _DBContext.GetTransactionContext())
        //	{
        //		return GetUTXO(outpoint, dbTx, IsInBlock);
        //	}
        //}

        public ContractUtxoGetter FactorContractUtxoGetter(TransactionContext dbTx, bool isInBlock, TransactionValidation.PointedTransaction ptx = null)
        {
            return ContractUtxoGetter.FromConverter(t =>
            {
                var output = GetUTXO(t, dbTx, isInBlock, ptx);
                return output == null ? FSharpOption<Types.Output>.None : new FSharpOption<Types.Output>(output);
            });
        }

		Types.Output GetUTXO(Types.Outpoint outpoint, TransactionContext dbTx, bool isInBlock, TransactionValidation.PointedTransaction validatingPtx = null)
		{
			try
			{
				if (!isInBlock)
				{
					foreach (var item in memPool.TxPool)
					{
						foreach (var pInput in item.Value.pInputs)
						{
                            if (outpoint.Equals(pInput.Item1))
								return null;
						}
					}
				}

				var result = UTXOStore.Get(dbTx, outpoint);

                if (result != null)
                {
                    return result.Value;
                }

                if (isInBlock)
                {
                    return null;
                }

                if (validatingPtx == null && memPool.TxPool.Contains(outpoint.txHash))
				{
					var tx = memPool.TxPool[outpoint.txHash];

					if (tx.outputs.Count() > outpoint.index)
					{
						return tx.outputs[(int)outpoint.index];
					}
				}

                if (validatingPtx != null) {
                    foreach (var pInput in validatingPtx.pInputs)
                    {
                        if (outpoint.Equals(pInput.Item1))
                            return pInput.Item2;
                    }
                }
			}
			catch (Exception e)
			{
				BlockChainTrace.Error("GetUTXO", e);
			}

			return null;
		}

        public static bool IsValidAutoTx(TransactionValidation.PointedTransaction ptx, ContractUtxoGetter contractUtxoGetter, byte[] contractHash, ContractFunction contractFunction)
		{
			var isWitness = false;
			var witnessIdx = -1;
			byte[] message = null;

			for (var i = 0; i < ptx.witnesses.Length; i++)
			{
				if (ptx.witnesses[i].Length > 0)
					witnessIdx = i;
			}

			if (witnessIdx == 0)
			{
				message = ptx.witnesses[0];
			}
			else if (witnessIdx == -1)
			{
				var contractLock = ptx.pInputs[0].Item2.@lock as Types.OutputLock.ContractLock;

				if (contractLock == null)
				{
					BlockChainTrace.Information("expected ContractLock, tx invalid");
					return false;
				}

				message = contractLock.data;
			}

			isWitness = witnessIdx == 0;

            Types.Transaction tx;

            var isExecutionSuccessful = ExecuteContract(
                contractHash,
                contractFunction,
                message,
                out tx, 
                contractUtxoGetter,
                isWitness
            );

			return isExecutionSuccessful && tx != null && TransactionValidation.unpoint(ptx).Equals(tx);
		}

		public static IsContractGeneratedTxResult IsContractGeneratedTx(TransactionValidation.PointedTransaction ptx, out byte[] contractHash)
		{
			contractHash = null;

			foreach (var input in ptx.pInputs)
			{
				if (input.Item2.@lock.IsContractLock)
				{
					if (contractHash == null)
						contractHash = ((Types.OutputLock.ContractLock)input.Item2.@lock).contractHash;
					else if (!contractHash.SequenceEqual(((Types.OutputLock.ContractLock)input.Item2.@lock).contractHash))
						return IsContractGeneratedTxResult.Invalid;

					else if (!contractHash.SequenceEqual(((Types.OutputLock.ContractLock)input.Item2.@lock).contractHash))
					{
						BlockChainTrace.Information("Unexpected contactHash", contractHash);
						return IsContractGeneratedTxResult.Invalid;
					}
				}
			}

			return contractHash == null ? IsContractGeneratedTxResult.NotContractGenerated : IsContractGeneratedTxResult.ContractGenerated;
		}


		// TODO replace with two functions:
		// IsContractActive(contractHash), which checks if the contract is in the ACS on disk or in the contractpool;
		// bool IsContractGeneratedTransactionValid(dbtx, contracthash, ptx), which raises an exception if called with a missing contract
		//public static bool IsContractGeneratedTransactionValid(TransactionContext dbTx, TransactionValidation.PointedTransaction ptx, byte[] contractHash)
		//{
		//	var chainTip = new ChainTip().Context(dbTx).Value;
		//	var tipBlockHeader = chainTip == null ? null : new BlockStore().GetBlock(dbTx, chainTip).Value.header;
		//	return xValid(ptx, contractHash, utxos, tipBlockHeader);
		//}

        public void InitBlockTimestamps(TransactionContext dbTx)
		{
			if (Tip != null)
			{
				var timestamps = new List<long>();
				var itr = Tip.Value;

				while (itr != null && timestamps.Count < BlockTimestamps.SIZE)
				{
					timestamps.Add(itr.header.timestamp);

                    if (itr.header.parent.Length == 0)
                    {
                        break;
                    }

                    var bk = BlockStore.GetBlock(dbTx, itr.header.parent);

                    System.Diagnostics.Debug.Assert(bk != null);

                    itr = bk.Value;
				}
				Timestamps.Init(timestamps.ToArray());
			}
		}

		public bool IsTipOld
		{
			get
			{
				return Tip == null ? true : DateTime.Now.ToUniversalTime() - DateTime.FromBinary(Tip.Value.header.timestamp) > OLD_TIP_TIME_SPAN;
			}
		}

		//TODO: refactor
		public Keyed<Types.Block> Tip { get; set; }

		Types.Transaction GetTransaction(byte[] key) //TODO: make concurrent
		{
			if (memPool.TxPool.Contains(key))
			{
				return TransactionValidation.unpoint(memPool.TxPool[key]);
			}
			else
			{
				using (TransactionContext context = _DBContext.GetTransactionContext())
				{
					if (BlockStore.TxStore.ContainsKey(context, key))
					{
						return BlockStore.TxStore.Get(context, key).Value;
					}
				}
			}

			return null;
		}

		//TODO: should asset that the block came from main?
		Types.Block GetBlock(byte[] key)
		{
			using (TransactionContext context = _DBContext.GetTransactionContext())
			{
				var location = BlockStore.GetLocation(context, key);

				if (location == LocationEnum.Main || location == LocationEnum.Genesis)
				{
					var bk = BlockStore.GetBlock(context, key);

					return bk == null ? null : bk.Value;
				}

				return null;
			}
		}

		byte[] GetContractCode(byte[] contractHash)
		{
			using (TransactionContext dbTx = _DBContext.GetTransactionContext())
			{
				var result = ContractsTxsStore.Get(dbTx.Transaction, contractHash);

				if (result != null && BlockStore.TxStore.ContainsKey(dbTx, result))
				{
					var transaction = BlockStore.TxStore.Get(dbTx, result).Value;

					if (FSharpOption<Types.ExtendedContract>.get_IsSome(transaction.contract))
					{
						if (transaction.contract.Value.IsContract)
						{
							return (transaction.contract.Value as Types.ExtendedContract.Contract).Item.code;
						}
					}
				}
			}

			return null;
		}

		//public Tuple<ulong, ulong> GetTotalAssets(byte[] contractHash)
		//{
		//	ulong confirmed = 0;
		//	ulong unconfirmed = 0;

		//	using (var dbTx = GetDBTransaction())
		//	{
		//		var x = UTXOStore.All(dbTx, null, false).ToList();

		//		foreach (var item in UTXOStore.All(dbTx, null, false).Where(t =>
		//		{
		//			var contractLock = t.Item2.@lock as Types.OutputLock.ContractLock;
		//			return contractLock != null; // && contractLock.contractHash.SequenceEqual(contractHash);
		//		}))
		//		{
		//			confirmed += item.Item2.spend.amount;
		//		}
		//	}

		//	foreach (var item in memPool.TxPool)
		//	{
		//		foreach (var output in item.Value.outputs)
		//		{
		//			var contractLock = output.@lock as Types.OutputLock.ContractLock;
		//			if (contractLock != null)
		//			{
		//				confirmed += output.spend.amount;
		//			}
		//		}
		//	}

		//	return new Tuple<ulong, ulong>(confirmed, unconfirmed);
		//}

		// TODO: use linq, return enumerator, remove predicate
		void GetUTXOSet(Func<Types.Output, bool> predicate, out HashDictionary<List<Types.Output>> txOutputs, out HashDictionary<Types.Transaction> txs)
		{
			txOutputs = new HashDictionary<List<Types.Output>>();
			txs = new HashDictionary<Types.Transaction>();

			using (TransactionContext context = _DBContext.GetTransactionContext())
			{
				foreach (var item in UTXOStore.All(context, predicate, true))
				{
					if (!txOutputs.ContainsKey(item.Item1.txHash))
					{
						txOutputs[item.Item1.txHash] = new List<Types.Output>();
					}

					txOutputs[item.Item1.txHash].Add(item.Item2);
					txs[item.Item1.txHash] = BlockStore.TxStore.Get(context, item.Item1.txHash).Value;
				}
			}
		}

		bool AssembleAutoTx(byte[] contractHash, byte[] message, out Types.Transaction transaction, bool isWitness)
		{
			using (TransactionContext dbTx = _DBContext.GetTransactionContext())
			{
                var contractUtxoGetter = FactorContractUtxoGetter(dbTx, false);
				var acsItem = ActiveContractSet.Get(dbTx, contractHash);
				var contractFunction = ContractExamples.Execution.deserialize(acsItem.Value.CompiledContract);

                return ExecuteContract(contractHash, contractFunction, message, out transaction, contractUtxoGetter, isWitness);
			}
		}

        // Note: when contract uses blokchain state as context, need to pass it in.
        public static bool ExecuteContract(
            byte[] contractHash, 
            ContractFunction contractFunction, 
            byte[] message, 
            out Types.Transaction transaction, 
            ContractUtxoGetter contractUtxoGetter, 
            bool isWitness)
		{
			var contractFunctionInput = new ContractFunctionInput(
				message,
				contractHash,
				contractUtxoGetter
            );
            TransactionSkeleton transactionSkeleton = null;

			transaction = null;

			try
            {
                transactionSkeleton = contractFunction.Invoke(contractFunctionInput) as TransactionSkeleton;
            }
			catch (Exception e)
			{
				BlockChainTrace.Error("Error executing contract", e);
                return false;
            }

            if (transactionSkeleton == null)
                return false;

            transaction = new Types.Transaction(
                Tests.tx.version,
                transactionSkeleton.Item1,
                ListModule.OfSeq<byte[]>(isWitness ? new byte[][] { message } : new byte[][] { }),
                transactionSkeleton.Item2,
                FSharpOption<Types.ExtendedContract>.None //TODO: get from txSkeleton.Item3
            );

            return true;
		}

		List<ACSItem> GetActiveContacts()
		{
			using (var dbTx = _DBContext.GetTransactionContext())
			{
				return ActiveContractSet.All(dbTx).Select(t => t.Item2).ToList();
			}
		}

		List<Tuple<Types.Outpoint, Types.Output>> GetContractPointedOutputs(byte[] contractHash)
		{
			var result = new List<Tuple<Types.Outpoint, Types.Output>>();

			using (var dbTx = _DBContext.GetTransactionContext())
			{
				foreach (var item in UTXOStore.All(dbTx, t => t.@lock is Types.OutputLock.ContractLock, false))
				{
					var lockContractHash = ((Types.OutputLock.ContractLock)item.Item2.@lock).contractHash;

					if (contractHash.SequenceEqual(lockContractHash))
						result.Add(item);
				}
			}

			foreach (var item in memPool.TxPool)
			{
				uint i = 0;
				foreach (var output in item.Value.outputs)
				{
					if (output.@lock is Types.OutputLock.ContractLock)
					{
						var lockContractHash = ((Types.OutputLock.ContractLock)output.@lock).contractHash;

						if (contractHash.SequenceEqual(lockContractHash))
						{
							result.Add(new Tuple<Types.Outpoint, Types.Output>(new Types.Outpoint(item.Key, i), output));
						}
					}

					i++;
				}
			}

			foreach (var item in memPool.TxPool)
			{
				foreach (var input in item.Value.pInputs)
				{
					result.RemoveAll(t => t.Item1.Equals(input.Item1));
				}
			}

			return result;
		}

		bool IsContractActive(byte[] contractHash)
		{
			using (var dbTx = _DBContext.GetTransactionContext())
			{
				return ActiveContractSet.IsActive(dbTx, contractHash);
			}

			//TODO: get number of blocks
			//var lastBlock = ActiveContractSet.LastBlock(dbTx, contractHash);
			//var currentHeight = Tip == null ? 0 : Tip.Value.header.blockNumber;
			//return nextBlocks > currentHeight - lastBlock;
		}

		bool IsConfirmedUTXOExist(Types.Outpoint outpoint)
		{
			using (var dbTx = _DBContext.GetTransactionContext())
			{
				return UTXOStore.ContainsKey(dbTx, outpoint);
			}
		}
	}
}
