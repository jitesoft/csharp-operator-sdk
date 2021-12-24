namespace Kubernetes.OperatorSdk;

/// <summary>
/// Kubernetes custom resource that exposes status
/// </summary>
public interface IStatus
{
    object Status { get; set; }
}
