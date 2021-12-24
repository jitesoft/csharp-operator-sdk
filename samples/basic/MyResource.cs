using System.Text.Json.Serialization;

namespace Kubernetes.OperatorSdk.Samples.Basic;

[CustomResourceDefinition("csharp-operator.example.com", "v1", "myresources")]
public class MyResource : CustomResource<MyResource.MyResourceSpec, MyResource.MyResourceStatus>
{
    public class MyResourceSpec
    {
        [JsonPropertyName("desiredProperty")] public int Desired { get; set; }
    }

    public class MyResourceStatus
    {
        [JsonPropertyName("actualProperty")] public int Actual { get; set; }
    }

    public override string ToString()
    {
        return
            $"{Metadata.NamespaceProperty}/{Metadata.Name} (gen: {Metadata.Generation}), Spec: {Spec.Desired} Status: {Status?.Actual}";
    }
}
