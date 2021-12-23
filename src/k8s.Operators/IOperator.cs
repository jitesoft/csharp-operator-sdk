using System;
using System.Threading.Tasks;

namespace k8s.Operators
{
    /// <summary>
    /// Represents a Kubernetes operator
    /// </summary>
    public interface IOperator : IDisposable
    {
        /// <summary>
        /// Adds a controller to handle the events of the custom resource R
        /// </summary>
        /// <param name="controller">The controller for the custom resource</param>
        /// <param name="watchNamespace">The watched namespace. Set to null to watch all namespaces</param>
        /// <param name="labelSelector">The <see href="https://kubernetes.io/docs/concepts/overview/working-with-objects/labels/#list-and-watch-filtering">label selector</see> to filter the sets of events returned/></param>
        /// <typeparam name="TResource">The type of the custom resource</typeparam>
        IOperator AddController<TResource>(IController<TResource> controller, string watchNamespace = "default", string labelSelector = null) where TResource : CustomResource;

        /// <summary>
        /// Adds a controller of type TController to handle events for the custom resource TResource.
        /// </summary>
        /// <typeparam name="TController">Controller.</typeparam>
        /// <typeparam name="TResource">Resource.</typeparam>
        /// <returns>The instance of the controller.</returns>
        IController AddControllerOfType<TController, TResource>() where TController : IController<TResource>
            where TResource : CustomResource;

        /// <summary>
        /// Adds a new instance of a controller of type C to handle the events of the custom resource
        /// </summary>
        /// <typeparam name="TController">The type of the controller. TController must implement IController&lt;TResource&gt; and expose a constructor that accepts (OperatorConfiguration, IKubernetes, ILoggerFactory).</typeparam>
        /// <return>The instance of the controller</return>
        IController AddControllerOfType<TController>() where TController : IController;

        /// <summary>
        /// Starts watching and handling events
        /// </summary>
        Task<int> StartAsync();

        /// <summary>
        /// Stops the operator and release the resources. Once stopped, an operator cannot be restarted. Stop() is an alias for Dispose()
        /// </summary>
        void Stop();

        /// <summary>
        /// Returns true if StartAsync has been called and the operator is running
        /// </summary>
        bool IsRunning { get; }

        /// <summary>
        /// Returns true if Stop/Dispose has been called and not completed
        /// </summary>
        /// <returns></returns>
        bool IsDisposing { get; }
    }
}
