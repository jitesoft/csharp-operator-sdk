using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Rest;
using Microsoft.Extensions.Logging;
using k8s.Operators.Logging;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Microsoft.Extensions.Logging.Abstractions;

namespace k8s.Operators
{
    /// <summary>
    /// Represents a Kubernetes operator
    /// </summary>
    public class Operator : Disposable, IOperator
    {
        private const string ALL_NAMESPACES = "";

        private readonly ILogger _logger;
        private readonly OperatorConfiguration _configuration;
        private readonly IKubernetes _client;
        protected readonly List<EventWatcher> _watchers;
        private readonly CancellationTokenSource _cts;
        private readonly ILoggerFactory _loggerFactory;
        private bool _isStarted;
        private bool _unexpectedWatcherTermination;

        public Operator(OperatorConfiguration configuration, IKubernetes client, ILoggerFactory loggerFactory = null)
        {
            _configuration = configuration;
            _client = client;
            _loggerFactory = loggerFactory;
            _logger = loggerFactory?.CreateLogger<Operator>() ?? NullLogger<Operator>.Instance;
            _watchers = new List<EventWatcher>();
            _cts = new CancellationTokenSource();

            TaskScheduler.UnobservedTaskException += (o, ev) =>
            {
                _logger.LogError(ev.Exception, "Unobserved exception");
                ev.SetObserved();
            };

            _logger.LogInformation("Operator SDK Version {Version}", typeof(Operator).Assembly.GetName().Version!.ToString(3));
        }

        /// <summary>
        /// Adds a controller to handle the events of the custom resource R
        /// </summary>
        /// <param name="controller">The controller for the custom resource</param>
        /// <param name="watchNamespace">The watched namespace. Set to null to watch all namespaces</param>
        /// <param name="labelSelector">The <see href="https://kubernetes.io/docs/concepts/overview/working-with-objects/labels/#list-and-watch-filtering">label selector</see> to filter the sets of events returned/></param>
        /// <typeparam name="R">The type of the custom resource</typeparam>
        public IOperator AddController<R>(IController<R> controller, string watchNamespace = "default", string labelSelector = null) where R : CustomResource
        {
            if (IsDisposed)
            {
                throw new ObjectDisposedException("Operator");
            }

            if (controller == null)
            {
                throw new ValidationException(ValidationRules.CannotBeNull, nameof(controller));
            }

            if (IsRunning)
            {
                throw new InvalidOperationException("A controller cannot be added once the operator has started");
            }

            watchNamespace ??= ALL_NAMESPACES;

            _logger.LogDebug($"Added controller {controller} on namespace {(string.IsNullOrEmpty(watchNamespace) ? "\"\"" : watchNamespace)}");

            _watchers.Add(new EventWatcher(typeof(R), watchNamespace, labelSelector, controller, _logger, _cts.Token));

            return this;
        }

        public IController AddControllerOfType<TController, TResource>() where TController : IController<TResource> where TResource : CustomResource
        {
            var controller = Activator.CreateInstance(
                typeof(TController),
                _configuration,
                _client,
                _loggerFactory
            ) as IController<TResource>;

            AddController(
                controller,
                _configuration.WatchNamespace,
                _configuration.WatchLabelSelector
            );

            return controller;
        }

        /// <summary>
        /// Adds a new instance of a controller of type C to handle the events of the custom resource
        /// </summary>
        /// <typeparam name="TController">The type of the controller. C must implement IController<R> and expose a constructor that accepts (OperatorConfiguration, IKubernetes, ILoggerFactory)</typeparam>
        /// <return>The instance of the controller</return>
        public IController AddControllerOfType<TController>() where TController : IController
        {
            // Use Reflection to instantiate the controller and pass it to AddController<R>()

            // ASSUMPTION: TController implements IController<TResource>, where TResource is a custom resource
            // Retrieve the type of the resource.
            var resourceType = typeof(TController).BaseType?.GetGenericArguments()[0];

            if (resourceType == null)
            {
                throw new Exception("Resource type not found.");
            }

            // Instantiate the controller implementing IController<R> via the standard constructor (OperatorConfiguration, IKubernetes, ILoggerFactory)
            var controller = Activator.CreateInstance(
                typeof(TController),
                _configuration,
                _client,
                _loggerFactory
            ) as IController;

            // Invoke AddController<R>()
            typeof(Operator).GetMethod("AddController")!
                .MakeGenericMethod(resourceType)
                .Invoke(this, new object[]
                {
                    controller,
                    _configuration.WatchNamespace,
                    _configuration.WatchLabelSelector
                });

            return controller;
        }

