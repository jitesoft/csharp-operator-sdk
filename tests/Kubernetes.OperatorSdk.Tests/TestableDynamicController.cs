using System.Threading.Tasks;
using System.Threading;

namespace Kubernetes.OperatorSdk.Tests;

public class TestableDynamicController : Controller<TestableDynamicCustomResource>
{
    public TestableDynamicController() : base(OperatorConfiguration.Default, null, null)
    {
    }

    protected override Task AddOrModifyAsync(TestableDynamicCustomResource resource, CancellationToken cancellationToken)
    {
        resource.Status.property = resource.Spec.property;
        return Task.CompletedTask;
    }
}
