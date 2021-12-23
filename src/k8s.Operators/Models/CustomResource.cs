using System.Text.Json.Serialization;
using k8s.Models;

namespace k8s.Operators
{
    /// <summary>
    /// Represents a Kubernetes custom resource
    /// </summary>
    public abstract class CustomResource : KubernetesObject, IKubernetesObject<V1ObjectMeta>
    {
        [JsonPropertyName("metadata")]
        public V1ObjectMeta Metadata { get; set; }

        public override string ToString()
        {
            return $"{Metadata.NamespaceProperty}/{Metadata.Name} (gen: {Metadata.Generation}, uid: {Metadata.Uid})";
        }
    }

    /// <summary>
    /// Represents a Kubernetes custom resource that has a spec
    /// </summary>
    public abstract class CustomResource<TSpec> : CustomResource, ISpec<TSpec>
    {
        [JsonPropertyName("spec")]
        public TSpec Spec { get; set; }
    }

    /// <summary>
    /// Represents a Kubernetes custom resource that has a spec and status
    /// </summary>
    public abstract class CustomResource<TSpec, TStatus> : CustomResource<TSpec>, IStatus, IStatus<TStatus>
    {
        [JsonPropertyName("status")]
        public TStatus Status { get; set; }

        object IStatus.Status { get => Status; set => Status = (TStatus) value; }
    }
}
