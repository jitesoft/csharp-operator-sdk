using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using k8s;
using k8s.Models;

namespace Kubernetes.OperatorSdk;

/// <summary>
/// Represents a Kubernetes list of custom resources of type T
/// </summary>
[ExcludeFromCodeCoverage]
public abstract class CustomResourceList<T> : KubernetesObject where T : CustomResource
{
    [JsonPropertyName("metadata")]
    public V1ListMeta Metadata { get; set; }

    [JsonPropertyName("items")]
    public List<T> Items { get; set; }
}
