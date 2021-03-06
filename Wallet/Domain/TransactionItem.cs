﻿using System;
using BlockChain.Data;
using Wallet.core;

namespace Wallet.Domain
{
	public class TransactionItem
	{
		public long Amount { get; set; }
		public byte[] Asset { get; set; }
		public DirectionEnum Direction { get; set; }
		public DateTime Date { get; set; }
		public String To { get; set; }
		public String Id { get; set; }
		public Decimal Fee { get; set; }

		public TxStateEnum TxState { get; set; }

        public TransactionItem(long Amount, DirectionEnum Direction, byte[] Asset, DateTime Date, String To, String Id, Decimal Fee, TxStateEnum txState) {
			this.Amount = Amount;
			this.Direction = Direction;
			this.Asset = Asset;
			this.Date = Date;
			this.To = To;
			this.Id = Id;
			this.Fee = Fee;
			this.TxState = txState;
		}
	}
}