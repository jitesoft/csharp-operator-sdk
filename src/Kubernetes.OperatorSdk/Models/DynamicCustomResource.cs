namespace Kubernetes.OperatorSdk;

/// <summary>
/// Represents a Kubernetes custom resource with dynamic typed spec and status
/// </summary>
public abstract class DynamicCustomResource : CustomResource<dynamic, dynamic>
{
}
