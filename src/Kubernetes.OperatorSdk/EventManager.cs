using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kubernetes.OperatorSdk;

/// <summary>
/// Manages the event queues for the watched resources
/// </summary>
public class EventManager
{
    private readonly ILogger _logger;

    // Next event to handle, for each resource.
    // A real queue is not used since intermediate events are discarded and only the queue head is stored.
    private readonly Dictionary<string, CustomResourceEvent> _queuesByResource;

    // Events that are currently being handled
    private readonly Dictionary<string, CustomResourceEvent> _handling;

    public EventManager(ILoggerFactory loggerFactory = null)
    {
        _logger = loggerFactory?.CreateLogger<EventManager>() ?? NullLogger<EventManager>.Instance;
        _queuesByResource = new Dictionary<string, CustomResourceEvent>();
        _handling = new Dictionary<string, CustomResourceEvent>();
    }

    /// <summary>
    /// Enqueue the event
    /// </summary>
    public void Enqueue(CustomResourceEvent resourceEvent)
    {
        lock (this)
        {
            _logger.LogTrace("Enqueue {Event}", resourceEvent);
            // Insert or update the next event for the resource
            _queuesByResource[resourceEvent.ResourceUid] = resourceEvent;
        }
    }

    /// <summary>
    /// Returns the next event to process, without dequeuing it
    /// </summary>
    public CustomResourceEvent Peek(string resourceUid)
    {
        lock (this)
        {
            if (_queuesByResource.TryGetValue(resourceUid, out CustomResourceEvent result))
            {
                _logger.LogTrace("Peek {Result}", result);
            }

            return result;
        }
    }

    /// <summary>
    /// Pops and returns the next event to process, if any
    /// </summary>
    public CustomResourceEvent Dequeue(string resourceUid)
    {
        lock (this)
        {
            if (IsHandling(resourceUid, out var handlingEvent))
            {
                _logger.LogDebug("Postponed Dequeue, handling {Event}", handlingEvent);
                return null;
            }
            else
            {
                if (!_queuesByResource.TryGetValue(resourceUid, out CustomResourceEvent result))
                {
                    return result;
                }

                _queuesByResource.Remove(resourceUid);
                _logger.LogTrace("Dequeue {Result}", result);
                return result;
            }
        }
    }

    /// <summary>
    /// Track the begin of an event handling
    /// </summary>
    public void BeginHandleEvent(CustomResourceEvent resourceEvent)
    {
        lock (this)
        {
            _logger.LogTrace("BeginHandleEvent {Event}", resourceEvent);
            _handling[resourceEvent.ResourceUid] = resourceEvent;
        }
    }

    /// <summary>
    /// Track the end of an event handling
    /// </summary>
    public void EndHandleEvent(CustomResourceEvent resourceEvent)
    {
        lock (this)
        {
            _logger.LogTrace("EndHandleEvent {Event}", resourceEvent);
            _handling.Remove(resourceEvent.ResourceUid);
        }
    }

    /// <summary>
    /// Returns true if there is an event being handled
    /// </summary>
    private bool IsHandling(string resourceUid, out CustomResourceEvent handlingEvent)
    {
        return _handling.TryGetValue(resourceUid, out handlingEvent);
    }
}
