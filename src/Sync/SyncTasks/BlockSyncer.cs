﻿// --------------------------------------------------------------------------------------------------------------------
// <copyright file="BlockSyncer.cs" company="SoftChains">
//   Copyright 2016 Dan Gershony
//   //  Licensed under the MIT license. See LICENSE file in the project root for full license information.
//   //  THIS CODE AND INFORMATION ARE PROVIDED "AS IS" WITHOUT WARRANTY OF ANY KIND, 
//   //  EITHER EXPRESSED OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE IMPLIED WARRANTIES 
//   //  OF MERCHANTABILITY AND/OR FITNESS FOR A PARTICULAR PURPOSE.
// </copyright>
// --------------------------------------------------------------------------------------------------------------------

namespace Nako.Sync.SyncTasks
{
    #region Using Directives

    using System;
    using System.Linq;
    using System.Threading.Tasks;

    using Nako.Client;
    using Nako.Config;
    using Nako.Extensions;
    using Nako.Operations;
    using Nako.Operations.Types;

    #endregion

    /// <summary>
    /// The block sync.
    /// </summary>
    public class BlockSyncer : TaskRunner<SyncBlockOperation>
    {
        /// <summary>
        /// The config.
        /// </summary>
        private readonly NakoConfiguration config;

        /// <summary>
        /// The sync operations.
        /// </summary>
        private readonly ISyncOperations syncOperations;

        /// <summary>
        /// The sync connection.
        /// </summary>
        private readonly SyncConnection syncConnection;

        /// <summary>
        /// The tracer.
        /// </summary>
        private readonly Tracer tracer;

        /// <summary>
        /// Initializes a new instance of the <see cref="BlockSyncer"/> class.
        /// </summary>
        /// <param name="application">
        /// The application.
        /// </param>
        /// <param name="config">
        /// The config.
        /// </param>
        /// <param name="syncOperations">
        /// The sync operations.
        /// </param>
        /// <param name="syncConnection">
        /// The sync connection.
        /// </param>
        /// <param name="tracer">
        /// The tracer.
        /// </param>
        public BlockSyncer(NakoApplication application, NakoConfiguration config, ISyncOperations syncOperations, SyncConnection syncConnection, Tracer tracer)
            : base(application, config, tracer)
        {
            this.tracer = tracer;
            this.syncConnection = syncConnection;
            this.syncOperations = syncOperations;
            this.config = config;
        }

        /// <inheritdoc />
        public override async Task<bool> OnExecute()
        {
            if (this.Runner.Get<BlockStore>().Queue.Count() >= this.config.MaxItemsInQueue)
            {
                return false;
            }

            SyncBlockOperation item;

            if (this.TryDequeue(out item))
            {
                var stoper = StopwatchExtension.CreateAndStart();

                try
                {
                    if (item.BlockInfo != null)
                    {
                        var block = await this.syncOperations.SyncBlock(this.syncConnection, item.BlockInfo);

                        var inputs = block.Transactions.SelectMany(s => s.VIn).Count();
                        var outputs = block.Transactions.SelectMany(s => s.VOut).Count();

                        this.Runner.Get<BlockStore>().Enqueue(block);

                        this.tracer.Trace("BlockSyncer", string.Format("Seconds = {0} - BlockIndex = {1} - Transactions {2} - Inputs {3} - Outputs {4} - ({5})", stoper.Elapsed.TotalSeconds, block.BlockInfo.Height, block.Transactions.Count(), inputs, outputs, inputs + outputs), ConsoleColor.DarkGreen);
                    }

                    if (item.PoolTransactions != null)
                    {
                        var pool = await this.syncOperations.SyncPool(this.syncConnection, item.PoolTransactions);

                        var inputs = pool.Transactions.SelectMany(s => s.VIn).Count();
                        var outputs = pool.Transactions.SelectMany(s => s.VOut).Count();

                        this.Runner.Get<BlockStore>().Enqueue(pool);

                        this.tracer.Trace("BlockSyncer", string.Format("Seconds = {0} - Pool = {1} - Transactions {2} - Inputs {3} - Outputs {4} - ({5})", stoper.Elapsed.TotalSeconds, "Sync", pool.Transactions.Count(), inputs, outputs, inputs + outputs), ConsoleColor.DarkGreen);
                    }
                }
                catch (BitcoinClientException bce)
                {
                    if (bce.ErrorCode == -5)
                    {
                        if (bce.ErrorMessage == "No information available about transaction")
                        {
                            throw new SyncRestartException(string.Empty, bce);
                        }
                    }

                    throw;
                }

                stoper.Stop();

                return true;
            }

            return false;
        }
    }
}