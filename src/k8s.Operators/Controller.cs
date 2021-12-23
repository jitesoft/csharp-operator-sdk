using System;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using k8s.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Rest;

namespace k8s.Operators
{
    /// <summary>
    /// Controller of a custom resource of type T
    /// </summary>
    public abstract class Controller<T> : IController<T> where T : CustomResource
    {
        protected readonly ILogger _logger;
        protected readonly IKubernetes _client;
        private readonly EventManager _eventManager;
        private readonly ResourceChangeTracker _changeTracker;
        private readonly CustomResourceDefinitionAttribute _crd;

        public Controller(OperatorConfiguration configuration, IKubernetes client, ILoggerFactory loggerFactory = null)
        {
            _client = client;
            _logger = loggerFactory?.CreateLogger<Controller<T>>() ?? NullLogger<Controller<T>>.Instance;
            _eventManager = new EventManager(loggerFactory);
            _changeTracker = new ResourceChangeTracker(configuration, loggerFactory);
            _crd = (CustomResourceDefinitionAttribute)Attribute.GetCustomAttribute(typeof(T),
                typeof(CustomResourceDefinitionAttribute));
            RetryPolicy = configuration.RetryPolicy;
        }

        /// <summary>
        /// Retry policy for the controller
        /// </summary>
        public RetryPolicy RetryPolicy { get; protected set; }

        /// <summary>
        /// Processes a custom resource event
        /// </summary>
        /// <param name="resourceEvent">The event to handle</param>
        /// <param name="cancellationToken">Signals if the current execution has been canceled</param>
        public async Task ProcessEventAsync(CustomResourceEvent resourceEvent, CancellationToken cancellationToken)
        {
            _logger.LogDebug(
                "Begin ProcessEvent, {Event}",
                resourceEvent
            );

            switch (resourceEvent.Type)
            {
                case WatchEventType.Error:
                    _logger.LogError(
                        "Received Error event, {Resource}",
                        resourceEvent.Resource
                    );
                    return;
                case WatchEventType.Deleted:
                    // Skip Deleted events since there is nothing else to do
                    _logger.LogDebug(
                        "Skip ProcessEvent, received Deleted event, {Resource}",
                        resourceEvent.Resource
                    );
                    return;
                case WatchEventType.Bookmark:
                    // Skip Bookmark events since there is nothing else to do
                    _logger.LogDebug(
                        "Skip ProcessEvent, received Bookmark event, {Resource}",
                        resourceEvent.Resource
                    );
                    return;
                case WatchEventType.Added:
                case WatchEventType.Modified:
                default:
                    break;
            }

            // Enqueue the event
            _eventManager.Enqueue(resourceEvent);

            while (!cancellationToken.IsCancellationRequested)
            {
                // Dequeue the next event to process for this resource, if any
                var nextEvent = _eventManager.Dequeue(resourceEvent.ResourceUid);
                if (nextEvent == null)
                {
                    break;
                }

                await HandleEventAsync(nextEvent, cancellationToken);
            }

            _logger.LogDebug(
                "End ProcessEvent, {Event}",
                resourceEvent
            );
        }

        private async Task HandleEventAsync(CustomResourceEvent resourceEvent, CancellationToken cancellationToken)
        {
            if (resourceEvent == null)
            {
                _logger.LogWarning(
                    "Skip HandleEvent, {EventName} is null",
                    nameof(resourceEvent)
                );
                return;
            }

            _logger.LogDebug("Begin HandleEvent, {Event}", resourceEvent);

            _eventManager.BeginHandleEvent(resourceEvent);

            var attempt = 1;
            var delay = RetryPolicy.InitialDelay;
            while (true)
            {
                // Try to handle the event
                var handled = await TryHandleEventAsync(resourceEvent, cancellationToken);
                if (handled)
                {
                    break;
                }

                // Something went wrong
                if (!CanTryAgain(resourceEvent, attempt, cancellationToken))
                {
                    break;
                }

                _logger.LogDebug(
                    "Retrying to handle {Event} in {Delay}ms (attempt #{Attempt})",
                    resourceEvent,
                    delay,
                    attempt
                );

                // Wait
                await Task.Delay(delay);

                // Increase the delay for the next attempt
                attempt++;
                delay = (int)(delay * RetryPolicy.DelayMultiplier);
            }

            _logger.LogDebug("End HandleEvent, {Event}", resourceEvent);

            _eventManager.EndHandleEvent(resourceEvent);
        }