        /// <summary>
        /// Starts watching and handling events
        /// </summary>
        public async Task<int> StartAsync()
        {
            if (IsDisposed)
            {
                throw new ObjectDisposedException("Operator");
            }

            _logger.LogInformation("Start operator");

            if (_watchers.Count == 0)
            {
                _logger.LogDebug("No controller added, stopping operator");
                Stop();
                return 0;
            }

            _isStarted = true;

            var tasks = _watchers.Select<EventWatcher, Task>(watcher =>
            {
                // Invoke WatchCustomResourceAsync via reflection, since T is in a variable
                var watchCustomResourceAsync = typeof(Operator)
                    .GetMethod("WatchCustomResourceAsync", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .MakeGenericMethod(watcher.ResourceType);

                return ((Task)watchCustomResourceAsync.Invoke(this, new[] { watcher })).ContinueWith(task =>
                {
                    if (task.IsFaulted)
                    {
                        _logger.LogError(
                            task.Exception!.Flatten().InnerException,
                            "Error watching {Namespace}/{Plural} {LabelSelector}",
                            watcher.Namespace,
                            watcher.CRD.Plural,
                            watcher.LabelSelector
                        );
                    }
                });
            });

            await Task.WhenAll(tasks.ToArray());
            return _unexpectedWatcherTermination ? 1 : 0;
        }

        /// <summary>
        /// Stops the operator and release the resources. Once stopped, an operator cannot be restarted. Stop() is an alias for Dispose()
        /// </summary>
        public void Stop()
        {
            _logger.LogInformation("Stop operator");
            Dispose();
        }

        /// <summary>
        /// Returns true if StartAsync has been called and the operator is running
        /// </summary>
        public bool IsRunning => !IsDisposing && !IsDisposed && _isStarted;

        /// <summary>
        /// Watches for events for a given resource definition and namespace. If namespace is empty string, it watches all namespaces
        /// </summary>
        private async Task WatchCustomResourceAsync<T>(EventWatcher watcher) where T : CustomResource
        {
            if (IsDisposing || IsDisposed)
            {
                return;
            }

            var response = string.IsNullOrEmpty(watcher.Namespace) switch
            {
                true => _client.ListClusterCustomObjectWithHttpMessagesAsync(
                    watcher.CRD.Group,
                    watcher.CRD.Version,
                    watcher.CRD.Plural,
                    watch: true,
                    labelSelector: watcher.LabelSelector,
                    timeoutSeconds: (int)TimeSpan.FromMinutes(60).TotalSeconds,
                    cancellationToken: _cts.Token
                ),
                _ => _client.ListNamespacedCustomObjectWithHttpMessagesAsync(
                    watcher.CRD.Group,
                    watcher.CRD.Version,
                    watcher.Namespace,
                    watcher.CRD.Plural,
                    watch: true,
                    labelSelector: watcher.LabelSelector,
                    timeoutSeconds: (int)TimeSpan.FromMinutes(60).TotalSeconds,
                    cancellationToken: _cts.Token
                ),
            };

            _logger.LogDebug(
                "Begin watch {Namespace}/{Plural} {LabelSelector}",
                string.IsNullOrEmpty(watcher.Namespace) ? "*" : watcher.Namespace,
                watcher.CRD.Plural,
                watcher.LabelSelector ?? ""
            );

            using var _ = response.Watch<T, object>(watcher.OnIncomingEvent, OnWatcherError, OnWatcherClose);
            await WaitOneAsync(_cts.Token.WaitHandle);

            _logger.LogDebug(
                "End watch {Namespace}/{Plural} {LabelSelector}",
                string.IsNullOrEmpty(watcher.Namespace) ? "*" : watcher.Namespace,
                watcher.CRD.Plural,
                watcher.LabelSelector ?? ""
            );
        }

        [ExcludeFromCodeCoverage]
        protected void OnWatcherError(Exception exception)
        {
            if (IsRunning)
            {
                _logger.LogError(exception, "Watcher error");
            }
        }

        [ExcludeFromCodeCoverage]
        protected virtual void OnWatcherClose()
        {
            _logger.LogError("Watcher closed");

            if (!IsRunning)
            {
                return;
            }

            // At least one watcher stopped unexpectedly. Stop the operator, let Kubernetes restart it
            _unexpectedWatcherTermination = true;
            Stop();
        }

        /// <summary>
        /// Returns a Task wrapper for a synchronous wait on a wait handle
        /// </summary>
        /// <see cref="https://msdn.microsoft.com/en-us/library/hh873178%28v=vs.110%29.aspx#WHToTap"/>
        private static Task<bool> WaitOneAsync(WaitHandle waitHandle, int millisecondsTimeOutInterval = Timeout.Infinite)
        {
            if (waitHandle == null)
            {
                throw new ArgumentNullException(nameof(waitHandle));
            }

            var tcs = new TaskCompletionSource<bool>();

            var rwh = ThreadPool.RegisterWaitForSingleObject(
                waitHandle,
                (_, timedOut) => { tcs.TrySetResult(!timedOut); },
                null,
                millisecondsTimeOutInterval,
                true
            );

            var task = tcs.Task;

            task.ContinueWith(t =>
            {
                rwh.Unregister(null);
                try
                {
                    return t.Result;
                }
                catch
                {
                    return false;
                }
            });

            return task;
        }

        protected override void DisposeInternal()
        {
            _logger.LogInformation("Disposing operator");

            // Signal the watchers to stop
            _cts.Cancel();
        }
    }
}
