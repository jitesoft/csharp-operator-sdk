using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using k8s.Models;
using Newtonsoft.Json;

namespace k8s.Operators
{
    /// <summary>
    /// Represents a Kubernetes list of custom resources of type T
    /// </summary>
    [ExcludeFromCodeCoverage]
    public abstract class CustomResourceList<T> : KubernetesObject where T : CustomResource
    {
        [JsonProperty("metadata")]
        [JsonPropertyName("metadata")]
        public V1ListMeta Metadata { get; set; }

        [JsonProperty("items")]
        [JsonPropertyName("items")]
        public List<T> Items { get; set; }
    }
}
