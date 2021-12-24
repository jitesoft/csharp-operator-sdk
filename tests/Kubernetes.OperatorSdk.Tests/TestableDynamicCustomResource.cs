using System.Dynamic;
using k8s.Models;

namespace Kubernetes.OperatorSdk.Tests;

[CustomResourceDefinition("group", "v1", "resources")]
public class TestableDynamicCustomResource : DynamicCustomResource
{
    public TestableDynamicCustomResource()
    {
        Metadata = new V1ObjectMeta();
        Metadata.EnsureFinalizers().Add(CustomResourceDefinitionAttribute.DEFAULT_FINALIZER);
        Metadata.NamespaceProperty = "ns1";
        Metadata.Name = "resource1";
        Metadata.Generation = 1;
        Metadata.Uid = "id1";

        Spec = new ExpandoObject();
        Status = new ExpandoObject();
    }
}
