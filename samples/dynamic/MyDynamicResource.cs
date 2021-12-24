using System.Text.Json;

namespace Kubernetes.OperatorSdk.Samples.Dynamic;

[CustomResourceDefinition("csharp-operator.example.com", "v1", "myresources")]
public class MyDynamicResource : DynamicCustomResource
{
    public override string ToString()
    {
        return
            $"{Metadata.NamespaceProperty}/{Metadata.Name} (gen: {Metadata.Generation}), Spec: {JsonSerializer.Serialize(Spec)} Status: {JsonSerializer.Serialize(Status ?? new object())}";
    }
}
