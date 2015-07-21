﻿using BitSharp.Common;
using BitSharp.Core.Storage;
using System.Threading.Tasks;

namespace BitSharp.Core.Builders
{
    public interface IDeferredChainStateCursor : IChainStateCursor
    {
        int CursorCount { get; }

        void WarmUnspentTx(UInt256 txHash);

        Task ApplyChangesAsync();
    }
}
