﻿using BitSharp.Common.ExtensionMethods;
using BitSharp.Common.Test;
using BitSharp.Core.Builders;
using BitSharp.Core.Domain;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks.Dataflow;

namespace BitSharp.Core.Test.Builders
{
    [TestClass]
    public class UtxoLookAheadTest
    {
        [TestMethod]
        public void TestEmptyUtxoLookAhead()
        {
            var blockTxes = new BufferBlock<DecodedBlockTx>();
            blockTxes.Complete();

            var deferredCursor = new Mock<IDeferredChainStateCursor>();
            deferredCursor.Setup(x => x.CursorCount).Returns(4);

            var lookAhead = UtxoLookAhead.LookAhead(blockTxes, deferredCursor.Object);
            Assert.AreEqual(0, lookAhead.ReceiveAllAsync().Result.Count);

            Assert.IsTrue(lookAhead.Completion.Wait(2000));
        }

        [TestMethod]
        public void TestUtxoLookAheadCompletion()
        {
            var blockTxes = new BufferBlock<DecodedBlockTx>();

            var deferredCursor = new Mock<IDeferredChainStateCursor>();
            deferredCursor.Setup(x => x.CursorCount).Returns(4);

            var lookAhead = UtxoLookAhead.LookAhead(blockTxes, deferredCursor.Object);

            Assert.IsFalse(lookAhead.Completion.IsCompleted);

            blockTxes.Complete();

            Assert.AreEqual(0, lookAhead.ReceiveAllAsync().Result.Count);

            Assert.IsTrue(lookAhead.Completion.Wait(2000));
        }

        [TestMethod]
        public void TestUtxoLookAheadCoinbaseOnly()
        {
            var blockTxes = new BufferBlock<DecodedBlockTx>();

            var deferredCursor = new Mock<IDeferredChainStateCursor>();
            deferredCursor.Setup(x => x.CursorCount).Returns(4);

            var lookAhead = UtxoLookAhead.LookAhead(blockTxes, deferredCursor.Object);

            // post a coinbase transaction, the inputs should not be looked up
            var blockTx = BlockTx.Create(0, RandomData.RandomTransaction(new RandomDataOptions { TxInputCount = 2 }));
            blockTxes.Post(blockTx);
            blockTxes.Complete();

            // verify coinbase tx was forarded, with no inputs used from the chain state (no inputs were mocked)
            var warmedTxes = lookAhead.ReceiveAllAsync().Result;
            Assert.AreEqual(1, warmedTxes.Count);
            Assert.AreEqual(warmedTxes[0].Hash, blockTx.Hash);

            Assert.IsTrue(lookAhead.Completion.Wait(2000));
        }

        [TestMethod]
        public void TestUtxoLookAheadWithTransactions()
        {
            var deferredCursor = new Mock<IDeferredChainStateCursor>();
            deferredCursor.Setup(x => x.CursorCount).Returns(4);

            var blockTxes = new BufferBlock<DecodedBlockTx>();

            var lookAhead = UtxoLookAhead.LookAhead(blockTxes, deferredCursor.Object);

            var blockTx0 = BlockTx.Create(0, RandomData.RandomTransaction(new RandomDataOptions { TxInputCount = 2 }));
            var blockTx1 = BlockTx.Create(1, RandomData.RandomTransaction(new RandomDataOptions { TxInputCount = 2 }));
            var blockTx2 = BlockTx.Create(2, RandomData.RandomTransaction(new RandomDataOptions { TxInputCount = 2 }));

            blockTxes.Post(blockTx0);
            blockTxes.Post(blockTx1);
            blockTxes.Post(blockTx2);
            blockTxes.Complete();

            // verify each transaction was forwarded
            var expectedBlockTxes = new[] { blockTx0, blockTx1, blockTx2 };
            var warmedTxes = lookAhead.ReceiveAllAsync().Result;
            Assert.AreEqual(3, warmedTxes.Count);
            CollectionAssert.AreEqual(expectedBlockTxes.Select(x => x.Hash).ToList(), warmedTxes.Select(x => x.Hash).ToList());

            // verify each non-coinbase input transaction was warmed up
            var expectedLookups = expectedBlockTxes.Skip(1).SelectMany(x => x.Transaction.Inputs.Select(input => input.PrevTxOutputKey.TxHash));
            foreach (var txHash in expectedLookups)
                deferredCursor.Verify(x => x.WarmUnspentTx(txHash));

            Assert.IsTrue(lookAhead.Completion.Wait(2000));
        }

