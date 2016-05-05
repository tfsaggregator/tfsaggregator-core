﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Caching;

using Aggregator.Core.Configuration;
using Aggregator.Core.Extensions;
using Aggregator.Core.Interfaces;
using Aggregator.Core.Monitoring;

namespace Aggregator.Core.Context
{
    /// <summary>
    /// Manages the global inter-call status
    /// </summary>
    public class RuntimeContext : IRuntimeContext
    {
        private const string CacheKey = "runtime:";
#pragma warning disable CA2213 // Disposable fields should be disposed
        private static readonly MemoryCache Cache = new MemoryCache("TFSAggregator2");
#pragma warning restore CA2213 // Disposable fields should be disposed

        /// <summary>
        /// Initializes a new instance of the <see cref="RuntimeContext"/> class.
        /// </summary>
        protected RuntimeContext()
        {
            // default
            this.HasErrors = true;
        }

        /// <summary>
        /// Return a proper context
        /// </summary>
        /// <returns></returns>
        public static RuntimeContext GetContext(
            Func<string> settingsPathGetter,
            IRequestContext requestContext,
            ILogEvents logger,
            Func<Uri, Microsoft.TeamFoundation.Framework.Client.IdentityDescriptor, IRuntimeContext, IWorkItemRepository> repoBuilder)
        {
            string settingsPath = settingsPathGetter();
            string cacheKey = CacheKey + settingsPath;
            var runtime = (RuntimeContext)Cache.Get(cacheKey);
            if (runtime == null)
            {
                logger.HelloWorld();

                logger.LoadingConfiguration(settingsPath);

                var settings = TFSAggregatorSettings.LoadFromFile(settingsPath, logger);
                runtime = MakeRuntimeContext(settingsPath, settings, requestContext, logger, repoBuilder);

                if (!runtime.HasErrors)
                {
                    var itemPolicy = new CacheItemPolicy();
                    itemPolicy.Priority = CacheItemPriority.NotRemovable;
                    itemPolicy.ChangeMonitors.Add(new HostFileChangeMonitor(new List<string>() { settingsPath }));

                    Cache.Set(cacheKey, runtime, itemPolicy);
                }

                logger.ConfigurationLoaded(settingsPath);
            }
            else
            {
                logger.UsingCachedConfiguration(settingsPath);

                // as it changes at each invocation, must be set again here
                runtime.RequestContext = requestContext;
                runtime.workItemRepository = null;
            }

            return runtime.Clone() as RuntimeContext;
        }

        public static RuntimeContext MakeRuntimeContext(
            string settingsPath,
            TFSAggregatorSettings settings,
            IRequestContext requestContext,
            ILogEvents logger,
            Func<Uri, Microsoft.TeamFoundation.Framework.Client.IdentityDescriptor, IRuntimeContext, IWorkItemRepository> repoBuilder)
        {
            var runtime = new RuntimeContext();

            runtime.Logger = logger;
            runtime.RequestContext = requestContext;
            runtime.SettingsPath = settingsPath;
            runtime.Settings = settings;
            runtime.RateLimiter = new RateLimiter(runtime);
            logger.MinimumLogLevel = runtime.Settings?.LogLevel ?? LogLevel.Normal;
            runtime.repoBuilder = repoBuilder;

            runtime.HasErrors = settings == null;
            return runtime;
        }

        public bool HasErrors { get; private set; }

        private readonly List<string> errorList = new List<string>();

        public IEnumerator<string> Errors
        {
            get
            {
                return this.errorList.GetEnumerator();
            }
        }

        public RateLimiter RateLimiter { get; private set; }

        public IRequestContext RequestContext { get; private set; }

        public string SettingsPath { get; private set; }

        public TFSAggregatorSettings Settings { get; private set; }

        public string Hash
        {
            get
            {
                return this.Settings.Hash;
            }
        }

        public ILogEvents Logger { get; private set; }

        private ScriptEngine cachedEngine = null;

        public ScriptEngine GetEngine()
        {
            if (this.cachedEngine == null)
            {
                System.Diagnostics.Debug.WriteLine("Cache empty for thread {0}", System.Threading.Thread.CurrentThread.ManagedThreadId);
                // HACK remove Facade dependency
                IScriptLibrary library = new Facade.ScriptLibrary(this);
                this.cachedEngine = ScriptEngine.MakeEngine(this.Settings.ScriptLanguage, this.Logger, this.Settings.Debug, library);

                List<Script.ScriptSourceElement> sourceElements = this.GetSourceElements();

                this.cachedEngine.Load(sourceElements);
            }

            return this.cachedEngine;
        }

        private List<Script.ScriptSourceElement> GetSourceElements()
        {
            var sourceElements = new List<Script.ScriptSourceElement>();
            var snippetElements = this.Settings.Snippets.ToList().ConvertAll(
                (snippet) =>
                {
                    return new Script.ScriptSourceElement()
                    {
                        Name = snippet.Name,
                        Type = Script.ScriptSourceElementType.Snippet,
                        SourceCode = snippet.Script
                    };
                });
            sourceElements.AddRange(snippetElements);
            var ruleElements = this.Settings.Rules.ToList().ConvertAll(
                (rule) =>
                {
                    return new Script.ScriptSourceElement()
                    {
                        Name = rule.Name,
                        Type = Script.ScriptSourceElementType.Rule,
                        SourceCode = rule.Script
                    };
                });
            sourceElements.AddRange(ruleElements);
            var functionElements = this.Settings.Functions.ToList().ConvertAll(
                (function) =>
                {
                    return new Script.ScriptSourceElement()
                    {
                        Name = string.Empty,
                        Type = Script.ScriptSourceElementType.Function,
                        SourceCode = function.Script
                    };
                });
            sourceElements.AddRange(functionElements);
            return sourceElements;
        }

        // isolate type constructor to facilitate Unit testing
        private Func<Uri, Microsoft.TeamFoundation.Framework.Client.IdentityDescriptor, IRuntimeContext, IWorkItemRepository> repoBuilder;

        protected virtual IWorkItemRepository CreateWorkItemRepository()
        {
            var requestUri = this.RequestContext.GetProjectCollectionUri();
            var uri = requestUri.ApplyServerSetting(this);

            Microsoft.TeamFoundation.Framework.Client.IdentityDescriptor toImpersonate = null;
            if (this.Settings.AutoImpersonate)
            {
                toImpersonate = this.RequestContext.GetIdentityToImpersonate(uri);
            }

            var newRepo = this.repoBuilder(uri, toImpersonate, this);
            this.Logger.WorkItemRepositoryBuilt(uri, toImpersonate);
            return newRepo;
        }

        private IWorkItemRepository workItemRepository;

        public IWorkItemRepository WorkItemRepository
        {
            get
            {
                if (this.workItemRepository == null)
                {
                    this.workItemRepository = this.CreateWorkItemRepository();
                }

                return this.workItemRepository;
            }
        }

        public object Clone()
        {
            return this.MemberwiseClone();
        }
    }
}
