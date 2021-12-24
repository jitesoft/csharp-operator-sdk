using k8s.Models;

namespace Kubernetes.OperatorSdk.Tests;

[CustomResourceDefinition("group", "v1", "resources")]
public class TestableCustomResource : CustomResource<TestableCustomResource.TestableSpec, TestableCustomResource.TestableStatus>
{
    public TestableCustomResource()
    {
        Metadata = new V1ObjectMeta
        {
            NamespaceProperty = "ns1",
            Name = "resource1",
            Generation = 1,
            Uid = "id1"
        };
    }

    public class TestableSpec
    {
        public string Property { get; set; }
    }

    public class TestableStatus
    {
        public string Property { get; set; }
    }
}