        private bool CanTryAgain(CustomResourceEvent resourceEvent, int attemptNumber,
            CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                _logger.LogDebug(
                    "Cannot retry {Event}, processing has been canceled",
                    resourceEvent
                );
                return false;
            }

            var upcoming = _eventManager.Peek(resourceEvent.ResourceUid);
            if (upcoming != null)
            {
                _logger.LogDebug(
                    "Cannot retry {Event}, received {NextId} in the meantime",
                    resourceEvent,
                    upcoming
                );
                return false;
            }

            if (attemptNumber > RetryPolicy.MaxAttempts)
            {
                _logger.LogDebug(
                    "Cannot retry {Event}, max number of attempts reached",
                    resourceEvent
                );
                return false;
            }

            return true;
        }

        private async Task<bool> TryHandleEventAsync(CustomResourceEvent resourceEvent,
            CancellationToken cancellationToken)
        {
            var handled = true;

            try
            {
                var resource = (T)resourceEvent.Resource;
                if (IsDeletePending(resource))
                {
                    await HandleDeletedEventAsync(resource, cancellationToken);
                }
                else
                {
                    await HandleAddedOrModifiedEventAsync(resource, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("Canceled HandleEvent, {Event}", resourceEvent);
            }
            catch (Exception exception)
            {
                if (exception is HttpOperationException httpException &&
                    httpException.Response?.StatusCode == System.Net.HttpStatusCode.Conflict)
                {
                    // Conflicts happen. The next event will make the resource consistent again
                    _logger.LogDebug(exception, "Conflict handling {Event}", resourceEvent);
                }
                else
                {
                    _logger.LogError(exception, "Error handling {Event}", resourceEvent);
                    handled = false;
                }
            }

            return handled;
        }

        private async Task HandleAddedOrModifiedEventAsync(T resource, CancellationToken cancellationToken)
        {
            _logger.LogDebug("Handle Added/Modified, {Resource}", resource);

            if (!HasFinalizer(resource))
            {
                // Before any custom logic, add a finalizer to be used later during the deletion phase
                _logger.LogDebug("Add missing finalizer");
                await AddFinalizerAsync(resource, cancellationToken);
                return;
            }

            if (_changeTracker.IsResourceGenerationAlreadyHandled(resource))
            {
                _logger.LogDebug("Skip AddOrModifyAsync, {Resource} already handled", resource);
            }
            else
            {
                _logger.LogDebug("Begin AddOrModifyAsync, {Resource}", resource);

                // Add/modify logic (implemented by the derived class)
                await AddOrModifyAsync(resource, cancellationToken);

                _changeTracker.TrackResourceGenerationAsHandled(resource);

                _logger.LogDebug("End AddOrModifyAsync, {Resource}", resource);
            }
        }

        private async Task HandleDeletedEventAsync(T resource, CancellationToken cancellationToken)
        {
            _logger.LogDebug("Handle Deleted, {Resource}", resource);

            if (!HasFinalizer(resource))
            {
                // The current deletion request is not handled by this controller
                _logger.LogDebug("Skip OnDeleted, {Resource} has no finalizer", resource);
                return;
            }

            _logger.LogDebug("Begin OnDeleted, {Resource}", resource);

            // Delete logic (implemented by the derived class)
            await DeleteAsync(resource, cancellationToken);

            _changeTracker.TrackResourceGenerationAsDeleted(resource);

            if (HasFinalizer(resource))
            {
                await RemoveFinalizerAsync(resource, cancellationToken);
            }

            _logger.LogDebug("End OnDeleted, {Resource}", resource);
        }

        /// <summary>
        /// Implements the logic to add or modify a resource
        /// </summary>
        /// <param name="resource">Resource being added or modified</param>
        /// <param name="cancellationToken">Signals if the current execution has been canceled</param>
        [ExcludeFromCodeCoverage]
        protected virtual Task AddOrModifyAsync(T resource, CancellationToken cancellationToken)
        {
            return Task.FromResult(0);
        }

        /// <summary>
        /// Implements the logic to delete a resource
        /// </summary>
        /// <param name="resource">Resource being deleted</param>
        /// <param name="cancellationToken">Signals if the current execution has been canceled</param>
        /// <returns></returns>
        [ExcludeFromCodeCoverage]
        protected virtual Task DeleteAsync(T resource, CancellationToken cancellationToken)
        {
            return Task.FromResult(0);
        }

        /// <summary>
        /// Updates the status subresource.
        /// See https://kubernetes.io/docs/tasks/extend-kubernetes/custom-resources/custom-resource-definitions/#status-subresource
        /// </summary>
        protected Task<T> UpdateStatusAsync<TResource>(TResource resource, string fieldManager = null, CancellationToken cancellationToken = default) where TResource : T, IStatus
        {
            return PatchCustomResourceStatusAsync(resource, fieldManager, cancellationToken);
        }

        /// <summary>
        /// Updates the resource (except the status)
        /// </summary>
        protected Task<T> UpdateResourceAsync(T resource, CancellationToken cancellationToken)
        {
            return ReplaceCustomResourceAsync(resource, cancellationToken);
        }

        private bool IsDeletePending(CustomResource resource)
        {
            return resource.Metadata.DeletionTimestamp != null;
        }

        private bool HasFinalizer(CustomResource resource)
        {
            return resource.Metadata.Finalizers?.Contains(_crd.Finalizer) == true;
        }

        private Task<T> AddFinalizerAsync(T resource, CancellationToken cancellationToken)
        {
            // Add the finalizer
            resource.Metadata.EnsureFinalizers().Add(_crd.Finalizer);

            return ReplaceCustomResourceAsync(resource, cancellationToken);
        }

        private Task<T> RemoveFinalizerAsync(T resource, CancellationToken cancellationToken)
        {
            // Remove the finalizer
            resource.Metadata.Finalizers.Remove(_crd.Finalizer);
            return ReplaceCustomResourceAsync(resource, cancellationToken);
        }

        private async Task<T> ReplaceCustomResourceAsync(T resource, CancellationToken cancellationToken)
        {
            _logger.LogDebug(
                "Replace Custom Resource, {Resource}",
                resource == null ? "" : JsonSerializer.Serialize(resource)
            );

            var result = string.IsNullOrEmpty(resource.Namespace()) switch
            {
                true => await _client.ReplaceClusterCustomObjectAsync(
                    resource,
                    _crd.Group,
                    _crd.Version,
                    _crd.Plural,
                    resource.Name(),
                    cancellationToken: cancellationToken
                ).ConfigureAwait(false),
                false => await _client.ReplaceNamespacedCustomObjectAsync(
                    resource,
                    _crd.Group,
                    _crd.Version,
                    resource.Namespace(),
                    _crd.Plural,
                    resource.Name(),
                    cancellationToken: cancellationToken
                ).ConfigureAwait(false),
            };

            return ToCustomResource(result);
        }

        private async Task<T> PatchCustomResourceStatusAsync<R>(R resource,
            string manager = null,
            CancellationToken cancellationToken = default) where R : T, IStatus
        {
            var patchObject = new V1Patch(new {
                status = resource.Status
            }, V1Patch.PatchType.MergePatch);
            patchObject.Validate();

            _logger.LogDebug(
                "Patch Status, {Resource}",
                JsonSerializer.Serialize(patchObject)
            );

            var result = string.IsNullOrEmpty(resource.Namespace()) switch
            {
                true => await _client.PatchClusterCustomObjectStatusAsync(
                    patchObject,
                    _crd.Group,
                    _crd.Version,
                    _crd.Plural,
                    resource.Name(),
                    fieldManager: manager,
                    cancellationToken: cancellationToken
                ).ConfigureAwait(false),
                false => await _client.PatchNamespacedCustomObjectStatusAsync(
                    patchObject,
                    _crd.Group,
                    _crd.Version,
                    resource.Namespace(),
                    _crd.Plural,
                    resource.Name(),
                    null,
                    manager,
                    null,
                    cancellationToken
                ).ConfigureAwait(false)
            };

            return ToCustomResource(result);
        }

        private static T ToCustomResource(object input)
        {
            return input switch
            {
                JsonElement json => json.Deserialize<T>(new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                }),
                _ => (T)input
            };
        }
    }
}
