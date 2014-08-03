﻿using BitSharp.Common;
using BitSharp.Common.ExtensionMethods;
using BitSharp.Core;
using BitSharp.Core.Builders;
using BitSharp.Core.Domain;
using BitSharp.Core.ExtensionMethods;
using BitSharp.Core.Storage;
using Microsoft.Isam.Esent.Collections.Generic;
using Microsoft.Isam.Esent.Interop;
using Microsoft.Isam.Esent.Interop.Windows8;
using Microsoft.Isam.Esent.Interop.Windows81;
using NLog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace BitSharp.Esent
{
    internal class ChainStateCursor : IChainStateCursor
    {
        //TODO
        public static bool IndexOutputs { get; set; }

        private readonly Logger logger;

        private readonly string jetDatabase;
        private readonly Instance jetInstance;

        private readonly ChainStateStorageCursor cursor;

        private readonly ChainStateStorageCursor[] cursors;
        private readonly object cursorsLock;

        private bool inTransaction;

        public ChainStateCursor(string jetDatabase, Instance jetInstance, Logger logger)
        {
            this.logger = logger;
            this.jetDatabase = jetDatabase;
            this.jetInstance = jetInstance;

            this.cursor = new ChainStateStorageCursor(jetDatabase, jetInstance, readOnly: false);

            this.cursors = new ChainStateStorageCursor[16];
            this.cursorsLock = new object();
        }

        ~ChainStateCursor()
        {
            this.Dispose();
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);

            this.cursor.Dispose();
        }

        public IEnumerable<ChainedHeader> ReadChain()
        {
            return ChainStateStorage.ReadChain(this.cursor);
        }

        public void AddChainedHeader(ChainedHeader chainedHeader)
        {
            if (!this.inTransaction)
                throw new InvalidOperationException();

            Api.JetPrepareUpdate(this.cursor.jetSession, this.cursor.chainTableId, JET_prep.Insert);
            try
            {
                Api.SetColumn(this.cursor.jetSession, this.cursor.chainTableId, this.cursor.blockHeightColumnId, chainedHeader.Height);
                Api.SetColumn(this.cursor.jetSession, this.cursor.chainTableId, this.cursor.chainedHeaderBytesColumnId, DataEncoder.EncodeChainedHeader(chainedHeader));

                Api.JetUpdate(this.cursor.jetSession, this.cursor.chainTableId);
            }
            catch (Exception)
            {
                Api.JetPrepareUpdate(this.cursor.jetSession, this.cursor.unspentTxTableId, JET_prep.Cancel);
                throw;
            }
        }

        public void RemoveChainedHeader(ChainedHeader chainedHeader)
        {
            if (!this.inTransaction)
                throw new InvalidOperationException();

            Api.JetSetCurrentIndex(this.cursor.jetSession, this.cursor.chainTableId, "IX_BlockHeight");

            Api.MakeKey(this.cursor.jetSession, this.cursor.chainTableId, chainedHeader.Height, MakeKeyGrbit.NewKey);

            if (!Api.TrySeek(this.cursor.jetSession, this.cursor.chainTableId, SeekGrbit.SeekEQ))
                throw new InvalidOperationException();

            Api.JetDelete(this.cursor.jetSession, this.cursor.chainTableId);
        }

        public int UnspentTxCount
        {
            //TODO
            get { return 0; }
        }

        public bool ConainsUnspentTx(UInt256 txHash)
        {
            return ChainStateStorage.ContainsTransaction(this.cursor, txHash);
        }

        public bool TryGetUnspentTx(UInt256 txHash, out UnspentTx unspentTx)
        {
            return ChainStateStorage.TryGetTransaction(this.cursor, txHash, out unspentTx);
        }

        public bool TryAddUnspentTx(UnspentTx unspentTx)
        {
            if (!this.inTransaction)
                throw new InvalidOperationException();

            try
            {
                Api.JetPrepareUpdate(this.cursor.jetSession, this.cursor.unspentTxTableId, JET_prep.Insert);
                try
                {
                    Api.SetColumn(this.cursor.jetSession, this.cursor.unspentTxTableId, this.cursor.txHashColumnId, DbEncoder.EncodeUInt256(unspentTx.TxHash));
                    Api.SetColumn(this.cursor.jetSession, this.cursor.unspentTxTableId, this.cursor.blockIndexColumnId, unspentTx.BlockIndex);
                    Api.SetColumn(this.cursor.jetSession, this.cursor.unspentTxTableId, this.cursor.txIndexColumnId, unspentTx.TxIndex);
                    Api.SetColumn(this.cursor.jetSession, this.cursor.unspentTxTableId, this.cursor.outputStatesColumnId, DataEncoder.EncodeOutputStates(unspentTx.OutputStates));

                    Api.JetUpdate(this.cursor.jetSession, this.cursor.unspentTxTableId);

                    return true;
                }
                catch (Exception)
                {
                    Api.JetPrepareUpdate(this.cursor.jetSession, this.cursor.unspentTxTableId, JET_prep.Cancel);
                    throw;
                }
            }
            catch (EsentKeyDuplicateException)
            {
                return false;
            }
        }

        public void PrepareSpentTransactions(int spentBlockIndex)
        {
            if (!this.inTransaction)
                throw new InvalidOperationException();

            Api.JetPrepareUpdate(this.cursor.jetSession, this.cursor.spentTxTableId, JET_prep.Insert);
            try
            {
                Api.SetColumn(this.cursor.jetSession, this.cursor.spentTxTableId, this.cursor.spentSpentBlockIndexColumnId, spentBlockIndex);
                Api.SetColumn(this.cursor.jetSession, this.cursor.spentTxTableId, this.cursor.spentDataColumnId, new byte[0]);

                Api.JetUpdate(this.cursor.jetSession, this.cursor.spentTxTableId);
            }
            catch (Exception)
            {
                Api.JetPrepareUpdate(this.cursor.jetSession, this.cursor.spentTxTableId, JET_prep.Cancel);
                throw;
            }

            Api.JetSetCurrentIndex(this.cursor.jetSession, this.cursor.spentTxTableId, "IX_SpentBlockIndex");
            Api.MakeKey(this.cursor.jetSession, this.cursor.spentTxTableId, spentBlockIndex, MakeKeyGrbit.NewKey);
            if (!Api.TrySeek(this.cursor.jetSession, this.cursor.spentTxTableId, SeekGrbit.SeekEQ))
                throw new InvalidOperationException();
        }

        public bool TryRemoveUnspentTx(UInt256 txHash)
        {
            if (!this.inTransaction)
                throw new InvalidOperationException();

            Api.JetSetCurrentIndex(this.cursor.jetSession, this.cursor.unspentTxTableId, "IX_TxHash");
            Api.MakeKey(this.cursor.jetSession, this.cursor.unspentTxTableId, DbEncoder.EncodeUInt256(txHash), MakeKeyGrbit.NewKey);
            if (Api.TrySeek(this.cursor.jetSession, this.cursor.unspentTxTableId, SeekGrbit.SeekEQ))
            {
                var addedBlockIndex = Api.RetrieveColumnAsInt32(cursor.jetSession, cursor.unspentTxTableId, cursor.blockIndexColumnId).Value;
                var txIndex = Api.RetrieveColumnAsInt32(cursor.jetSession, cursor.unspentTxTableId, cursor.txIndexColumnId).Value;
                var outputStates = DataEncoder.DecodeOutputStates(Api.RetrieveColumn(cursor.jetSession, cursor.unspentTxTableId, cursor.outputStatesColumnId));

                Api.JetDelete(this.cursor.jetSession, this.cursor.unspentTxTableId);

                return true;
            }
            else
            {
                return false;
            }
        }

        public bool TryUpdateUnspentTx(UnspentTx unspentTx)
        {
            if (!this.inTransaction)
                throw new InvalidOperationException();

            Api.JetSetCurrentIndex(this.cursor.jetSession, this.cursor.unspentTxTableId, "IX_TxHash");
            Api.MakeKey(this.cursor.jetSession, this.cursor.unspentTxTableId, DbEncoder.EncodeUInt256(unspentTx.TxHash), MakeKeyGrbit.NewKey);

            if (Api.TrySeek(this.cursor.jetSession, this.cursor.unspentTxTableId, SeekGrbit.SeekEQ))
            {
                Api.JetPrepareUpdate(this.cursor.jetSession, this.cursor.unspentTxTableId, JET_prep.Replace);
                try
                {
                    Api.SetColumn(this.cursor.jetSession, this.cursor.unspentTxTableId, this.cursor.outputStatesColumnId, DataEncoder.EncodeOutputStates(unspentTx.OutputStates));

                    Api.JetUpdate(this.cursor.jetSession, this.cursor.unspentTxTableId);
                }
                catch (Exception)
                {
                    Api.JetPrepareUpdate(this.cursor.jetSession, this.cursor.unspentTxTableId, JET_prep.Cancel);
                    throw;
                }

                return true;
            }
            else
            {
                return false;
            }
        }

        public IEnumerable<UnspentTx> ReadUnspentTransactions()
        {
            return ChainStateStorage.ReadUnspentTransactions(this.cursor);
        }

        public IEnumerable<SpentTx> ReadSpentTransactions(int spentBlockIndex)
        {
            var readCursor = this.OpenCursor();
            try
            {
                Api.JetSetCurrentIndex(readCursor.jetSession, readCursor.spentTxTableId, "IX_SpentBlockIndex");

                Api.MakeKey(readCursor.jetSession, readCursor.spentTxTableId, spentBlockIndex, MakeKeyGrbit.NewKey);

                if (Api.TrySeek(readCursor.jetSession, readCursor.spentTxTableId, SeekGrbit.SeekEQ))
                {
                    var spentData = Api.RetrieveColumn(readCursor.jetSession, readCursor.spentTxTableId, readCursor.spentDataColumnId);
                    using (var stream = new MemoryStream(spentData))
                    {
                        while (stream.Position < stream.Length)
                        {
                            yield return DataEncoder.DecodeSpentTx(stream);
                        }
                    }
                }
            }
            finally
            {
                this.FreeCursor(readCursor);
            }
        }

        public void AddSpentTransaction(SpentTx spentTx)
        {
            Debug.Assert(spentTx.SpentBlockIndex == Api.RetrieveColumnAsInt32(this.cursor.jetSession, this.cursor.spentTxTableId, this.cursor.spentSpentBlockIndexColumnId).Value);

            Api.JetPrepareUpdate(this.cursor.jetSession, this.cursor.spentTxTableId, JET_prep.Replace);
            try
            {
                var spentTxBytes = DataEncoder.EncodeSpentTx(spentTx);

                Api.SetColumn(this.cursor.jetSession, this.cursor.spentTxTableId, this.cursor.spentDataColumnId, spentTxBytes, SetColumnGrbit.AppendLV);

                Api.JetUpdate(this.cursor.jetSession, this.cursor.spentTxTableId);
            }
            catch (Exception)
            {
                Api.JetPrepareUpdate(this.cursor.jetSession, this.cursor.spentTxTableId, JET_prep.Cancel);
                throw;
            }
        }

        public void RemoveSpentTransactions(int spentBlockIndex)
        {
            var pruneCursor = this.OpenCursor();
            try
            {
                Api.JetBeginTransaction(pruneCursor.jetSession);
                try
                {
                    Api.JetSetCurrentIndex(pruneCursor.jetSession, pruneCursor.spentTxTableId, "IX_SpentBlockIndex");

                    Api.MakeKey(pruneCursor.jetSession, pruneCursor.spentTxTableId, spentBlockIndex, MakeKeyGrbit.NewKey);

                    if (Api.TrySeek(pruneCursor.jetSession, pruneCursor.spentTxTableId, SeekGrbit.SeekEQ))
                    {
                        Api.JetDelete(pruneCursor.jetSession, pruneCursor.spentTxTableId);
                    }

                    Api.JetCommitTransaction(pruneCursor.jetSession, CommitTransactionGrbit.LazyFlush);
                }
                catch (Exception)
                {
                    Api.JetRollback(pruneCursor.jetSession, RollbackTransactionGrbit.None);
                    throw;
                }
            }
            finally
            {
                this.FreeCursor(pruneCursor);
            }
        }

        public void RemoveSpentTransactionsToHeight(int spentBlockIndex)
        {
            var pruneCursor = this.OpenCursor();
            try
            {
                Api.JetBeginTransaction(pruneCursor.jetSession);
                try
                {
                    Api.JetSetCurrentIndex(pruneCursor.jetSession, pruneCursor.spentTxTableId, "IX_SpentBlockIndex");

                    Api.MakeKey(pruneCursor.jetSession, pruneCursor.spentTxTableId, -1, MakeKeyGrbit.NewKey);

                    if (Api.TrySeek(pruneCursor.jetSession, pruneCursor.spentTxTableId, SeekGrbit.SeekGE))
                    {
                        do
                        {
                            if (spentBlockIndex >= Api.RetrieveColumnAsInt32(pruneCursor.jetSession, pruneCursor.spentTxTableId, pruneCursor.spentSpentBlockIndexColumnId).Value)
                            {
                                Api.JetDelete(pruneCursor.jetSession, pruneCursor.spentTxTableId);
                            }
                            else
                            {
                                break;
                            }
                        } while (Api.TryMoveNext(pruneCursor.jetSession, pruneCursor.spentTxTableId));
                    }

                    Api.JetCommitTransaction(pruneCursor.jetSession, CommitTransactionGrbit.LazyFlush);
                }
                catch (Exception)
                {
                    Api.JetRollback(pruneCursor.jetSession, RollbackTransactionGrbit.None);
                    throw;
                }
            }
            finally
            {
                this.FreeCursor(pruneCursor);
            }
        }

        public IChainStateStorage ToImmutable()
        {
            if (this.inTransaction)
                throw new InvalidOperationException();

            return new ChainStateStorage(this.jetDatabase, this.jetInstance);
        }

        public void BeginTransaction()
        {
            if (this.inTransaction)
                throw new InvalidOperationException();

            Api.JetBeginTransaction(this.cursor.jetSession);

            this.inTransaction = true;
        }

        public void CommitTransaction()
        {
            if (!this.inTransaction)
                throw new InvalidOperationException();

            Api.JetCommitTransaction(this.cursor.jetSession, CommitTransactionGrbit.LazyFlush);

            this.inTransaction = false;
        }

        public void RollbackTransaction()
        {
            if (!this.inTransaction)
                throw new InvalidOperationException();

            Api.JetRollback(this.cursor.jetSession, RollbackTransactionGrbit.None);

            this.inTransaction = false;
        }

        public void Defragment()
        {
            var defragCursor = this.OpenCursor();
            try
            {
                //int passes = -1, seconds = -1;
                //Api.JetDefragment(defragCursor.jetSession, defragCursor.chainStateDbId, "Chain", ref passes, ref seconds, DefragGrbit.BatchStart);
                //Api.JetDefragment(defragCursor.jetSession, defragCursor.chainStateDbId, "ChainState", ref passes, ref seconds, DefragGrbit.BatchStart);

                if (EsentVersion.SupportsWindows81Features)
                {
                    this.logger.Info("Begin shrinking chain state database");

                    int actualPages;
                    Windows8Api.JetResizeDatabase(defragCursor.jetSession, defragCursor.chainStateDbId, 0, out actualPages, Windows81Grbits.OnlyShrink);

                    this.logger.Info("Finished shrinking chain state database: {0:#,##0} pages".Format2(actualPages));
                }
            }
            finally
            {
                this.FreeCursor(defragCursor);
            }
        }

        private ChainStateStorageCursor OpenCursor()
        {
            ChainStateStorageCursor cursor = null;

            lock (this.cursorsLock)
            {
                for (var i = 0; i < this.cursors.Length; i++)
                {
                    if (this.cursors[i] != null)
                    {
                        cursor = this.cursors[i];
                        this.cursors[i] = null;
                        break;
                    }
                }
            }

            if (cursor == null)
                cursor = new ChainStateStorageCursor(this.jetDatabase, this.jetInstance, readOnly: false);

            return cursor;
        }

        private void FreeCursor(ChainStateStorageCursor cursor)
        {
            var cached = false;

            lock (this.cursorsLock)
            {
                for (var i = 0; i < this.cursors.Length; i++)
                {
                    if (this.cursors[i] == null)
                    {
                        this.cursors[i] = cursor;
                        cached = true;
                        break;
                    }
                }
            }

            if (!cached)
                cursor.Dispose();
        }
    }
}