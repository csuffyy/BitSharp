﻿using BitSharp.Core.Domain;
using BitSharp.Core.Rules;
using BitSharp.Core.Storage;
using NLog;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace BitSharp.Core.Builders
{
    internal static class BlockValidator
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        public static async Task ValidateBlockAsync(ICoreStorage coreStorage, IBlockchainRules rules, ChainedHeader chainedHeader, ISourceBlock<LoadedTx> loadedTxes, CancellationToken cancelToken = default(CancellationToken))
        {
            // validate merkle root
            var merkleStream = new MerkleStream();
            var merkleValidator = InitMerkleValidator(chainedHeader, merkleStream, cancelToken);

            // begin feeding the merkle validator
            loadedTxes.LinkTo(merkleValidator, new DataflowLinkOptions { PropagateCompletion = true });

            // validate transactions
            var txValidator = InitTxValidator(rules, chainedHeader, cancelToken);

            // begin feeding the tx validator
            merkleValidator.LinkTo(txValidator, new DataflowLinkOptions { PropagateCompletion = true });

            // validate scripts
            var scriptValidator = InitScriptValidator(rules, chainedHeader, cancelToken);

            // begin feeding the script validator
            txValidator.LinkTo(scriptValidator, new DataflowLinkOptions { PropagateCompletion = true });

            await merkleValidator.Completion;
            await txValidator.Completion;
            await scriptValidator.Completion;

            if (!rules.BypassPrevTxLoading)
            {
                try
                {
                    merkleStream.FinishPairing();
                }
                //TODO
                catch (InvalidOperationException)
                {
                    throw CreateMerkleRootException(chainedHeader);
                }
                if (merkleStream.RootNode.Hash != chainedHeader.MerkleRoot)
                    throw CreateMerkleRootException(chainedHeader);
            }
        }

        private static TransformBlock<LoadedTx, LoadedTx> InitMerkleValidator(ChainedHeader chainedHeader, MerkleStream merkleStream, CancellationToken cancelToken)
        {
            return new TransformBlock<LoadedTx, LoadedTx>(
                loadedTx =>
                {
                    try
                    {
                        merkleStream.AddNode(new MerkleTreeNode(loadedTx.TxIndex, 0, loadedTx.Transaction.Hash, false));
                    }
                    //TODO
                    catch (InvalidOperationException)
                    {
                        throw CreateMerkleRootException(chainedHeader);
                    }
                    return loadedTx;
                },
                new ExecutionDataflowBlockOptions { CancellationToken = cancelToken });
        }

        private static TransformManyBlock<LoadedTx, Tuple<LoadedTx, int>> InitTxValidator(IBlockchainRules rules, ChainedHeader chainedHeader, CancellationToken cancelToken)
        {
            return new TransformManyBlock<LoadedTx, Tuple<LoadedTx, int>>(
                loadedTx =>
                {
                    rules.ValidateTransaction(chainedHeader, loadedTx);

                    if (!rules.IgnoreScripts && !loadedTx.IsCoinbase)
                    {
                        var scripts = new Tuple<LoadedTx, int>[loadedTx.Transaction.Inputs.Length];
                        for (var i = 0; i < loadedTx.Transaction.Inputs.Length; i++)
                            scripts[i] = Tuple.Create(loadedTx, i);

                        return scripts;
                    }
                    else
                        return new Tuple<LoadedTx, int>[0];
                },
                new ExecutionDataflowBlockOptions { CancellationToken = cancelToken, MaxDegreeOfParallelism = 16 });
        }

        private static ActionBlock<Tuple<LoadedTx, int>> InitScriptValidator(IBlockchainRules rules, ChainedHeader chainedHeader, CancellationToken cancelToken)
        {
            return new ActionBlock<Tuple<LoadedTx, int>>(
                tuple =>
                {
                    var loadedTx = tuple.Item1;
                    var inputIndex = tuple.Item2;
                    var txInput = loadedTx.Transaction.Inputs[inputIndex];
                    var prevTxOutput = loadedTx.GetInputPrevTxOutput(inputIndex);

                    if (!rules.IgnoreScriptErrors)
                    {
                        rules.ValidationTransactionScript(chainedHeader, loadedTx.Transaction, loadedTx.TxIndex, txInput, inputIndex, prevTxOutput);
                    }
                    else
                    {
                        try
                        {
                            rules.ValidationTransactionScript(chainedHeader, loadedTx.Transaction, loadedTx.TxIndex, txInput, inputIndex, prevTxOutput);
                        }
                        catch (Exception ex)
                        {
                            var aggEx = ex as AggregateException;
                            logger.Debug($"Ignoring script errors in block: {chainedHeader.Height,9:N0}, errors: {(aggEx != null ? aggEx.InnerExceptions.Count : -1):N0}");
                        }
                    }
                },
                new ExecutionDataflowBlockOptions { CancellationToken = cancelToken, MaxDegreeOfParallelism = 16 });
        }

        private static ValidationException CreateMerkleRootException(ChainedHeader chainedHeader)
        {
            return new ValidationException(chainedHeader.Hash, $"Failing block {chainedHeader.Hash} at height {chainedHeader.Height}: Merkle root is invalid");
        }
    }
}
