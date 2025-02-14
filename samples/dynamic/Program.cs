﻿using System;
using System.Text.Json;
using System.Threading.Tasks;
using k8s;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Rest;
using Kubernetes.OperatorSdk.Logging;

namespace Kubernetes.OperatorSdk.Samples.Dynamic;

class Program
{
    static async Task<int> Main(string[] args)
    {
        IOperator dynamicOperator = null;

        // Setup logging
        using var loggerFactory = SetupLogging(args);
        var logger = loggerFactory.CreateLogger<Program>();

        try
        {
            logger.LogDebug($"Environment variables: {JsonSerializer.Serialize(Environment.GetEnvironmentVariables())}");

            // Setup termination handlers
            SetupSignalHandlers();

            // Setup the Kubernetes client
            using var client = SetupClient(args);

            // Setup the operator
            var configuration = GetOperatorConfiguration();
            dynamicOperator = new Operator(configuration, client, loggerFactory);
            dynamicOperator.AddControllerOfType<MyDynamicResourceController>();

            // Start the operator
            return await dynamicOperator.StartAsync();
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Operator error");
            return 1;
        }

        IKubernetes SetupClient(string[] args)
        {
            // Load the Kubernetes configuration
            KubernetesClientConfiguration config = null;

            if (KubernetesClientConfiguration.IsInCluster())
            {
                logger.LogDebug("Loading cluster configuration");
                config = KubernetesClientConfiguration.InClusterConfig();
            }
            else
            {
                logger.LogDebug("Loading local configuration");
                config = KubernetesClientConfiguration.BuildConfigFromConfigFile();
            }

            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug($"Client configuration: {JsonSerializer.Serialize(config)}");
            }

            return new k8s.Kubernetes(config);
        }

        ILoggerFactory SetupLogging(string[] args)
        {
            if (!System.Enum.TryParse<LogLevel>(Environment.GetEnvironmentVariable("LOG_LEVEL"), true, out LogLevel logLevel))
            {
                logLevel = LogLevel.Debug;
            }

            var loggerFactory = LoggerFactory.Create(builder => builder
                .AddSimpleConsole()
                .SetMinimumLevel(logLevel)
            );

            // Enable Kubernetes client logging if level = DEBUG
            ServiceClientTracing.IsEnabled = logLevel <= LogLevel.Debug;
            ServiceClientTracing.AddTracingInterceptor(new ConsoleTracingInterceptor());

            return loggerFactory;
        }

        void SetupSignalHandlers()
        {
            // SIGTERM: signal the operator to shut down gracefully
            AppDomain.CurrentDomain.ProcessExit += (s, e) =>
            {
                logger.LogDebug("Received SIGTERM");
                dynamicOperator?.Stop();
            };

            // SIGINT: try to shut down gracefully on the first attempt
            Console.CancelKeyPress += (s, e) =>
            {
                bool isFirstSignal = !dynamicOperator.IsDisposing;
                logger.LogDebug($"Received SIGINT, first signal: {isFirstSignal}");
                if (isFirstSignal)
                {
                    e.Cancel = true;
                    Environment.Exit(0);
                }
            };
        }
    }

    private static OperatorConfiguration GetOperatorConfiguration()
    {
        var configuration = new OperatorConfiguration();

        var retryPolicy = new RetryPolicy();
        if (int.TryParse(Environment.GetEnvironmentVariable("RETRY_MAX_ATTEMPTS"), out int maxAttempts))
        {
            retryPolicy.MaxAttempts = Math.Max(1, maxAttempts);
        }
        if (int.TryParse(Environment.GetEnvironmentVariable("RETRY_INITIAL_DELAY"), out int initialDelay))
        {
            retryPolicy.InitialDelay = Math.Max(0, initialDelay);
        }
        if (int.TryParse(Environment.GetEnvironmentVariable("RETRY_DELAY_MULTIPLIER"), out int delayMultiplier))
        {
            retryPolicy.DelayMultiplier = delayMultiplier;
        }
        configuration.RetryPolicy = retryPolicy;

        configuration.WatchNamespace = Environment.GetEnvironmentVariable("WATCH_NAMESPACE");

        configuration.WatchLabelSelector = Environment.GetEnvironmentVariable("WATCH_LABEL_SELECTOR");

        return configuration;
    }
}