        [TestMethod]
        public void TestUtxoLookAheadOrdering()
        {
            var deferredCursor = new Mock<IDeferredChainStateCursor>();
            deferredCursor.Setup(x => x.CursorCount).Returns(4);

            var blockTxes = new BufferBlock<DecodedBlockTx>();

            var lookAhead = UtxoLookAhead.LookAhead(blockTxes, deferredCursor.Object);

            var blockTx0 = BlockTx.Create(0, RandomData.RandomTransaction(new RandomDataOptions { TxInputCount = 2 }));
            var blockTx1 = BlockTx.Create(1, RandomData.RandomTransaction(new RandomDataOptions { TxInputCount = 2 }));
            var blockTx2 = BlockTx.Create(2, RandomData.RandomTransaction(new RandomDataOptions { TxInputCount = 2 }));
            var blockTx3 = BlockTx.Create(3, RandomData.RandomTransaction(new RandomDataOptions { TxInputCount = 2 }));

            // setup events so that transactions finish in 0, 3, 1, 2 order
            using (var blockTx1ReadEvent = new ManualResetEventSlim())
            using (var blockTx2ReadEvent = new ManualResetEventSlim())
            using (var blockTx3ReadEvent = new ManualResetEventSlim())
            {
                deferredCursor.Setup(x => x.WarmUnspentTx(blockTx1.Transaction.Inputs[0].PrevTxOutputKey.TxHash))
                    .Callback(() =>
                    {
                        blockTx3ReadEvent.Wait();
                        blockTx1ReadEvent.Set();
                    });

                deferredCursor.Setup(x => x.WarmUnspentTx(blockTx2.Transaction.Inputs[0].PrevTxOutputKey.TxHash))
                    .Callback(() =>
                    {
                        blockTx3ReadEvent.Wait();
                        blockTx1ReadEvent.Wait();
                        blockTx2ReadEvent.Set();
                    });

                deferredCursor.Setup(x => x.WarmUnspentTx(blockTx3.Transaction.Inputs[0].PrevTxOutputKey.TxHash))
                    .Callback(() =>
                    {
                        blockTx3ReadEvent.Set();
                    });

                blockTxes.Post(blockTx0);
                blockTxes.Post(blockTx1);
                blockTxes.Post(blockTx2);
                blockTxes.Post(blockTx3);
                blockTxes.Complete();

                // verify each transaction was forwarded, in the correct order
                var expectedBlockTxes = new[] { blockTx0, blockTx1, blockTx2, blockTx3 };
                var warmedTxes = lookAhead.ReceiveAllAsync().Result;
                Assert.AreEqual(4, warmedTxes.Count);
                CollectionAssert.AreEqual(expectedBlockTxes.Select(x => x.Hash).ToList(), warmedTxes.Select(x => x.Hash).ToList());
            }

            Assert.IsTrue(lookAhead.Completion.Wait(2000));
        }

        [TestMethod]
        public void TestUtxoLookAheadWarmupException()
        {
            var deferredCursor = new Mock<IDeferredChainStateCursor>();
            deferredCursor.Setup(x => x.CursorCount).Returns(4);

            var blockTxes = new BufferBlock<DecodedBlockTx>();

            var lookAhead = UtxoLookAhead.LookAhead(blockTxes, deferredCursor.Object);

            var blockTx0 = BlockTx.Create(0, RandomData.RandomTransaction(new RandomDataOptions { TxInputCount = 2 }));
            var blockTx1 = BlockTx.Create(1, RandomData.RandomTransaction(new RandomDataOptions { TxInputCount = 2 }));

            var expectedException = new InvalidOperationException();
            deferredCursor.Setup(x => x.WarmUnspentTx(blockTx1.Transaction.Inputs[0].PrevTxOutputKey.TxHash)).Throws(expectedException);

            blockTxes.Post(blockTx0);
            blockTxes.Post(blockTx1);
            blockTxes.Complete();

            Exception actualEx;
            AssertMethods.AssertAggregateThrows<Exception>(() =>
                lookAhead.Completion.Wait(2000), out actualEx);
            Assert.AreSame(expectedException, actualEx);
        }
    }
}
