﻿using BitSharp.Common;
using BitSharp.Core.Domain;
using System;
using System.Collections.Immutable;

namespace BitSharp.Core.Storage.Memory
{
    public class MemoryStorageManager : IStorageManager
    {
        private readonly MemoryBlockStorage blockStorage;
        private readonly MemoryBlockTxesStorage blockTxesStorage;
        private readonly MemoryChainStateStorage chainStateStorage;

        private bool isDisposed;

        public MemoryStorageManager()
            : this(null, null, null, null)
        { }

        internal MemoryStorageManager(Chain chain = null, int? unspentTxCount = null, int? unspentOutputCount = null, int? totalTxCount = null, int? totalInputCount = null, int? totalOutputCount = null, ImmutableSortedDictionary<UInt256, UnspentTx> unspentTransactions = null, ImmutableDictionary<int, IImmutableList<UInt256>> spentTransactions = null)
        {
            this.blockStorage = new MemoryBlockStorage();
            this.blockTxesStorage = new MemoryBlockTxesStorage();
            this.chainStateStorage = new MemoryChainStateStorage(chain, unspentTxCount, unspentOutputCount, totalTxCount, totalInputCount, totalOutputCount, unspentTransactions, spentTransactions);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!isDisposed && disposing)
            {
                this.blockStorage.Dispose();
                this.blockTxesStorage.Dispose();
                this.chainStateStorage.Dispose();

                isDisposed = true;
            }
        }

        public IBlockStorage BlockStorage
        {
            get { return this.blockStorage; }
        }

        public IBlockTxesStorage BlockTxesStorage
        {
            get { return this.blockTxesStorage; }
        }

        public DisposeHandle<IChainStateCursor> OpenChainStateCursor()
        {
            return new DisposeHandle<IChainStateCursor>(null, new MemoryChainStateCursor(this.chainStateStorage));
        }
    }
}
