using System;
using System.Threading;
using k8s;
using Microsoft.Extensions.Logging;

namespace Kubernetes.OperatorSdk;

/// <summary>
/// Implements the watch callback method for a given namespace, resource, label selector
/// </summary>
public class EventWatcher
{
    private readonly ILogger _logger;
    private readonly CancellationToken _cancellationToken;

    public Type ResourceType { get; private set; }
    public CustomResourceDefinitionAttribute CRD { get; private set; }
    public string Namespace { get; private set; }
    public string LabelSelector { get; private set; }
    public IController Controller { get; private set; }

    public EventWatcher(Type resourceType, string namespaceName, string labelSelector, IController controller,
        ILogger logger, CancellationToken cancellationToken = default)
    {
        ResourceType = resourceType;
        Namespace = namespaceName;
        LabelSelector = labelSelector;
        Controller = controller;
        _logger = logger;
        _cancellationToken = cancellationToken;

        // Retrieve the CRD associated to the CR
        CRD = (CustomResourceDefinitionAttribute)Attribute.GetCustomAttribute(
            resourceType,
            typeof(CustomResourceDefinitionAttribute)
        );
    }

    /// <summary>
    /// Dispatches an incoming event to the controller
    /// </summary>
    public void OnIncomingEvent(WatchEventType eventType, CustomResource resource)
    {
        var resourceEvent = new CustomResourceEvent(eventType, resource);
        _logger.LogDebug("Received event {Event}", resourceEvent);

        Controller.ProcessEventAsync(resourceEvent, _cancellationToken)
            // ReSharper disable once MethodSupportsCancellation
            .ContinueWith(task =>
            {
                if (!task.IsFaulted)
                {
                    return;
                }

                var exception = task.Exception?.Flatten().InnerException;
                _logger.LogError(
                    exception,
                    "Error processing {Event}",
                    resourceEvent
                );
            });
    }
}
