﻿using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;

namespace BitSharp.Core.Test
{
    [TestClass]
    public partial class DataEncoderTest
    {
        [TestMethod]
        public void TestWireEncodeBlockHeader()
        {
            var actual = DataEncoder.EncodeBlockHeader(BLOCK_HEADER_1);
            CollectionAssert.AreEqual(BLOCK_HEADER_1_BYTES.ToList(), actual.ToList());
        }

        [TestMethod]
        public void TestWireDecodeBlockHeader()
        {
            var actual = DataEncoder.EncodeBlockHeader(DataDecoder.DecodeBlockHeader(null, BLOCK_HEADER_1_BYTES.ToArray()));
            CollectionAssert.AreEqual(BLOCK_HEADER_1_BYTES.ToList(), actual.ToList());
        }

        [TestMethod]
        public void TestWireEncodeBlock()
        {
            var actual = DataEncoder.EncodeBlock(BLOCK_1);
            CollectionAssert.AreEqual(BLOCK_1_BYTES.ToList(), actual.ToList());
        }

        [TestMethod]
        public void TestWireDecodeBlock()
        {
            var actual = DataEncoder.EncodeBlock(DataDecoder.DecodeBlock(null, BLOCK_1_BYTES.ToArray()));
            CollectionAssert.AreEqual(BLOCK_1_BYTES.ToList(), actual.ToList());
        }

        [TestMethod]
        public void TestWireEncodeTransactionIn()
        {
            var actual = DataEncoder.EncodeTxInput(TRANSACTION_INPUT_1);
            CollectionAssert.AreEqual(TRANSACTION_INPUT_1_BYTES.ToList(), actual.ToList());
        }

        [TestMethod]
        public void TestWireDecodeTransactionIn()
        {
            var actual = DataEncoder.EncodeTxInput(DataDecoder.DecodeTxInput(TRANSACTION_INPUT_1_BYTES.ToArray()));
            CollectionAssert.AreEqual(TRANSACTION_INPUT_1_BYTES.ToList(), actual.ToList());
        }

        [TestMethod]
        public void TestWireEncodeTransactionOut()
        {
            var actual = DataEncoder.EncodeTxOutput(TRANSACTION_OUTPUT_1);
            CollectionAssert.AreEqual(TRANSACTION_OUTPUT_1_BYTES.ToList(), actual.ToList());
        }

        [TestMethod]
        public void TestWireDecodeTransactionOut()
        {
            var actual = DataEncoder.EncodeTxOutput(DataDecoder.DecodeTxOutput(TRANSACTION_OUTPUT_1_BYTES.ToArray()));
            CollectionAssert.AreEqual(TRANSACTION_OUTPUT_1_BYTES.ToList(), actual.ToList());
        }

        [TestMethod]
        public void TestWireEncodeTransaction()
        {
            var actual = DataEncoder.EncodeTransaction(TRANSACTION_1).TxBytes;
            CollectionAssert.AreEqual(TRANSACTION_1_BYTES.ToList(), actual.ToList());
        }

        [TestMethod]
        public void TestWireDecodeTransaction()
        {
            var actual = DataDecoder.DecodeTransaction(null, TRANSACTION_1_BYTES.ToArray());
            var actualBytes = DataEncoder.EncodeTransaction(actual).TxBytes;
            CollectionAssert.AreEqual(TRANSACTION_1_BYTES.ToList(), actualBytes.ToList());
        }
    }
}
